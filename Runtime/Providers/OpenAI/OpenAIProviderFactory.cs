using UniAI.Providers;

namespace UniAI.Providers.OpenAI
{
    [Adapter(
        "openai.chat.provider",
        AdapterTarget.ConversationProvider,
        "OpenAI Chat Provider",
        "Creates OpenAI-compatible Chat Completions providers.",
        priority: 100,
        protocolId: "OpenAI",
        capabilities: ModelCapability.Chat)]
    internal sealed class OpenAIProviderFactory : IAIProviderFactory
    {
        public bool CanHandle(ChannelEntry channel, ModelEntry model, string modelId)
        {
            return channel != null && channel.Protocol == ProviderProtocol.OpenAI;
        }

        public IAIProvider Create(ChannelEntry channel, ModelEntry model, string modelId, GeneralConfig general)
        {
            return new OpenAIProvider(AIProviderFactoryRegistry.BuildConfig(channel, modelId, general));
        }
    }
}
