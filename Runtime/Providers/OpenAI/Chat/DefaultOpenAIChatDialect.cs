namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// 标准 OpenAI Chat 方言。
    /// 不增加额外 provider-native 字段，也不解析 reasoning_content。
    /// </summary>
    internal sealed class DefaultOpenAIChatDialect : IOpenAIChatDialect
    {
        public static readonly DefaultOpenAIChatDialect Instance = new();

        private DefaultOpenAIChatDialect() { }

        /// <summary>标准 OpenAI chat completions 保留 temperature。</summary>
        public bool ShouldOmitTemperature(AIRequest request) => false;

        /// <summary>默认方言不向 assistant 消息注入额外字段。</summary>
        public void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls) { }

        /// <summary>默认方言不读取 reasoning_content。</summary>
        public string GetReasoningContent(OpenAIResponseMessage message) => null;

        /// <summary>默认方言不读取 reasoning delta。</summary>
        public string GetReasoningDelta(OpenAIStreamDelta delta) => null;
    }
}
