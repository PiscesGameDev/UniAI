namespace UniAI.Providers
{
    /// <summary>
    /// Provider 配置 — 从 ChannelEntry 直接构造，替代协议特定的 ClaudeConfig / OpenAIConfig
    /// </summary>
    public class ProviderConfig
    {
        public string ApiKey;
        public string BaseUrl;
        public string Model;
        public int TimeoutSeconds;
        public string ApiVersion; // Claude 专用
    }
}