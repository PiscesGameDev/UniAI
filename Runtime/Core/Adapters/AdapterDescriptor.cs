using System;

namespace UniAI
{
    /// <summary>
    /// Read-only adapter metadata used by runtime registries and editor UI.
    /// </summary>
    public sealed class AdapterDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public AdapterTarget Target { get; }
        public int Priority { get; }
        public Type FactoryType { get; }
        public string ProtocolId { get; }
        public string Vendor { get; }
        public ModelCapability Capabilities { get; }
        public ModelEndpoint? Endpoint { get; }

        public AdapterDescriptor(
            string id,
            string displayName,
            string description,
            AdapterTarget target,
            int priority,
            Type factoryType,
            string protocolId = null,
            string vendor = null,
            ModelCapability capabilities = ModelCapability.None,
            ModelEndpoint? endpoint = null)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Description = description;
            Target = target;
            Priority = priority;
            FactoryType = factoryType;
            ProtocolId = protocolId;
            Vendor = vendor;
            Capabilities = capabilities;
            Endpoint = endpoint;
        }

        public bool IsCompatibleWith(ModelEntry model, ChannelEntry channel)
        {
            if (model == null)
                return false;

            if (!TargetMatchesModel(model))
                return false;

            if (Capabilities != ModelCapability.None && (model.Capabilities & Capabilities) == 0)
                return false;

            if (Endpoint.HasValue && model.Endpoint != Endpoint.Value)
                return false;

            if (!string.IsNullOrEmpty(Vendor)
                && !string.Equals(model.Vendor, Vendor, StringComparison.OrdinalIgnoreCase))
                return false;

            if (channel != null
                && !string.IsNullOrEmpty(ProtocolId)
                && !string.Equals(channel.Protocol.ToString(), ProtocolId, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private bool TargetMatchesModel(ModelEntry model)
        {
            return Target switch
            {
                AdapterTarget.OpenAIChatDialect => model.HasCapability(ModelCapability.Chat),
                AdapterTarget.OpenAIImageDialect => model.HasCapability(ModelCapability.ImageGen)
                                                    || model.HasCapability(ModelCapability.ImageEdit),
                AdapterTarget.EmbeddingProvider => model.HasCapability(ModelCapability.Embedding),
                AdapterTarget.RerankProvider => model.HasCapability(ModelCapability.Rerank),
                AdapterTarget.AudioGenerationProvider => model.HasCapability(ModelCapability.AudioGen),
                AdapterTarget.VideoGenerationProvider => model.HasCapability(ModelCapability.VideoGen),
                _ => false
            };
        }
    }
}
