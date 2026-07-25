/******************************************************************************
 * 文件名称：ComponentInitializer.cs
 *
 * 功能描述：
 *     根据 JSON 配置自动添加 Component，并通过反射初始化公开字段。
 *
 * 设计原则：
 *     1. 不依赖任何具体 Component 或字段类型；
 *     2. 反射查询统一交由 ReflectionCache；
 *     3. 字段值转换统一交由 TypeConverter；
 *     4. 单组件或单字段错误不会中断同一对象的其余初始化。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 提供数据驱动的 GameObject 组件添加和字段初始化唯一入口。
    /// </summary>
    public static class ComponentInitializer
    {
        /// <summary>
        /// 根据 JSON 初始化 GameObject 上的所有组件及公开字段。
        /// </summary>
        /// <param name="obj">待初始化的对象。</param>
        /// <param name="root">已经解析并校验的 JSON 数据。</param>
        /// <param name="prefabDictionary">Prefab 注册表。</param>
        public static void Apply(
            GameObject obj,
            JsonRoot root,
            Dictionary<string, GameObject> prefabDictionary)
        {
            if (obj == null)
            {
                Debug.LogError("组件初始化失败：目标 GameObject 为空。");
                return;
            }

            if (root == null)
            {
                Debug.LogError("对象“" + obj.name + "”缺少有效 JSON 数据，已放弃当前对象初始化。");
                return;
            }

            IReadOnlyList<JsonComponentData> componentDataList = root.ComponentVar;
            for (int componentIndex = 0; componentIndex < componentDataList.Count; componentIndex++)
            {
                JsonComponentData componentData = componentDataList[componentIndex];
                ApplyComponent(obj, componentData, prefabDictionary);
            }
        }

        private static void ApplyComponent(
            GameObject obj,
            JsonComponentData componentData,
            Dictionary<string, GameObject> prefabDictionary)
        {
            // =========================
            // 查找或添加 Component
            // =========================
            Type componentType = ReflectionCache.FindComponentType(componentData.Component);
            if (componentType == null)
            {
                Debug.LogWarning("对象“" + obj.name + "”无法找到 Component“" + componentData.Component + "”，已跳过该组件。");
                return;
            }

            Component component = obj.GetComponent(componentType);
            if (component == null)
            {
                try
                {
                    component = obj.AddComponent(componentType);
                }
                catch (Exception exception)
                {
                    Debug.LogError("对象“" + obj.name + "”添加 Component“" + componentType.FullName + "”失败，已跳过该组件。原因：" + exception.Message);
                    return;
                }
            }

            // =========================
            // 遍历并初始化字段
            // =========================
            IReadOnlyList<JsonVariableData> variables = componentData.Variables;
            for (int variableIndex = 0; variableIndex < variables.Count; variableIndex++)
            {
                ApplyField(obj, component, componentType, variables[variableIndex], prefabDictionary);
            }
        }

        private static void ApplyField(
            GameObject obj,
            Component component,
            Type componentType,
            JsonVariableData variable,
            Dictionary<string, GameObject> prefabDictionary)
        {
            FieldInfo field = ReflectionCache.FindPublicField(componentType, variable.Name);
            if (field == null)
            {
                Debug.LogWarning(
                    "对象“" + obj.name + "”的 Component“" + componentType.FullName +
                    "”不存在可写公开字段“" + variable.Name + "”，已跳过该字段。");
                return;
            }

            try
            {
                // 类型推导完全取自 FieldInfo，JSON 无需维护容易失真的 Type 字段。
                object convertedValue = TypeConverter.Convert(variable.Data, field.FieldType, prefabDictionary);

                // 所有 Component 共用同一条反射赋值路径，新增业务脚本不需要任何特殊分支。
                field.SetValue(component, convertedValue);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "对象“" + obj.name + "”初始化字段“" + componentType.FullName + "." + field.Name +
                    "”失败，已继续处理其它字段。原因：" + GetRootMessage(exception));
            }
        }

        private static string GetRootMessage(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null) current = current.InnerException;
            return current.Message;
        }
    }
}
