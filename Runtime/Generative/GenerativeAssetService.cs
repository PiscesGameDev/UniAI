using System.Collections.Generic;
using System.Linq;

namespace UniAI
{
    /// <summary>
    /// 生成式资产服务 — Provider 注册表与路由单例。
    /// Tool 层通过此服务查找和调用合适的 Provider。
    /// </summary>
    public class GenerativeAssetService
    {
        private static GenerativeAssetService _instance;
        public static GenerativeAssetService Instance => _instance ??= new GenerativeAssetService();

        private readonly Dictionary<string, IGenerativeAssetProvider> _providers = new();
        private readonly List<IGenerativeAssetProvider> _orderedProviders = new();

        /// <summary>注册 Provider</summary>
        public void Register(IGenerativeAssetProvider provider)
        {
            if (_providers.ContainsKey(provider.ProviderId))
            {
                AILogger.Warning($"[GenerativeAssetService] Provider '{provider.ProviderId}' already registered, replacing.");
                Unregister(provider.ProviderId);
            }

            _providers[provider.ProviderId] = provider;
            _orderedProviders.Add(provider);
        }

        /// <summary>注销 Provider</summary>
        public void Unregister(string providerId)
        {
            if (_providers.Remove(providerId, out var removed))
                _orderedProviders.Remove(removed);
        }

        /// <summary>清空所有 Provider（重新初始化时使用）</summary>
        public void Clear()
        {
            _providers.Clear();
            _orderedProviders.Clear();
        }

        /// <summary>按 Provider ID 查找</summary>
        public bool TryGet(string id, out IGenerativeAssetProvider provider)
            => _providers.TryGetValue(id, out provider);

        /// <summary>按资产类型获取默认 Provider（第一个注册的）</summary>
        public IGenerativeAssetProvider GetDefault(GenerativeAssetType type)
            => _orderedProviders.FirstOrDefault(p => p.AssetType == type);

        /// <summary>列出所有已注册 Provider</summary>
        public IReadOnlyList<IGenerativeAssetProvider> All => _orderedProviders;

        /// <summary>按类型列出</summary>
        public IReadOnlyList<IGenerativeAssetProvider> GetByType(GenerativeAssetType type)
            => _orderedProviders.Where(p => p.AssetType == type).ToList();
    }
}
