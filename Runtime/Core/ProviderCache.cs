using System.Collections.Concurrent;
using UniAI.Providers;

namespace UniAI
{
    /// <summary>
    /// 按渠道、模型和生效配置缓存 Provider 实例。
    /// </summary>
    internal sealed class ProviderCache
    {
        private const int MAX_POOL_SIZE = 64;

        private readonly ConcurrentDictionary<string, IAIProvider> _providers = new();

        public IAIProvider GetOrCreate(ChannelEntry channel, string modelId, GeneralConfig general)
        {
            var poolKey = BuildPoolKey(channel, modelId, general);
            if (_providers.TryGetValue(poolKey, out var existing))
                return existing;

            if (_providers.Count >= MAX_POOL_SIZE)
            {
                AILogger.Info($"ProviderCache reached {MAX_POOL_SIZE}, clearing.");
                _providers.Clear();
            }

            return _providers.GetOrAdd(
                poolKey,
                _ => AIProviderFactoryRegistry.CreateProvider(channel, modelId, general));
        }

        public void Clear()
        {
            _providers.Clear();
        }

        private static string BuildPoolKey(ChannelEntry channel, string modelId, GeneralConfig general)
        {
            var fingerprintSource =
                $"{channel.Protocol}|{channel.BaseUrl}|{channel.ApiVersion}|{channel.GetEffectiveApiKey()}|{general?.TimeoutSeconds ?? 0}";

            return $"{channel.Id}:{modelId}:{StableHash(fingerprintSource):x8}";
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                var hash = offset;

                if (value != null)
                {
                    foreach (var ch in value)
                    {
                        hash ^= ch;
                        hash *= prime;
                    }
                }

                return hash;
            }
        }
    }
}
