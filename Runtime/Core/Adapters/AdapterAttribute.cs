using System;

namespace UniAI
{
    /// <summary>
    /// Declares the metadata used to discover an adapter factory.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AdapterAttribute : Attribute
    {
        public string Id { get; }
        public AdapterTarget Target { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int Priority { get; }

        public AdapterAttribute(
            string id,
            AdapterTarget target,
            string displayName = null,
            string description = null,
            int priority = 0)
        {
            Id = id;
            Target = target;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Description = description;
            Priority = priority;
        }
    }
}
