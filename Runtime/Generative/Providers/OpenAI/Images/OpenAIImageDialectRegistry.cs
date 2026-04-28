using System.Collections.Generic;

namespace UniAI.Providers.OpenAI.Images
{
    /// <summary>
    /// OpenAI image dialect registry.
    /// Resolution order: explicit AdapterId -> Factory.CanHandle metadata match -> default dialect.
    /// </summary>
    public static class OpenAIImageDialectRegistry
    {
        private static readonly AdapterRegistry<IOpenAIImageDialectFactory> _registry =
            new(AdapterTarget.OpenAIImageDialect);

        public static void Register(AdapterDescriptor descriptor, IOpenAIImageDialectFactory factory)
        {
            _registry.Register(descriptor, factory);
        }

        public static IReadOnlyList<AdapterDescriptor> GetAdapters()
        {
            return _registry.GetAdapters();
        }

        public static IOpenAIImageDialect Resolve(string modelId)
        {
            var model = ModelRegistry.Get(modelId);
            if (model == null)
                return DefaultOpenAIImageDialect.Instance;

            if (_registry.TryGetFactory(model.AdapterId, out var explicitFactory))
                return explicitFactory.Create(model);

            foreach (var registration in _registry.Registrations)
            {
                if (registration.Factory.CanHandle(model))
                    return registration.Factory.Create(model);
            }

            return DefaultOpenAIImageDialect.Instance;
        }
    }
}
