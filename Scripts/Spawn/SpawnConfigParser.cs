/******************************************************************************
 * 文件名称：SpawnConfigParser.cs
 *
 * 功能描述：
 *     将导演文本解析为按原文件顺序执行的刷新与等待指令。
 *
 * 设计原则：
 *     1. 固定列只使用 Tab 分隔，JSON 内部可以继续包含 Tab；
 *     2. 指令严格保留原文件顺序，使等待条件可以形成可靠的流程屏障；
 *     3. 单行错误只影响当前指令，不中断整个关卡。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>表示导演系统可以顺序执行的一条指令。</summary>
    public abstract class DirectorInstruction
    {
        /// <summary>指令在导演文本中的原始行号。</summary>
        public int SourceLine { get; private set; }

        /// <summary>创建带来源行号的导演指令。</summary>
        protected DirectorInstruction(int sourceLine) { SourceLine = sourceLine; }
    }

    /// <summary>表示一条按时间创建并初始化对象的指令。</summary>
    public sealed class SpawnRecord : DirectorInstruction
    {
        /// <summary>相对导演启动时刻的创建时间，单位为毫秒。</summary>
        public long CreateTime { get; private set; }
        /// <summary>Prefab 注册名称。</summary>
        public string CreateName { get; private set; }
        /// <summary>相对或世界出生坐标，具体空间由 SpawnController 配置。</summary>
        public Vector3 Position { get; private set; }
        /// <summary>对象所属分组；空字符串表示不追踪分组。</summary>
        public string Group { get; private set; }
        /// <summary>对象的组件与公开字段初始化配置。</summary>
        public JsonRoot Root { get; private set; }

        /// <summary>创建一条对象刷新指令。</summary>
        public SpawnRecord(long createTime, string createName, Vector3 position, string group, JsonRoot root, int sourceLine)
            : base(sourceLine)
        {
            CreateTime = createTime;
            CreateName = createName;
            Position = position;
            Group = group;
            Root = root;
        }
    }

    /// <summary>表示一条等待条件成立后才继续后续流程的指令。</summary>
    public sealed class WaitRecord : DirectorInstruction
    {
        /// <summary>条件关键字，不区分大小写。</summary>
        public string Condition { get; private set; }
        /// <summary>传递给条件求值器的只读参数。</summary>
        public IReadOnlyList<string> Arguments { get; private set; }

        /// <summary>创建一条条件等待指令。</summary>
        public WaitRecord(string condition, IReadOnlyList<string> arguments, int sourceLine) : base(sourceLine)
        {
            Condition = condition;
            Arguments = arguments;
        }
    }

    /// <summary>提供导演文本到顺序指令列表的转换能力。</summary>
    public static class SpawnConfigParser
    {
        private const char ColumnSeparator = '\t';
        private const char CommentPrefix = '#';
        private const char CommandPrefix = '$';
        private const int LegacyFixedColumnCount = 5;
        private const int GroupedFixedColumnCount = 6;

        /// <summary>解析完整导演文本，并保留全部有效指令的原始顺序。</summary>
        public static IReadOnlyList<DirectorInstruction> Parse(string text)
        {
            List<DirectorInstruction> instructions = new List<DirectorInstruction>();
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("导演文本为空，本关卡不会创建任何配置对象。");
                return instructions;
            }

            using (StringReader reader = new StringReader(text))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == CommentPrefix) continue;

                    DirectorInstruction instruction;
                    bool parsed = trimmed[0] == CommandPrefix
                        ? TryParseCommand(trimmed, lineNumber, out instruction)
                        : TryParseSpawn(line, lineNumber, out instruction);
                    if (parsed) instructions.Add(instruction);
                }
            }

            return instructions;
        }

        private static bool TryParseCommand(string line, int lineNumber, out DirectorInstruction instruction)
        {
            instruction = null;
            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !string.Equals(tokens[0], "$WAIT", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行指令非法。格式应为：$WAIT 条件 [参数...]。");
                return false;
            }

            string condition = NormalizeCondition(tokens[1]);
            List<string> arguments = new List<string>(tokens.Length - 2);
            for (int index = 2; index < tokens.Length; index++) arguments.Add(tokens[index]);
            instruction = new WaitRecord(condition, arguments, lineNumber);
            return true;
        }

        private static string NormalizeCondition(string condition)
        {
            string normalized = condition.Trim().ToUpperInvariant();
            // 兼容项目现有文本中的“Destory”拼写，并保留更直观的 Destroy 别名。
            if (normalized == "DESTORY" || normalized == "DESTROY" || normalized == "GROUP_DESTROYED")
            {
                return DirectorConditionNames.GroupEmpty;
            }

            return normalized;
        }

        private static bool TryParseSpawn(string line, int lineNumber, out DirectorInstruction instruction)
        {
            instruction = null;
            string[] columns;
            string json;
            string group;

            if (!TryExtractColumns(line, GroupedFixedColumnCount, out columns, out json) ||
                columns[5].TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                if (!TryExtractColumns(line, LegacyFixedColumnCount, out columns, out json))
                {
                    Debug.LogWarning("导演文本第 " + lineNumber + " 行列数不足，已跳过。");
                    return false;
                }

                group = string.Empty;
            }
            else
            {
                group = NormalizeGroup(columns[5]);
            }

            long createTime;
            float x;
            float y;
            float z;
            string createName = columns[1].Trim();
            if (!long.TryParse(columns[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out createTime) || createTime < 0)
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行的 createTime 非法，已跳过。");
                return false;
            }

            if (createName.Length == 0)
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行的 createName 为空，已跳过。");
                return false;
            }

            if (!TryParseFiniteFloat(columns[2], out x) || !TryParseFiniteFloat(columns[3], out y) ||
                !TryParseFiniteFloat(columns[4], out z))
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行的出生坐标非法，已跳过。");
                return false;
            }

            JsonRoot root = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { root = JsonParser.Parse(json); }
                catch (FormatException exception)
                {
                    Debug.LogError("导演文本第 " + lineNumber + " 行 JSON 解析失败；对象仍会创建，但会跳过参数初始化。原因：" + exception.Message);
                }
            }

            instruction = new SpawnRecord(createTime, createName, new Vector3(x, y, z), group, root, lineNumber);
            return true;
        }

        private static string NormalizeGroup(string group)
        {
            string normalized = group.Trim();
            return normalized == "-" ? string.Empty : normalized;
        }

        private static bool TryExtractColumns(string line, int fixedColumnCount, out string[] columns, out string remainder)
        {
            columns = new string[fixedColumnCount];
            remainder = null;
            int columnStart = 0;
            for (int columnIndex = 0; columnIndex < fixedColumnCount; columnIndex++)
            {
                int separatorIndex = line.IndexOf(ColumnSeparator, columnStart);
                if (separatorIndex < 0) return false;
                columns[columnIndex] = line.Substring(columnStart, separatorIndex - columnStart);
                columnStart = separatorIndex + 1;
            }

            remainder = line.Substring(columnStart).Trim();
            return true;
        }

        private static bool TryParseFiniteFloat(string value, out float result)
        {
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                   !float.IsNaN(result) && !float.IsInfinity(result);
        }
    }
}
