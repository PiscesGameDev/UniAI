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
            EnsureInitialized();

            if (model == null)
                return Array.Empty<AdapterDescriptor>();

            var targets = GetTargetsFor(model);
            lock (_sync)
            {
                return Sort(_descriptors.Values.Where(d => targets.Contains(d.Target))).ToList();
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

        private static IReadOnlyList<AdapterTarget> GetTargetsFor(ModelEntry model)
        {
            var targets = new List<AdapterTarget>();

            if (model.HasCapability(ModelCapability.Chat))
                targets.Add(AdapterTarget.OpenAIChatDialect);

            if (model.HasCapability(ModelCapability.ImageGen) || model.HasCapability(ModelCapability.ImageEdit))
                targets.Add(AdapterTarget.OpenAIImageDialect);

            if (model.HasCapability(ModelCapability.Embedding))
                targets.Add(AdapterTarget.EmbeddingProvider);

            if (model.HasCapability(ModelCapability.Rerank))
                targets.Add(AdapterTarget.RerankProvider);

            if (model.HasCapability(ModelCapability.AudioGen))
                targets.Add(AdapterTarget.AudioGenerationProvider);

            if (model.HasCapability(ModelCapability.VideoGen))
                targets.Add(AdapterTarget.VideoGenerationProvider);

            return targets;
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
