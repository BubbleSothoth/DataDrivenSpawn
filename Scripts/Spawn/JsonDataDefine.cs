/******************************************************************************
 * 文件名称：JsonDataDefine.cs
 *
 * 功能描述：
 *     定义对象初始化 JSON 对应的只读数据模型。
 *
 * 设计原则：
 *     1. 数据模型不依赖任何具体 Component；
 *     2. 解析结果只读，避免刷新过程中被意外修改；
 *     3. 原始 Data 保留为通用对象，由 TypeConverter 按目标字段类型转换。
 ******************************************************************************/

using System;
using System.Collections.Generic;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 定义初始化 JSON 中使用的属性名称，避免解析层散落重复字符串。
    /// </summary>
    public static class JsonPropertyNames
    {
        /// <summary>组件配置数组属性名。</summary>
        public const string ComponentVar = "ComponentVar";

        /// <summary>组件名称属性名。</summary>
        public const string Component = "Component";

        /// <summary>变量配置数组属性名。</summary>
        public const string Variables = "Variables";

        /// <summary>字段名称属性名。</summary>
        public const string Name = "Name";

        /// <summary>字段原始数据属性名。</summary>
        public const string Data = "Data";
    }

    /// <summary>
    /// 表示单个对象的全部组件初始化配置。
    /// </summary>
    public sealed class JsonRoot
    {
        /// <summary>
        /// 初始化配置中包含的组件列表。
        /// </summary>
        public IReadOnlyList<JsonComponentData> ComponentVar { get; private set; }

        /// <summary>
        /// 创建对象初始化配置根节点。
        /// </summary>
        /// <param name="componentVar">组件配置列表。</param>
        public JsonRoot(IReadOnlyList<JsonComponentData> componentVar)
        {
            ComponentVar = componentVar ?? throw new ArgumentNullException(nameof(componentVar));
        }
    }

    /// <summary>
    /// 表示一个待查找或添加的 Component 及其字段配置。
    /// </summary>
    public sealed class JsonComponentData
    {
        /// <summary>
        /// Component 的完整类型名或不含命名空间的类型名。
        /// </summary>
        public string Component { get; private set; }

        /// <summary>
        /// 需要写入该 Component 的字段列表。
        /// </summary>
        public IReadOnlyList<JsonVariableData> Variables { get; private set; }

        /// <summary>
        /// 创建组件初始化配置。
        /// </summary>
        /// <param name="component">Component 类型名。</param>
        /// <param name="variables">字段配置列表。</param>
        public JsonComponentData(string component, IReadOnlyList<JsonVariableData> variables)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }
    }

    /// <summary>
    /// 表示一个字段名称及其尚未转换的 JSON 数据。
    /// </summary>
    public sealed class JsonVariableData
    {
        /// <summary>
        /// 目标公开字段名称。
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// JSON 解析后的原始值，实际类型由目标字段和 TypeConverter 共同决定。
        /// </summary>
        public object Data { get; private set; }

        /// <summary>
        /// 创建字段初始化配置。
        /// </summary>
        /// <param name="name">目标字段名称。</param>
        /// <param name="data">JSON 原始数据。</param>
        public JsonVariableData(string name, object data)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Data = data;
        }
    }
}
