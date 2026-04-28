using System.Collections.Generic;

namespace UniAI
{
    public sealed class GenerativeProviderRoute
    {
        public ChannelEntry Channel { get; }
        public IGenerativeAssetProvider Provider { get; }
        public string Error { get; }

        public GenerativeProviderRoute(ChannelEntry channel, IGenerativeAssetProvider provider, string error = null)
        {
            Channel = channel;
            Provider = provider;
            Error = error;
        }
    }

    /// <summary>
    /// Creates generation providers from model metadata and channel protocol.
    /// </summary>
    public static class GenerativeProviderRouter
    {
        public static GenerativeProviderRoute Resolve(
            IReadOnlyList<ChannelEntry> channels,
            ModelEntry entry,
            string modelId)
        {
            var errors = new List<string>();

            if (channels != null)
            {
                foreach (var channel in channels)
                {
                    if (TryCreateProvider(channel, entry, modelId, out var provider, out var error))
                        return new GenerativeProviderRoute(channel, provider);

                    if (!string.IsNullOrEmpty(error))
                        errors.Add($"{channel.Name}: {error}");
                }
            }

            return new GenerativeProviderRoute(
                null,
                null,
                errors.Count > 0
                    ? $"No compatible generation provider found for model '{modelId}'. {string.Join("; ", errors)}"
                    : $"No compatible generation provider found for model '{modelId}'.");
        }

        public static bool TryCreateProvider(
            ChannelEntry channel,
            ModelEntry entry,
            string modelId,
            out IGenerativeAssetProvider provider,
            out string error)
        {
            provider = null;
            error = null;

            if (channel == null)
            {
                error = "channel is null.";
                return false;
            }

            var capabilities = entry?.Capabilities ?? ModelCapability.Chat;
            if (HasImageGenerationCapability(capabilities))
                return TryCreateImageProvider(channel, entry, modelId, out provider, out error);

            if (entry?.HasCapability(ModelCapability.AudioGen) == true)
                error = "audio generation is not supported yet.";
            else if (entry?.HasCapability(ModelCapability.VideoGen) == true)
                error = "video generation is not supported yet.";
            else
                error = $"capabilities ({capabilities}) are not supported for generation.";

            return false;
        }

        public static bool IsGenerativeModel(ModelCapability capabilities)
        {
            const ModelCapability generativeCaps =
                ModelCapability.ImageGen |
                ModelCapability.ImageEdit |
                ModelCapability.AudioGen |
                ModelCapability.VideoGen;

            return (capabilities & generativeCaps) != 0;
        }

        public static bool HasImageGenerationCapability(ModelCapability capabilities)
        {
            const ModelCapability imageCaps = ModelCapability.ImageGen | ModelCapability.ImageEdit;
            return (capabilities & imageCaps) != 0;
        }

        private static bool TryCreateImageProvider(
            ChannelEntry channel,
            ModelEntry entry,
            string modelId,
            out IGenerativeAssetProvider provider,
            out string error)
        {
            provider = null;
            error = null;

            if (channel.Protocol != ProviderProtocol.OpenAI)
            {
                error = $"protocol '{channel.Protocol}' has no image generation provider.";
                return false;
            }

            var endpoint = entry?.Endpoint ?? ModelEndpoint.ChatCompletions;
            var adapterTarget = ResolveAdapterTarget(entry?.AdapterId);
            var usesOpenAIImagesApi =
                endpoint == ModelEndpoint.ImageGenerations
                || endpoint == ModelEndpoint.ImageEdits
                || adapterTarget == AdapterTarget.OpenAIImageDialect;

            if (!usesOpenAIImagesApi)
            {
                error = $"endpoint '{endpoint}' is not routed to the OpenAI Images API.";
                return false;
            }

            var apiKey = channel.GetEffectiveApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                error = "no API key configured.";
                return false;
            }

            provider = new OpenAIImageProvider(
                apiKey,
                channel.BaseUrl,
                modelId,
                providerId: $"image-{channel.Id}-{modelId}",
                displayName: $"{channel.Name} ({modelId})");
            return true;
        }

        private static AdapterTarget? ResolveAdapterTarget(string adapterId)
        {
            return AdapterCatalog.TryGet(adapterId, out var adapter)
                ? adapter.Target
                : null;
        }
    }
}
