namespace UniAI
{
    /// <summary>
    /// 内置渠道预设。
    /// 只保存框架出厂模板信息；API Key、启用状态、用户编辑后的模型列表属于 ChannelEntry。
    /// </summary>
    public sealed class ChannelPreset
    {
        public readonly string Name;
        public readonly ProviderProtocol Protocol;
        public readonly string BaseUrl;
        public readonly string EnvVarName;
        public readonly bool UseEnvVar;
        public readonly string ApiVersion;

        public ChannelPreset(
            string name,
            ProviderProtocol protocol,
            string baseUrl,
            string envVarName,
            bool useEnvVar = true,
            string apiVersion = null)
        {
            Name = name;
            Protocol = protocol;
            BaseUrl = baseUrl;
            EnvVarName = envVarName;
            UseEnvVar = useEnvVar;
            ApiVersion = apiVersion;
        }
    }
}
