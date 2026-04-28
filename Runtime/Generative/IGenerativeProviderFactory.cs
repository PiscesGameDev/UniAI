namespace UniAI
{
    /// <summary>
    /// 生成式资产 Provider 工厂。
    /// 实现类通过 AdapterAttribute 自动发现，用于把渠道和模型解析为具体生成 Provider。
    /// </summary>
    public interface IGenerativeProviderFactory
    {
        bool CanHandle(ChannelEntry channel, ModelEntry model, string modelId);
        IGenerativeAssetProvider Create(ChannelEntry channel, ModelEntry model, string modelId, GeneralConfig general);
    }
}
