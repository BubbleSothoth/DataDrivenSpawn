/******************************************************************************
 * 文件名称：BuiltInTypeConverters.cs
 *
 * 功能描述：
 *     提供需求规定的基础类型、Unity 结构、枚举和 GameObject 转换器。
 *
 * 设计原则：
 *     1. 每类转换职责独立，ComponentInitializer 不感知类型细节；
 *     2. 所有数值使用固定区域格式，避免设备语言影响结果；
 *     3. 转换失败抛出含上下文的异常，由上层隔离到单个字段。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 转换 bool、int、long、float 和 double 基础值。
    /// </summary>
    [Preserve]
    public sealed class PrimitiveTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标类型是否为框架要求支持的基础值类型。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>支持目标类型时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(bool) || type == typeof(int) || type == typeof(long) ||
                   type == typeof(float) || type == typeof(double);
        }

        /// <summary>
        /// 使用固定区域格式转换基础值，并拒绝不合法的布尔值或 null 值类型。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>完成转换的基础值。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            if (jsonData == null)
            {
                throw new InvalidCastException("null 不能转换为值类型“" + targetType.Name + "”。");
            }

            try
            {
                if (targetType == typeof(bool))
                {
                    bool booleanValue;
                    if (jsonData is bool) return jsonData;
                    if (jsonData is string && bool.TryParse((string)jsonData, out booleanValue)) return booleanValue;
                    throw new InvalidCastException("布尔值必须是 true 或 false。");
                }

                return System.Convert.ChangeType(jsonData, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw new InvalidCastException(
                    "数据“" + JsonValueFormatter.Format(jsonData) + "”无法转换为“" + targetType.Name + "”。",
                    exception);
            }
        }
    }

    /// <summary>
    /// 将 JSON 字符串转换为 C# string。
    /// </summary>
    [Preserve]
    public sealed class StringTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 string。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 string 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(string);
        }

        /// <summary>
        /// 将 JSON 字符串原样返回，并保留合法的 null。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>字符串或 null。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            if (jsonData == null) return null;
            string stringValue = jsonData as string;
            if (stringValue == null)
            {
                throw new InvalidCastException("string 字段的 Data 必须是 JSON 字符串。");
            }

            return stringValue;
        }
    }

    /// <summary>
    /// 将包含 x、y 的 JSON 对象转换为 Vector2。
    /// </summary>
    [Preserve]
    public sealed class Vector2TypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 Vector2。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 Vector2 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(Vector2);
        }

        /// <summary>
        /// 读取 JSON 对象的 x、y 数值并创建 Vector2。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>转换完成的 Vector2。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            Dictionary<string, object> data = JsonObjectReader.RequireObject(jsonData, targetType);
            return new Vector2(
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.X),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.Y));
        }
    }

    /// <summary>
    /// 将包含 x、y、z 的 JSON 对象转换为 Vector3。
    /// </summary>
    [Preserve]
    public sealed class Vector3TypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 Vector3。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 Vector3 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(Vector3);
        }

        /// <summary>
        /// 读取 JSON 对象的 x、y、z 数值并创建 Vector3。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>转换完成的 Vector3。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            Dictionary<string, object> data = JsonObjectReader.RequireObject(jsonData, targetType);
            return new Vector3(
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.X),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.Y),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.Z));
        }
    }

    /// <summary>
    /// 将包含欧拉角 x、y、z 的 JSON 对象转换为 Quaternion。
    /// </summary>
    [Preserve]
    public sealed class QuaternionTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 Quaternion。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 Quaternion 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(Quaternion);
        }

        /// <summary>
        /// 将 JSON 对象的 x、y、z 作为欧拉角创建 Quaternion。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>由欧拉角创建的 Quaternion。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            Dictionary<string, object> data = JsonObjectReader.RequireObject(jsonData, targetType);
            Vector3 eulerAngles = new Vector3(
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.X),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.Y),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.Z));

            // 配置约定传入欧拉角，因此必须通过 Euler 构造，不能误将三个值当作四元数分量。
            return Quaternion.Euler(eulerAngles);
        }
    }

    /// <summary>
    /// 将包含 r、g、b、a 的 JSON 对象转换为 Color；a 缺省时使用 1。
    /// </summary>
    [Preserve]
    public sealed class ColorTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 Color。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 Color 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(Color);
        }

        /// <summary>
        /// 读取 JSON 对象的颜色通道并创建 Color，透明度缺省时使用 1。
        /// </summary>
        /// <param name="jsonData">JSON 原始数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>转换完成的 Color。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            Dictionary<string, object> data = JsonObjectReader.RequireObject(jsonData, targetType);
            return new Color(
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.R),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.G),
                JsonObjectReader.GetFloat(data, JsonStructurePropertyNames.B),
                JsonObjectReader.GetOptionalFloat(data, JsonStructurePropertyNames.A, 1f));
        }
    }

    /// <summary>
    /// 根据 Prefab 注册名称解析 GameObject 引用，禁止隐式使用 Resources.Load。
    /// </summary>
    [Preserve]
    public sealed class GameObjectTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为 GameObject。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为 GameObject 时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type == typeof(GameObject);
        }

        /// <summary>
        /// 使用 JSON 字符串从 Prefab 注册表中查询 GameObject 引用。
        /// </summary>
        /// <param name="jsonData">Prefab 注册名称。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">Prefab 注册表。</param>
        /// <returns>注册表中的 GameObject。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            string prefabName = jsonData as string;
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                throw new InvalidCastException("GameObject 字段的 Data 必须是非空 Prefab 名称字符串。");
            }

            if (prefabDictionary == null)
            {
                throw new InvalidOperationException("Prefab 注册表尚未建立。");
            }

            GameObject prefab;
            if (!prefabDictionary.TryGetValue(prefabName, out prefab) || prefab == null)
            {
                throw new KeyNotFoundException("Prefab 注册表中不存在名称“" + prefabName + "”。");
            }

            return prefab;
        }
    }

    /// <summary>
    /// 将 JSON 字符串按枚举成员名称转换为任意 Enum 类型。
    /// </summary>
    [Preserve]
    public sealed class EnumTypeConverter : ITypeConverter
    {
        /// <summary>
        /// 判断目标字段是否为任意枚举类型。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>目标类型为枚举时返回 true。</returns>
        public bool CanConvert(Type type)
        {
            return type != null && type.IsEnum;
        }

        /// <summary>
        /// 按不区分大小写的成员名称解析枚举值。
        /// </summary>
        /// <param name="jsonData">枚举成员名称。</param>
        /// <param name="targetType">目标枚举类型。</param>
        /// <param name="prefabDictionary">当前转换不使用的 Prefab 注册表。</param>
        /// <returns>解析完成的枚举值。</returns>
        public object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary)
        {
            string enumName = jsonData as string;
            if (string.IsNullOrWhiteSpace(enumName))
            {
                throw new InvalidCastException("Enum 字段的 Data 必须是非空枚举成员名称字符串。");
            }

            try
            {
                return Enum.Parse(targetType, enumName, true);
            }
            catch (Exception exception)
            {
                throw new InvalidCastException(
                    "“" + enumName + "”不是枚举“" + targetType.Name + "”的有效成员。",
                    exception);
            }
        }
    }

    /// <summary>
    /// 定义向量、旋转及颜色对象的结构属性名称，避免转换器散落魔法字符串。
    /// </summary>
    internal static class JsonStructurePropertyNames
    {
        internal const string X = "x";
        internal const string Y = "y";
        internal const string Z = "z";
        internal const string R = "r";
        internal const string G = "g";
        internal const string B = "b";
        internal const string A = "a";
    }

    /// <summary>
    /// 为 Unity 结构转换器提供 JSON 对象成员读取和数值校验能力。
    /// </summary>
    internal static class JsonObjectReader
    {
        public static Dictionary<string, object> RequireObject(object jsonData, Type targetType)
        {
            Dictionary<string, object> result = jsonData as Dictionary<string, object>;
            if (result == null)
            {
                throw new InvalidCastException("类型“" + targetType.Name + "”的 Data 必须是 JSON 对象。");
            }

            return result;
        }

        public static float GetFloat(Dictionary<string, object> data, string propertyName)
        {
            object value;
            if (!data.TryGetValue(propertyName, out value))
            {
                throw new InvalidCastException("JSON 对象缺少数值属性“" + propertyName + "”。");
            }

            return ConvertToFiniteFloat(value, propertyName);
        }

        public static float GetOptionalFloat(Dictionary<string, object> data, string propertyName, float defaultValue)
        {
            object value;
            return data.TryGetValue(propertyName, out value) ? ConvertToFiniteFloat(value, propertyName) : defaultValue;
        }

        private static float ConvertToFiniteFloat(object value, string propertyName)
        {
            try
            {
                float result = System.Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsNaN(result) || float.IsInfinity(result)) throw new OverflowException();
                return result;
            }
            catch (Exception exception)
            {
                throw new InvalidCastException("属性“" + propertyName + "”必须是有效有限数值。", exception);
            }
        }
    }

    /// <summary>
    /// 生成适合错误日志的简短 JSON 值文本。
    /// </summary>
    internal static class JsonValueFormatter
    {
        public static string Format(object value)
        {
            if (value == null) return "null";
            IFormattable formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }
    }
}
