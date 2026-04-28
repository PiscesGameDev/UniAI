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

        public AdapterDescriptor(
            string id,
            string displayName,
            string description,
            AdapterTarget target,
            int priority,
            Type factoryType)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Description = description;
            Target = target;
            Priority = priority;
            FactoryType = factoryType;
        }
    }
}
