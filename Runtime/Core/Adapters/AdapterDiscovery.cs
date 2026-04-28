using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UniAI
{
    /// <summary>
    /// Reflection-based adapter discovery.
    /// </summary>
    internal static class AdapterDiscovery
    {
        public static IReadOnlyList<AdapterRegistration<TFactory>> Discover<TFactory>(AdapterTarget target)
        {
            var factoryType = typeof(TFactory);
            var registrations = new List<AdapterRegistration<TFactory>>();

            foreach (var type in GetLoadableTypes())
            {
                if (!IsFactoryType(type, factoryType))
                    continue;

                var attribute = type.GetCustomAttribute<AdapterAttribute>(false);
                if (attribute == null)
                {
                    AILogger.Warning($"Adapter factory '{type.FullName}' is missing AdapterAttribute and will be ignored.");
                    continue;
                }

                if (attribute.Target != target)
                {
                    AILogger.Warning($"Adapter factory '{type.FullName}' has target '{attribute.Target}', expected '{target}', and will be ignored.");
                    continue;
                }

                if (string.IsNullOrEmpty(attribute.Id))
                {
                    AILogger.Warning($"Adapter factory '{type.FullName}' has an empty adapter id and will be ignored.");
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type, nonPublic: true) is not TFactory factory)
                        continue;

                    var descriptor = new AdapterDescriptor(
                        attribute.Id,
                        attribute.DisplayName,
                        attribute.Description,
                        attribute.Target,
                        attribute.Priority,
                        type,
                        attribute.ProtocolId,
                        attribute.Vendor,
                        attribute.Capabilities,
                        ParseEndpoint(attribute.EndpointId, type.FullName));

                    registrations.Add(new AdapterRegistration<TFactory>(descriptor, factory));
                }
                catch (Exception ex)
                {
                    AILogger.Warning($"Failed to create adapter factory '{type.FullName}': {ex.Message}");
                }
            }

            return registrations
                .OrderByDescending(r => r.Descriptor.Priority)
                .ThenBy(r => r.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<AdapterDescriptor> DiscoverDescriptors()
        {
            var descriptors = new List<AdapterDescriptor>();

            foreach (var type in GetLoadableTypes())
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;

                var attribute = type.GetCustomAttribute<AdapterAttribute>(false);
                if (attribute == null)
                    continue;

                if (string.IsNullOrEmpty(attribute.Id))
                {
                    AILogger.Warning($"Adapter type '{type.FullName}' has an empty adapter id and will be ignored.");
                    continue;
                }

                descriptors.Add(new AdapterDescriptor(
                    attribute.Id,
                    attribute.DisplayName,
                    attribute.Description,
                    attribute.Target,
                    attribute.Priority,
                    type,
                    attribute.ProtocolId,
                    attribute.Vendor,
                    attribute.Capabilities,
                    ParseEndpoint(attribute.EndpointId, type.FullName)));
            }

            return descriptors
                .OrderBy(d => d.Target)
                .ThenByDescending(d => d.Priority)
                .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<Type> GetLoadableTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                    yield return type;
            }
        }

        private static bool IsFactoryType(Type type, Type factoryType)
        {
            return type != null
                   && !type.IsAbstract
                   && !type.IsInterface
                   && factoryType.IsAssignableFrom(type);
        }

        private static ModelEndpoint? ParseEndpoint(string endpointId, string typeName)
        {
            if (string.IsNullOrEmpty(endpointId))
                return null;

            if (Enum.TryParse<ModelEndpoint>(endpointId, true, out var endpoint))
                return endpoint;

            AILogger.Warning($"Adapter type '{typeName}' has unknown endpoint '{endpointId}'. The endpoint constraint will be ignored.");
            return null;
        }
    }
}
