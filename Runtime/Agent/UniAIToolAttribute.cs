using System;

namespace UniAI
{
    /// <summary>
    /// 标记一个 static 类为 UniAI 工具处理器。
    /// 类必须提供签名 <c>public static UniTask&lt;object&gt; HandleAsync(JObject args, CancellationToken ct)</c> 的入口。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class UniAIToolAttribute : Attribute
    {
        /// <summary>
        /// 工具名称。为空时从类名自动推导（PascalCase → snake_case），如 <c>ManageFile</c> → <c>manage_file</c>。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 工具描述，会传给 LLM 指导其何时使用此工具。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 工具所属分组。Agent 通过 <see cref="AgentDefinition.ToolGroups"/> 按组启用工具。
        /// 约定组名见 <see cref="ToolGroups"/>。
        /// </summary>
        public string Group { get; set; } = ToolGroups.Core;

        /// <summary>
        /// 是否使用 action 字段做二级派发。
        /// 为 true 时 Schema 生成器会在参数根部强制添加 <c>action</c> 必填字段（枚举值由 <see cref="Actions"/> 提供）。
        /// 单功能工具设为 false，直接用参数类字段。
        /// </summary>
        public bool HasActions { get; set; } = true;

        /// <summary>
        /// action 候选值列表。仅在 <see cref="HasActions"/> 为 true 时生效。
        /// </summary>
        public string[] Actions { get; set; }

        /// <summary>
        /// 是否为长耗时工具。为 true 时 <see cref="AIAgentRunner"/> 会绕过全局 ToolTimeoutSeconds，
        /// 使用 <see cref="MaxPollSeconds"/> 作为本次调用的最大等待时间。
        /// </summary>
        public bool RequiresPolling { get; set; }

        /// <summary>
        /// 长耗时工具的最大等待秒数（仅在 <see cref="RequiresPolling"/> 为 true 时生效）。
        /// 0 或负数表示不限制。
        /// </summary>
        public int MaxPollSeconds { get; set; }
    }

    /// <summary>
    /// 标记工具参数类的字段或属性，供 Schema 生成器读取描述信息。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ToolParamAttribute : Attribute
    {
        /// <summary>
        /// 参数描述（LLM 可读）。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否必填。默认 true。
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// 默认值（字符串形式，写入 Schema 的 <c>default</c> 字段）。
        /// </summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// 枚举候选值，写入 Schema 的 <c>enum</c> 字段。
        /// </summary>
        public string[] Enum { get; set; }

        public ToolParamAttribute() { }

        public ToolParamAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// 约定的工具分组常量。避免字符串拼写错误。
    /// </summary>
    public static class ToolGroups
    {
        public const string Core = "core";
        public const string Scene = "scene";
        public const string Asset = "asset";
        public const string Editor = "editor";
        public const string Testing = "testing";
        public const string Runtime = "runtime";
    }
}
