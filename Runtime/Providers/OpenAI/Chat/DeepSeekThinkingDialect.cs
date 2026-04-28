namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// DeepSeek thinking 模式方言。
    /// DeepSeek 在 thinking 模式下会返回 reasoning_content；如果 assistant 消息包含 tool_calls，
    /// 后续 agent loop 请求必须把这段 reasoning_content 一起回放。
    /// </summary>
    internal sealed class DeepSeekThinkingDialect : IOpenAIChatDialect
    {
        public static readonly DeepSeekThinkingDialect Instance = new();

        private DeepSeekThinkingDialect() { }

        public bool ShouldOmitTemperature(AIRequest request) => true;

        public void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls)
        {
            if (!hasToolCalls || string.IsNullOrEmpty(source?.ReasoningContent))
                return;

            target.ReasoningContent = source.ReasoningContent;
        }

        public string GetReasoningContent(OpenAIResponseMessage message) => message?.ReasoningContent;

        public string GetReasoningDelta(OpenAIStreamDelta delta) => delta?.ReasoningContent;
    }

    /// <summary>
    /// DeepSeek thinking 方言工厂。
    /// Adapter identity comes from AdapterAttribute; the factory only handles metadata matching and creation.
    /// </summary>
    [Adapter(
        "deepseek.openai_chat.thinking",
        AdapterTarget.OpenAIChatDialect,
        "DeepSeek Thinking",
        "DeepSeek thinking mode with reasoning_content replay.",
        priority: 100,
        protocolId: "OpenAI",
        capabilities: ModelCapability.Chat,
        endpointId: "ChatCompletions")]
    internal sealed class DeepSeekThinkingDialectFactory : IOpenAIChatDialectFactory
    {
        public bool CanHandle(ModelEntry model)
        {
            if (model == null)
                return false;

            return model.HasBehavior(ModelBehavior.RequiresReasoningReplayForToolCalls)
                   || model.HasBehaviorTag("chat.reasoning_content")
                   || model.HasBehaviorTag("chat.reasoning_content_replay");
        }

        public IOpenAIChatDialect Create(ModelEntry model) => DeepSeekThinkingDialect.Instance;
    }
}
