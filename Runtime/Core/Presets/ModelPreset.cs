using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// Built-in model preset with metadata and default channel membership.
    /// </summary>
    public sealed class ModelPreset
    {
        private readonly string[] _defaultChannels;
        private readonly string[] _behaviorTags;
        private readonly ModelBehaviorOption[] _behaviorOptions;

        public readonly string Id;
        public readonly string Vendor;
        public readonly ModelCapability Capabilities;
        public readonly ModelEndpoint Endpoint;
        public readonly string Description;
        public readonly int ContextWindow;
        public readonly string AdapterId;
        public readonly ModelBehavior Behavior;

        public IReadOnlyList<string> DefaultChannels => _defaultChannels;
        public IReadOnlyList<string> BehaviorTags => _behaviorTags;
        public IReadOnlyList<ModelBehaviorOption> BehaviorOptions => _behaviorOptions;

        public ModelPreset(
            string id,
            string vendor,
            ModelCapability capabilities,
            ModelEndpoint endpoint,
            string description = null,
            int contextWindow = 0,
            string adapterId = null,
            ModelBehavior behavior = ModelBehavior.None,
            string[] behaviorTags = null,
            ModelBehaviorOption[] behaviorOptions = null,
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
            _behaviorTags = behaviorTags ?? Array.Empty<string>();
            _behaviorOptions = behaviorOptions ?? Array.Empty<ModelBehaviorOption>();
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
            return new ModelEntry(
                Id,
                Vendor,
                Capabilities,
                Endpoint,
                Description,
                ContextWindow,
                AdapterId,
                Behavior,
                _behaviorTags,
                _behaviorOptions);
        }
    }
}
