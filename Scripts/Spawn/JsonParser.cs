/******************************************************************************
 * 文件名称：JsonParser.cs
 *
 * 功能描述：
 *     将标准 JSON 文本解析为通用对象树，并映射为对象初始化数据模型。
 *
 * 设计原则：
 *     1. 不依赖第三方 JSON 包，放入 Unity 工程即可使用；
 *     2. 数字保留整数或浮点语义，最终类型交由 TypeConverter 推导；
 *     3. 严格校验配置结构，使单个对象的错误不会污染后续刷新数据。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 提供初始化 JSON 的解析和结构校验能力。
    /// </summary>
    public static class JsonParser
    {
        private const int MaximumDepth = 128;

        /// <summary>
        /// 将 JSON 文本解析为强类型的对象初始化配置。
        /// </summary>
        /// <param name="json">待解析的标准 JSON 文本。</param>
        /// <returns>经过结构校验的初始化配置。</returns>
        /// <exception cref="FormatException">JSON 语法或配置结构不合法时抛出。</exception>
        public static JsonRoot Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("初始化 JSON 不能为空。");
            }

            Parser parser = new Parser(json);
            object rootValue = parser.ParseDocument();
            Dictionary<string, object> rootObject = RequireObject(rootValue, "JSON 根节点");
            object componentValue = RequireProperty(rootObject, JsonPropertyNames.ComponentVar, "JSON 根节点");
            List<object> componentArray = RequireArray(componentValue, JsonPropertyNames.ComponentVar);
            List<JsonComponentData> components = new List<JsonComponentData>(componentArray.Count);

            for (int componentIndex = 0; componentIndex < componentArray.Count; componentIndex++)
            {
                string context = JsonPropertyNames.ComponentVar + "[" + componentIndex + "]";
                Dictionary<string, object> componentObject = RequireObject(componentArray[componentIndex], context);
                string componentName = RequireNonEmptyString(
                    RequireProperty(componentObject, JsonPropertyNames.Component, context),
                    context + "." + JsonPropertyNames.Component);

                List<JsonVariableData> variables = ParseVariables(componentObject, context);
                components.Add(new JsonComponentData(componentName, variables));
            }

            return new JsonRoot(components);
        }

        private static List<JsonVariableData> ParseVariables(Dictionary<string, object> componentObject, string context)
        {
            object variablesValue;
            if (!componentObject.TryGetValue(JsonPropertyNames.Variables, out variablesValue))
            {
                return new List<JsonVariableData>();
            }

            List<object> variableArray = RequireArray(variablesValue, context + "." + JsonPropertyNames.Variables);
            List<JsonVariableData> variables = new List<JsonVariableData>(variableArray.Count);

            for (int variableIndex = 0; variableIndex < variableArray.Count; variableIndex++)
            {
                string variableContext = context + "." + JsonPropertyNames.Variables + "[" + variableIndex + "]";
                Dictionary<string, object> variableObject = RequireObject(variableArray[variableIndex], variableContext);
                string fieldName = RequireNonEmptyString(
                    RequireProperty(variableObject, JsonPropertyNames.Name, variableContext),
                    variableContext + "." + JsonPropertyNames.Name);
                object data = RequireProperty(variableObject, JsonPropertyNames.Data, variableContext);
                variables.Add(new JsonVariableData(fieldName, data));
            }

            return variables;
        }

        private static object RequireProperty(Dictionary<string, object> source, string propertyName, string context)
        {
            object value;
            if (!source.TryGetValue(propertyName, out value))
            {
                throw new FormatException(context + " 缺少必需属性“" + propertyName + "”。");
            }

            return value;
        }

        private static Dictionary<string, object> RequireObject(object value, string context)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null)
            {
                throw new FormatException(context + " 必须是 JSON 对象。");
            }

            return result;
        }

        private static List<object> RequireArray(object value, string context)
        {
            List<object> result = value as List<object>;
            if (result == null)
            {
                throw new FormatException(context + " 必须是 JSON 数组。");
            }

            return result;
        }

        private static string RequireNonEmptyString(object value, string context)
        {
            string result = value as string;
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new FormatException(context + " 必须是非空字符串。");
            }

            return result.Trim();
        }

        /// <summary>
        /// 使用游标递归下降解析 JSON。游标只向前移动，解析复杂度为 O(n)。
        /// </summary>
        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public object ParseDocument()
            {
                SkipWhitespace();
                object value = ParseValue(0);
                SkipWhitespace();

                if (index != json.Length)
                {
                    throw Error("根节点结束后存在多余字符");
                }

                return value;
            }

            private object ParseValue(int depth)
            {
                if (depth > MaximumDepth)
                {
                    throw Error("JSON 嵌套层级超过限制");
                }

                SkipWhitespace();
                if (index >= json.Length)
                {
                    throw Error("值不完整");
                }

                char current = json[index];
                switch (current)
                {
                    case '{':
                        return ParseObject(depth + 1);
                    case '[':
                        return ParseArray(depth + 1);
                    case '"':
                        return ParseString();
                    case 't':
                        ReadLiteral("true");
                        return true;
                    case 'f':
                        ReadLiteral("false");
                        return false;
                    case 'n':
                        ReadLiteral("null");
                        return null;
                    default:
                        if (current == '-' || (current >= '0' && current <= '9'))
                        {
                            return ParseNumber();
                        }

                        throw Error("无法识别的值起始字符");
                }
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                index++;
                SkipWhitespace();

                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (index >= json.Length || json[index] != '"')
                    {
                        throw Error("对象属性名必须是字符串");
                    }

                    string name = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    object value = ParseValue(depth);

                    if (result.ContainsKey(name))
                    {
                        throw Error("对象包含重复属性“" + name + "”");
                    }

                    result.Add(name, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray(int depth)
            {
                List<object> result = new List<object>();
                index++;
                SkipWhitespace();

                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                StringBuilder builder = new StringBuilder();
                index++;

                while (index < json.Length)
                {
                    char current = json[index++];
                    if (current == '"')
                    {
                        return builder.ToString();
                    }

                    if (current == '\\')
                    {
                        builder.Append(ParseEscapeSequence());
                        continue;
                    }

                    if (current < 0x20)
                    {
                        throw Error("字符串包含未转义的控制字符");
                    }

                    builder.Append(current);
                }

                throw Error("字符串缺少结束引号");
            }

            private char ParseEscapeSequence()
            {
                if (index >= json.Length)
                {
                    throw Error("转义序列不完整");
                }

                char escaped = json[index++];
                switch (escaped)
                {
                    case '"': return '"';
                    case '\\': return '\\';
                    case '/': return '/';
                    case 'b': return '\b';
                    case 'f': return '\f';
                    case 'n': return '\n';
                    case 'r': return '\r';
                    case 't': return '\t';
                    case 'u': return ParseUnicodeEscape();
                    default: throw Error("不支持的转义字符");
                }
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    throw Error("Unicode 转义序列不完整");
                }

                int code = 0;
                for (int count = 0; count < 4; count++)
                {
                    char value = json[index++];
                    code <<= 4;
                    if (value >= '0' && value <= '9') code += value - '0';
                    else if (value >= 'a' && value <= 'f') code += value - 'a' + 10;
                    else if (value >= 'A' && value <= 'F') code += value - 'A' + 10;
                    else throw Error("Unicode 转义序列包含非法字符");
                }

                return (char)code;
            }

            private object ParseNumber()
            {
                int start = index;
                if (json[index] == '-') index++;

                if (index >= json.Length)
                {
                    throw Error("数字不完整");
                }

                if (json[index] == '0')
                {
                    index++;
                }
                else
                {
                    ReadDigits(true);
                }

                bool floatingPoint = false;
                if (index < json.Length && json[index] == '.')
                {
                    floatingPoint = true;
                    index++;
                    ReadDigits(true);
                }

                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    floatingPoint = true;
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                    ReadDigits(true);
                }

                string number = json.Substring(start, index - start);
                long integerValue;
                if (!floatingPoint && long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue))
                {
                    return integerValue;
                }

                double floatingValue;
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out floatingValue) &&
                    !double.IsNaN(floatingValue) && !double.IsInfinity(floatingValue))
                {
                    return floatingValue;
                }

                throw Error("数字格式非法");
            }

            private void ReadDigits(bool requireAtLeastOne)
            {
                int start = index;
                while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                if (requireAtLeastOne && index == start)
                {
                    throw Error("数字缺少必要的数字位");
                }
            }

            private void ReadLiteral(string literal)
            {
                if (index + literal.Length > json.Length ||
                    string.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
                {
                    throw Error("非法字面量");
                }

                index += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length)
                {
                    char current = json[index];
                    if (current != ' ' && current != '\t' && current != '\r' && current != '\n') return;
                    index++;
                }
            }

            private bool TryConsume(char expected)
            {
                if (index >= json.Length || json[index] != expected) return false;
                index++;
                return true;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (!TryConsume(expected))
                {
                    throw Error("缺少字符“" + expected + "”");
                }
            }

            private FormatException Error(string message)
            {
                return new FormatException(message + "，位置：" + index + "。");
            }
        }
    }
}
