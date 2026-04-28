using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 内置模型预设。
    /// 描述模型元信息，以及它默认归属的内置渠道。
    /// </summary>
    public sealed class ModelPreset
    {
        private readonly string[] _defaultChannels;

        public readonly string Id;
        public readonly string Vendor;
        public readonly ModelCapability Capabilities;
        public readonly ModelEndpoint Endpoint;
        public readonly string Description;
        public readonly int ContextWindow;
        public readonly string AdapterId;
        public readonly ModelBehavior Behavior;

        public IReadOnlyList<string> DefaultChannels => _defaultChannels;

        public ModelPreset(
            string id,
            string vendor,
            ModelCapability capabilities,
            ModelEndpoint endpoint,
            string description = null,
            int contextWindow = 0,
            string adapterId = null,
            ModelBehavior behavior = ModelBehavior.None,
            params string[] defaultChannels)
        {
            Id = id;
            Vendor = vendor;
            Capabilities = capabilities;
            Endpoint = endpoint;
            Description = description;
            ContextWindow = contextWindow;
            AdapterId = adapterId;
            Behavior = behavior;
            _defaultChannels = defaultChannels ?? Array.Empty<string>();
        }

        public bool IsDefaultForChannel(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return false;

            foreach (var defaultChannel in _defaultChannels)
            {
                if (defaultChannel == channelName)
                    return true;
            }

            return false;
        }

        public ModelEntry ToModelEntry()
        {
            return new ModelEntry(Id, Vendor, Capabilities, Endpoint, Description, ContextWindow, AdapterId, Behavior);
        }
    }
}
