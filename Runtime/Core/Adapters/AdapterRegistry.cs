using System;
using System.Collections.Generic;
using System.Linq;

namespace UniAI
{
    /// <summary>
    /// Shared implementation for adapter registries that resolve factories by target.
    /// </summary>
    internal sealed class AdapterRegistry<TFactory>
    {
        private readonly AdapterTarget _target;
        private readonly List<AdapterRegistration<TFactory>> _registrations = new();

        public AdapterRegistry(AdapterTarget target)
        {
            _target = target;

            foreach (var registration in AdapterDiscovery.Discover<TFactory>(target))
                Register(registration.Descriptor, registration.Factory);
        }

        public IReadOnlyList<AdapterRegistration<TFactory>> Registrations => _registrations;

        public void Register(AdapterDescriptor descriptor, TFactory factory)
        {
            if (descriptor == null || factory == null || string.IsNullOrEmpty(descriptor.Id))
                return;

            if (descriptor.Target != _target)
            {
                AILogger.Warning($"Adapter '{descriptor.Id}' targets '{descriptor.Target}', expected '{_target}', and will be ignored.");
                return;
            }

            var existingIndex = _registrations.FindIndex(r =>
                string.Equals(r.Descriptor.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));

            var registration = new AdapterRegistration<TFactory>(descriptor, factory);
            if (existingIndex >= 0)
            {
                var existing = _registrations[existingIndex];
                if (descriptor.Priority < existing.Descriptor.Priority)
                    return;

                if (descriptor.Priority == existing.Descriptor.Priority
                    && descriptor.FactoryType != existing.Descriptor.FactoryType)
                {
                    AILogger.Warning($"Adapter '{descriptor.Id}' is already registered. The later registration will replace the previous one.");
                }

                _registrations[existingIndex] = registration;
            }
            else
            {
                _registrations.Add(registration);
            }

            SortRegistrations();
            AdapterCatalog.RegisterDiscovered(descriptor);
        }

        public IReadOnlyList<AdapterDescriptor> GetAdapters()
        {
            return _registrations.Select(r => r.Descriptor).ToList();
        }

        public bool TryGetFactory(string adapterId, out TFactory factory)
        {
            if (!string.IsNullOrEmpty(adapterId))
            {
                foreach (var registration in _registrations)
                {
                    if (string.Equals(registration.Descriptor.Id, adapterId, StringComparison.OrdinalIgnoreCase))
                    {
                        factory = registration.Factory;
                        return true;
                    }
                }
            }

            factory = default;
            return false;
        }

        private void SortRegistrations()
        {
            _registrations.Sort((a, b) =>
            {
                var priority = b.Descriptor.Priority.CompareTo(a.Descriptor.Priority);
                if (priority != 0)
                    return priority;

                return string.Compare(a.Descriptor.Id, b.Descriptor.Id, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
