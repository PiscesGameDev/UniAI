using UniAI.Providers;

namespace UniAI.Providers.Claude
{
    [Adapter(
        "claude.chat.provider",
        AdapterTarget.ConversationProvider,
        "Claude Chat Provider",
        "Creates Claude Messages API providers.",
        priority: 100,
        protocolId: "Claude",
        capabilities: ModelCapability.Chat)]
    internal sealed class ClaudeProviderFactory : IAIProviderFactory
    {
        public bool CanHandle(ChannelEntry channel, ModelEntry model, string modelId)
        {
            return channel != null && channel.Protocol == ProviderProtocol.Claude;
        }

        public IAIProvider Create(ChannelEntry channel, ModelEntry model, string modelId, GeneralConfig general)
        {
            return new ClaudeProvider(AIProviderFactoryRegistry.BuildConfig(channel, modelId, general));
        }
    }
}
