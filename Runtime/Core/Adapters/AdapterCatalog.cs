using System;
using System.Collections.Generic;
using System.Linq;

namespace UniAI
{
    /// <summary>
    /// Framework-level adapter catalog used by editor UI and diagnostics.
    /// </summary>
    public static class AdapterCatalog
    {
        private static readonly object _sync = new();
        private static readonly Dictionary<string, AdapterDescriptor> _descriptors =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;

        public static IReadOnlyList<AdapterDescriptor> GetAll()
        {
            EnsureInitialized();

            lock (_sync)
                return Sort(_descriptors.Values).ToList();
        }

        public static IReadOnlyList<AdapterDescriptor> GetByTarget(AdapterTarget target)
        {
            EnsureInitialized();

            lock (_sync)
                return Sort(_descriptors.Values.Where(d => d.Target == target)).ToList();
        }

        public static IReadOnlyList<AdapterDescriptor> GetAdaptersFor(ModelEntry model)
        {
            return GetAdaptersFor(model, (ChannelEntry)null);
        }

        public static IReadOnlyList<AdapterDescriptor> GetAdaptersFor(ModelEntry model, ChannelEntry channel)
        {
            EnsureInitialized();

            if (model == null)
                return Array.Empty<AdapterDescriptor>();

            lock (_sync)
            {
                return Sort(_descriptors.Values.Where(d => d.IsCompatibleWith(model, channel))).ToList();
            }
        }

        public static IReadOnlyList<AdapterDescriptor> GetAdaptersFor(
            ModelEntry model,
            IEnumerable<ChannelEntry> channels)
        {
            EnsureInitialized();

            if (model == null)
                return Array.Empty<AdapterDescriptor>();

            var channelList = channels?.Where(c => c != null).ToList();
            lock (_sync)
            {
                if (channelList == null || channelList.Count == 0)
                    return Sort(_descriptors.Values.Where(d => d.IsCompatibleWith(model, null))).ToList();

                return Sort(_descriptors.Values.Where(d =>
                    channelList.Any(channel => d.IsCompatibleWith(model, channel)))).ToList();
            }
        }

        public static bool TryGet(string adapterId, out AdapterDescriptor descriptor)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(adapterId))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
                return _descriptors.TryGetValue(adapterId, out descriptor);
        }

        public static void Register(AdapterDescriptor descriptor)
        {
            EnsureInitialized();

            lock (_sync)
                RegisterCore(descriptor);
        }

        internal static void RegisterDiscovered(AdapterDescriptor descriptor)
        {
            lock (_sync)
                RegisterCore(descriptor);
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_sync)
            {
                if (_initialized)
                    return;

                foreach (var descriptor in AdapterDiscovery.DiscoverDescriptors())
                    RegisterCore(descriptor);

                _initialized = true;
            }
        }

        private static void RegisterCore(AdapterDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id))
                return;

            if (_descriptors.TryGetValue(descriptor.Id, out var existing))
            {
                if (descriptor.Priority < existing.Priority)
                    return;

                if (descriptor.Priority == existing.Priority
                    && descriptor.FactoryType != existing.FactoryType)
                {
                    AILogger.Warning($"Adapter '{descriptor.Id}' is already registered. The later descriptor will replace the previous one.");
                }
            }

            _descriptors[descriptor.Id] = descriptor;
        }

        private static IEnumerable<AdapterDescriptor> Sort(IEnumerable<AdapterDescriptor> descriptors)
        {
            return descriptors
                .OrderBy(d => d.Target)
                .ThenByDescending(d => d.Priority)
                .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase);
        }
    }
}
