using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// Model metadata: describes what a model can do and which adapter/dialect should handle it.
    /// </summary>
    [Serializable]
    public class ModelEntry
    {
        /// <summary>Unique model id, for example "dall-e-3" or "gpt-4o".</summary>
        public string Id;

        /// <summary>Model vendor, for example "OpenAI", "Google", or "Anthropic".</summary>
        public string Vendor;

        /// <summary>Model capabilities. Flags can be combined.</summary>
        public ModelCapability Capabilities;

        /// <summary>Default endpoint family for this model.</summary>
        public ModelEndpoint Endpoint;

        /// <summary>Short description for UI display.</summary>
        public string Description;

        /// <summary>Optional icon for UI display.</summary>
        public Texture2D Icon;

        /// <summary>Context window in tokens. 0 means ModelRegistry fallback.</summary>
        public int ContextWindow;

        /// <summary>Provider-specific adapter/dialect id. Empty means protocol default.</summary>
        public string AdapterId;

        /// <summary>Framework-known behavior flags consumed by core providers and runners.</summary>
        public ModelBehavior Behavior;

        /// <summary>User-configurable behavior tags consumed by adapters/dialects.</summary>
        public List<string> BehaviorTags = new();

        /// <summary>User-configurable key-value behavior options consumed by adapters/dialects.</summary>
        public List<ModelBehaviorOption> BehaviorOptions = new();

        public ModelEntry() { }

        public ModelEntry(
            string id,
            string vendor,
            ModelCapability capabilities,
            ModelEndpoint endpoint,
            string description = null,
            int contextWindow = 0,
            string adapterId = null,
            ModelBehavior behavior = ModelBehavior.None,
            IEnumerable<string> behaviorTags = null,
            IEnumerable<ModelBehaviorOption> behaviorOptions = null)
        {
            Id = id;
            Vendor = vendor;
            Capabilities = capabilities;
            Endpoint = endpoint;
            Description = description;
            ContextWindow = contextWindow;
            AdapterId = adapterId;
            Behavior = behavior;
            BehaviorTags = behaviorTags != null ? new List<string>(behaviorTags) : new List<string>();
            BehaviorOptions = behaviorOptions != null ? new List<ModelBehaviorOption>(behaviorOptions) : new List<ModelBehaviorOption>();
        }

        public bool HasCapability(ModelCapability cap) => (Capabilities & cap) != 0;

        public bool HasBehavior(ModelBehavior behavior) => (Behavior & behavior) != 0;

        public bool HasBehaviorTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || BehaviorTags == null)
                return false;

            foreach (var item in BehaviorTags)
            {
                if (string.Equals(item, tag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public string GetBehaviorOption(string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(key) || BehaviorOptions == null)
                return defaultValue;

            foreach (var option in BehaviorOptions)
            {
                if (option == null)
                    continue;

                if (string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
                    return option.Value;
            }

            return defaultValue;
        }

        public int GetBehaviorOptionInt(string key, int defaultValue)
        {
            var value = GetBehaviorOption(key);
            return int.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        public bool GetBehaviorOptionBool(string key, bool defaultValue)
        {
            var value = GetBehaviorOption(key);
            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }
    }

    [Serializable]
    public class ModelBehaviorOption
    {
        public string Key;
        public string Value;

        public ModelBehaviorOption() { }

        public ModelBehaviorOption(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
