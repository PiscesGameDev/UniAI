using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// Provider 使用的 API 协议类型
    /// </summary>
    public enum ProviderProtocol
    {
        Claude,
        OpenAI
    }

    public enum AILogLevel
    {
        Verbose,
        Info,
        Warning,
        Error,
        None
    }

    /// <summary>
    /// AI 框架配置 — 动态 Provider 列表
    /// </summary>
    [Serializable]
    public class AIConfig
    {
        /// <summary>
        /// Provider 列表（支持动态增删）
        /// </summary>
        public List<ProviderEntry> Providers = new();

        /// <summary>
        /// 当前激活的 Provider Id
        /// </summary>
        public string ActiveProviderId;

        /// <summary>
        /// 通用设置
        /// </summary>
        public GeneralConfig General = new();

        /// <summary>
        /// 获取当前激活的 Provider，找不到则返回第一个
        /// </summary>
        public ProviderEntry GetActiveProvider()
        {
            if (Providers.Count == 0) return null;
            return Providers.Find(p => p.Id == ActiveProviderId) ?? Providers[0];
        }
    }

    /// <summary>
    /// 单个 Provider 配置条目
    /// </summary>
    [Serializable]
    public class ProviderEntry
    {
        public string Id;
        public string Name;
        public ProviderProtocol Protocol;
        public string ApiKey;
        public string BaseUrl;
        public string Model;
        /// <summary>
        /// Claude 协议专用：API 版本号
        /// </summary>
        public string ApiVersion;
        /// <summary>
        /// 图标文件名（不含扩展名），对应 Editor/Icons/ 目录
        /// </summary>
        public string IconName;
        /// <summary>
        /// 环境变量名（用于自动读取 API Key）
        /// </summary>
        public string EnvVarName;

        /// <summary>
        /// 获取有效的 API Key（环境变量优先，其次使用配置值）
        /// </summary>
        public string GetEffectiveApiKey()
        {
            if (!string.IsNullOrEmpty(EnvVarName))
            {
                var envKey = Environment.GetEnvironmentVariable(EnvVarName);
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;
            }
            return ApiKey;
        }

        /// <summary>
        /// 当前 ApiKey 是否来自环境变量
        /// </summary>
        public bool IsApiKeyFromEnv()
        {
            if (string.IsNullOrEmpty(EnvVarName)) return false;
            var envKey = System.Environment.GetEnvironmentVariable(EnvVarName);
            return !string.IsNullOrEmpty(envKey) && ApiKey == envKey;
        }
    }

    // ─── Provider 级别配置（供直接构造 Provider 使用）───

    [Serializable]
    public class ClaudeConfig
    {
        public string ApiKey;
        public string BaseUrl = "https://api.anthropic.com";
        public string Model = "claude-sonnet-4-20250514";
        public string ApiVersion = "2023-06-01";
    }

    [Serializable]
    public class OpenAIConfig
    {
        public string ApiKey;
        public string BaseUrl = "https://api.openai.com/v1";
        public string Model = "gpt-4o";
    }

    [Serializable]
    public class GeneralConfig
    {
        public int TimeoutSeconds = 60;
        public AILogLevel LogLevel = AILogLevel.Info;
    }

    /// <summary>
    /// 内置 Provider 预设模板
    /// </summary>
    public static class ProviderPresets
    {
        public static ProviderEntry Claude() => new()
        {
            Id = "claude", Name = "Claude", Protocol = ProviderProtocol.Claude,
            BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-20250514",
            ApiVersion = "2023-06-01", IconName = "provider-claude", EnvVarName = "ANTHROPIC_API_KEY"
        };

        public static ProviderEntry OpenAI() => new()
        {
            Id = "openai", Name = "OpenAI", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o",
            IconName = "provider-openai", EnvVarName = "OPENAI_API_KEY"
        };

        public static ProviderEntry Gemini() => new()
        {
            Id = "gemini", Name = "Gemini", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai", Model = "gemini-2.0-flash",
            IconName = "provider-gemini", EnvVarName = "GEMINI_API_KEY"
        };

        public static ProviderEntry DeepSeek() => new()
        {
            Id = "deepseek", Name = "DeepSeek", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat",
            IconName = "provider-deepseek", EnvVarName = "DEEPSEEK_API_KEY"
        };

        /// <summary>
        /// 创建默认 Provider 列表（Claude + OpenAI + Gemini + DeepSeek）
        /// </summary>
        public static List<ProviderEntry> CreateDefaults() => new() { Claude(), OpenAI(), Gemini(), DeepSeek() };

        private static readonly HashSet<string> _presetIds = new() { "claude", "openai", "gemini", "deepseek" };

        /// <summary>
        /// 判断是否为内置预设 Provider
        /// </summary>
        public static bool IsPresetId(string id) => _presetIds.Contains(id);

        /// <summary>
        /// 所有可用预设（用于 Add Provider 菜单）
        /// </summary>
        public static ProviderEntry[] All => new[] { Claude(), OpenAI(), Gemini(), DeepSeek() };
    }
}
