using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 渠道使用的 API 协议类型
    /// </summary>
    public enum ProviderProtocol
    {
        Claude,
        OpenAI
    }
    
    /// <summary>
    /// 单个渠道配置条目
    /// </summary>
    [Serializable]
    public class ChannelEntry
    {
        [SerializeField]
        private string _id = Guid.NewGuid().ToString("N");
        public string Id => _id;
        public string Name;
        public ProviderProtocol Protocol;

        /// <summary>
        /// 渠道是否启用（禁用后不参与模型路由和 Fallback）
        /// </summary>
        public bool Enabled = true;

        public string ApiKey;
        public string BaseUrl;

        /// <summary>
        /// 环境变量名（如 ANTHROPIC_API_KEY），非空时可用于覆盖 ApiKey
        /// </summary>
        public string EnvVarName;

        /// <summary>
        /// 是否优先使用环境变量中的 API Key
        /// </summary>
        public bool UseEnvVar;

        /// <summary>
        /// 该渠道支持的模型列表
        /// </summary>
        public List<string> Models = new();

        /// <summary>
        /// Claude 协议专用：API 版本号
        /// </summary>
        public string ApiVersion;

        /// <summary>
        /// 默认模型（Models 列表中的第一个）
        /// </summary>
        public string DefaultModel => Models?.Count > 0 ? Models[0] : null;
        
        /// <summary>
        /// 克隆
        /// </summary>
        /// <returns></returns>
        public ChannelEntry Clone()
        {
            var channelEntry = new ChannelEntry
            {
                _id = Id,
                Name = Name,
                Protocol = Protocol,
                Enabled = Enabled,
                ApiKey = ApiKey,
                BaseUrl = BaseUrl,
                EnvVarName = EnvVarName,
                UseEnvVar = UseEnvVar,
                Models = new List<string>(Models),
                ApiVersion = ApiVersion
            };
            return channelEntry;
        }
        
        /// <summary>
        /// 获取有效的 API Key（UseEnvVar 启用时优先读取环境变量）
        /// </summary>
        public string GetEffectiveApiKey()
        {
            if (UseEnvVar && !string.IsNullOrEmpty(EnvVarName))
            {
                var envKey = Environment.GetEnvironmentVariable(EnvVarName);
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;
            }
            return ApiKey;
        }

        /// <summary>
        /// 当前生效的 ApiKey 是否来自环境变量
        /// </summary>
        public bool IsApiKeyFromEnv()
        {
            if (!UseEnvVar || string.IsNullOrEmpty(EnvVarName))
                return false;
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVarName));
        }

        public bool IsValid(string modelId)
        {
            if (!Enabled)
            {
                return false;
            }

            if (Models == null || !Models.Contains(modelId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(BaseUrl))
            {
                return false;
            }
            return !string.IsNullOrEmpty(GetEffectiveApiKey());
        }
    }
}
