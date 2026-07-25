/******************************************************************************
 * 文件名称：ReflectionCache.cs
 *
 * 功能描述：
 *     集中完成 Component 类型发现和公开字段信息缓存。
 *
 * 设计原则：
 *     1. 反射细节不进入刷新或初始化业务模块；
 *     2. 每种 Component 的字段集合只反射一次；
 *     3. 自动扫描已加载程序集，新增 Component 无需注册或修改框架代码。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 提供 Component 类型解析与公开实例字段的共享反射缓存。
    /// </summary>
    public static class ReflectionCache
    {
        private const BindingFlags PublicInstanceFieldFlags = BindingFlags.Instance | BindingFlags.Public;
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Type> ComponentTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> MissingComponentTypes = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> Fields =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();

        /// <summary>
        /// 根据完整类型名或简单类型名查找可实例化的 Unity Component 类型。
        /// </summary>
        /// <param name="componentName">JSON 中配置的 Component 类型名。</param>
        /// <returns>匹配且可添加到 GameObject 的类型；未找到或名称歧义时返回 null。</returns>
        public static Type FindComponentType(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName)) return null;
            string normalizedName = componentName.Trim();

            lock (SyncRoot)
            {
                Type cachedType;
                if (ComponentTypes.TryGetValue(normalizedName, out cachedType)) return cachedType;
                if (MissingComponentTypes.Contains(normalizedName)) return null;

                Type resolvedType = ResolveComponentType(normalizedName);
                if (resolvedType == null) MissingComponentTypes.Add(normalizedName);
                else ComponentTypes.Add(normalizedName, resolvedType);
                return resolvedType;
            }
        }

        /// <summary>
        /// 获取指定类型的公开实例字段；首次访问时建立缓存，后续直接进行字典查询。
        /// </summary>
        /// <param name="componentType">目标 Component 类型。</param>
        /// <param name="fieldName">JSON 中配置的字段名称。</param>
        /// <returns>匹配的公开实例字段；不存在时返回 null。</returns>
        public static FieldInfo FindPublicField(Type componentType, string fieldName)
        {
            if (componentType == null || string.IsNullOrWhiteSpace(fieldName)) return null;

            lock (SyncRoot)
            {
                Dictionary<string, FieldInfo> typeFields;
                if (!Fields.TryGetValue(componentType, out typeFields))
                {
                    typeFields = BuildFieldCache(componentType);
                    Fields.Add(componentType, typeFields);
                }

                FieldInfo field;
                return typeFields.TryGetValue(fieldName.Trim(), out field) ? field : null;
            }
        }

        /// <summary>
        /// 清空全部反射结果。通常仅在编辑器热重载测试或动态加载程序集后使用。
        /// </summary>
        public static void Clear()
        {
            lock (SyncRoot)
            {
                ComponentTypes.Clear();
                MissingComponentTypes.Clear();
                Fields.Clear();
            }
        }

        private static Type ResolveComponentType(string componentName)
        {
            Type simpleNameMatch = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];

                // 完整类型名可直接通过程序集查询，优先级最高且不会产生命名空间歧义。
                Type fullNameType = assembly.GetType(componentName, false);
                if (IsConcreteComponent(fullNameType)) return fullNameType;

                Type[] assemblyTypes = GetLoadableTypes(assembly);
                for (int typeIndex = 0; typeIndex < assemblyTypes.Length; typeIndex++)
                {
                    Type candidate = assemblyTypes[typeIndex];
                    if (!IsConcreteComponent(candidate) || !string.Equals(candidate.Name, componentName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (simpleNameMatch != null && simpleNameMatch != candidate)
                    {
                        Debug.LogWarning(
                            "Component 简单名称“" + componentName + "”存在歧义：" +
                            simpleNameMatch.FullName + " 与 " + candidate.FullName + "。请在 JSON 中填写完整类型名。");
                        return null;
                    }

                    simpleNameMatch = candidate;
                }
            }

            return simpleNameMatch;
        }

        private static bool IsConcreteComponent(Type type)
        {
            return type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                // 某些插件程序集可能只有部分类型可加载；保留有效类型可避免一个插件阻断整个发现流程。
                List<Type> loadableTypes = new List<Type>();
                Type[] partialTypes = exception.Types;
                for (int index = 0; index < partialTypes.Length; index++)
                {
                    if (partialTypes[index] != null) loadableTypes.Add(partialTypes[index]);
                }

                return loadableTypes.ToArray();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("扫描程序集“" + assembly.FullName + "”时发生错误，已跳过该程序集。原因：" + exception.Message);
                return new Type[0];
            }
        }

        private static Dictionary<string, FieldInfo> BuildFieldCache(Type componentType)
        {
            FieldInfo[] reflectedFields = componentType.GetFields(PublicInstanceFieldFlags);
            Dictionary<string, FieldInfo> result = new Dictionary<string, FieldInfo>(reflectedFields.Length, StringComparer.Ordinal);

            // 缓存整个类型的字段表，而非逐字段缓存，使首次初始化后所有对象都只进行 O(1) 字典查询。
            for (int index = 0; index < reflectedFields.Length; index++)
            {
                FieldInfo field = reflectedFields[index];
                if (!field.IsInitOnly && !field.IsLiteral && !result.ContainsKey(field.Name))
                {
                    result.Add(field.Name, field);
                }
            }

            return result;
        }
    }
}
