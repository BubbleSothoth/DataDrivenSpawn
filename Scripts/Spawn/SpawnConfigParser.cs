/******************************************************************************
 * 文件名称：SpawnConfigParser.cs
 *
 * 功能描述：
 *     将导演文本解析为按原文件顺序执行的刷新、等待与状态控制指令。
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

    /// <summary>表示一条重设导演时间的立即执行指令。</summary>
    public sealed class ResetTimerRecord : DirectorInstruction
    {
        /// <summary>重设后的导演时间，单位为毫秒。</summary>
        public long Milliseconds { get; private set; }

        /// <summary>创建一条重设导演时间指令。</summary>
        public ResetTimerRecord(long milliseconds, int sourceLine) : base(sourceLine)
        {
            Milliseconds = milliseconds;
        }
    }

    /// <summary>表示一条解除已有对象分组归属的立即执行指令。</summary>
    public sealed class RemoveGroupRecord : DirectorInstruction
    {
        /// <summary>需要移除的分组名称。</summary>
        public string Group { get; private set; }

        /// <summary>创建一条移除分组指令。</summary>
        public RemoveGroupRecord(string group, int sourceLine) : base(sourceLine)
        {
            Group = group;
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
            if (tokens.Length == 0)
            {
                return false;
            }

            if (string.Equals(tokens[0], "$WAIT", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseWait(tokens, lineNumber, out instruction);
            }

            if (string.Equals(tokens[0], "$RESET", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseReset(tokens, lineNumber, out instruction);
            }

            if (string.Equals(tokens[0], "$REMOVE", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseRemove(tokens, lineNumber, out instruction);
            }

            Debug.LogWarning("导演文本第 " + lineNumber + " 行指令非法，已跳过。");
            return false;
        }

        private static bool TryParseWait(string[] tokens, int lineNumber, out DirectorInstruction instruction)
        {
            instruction = null;
            if (tokens.Length < 2)
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行 WAIT 指令缺少条件，已跳过。");
                return false;
            }

            string keyword = tokens[1].Trim().ToUpperInvariant();
            int argumentStart = 2;
            if (keyword == "TIMER")
            {
                if (tokens.Length < 3)
                {
                    Debug.LogWarning("导演文本第 " + lineNumber + " 行 TIMER 指令缺少 UNTIL 或 SLEEP，已跳过。");
                    return false;
                }

                string mode = tokens[2].Trim().ToUpperInvariant();
                if (mode == "UNTIL") keyword = DirectorConditionNames.TimerUntil;
                else if (mode == "SLEEP") keyword = DirectorConditionNames.TimerSleep;
                else
                {
                    Debug.LogWarning("导演文本第 " + lineNumber + " 行 TIMER 模式非法，应为 UNTIL 或 SLEEP，已跳过。");
                    return false;
                }

                argumentStart = 3;
            }
            else if (keyword == "DESTORY" || keyword == "DESTROY" || keyword == "GROUP_EMPTY" ||
                     keyword == "GROUP_DESTROYED")
            {
                // GROUP_EMPTY 等旧拼写继续解析，但统一路由到新规范关键字 DESTORY。
                keyword = DirectorConditionNames.Destory;
            }
            else if (keyword == "TIME")
            {
                // 兼容旧的绝对时间等待指令。
                keyword = DirectorConditionNames.TimerUntil;
            }

            List<string> arguments = new List<string>(tokens.Length - argumentStart);
            for (int index = argumentStart; index < tokens.Length; index++) arguments.Add(tokens[index]);
            instruction = new WaitRecord(keyword, arguments, lineNumber);
            return true;
        }

        private static bool TryParseReset(string[] tokens, int lineNumber, out DirectorInstruction instruction)
        {
            instruction = null;
            if ((tokens.Length != 2 && tokens.Length != 3) ||
                !string.Equals(tokens[1], "TIMER", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行 RESET 指令非法。格式应为：$RESET TIMER [ms_time]。");
                return false;
            }

            long milliseconds = 0;
            if (tokens.Length == 3 &&
                (!long.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds) ||
                 milliseconds < 0))
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行 RESET TIMER 参数必须是非负毫秒整数，已跳过。");
                return false;
            }

            instruction = new ResetTimerRecord(milliseconds, lineNumber);
            return true;
        }

        private static bool TryParseRemove(string[] tokens, int lineNumber, out DirectorInstruction instruction)
        {
            instruction = null;
            if (tokens.Length != 3 || !string.Equals(tokens[1], "GROUP", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(tokens[2]) || tokens[2] == "-")
            {
                Debug.LogWarning("导演文本第 " + lineNumber + " 行 REMOVE 指令非法。格式应为：$REMOVE GROUP GROUP_NAME。");
                return false;
            }

            instruction = new RemoveGroupRecord(tokens[2].Trim(), lineNumber);
            return true;
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
