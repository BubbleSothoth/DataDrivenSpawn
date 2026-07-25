/******************************************************************************
 * 文件名称：TypeConverter.cs
 *
 * 功能描述：
 *     定义统一类型转换接口，并将 JSON 原始数据路由到注册的转换器。
 *
 * 设计原则：
 *     1. ComponentInitializer 不判断任何字段类型；
 *     2. 转换器通过接口注册，新增类型不影响核心业务模块；
 *     3. 自动发现转换器，同时保留显式注册接口以支持运行期扩展。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 定义从 JSON 通用数据到目标字段类型的转换契约。
    /// </summary>
    public interface ITypeConverter
    {
        /// <summary>
        /// 判断当前转换器是否支持目标字段类型。
        /// </summary>
        /// <param name="type">目标字段类型。</param>
        /// <returns>支持时返回 true。</returns>
        bool CanConvert(Type type);

        /// <summary>
        /// 将 JSON 原始数据转换为目标字段值。
        /// </summary>
        /// <param name="jsonData">JSON 解析后的通用数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">供 GameObject 等引用类型使用的 Prefab 注册表。</param>
        /// <returns>可直接通过 FieldInfo 写入的字段值。</returns>
        object Convert(object jsonData, Type targetType, Dictionary<string, GameObject> prefabDictionary);
    }

    /// <summary>
    /// 维护类型转换器注册表，并为每种目标类型缓存最终匹配的转换器。
    /// </summary>
    public static class TypeConverter
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<ITypeConverter> Converters = new List<ITypeConverter>();
        private static readonly Dictionary<Type, ITypeConverter> ConverterCache = new Dictionary<Type, ITypeConverter>();
        private static readonly HashSet<Type> MissingConverterTypes = new HashSet<Type>();
        private static bool initialized;

        /// <summary>
        /// 将 JSON 原始数据转换为目标字段类型。
        /// </summary>
        /// <param name="jsonData">JSON 解析后的通用数据。</param>
        /// <param name="targetType">目标字段类型。</param>
        /// <param name="prefabDictionary">Prefab 注册表。</param>
        /// <returns>转换后的字段值。</returns>
        /// <exception cref="NotSupportedException">没有转换器支持目标类型时抛出。</exception>
        public static object Convert(
            object jsonData,
            Type targetType,
            Dictionary<string, GameObject> prefabDictionary)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            EnsureInitialized();

            ITypeConverter converter;
            lock (SyncRoot)
            {
                if (!ConverterCache.TryGetValue(targetType, out converter))
                {
                    if (MissingConverterTypes.Contains(targetType))
                    {
                        throw new NotSupportedException("没有类型转换器支持字段类型“" + targetType.FullName + "”。");
                    }

                    converter = FindConverter(targetType);
                    if (converter == null)
                    {
                        MissingConverterTypes.Add(targetType);
                        throw new NotSupportedException("没有类型转换器支持字段类型“" + targetType.FullName + "”。");
                    }

                    ConverterCache.Add(targetType, converter);
                }
            }

            return converter.Convert(jsonData, targetType, prefabDictionary);
        }

        /// <summary>
        /// 显式注册一个转换器。后注册的转换器优先，可用于覆盖框架默认行为。
        /// </summary>
        /// <param name="converter">无状态或可安全复用的转换器实例。</param>
        public static void Register(ITypeConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            EnsureInitialized();

            lock (SyncRoot)
            {
                Type converterType = converter.GetType();
                for (int index = Converters.Count - 1; index >= 0; index--)
                {
                    if (Converters[index].GetType() == converterType)
                    {
                        Converters.RemoveAt(index);
                    }
                }

                // 显式注册代表调用方有确定的覆盖意图，因此移动到首位并清空路由结果。
                Converters.Insert(0, converter);
                ConverterCache.Clear();
                MissingConverterTypes.Clear();
            }
        }

        /// <summary>
        /// 清空转换器状态，并在下次转换时重新扫描当前应用域中的实现。
        /// </summary>
        public static void Reset()
        {
            lock (SyncRoot)
            {
                Converters.Clear();
                ConverterCache.Clear();
                MissingConverterTypes.Clear();
                initialized = false;
            }
        }

        private static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (initialized) return;

                // 转换器按类型自动发现，因此新增 ITypeConverter 实现后无需修改本类或初始化业务。
                Type interfaceType = typeof(ITypeConverter);
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
                {
                    Type[] types = GetLoadableTypes(assemblies[assemblyIndex]);
                    for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                    {
                        Type candidate = types[typeIndex];
                        if (candidate == null || candidate.IsAbstract || candidate.IsInterface ||
                            candidate.ContainsGenericParameters || !interfaceType.IsAssignableFrom(candidate) ||
                            candidate.GetConstructor(Type.EmptyTypes) == null)
                        {
                            continue;
                        }

                        try
                        {
                            Converters.Add((ITypeConverter)Activator.CreateInstance(candidate));
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("类型转换器“" + candidate.FullName + "”创建失败，已跳过。原因：" + exception.Message);
                        }
                    }
                }

                initialized = true;
            }
        }

        private static ITypeConverter FindConverter(Type targetType)
        {
            for (int index = 0; index < Converters.Count; index++)
            {
                if (Converters[index].CanConvert(targetType)) return Converters[index];
            }

            return null;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                List<Type> result = new List<Type>();
                for (int index = 0; index < exception.Types.Length; index++)
                {
                    if (exception.Types[index] != null) result.Add(exception.Types[index]);
                }

                return result.ToArray();
            }
            catch
            {
                // 转换器扫描面向全部程序集，无法读取的第三方程序集不应影响其余有效转换器。
                return new Type[0];
            }
        }
    }
}
