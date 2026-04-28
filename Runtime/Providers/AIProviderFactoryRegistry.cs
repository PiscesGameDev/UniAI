using System;

namespace UniAI.Providers
{
    /// <summary>
    /// 根据渠道协议和模型元数据解析对话 Provider 工厂。
    /// </summary>
    internal static class AIProviderFactoryRegistry
    {
        private static readonly AdapterRegistry<IAIProviderFactory> _registry =
            new(AdapterTarget.ConversationProvider);

        public static IAIProvider CreateProvider(ChannelEntry channel, string modelId, GeneralConfig general)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            var model = ModelRegistry.Get(modelId);
            foreach (var registration in _registry.Registrations)
            {
                if (registration.Factory.CanHandle(channel, model, modelId))
                    return registration.Factory.Create(channel, model, modelId, general);
            }

            throw new NotSupportedException($"Protocol '{channel.Protocol}' is not supported.");
        }

        internal static ProviderConfig BuildConfig(ChannelEntry channel, string modelId, GeneralConfig general)
        {
            general ??= new GeneralConfig();

            return new ProviderConfig
            {
                ApiKey = channel.GetEffectiveApiKey(),
                BaseUrl = channel.BaseUrl,
                Model = modelId ?? channel.DefaultModel,
                TimeoutSeconds = general.TimeoutSeconds,
                ApiVersion = channel.ApiVersion ?? "2023-06-01"
            };
        }
    }
}
