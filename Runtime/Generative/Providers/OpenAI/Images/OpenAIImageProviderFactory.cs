using System;

namespace UniAI.Providers.OpenAI.Images
{
    [Adapter(
        "openai.images.provider",
        AdapterTarget.ImageGenerationProvider,
        "OpenAI Images Provider",
        "Creates OpenAI-compatible Images API providers.",
        priority: 100,
        protocolId: "OpenAI",
        capabilities: ModelCapability.ImageGen | ModelCapability.ImageEdit)]
    internal sealed class OpenAIImageProviderFactory : IGenerativeProviderFactory
    {
        public bool CanHandle(ChannelEntry channel, ModelEntry model, string modelId)
        {
            if (channel == null || channel.Protocol != ProviderProtocol.OpenAI)
                return false;

            var capabilities = model?.Capabilities ?? ModelCapability.Chat;
            if ((capabilities & (ModelCapability.ImageGen | ModelCapability.ImageEdit)) == 0)
                return false;

            var endpoint = model?.Endpoint ?? ModelEndpoint.ChatCompletions;
            var adapterTarget = ResolveAdapterTarget(model?.AdapterId);
            return endpoint == ModelEndpoint.ImageGenerations
                   || endpoint == ModelEndpoint.ImageEdits
                   || adapterTarget == AdapterTarget.OpenAIImageDialect;
        }

        public IGenerativeAssetProvider Create(ChannelEntry channel, ModelEntry model, string modelId, GeneralConfig general)
        {
            var apiKey = channel.GetEffectiveApiKey();
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("no API key configured.");

            general ??= new GeneralConfig();

            return new OpenAIImageProvider(
                apiKey,
                channel.BaseUrl,
                modelId,
                general.TimeoutSeconds,
                providerId: $"image-{channel.Id}-{modelId}",
                displayName: $"{channel.Name} ({modelId})");
        }

        private static AdapterTarget? ResolveAdapterTarget(string adapterId)
        {
            return AdapterCatalog.TryGet(adapterId, out var adapter)
                ? adapter.Target
                : null;
        }
    }
}
