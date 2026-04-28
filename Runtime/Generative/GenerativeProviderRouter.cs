using System;
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
        private static readonly AdapterRegistry<IGenerativeProviderFactory> _imageProviders =
            new(AdapterTarget.ImageGenerationProvider);

        public static GenerativeProviderRoute Resolve(
            IReadOnlyList<ChannelEntry> channels,
            ModelEntry entry,
            string modelId,
            GeneralConfig general = null)
        {
            var errors = new List<string>();

            if (channels != null)
            {
                foreach (var channel in channels)
                {
                    if (TryCreateProvider(channel, entry, modelId, general, out var provider, out var error))
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
            GeneralConfig general,
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
                return TryCreateImageProvider(channel, entry, modelId, general, out provider, out error);

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
            GeneralConfig general,
            out IGenerativeAssetProvider provider,
            out string error)
        {
            provider = null;
            error = null;

            foreach (var registration in _imageProviders.Registrations)
            {
                if (!registration.Factory.CanHandle(channel, entry, modelId))
                    continue;

                try
                {
                    provider = registration.Factory.Create(channel, entry, modelId, general);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"{registration.Descriptor.Id}: {ex.Message}";
                    return false;
                }
            }

            error = $"protocol '{channel.Protocol}' and model metadata have no compatible image generation provider.";
            return false;
        }
    }
}
