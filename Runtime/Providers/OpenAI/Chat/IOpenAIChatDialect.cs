namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// OpenAI Chat 协议方言接口。
    /// 同样是 OpenAI-compatible 的 /chat/completions，不同模型或中转服务仍可能有字段差异；
    /// 这些差异应收敛在 Dialect 内，避免 OpenAIProvider 主流程出现模型名特判。
    /// </summary>
    public interface IOpenAIChatDialect
    {
        /// <summary>是否应省略 temperature 等采样参数。</summary>
        bool ShouldOmitTemperature(AIRequest request);

        /// <summary>
        /// 将 assistant 消息转换为 OpenAI 消息后，给方言补充 provider-native 字段的机会。
        /// </summary>
        void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls);

        /// <summary>从非流式响应中读取 provider-native reasoning 内容。</summary>
        string GetReasoningContent(OpenAIResponseMessage message);

        /// <summary>从流式 delta 中读取 provider-native reasoning 增量。</summary>
        string GetReasoningDelta(OpenAIStreamDelta delta);
    }

    /// <summary>
    /// Chat dialect factory. Adapter metadata is declared by AdapterAttribute.
    /// </summary>
    public interface IOpenAIChatDialectFactory
    {
        /// <summary>当模型没有显式 AdapterId 或 AdapterId 未命中时，用于基于元数据做兜底匹配。</summary>
        bool CanHandle(ModelEntry model);

        /// <summary>为指定模型创建方言实例。</summary>
        IOpenAIChatDialect Create(ModelEntry model);
    }
}
