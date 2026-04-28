using System.Collections.Generic;

namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// OpenAI Chat dialect registry.
    /// Resolution order: explicit AdapterId -> Factory.CanHandle metadata match -> default dialect.
    /// </summary>
    public static class OpenAIChatDialectRegistry
    {
        private static readonly AdapterRegistry<IOpenAIChatDialectFactory> _registry =
            new(AdapterTarget.OpenAIChatDialect);

        /// <summary>
        /// Registers a chat dialect factory. Built-in adapters use AdapterAttribute discovery.
        /// </summary>
        public static void Register(AdapterDescriptor descriptor, IOpenAIChatDialectFactory factory)
        {
            _registry.Register(descriptor, factory);
        }

        /// <summary>Returns all discovered or manually registered chat adapters.</summary>
        public static IReadOnlyList<AdapterDescriptor> GetAdapters()
        {
            return _registry.GetAdapters();
        }

        public static IOpenAIChatDialect Resolve(string modelId)
        {
            var model = ModelRegistry.Get(modelId);
            if (model == null)
                return DefaultOpenAIChatDialect.Instance;

            if (_registry.TryGetFactory(model.AdapterId, out var explicitFactory))
                return explicitFactory.Create(model);

            foreach (var registration in _registry.Registrations)
            {
                if (registration.Factory.CanHandle(model))
                    return registration.Factory.Create(model);
            }

            return DefaultOpenAIChatDialect.Instance;
        }
    }
}
