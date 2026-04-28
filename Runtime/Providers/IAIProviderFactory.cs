namespace UniAI.Providers
{
    /// <summary>
    /// 对话 Provider 工厂。
    /// 实现类通过 AdapterAttribute 自动发现，用于把渠道协议解析为具体 Provider。
    /// </summary>
    public interface IAIProviderFactory
    {
        bool CanHandle(ChannelEntry channel, ModelEntry model, string modelId);
        IAIProvider Create(ChannelEntry channel, ModelEntry model, string modelId, GeneralConfig general);
    }
}
