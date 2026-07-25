/******************************************************************************
 * 文件名称：SpawnConfigParser.cs
 *
 * 功能描述：
 *     解析刷新文本的固定列和剩余 JSON，生成按时间排序的刷新记录。
 *
 * 设计原则：
 *     1. 将文本及 JSON 解析职责从 SpawnController 中彻底分离；
 *     2. 只识别前五个制表符，确保 JSON 内部允许继续包含 Tab；
 *     3. 单行错误仅影响当前记录，不阻断整个关卡。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 表示已经解析完成、等待按时间创建的单条刷新记录。
    /// </summary>
    public sealed class SpawnRecord
    {
        /// <summary>相对关卡开始时间的创建时刻，单位为毫秒。</summary>
        public long CreateTime { get; private set; }

        /// <summary>Prefab 注册名称。</summary>
        public string CreateName { get; private set; }

        /// <summary>对象的世界坐标出生位置。</summary>
        public Vector3 Position { get; private set; }

        /// <summary>
        /// 对象初始化配置。当前行 JSON 非法时为 null，此时对象仍会创建但跳过初始化。
        /// </summary>
        public JsonRoot Root { get; private set; }

        /// <summary>记录在刷新文本中的原始行号，用于稳定排序和错误定位。</summary>
        public int SourceLine { get; private set; }

        /// <summary>
        /// 创建一条已解析的刷新记录。
        /// </summary>
        /// <param name="createTime">创建时刻，单位为毫秒。</param>
        /// <param name="createName">Prefab 注册名称。</param>
        /// <param name="position">世界坐标出生位置。</param>
        /// <param name="root">对象初始化配置，解析失败时为 null。</param>
        /// <param name="sourceLine">原始文本行号。</param>
        public SpawnRecord(long createTime, string createName, Vector3 position, JsonRoot root, int sourceLine)
        {
            CreateTime = createTime;
            CreateName = createName;
            Position = position;
            Root = root;
            SourceLine = sourceLine;
        }
    }

    /// <summary>
    /// 提供刷新文本到刷新记录列表的转换能力。
    /// </summary>
    public static class SpawnConfigParser
    {
        private const int FixedColumnCount = 5;
        private const char ColumnSeparator = '\t';
        private const char CommentPrefix = '#';

        /// <summary>
        /// 解析完整刷新文本，并按创建时间和原始行号排序。
        /// </summary>
        /// <param name="text">刷新文本内容。</param>
        /// <returns>有效固定列对应的刷新记录；JSON 错误会保留记录并将 Root 设为 null。</returns>
        public static IReadOnlyList<SpawnRecord> Parse(string text)
        {
            List<SpawnRecord> records = new List<SpawnRecord>();
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("刷新文本为空，本关卡不会创建任何配置对象。");
                return records;
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

                    SpawnRecord record;
                    if (TryParseLine(line, lineNumber, out record))
                    {
                        records.Add(record);
                    }
                }
            }

            // 时间相同时按原始行号排序，保证配置顺序在不同运行环境中完全一致。
            records.Sort(delegate(SpawnRecord left, SpawnRecord right)
            {
                int timeComparison = left.CreateTime.CompareTo(right.CreateTime);
                return timeComparison != 0 ? timeComparison : left.SourceLine.CompareTo(right.SourceLine);
            });

            return records;
        }

        private static bool TryParseLine(string line, int lineNumber, out SpawnRecord record)
        {
            record = null;
            string[] fixedColumns;
            string json;
            if (!TryExtractColumns(line, out fixedColumns, out json))
            {
                Debug.LogWarning("刷新文本第 " + lineNumber + " 行列数不足，已跳过。前五列必须使用 Tab 分隔。");
                return false;
            }

            long createTime;
            float x;
            float y;
            float z;
            string createName = fixedColumns[1].Trim();
            if (!long.TryParse(fixedColumns[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out createTime) || createTime < 0)
            {
                Debug.LogWarning("刷新文本第 " + lineNumber + " 行的 createTime 非法，已跳过。");
                return false;
            }

            if (createName.Length == 0)
            {
                Debug.LogWarning("刷新文本第 " + lineNumber + " 行的 createName 为空，已跳过。");
                return false;
            }

            if (!TryParseFiniteFloat(fixedColumns[2], out x) ||
                !TryParseFiniteFloat(fixedColumns[3], out y) ||
                !TryParseFiniteFloat(fixedColumns[4], out z))
            {
                Debug.LogWarning("刷新文本第 " + lineNumber + " 行的出生坐标非法，已跳过。");
                return false;
            }

            JsonRoot root = null;
            try
            {
                // 每条记录在读取阶段只解析一次，刷新时直接复用结果，避免运行期重复 JSON 开销。
                root = JsonParser.Parse(json);
            }
            catch (FormatException exception)
            {
                Debug.LogError("刷新文本第 " + lineNumber + " 行 JSON 解析失败；对象仍会按时创建，但会放弃该对象初始化。原因：" + exception.Message);
            }

            record = new SpawnRecord(createTime, createName, new Vector3(x, y, z), root, lineNumber);
            return true;
        }

        private static bool TryExtractColumns(string line, out string[] fixedColumns, out string json)
        {
            fixedColumns = new string[FixedColumnCount];
            json = null;
            int columnStart = 0;

            // 仅消费前五个分隔符，之后的完整字符串全部属于 JSON，内部 Tab 不会被破坏。
            for (int columnIndex = 0; columnIndex < FixedColumnCount; columnIndex++)
            {
                int separatorIndex = line.IndexOf(ColumnSeparator, columnStart);
                if (separatorIndex < 0) return false;

                fixedColumns[columnIndex] = line.Substring(columnStart, separatorIndex - columnStart);
                columnStart = separatorIndex + 1;
            }

            json = line.Substring(columnStart).Trim();
            return true;
        }

        private static bool TryParseFiniteFloat(string value, out float result)
        {
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                   !float.IsNaN(result) && !float.IsInfinity(result);
        }
    }
}
