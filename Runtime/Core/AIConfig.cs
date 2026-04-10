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
        /// 渠道列表（支持动态增删）
        /// </summary>
        public List<ChannelEntry> Providers = new();

        /// <summary>
        /// 当前激活的 Provider Id
        /// </summary>
        public string ActiveProviderId;

        /// <summary>
        /// 通用设置
        /// </summary>
        public GeneralConfig General = new();

        /// <summary>
        /// 获取当前激活的 Provider，找不到则返回第一个启用的
        /// </summary>
        public ChannelEntry GetActiveProvider()
        {
            if (Providers.Count == 0) return null;
            var active = Providers.Find(p => p.Id == ActiveProviderId && p.Enabled);
            return active ?? Providers.Find(p => p.Enabled) ?? Providers[0];
        }

        /// <summary>
        /// 获取所有可用模型（扁平化、去重）
        /// </summary>
        public List<string> GetAllModels()
        {
            var result = new List<string>();
            var seen = new HashSet<string>();

            foreach (var provider in Providers)
            {
                if (!provider.Enabled) continue;
                if (provider.Models == null) continue;
                foreach (var modelId in provider.Models)
                {
                    if (string.IsNullOrEmpty(modelId)) continue;
                    if (seen.Add(modelId))
                        result.Add(modelId);
                }
            }

            return result;
        }

        /// <summary>
        /// 查找所有支持指定模型的渠道（按列表顺序 = 优先级）
        /// </summary>
        public List<ChannelEntry> FindProvidersForModel(string modelId)
        {
            var result = new List<ChannelEntry>();
            if (string.IsNullOrEmpty(modelId)) return result;

            foreach (var provider in Providers)
            {
                if (!provider.Enabled) continue;
                if (provider.Models != null && provider.Models.Contains(modelId))
                    result.Add(provider);
            }
            return result;
        }
    }

    /// <summary>
    /// 单个渠道配置条目
    /// </summary>
    [Serializable]
    public class ChannelEntry
    {
        public string Id;
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
        /// 获取有效的 API Key（UseEnvVar 启用时优先读取环境变量）
        /// </summary>
        public string GetEffectiveApiKey()
        {
            if (UseEnvVar && !string.IsNullOrEmpty(EnvVarName))
            {
                var envKey = System.Environment.GetEnvironmentVariable(EnvVarName);
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
            return !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(EnvVarName));
        }
    }

    // ─── Provider 级别配置 → 已迁移到 ProviderBase.ProviderConfig ───

    [Serializable]
    public class GeneralConfig
    {
        public int TimeoutSeconds = 60;
        public AILogLevel LogLevel = AILogLevel.Info;

        /// <summary>
        /// 上下文窗口管理配置
        /// </summary>
        public ContextWindowConfig ContextWindow = new();

        /// <summary>
        /// MCP 相关配置
        /// </summary>
        public McpRuntimeConfig Mcp = new();
    }

    /// <summary>
    /// MCP 运行时配置（初始化 + 调用超时等，区别于 McpServerConfig 资产）
    /// </summary>
    [Serializable]
    public class McpRuntimeConfig
    {
        /// <summary>
        /// MCP Server 初始化超时（connect + initialize + tools/list 全流程，秒）
        /// </summary>
        public int InitTimeoutSeconds = 30;

        /// <summary>
        /// 单次 MCP Tool 调用超时（秒），0 = 不限制
        /// </summary>
        public int ToolCallTimeoutSeconds = 60;
    }

    /// <summary>
    /// 内置 Provider 预设模板
    /// </summary>
    public static class ChannelPresets
    {
        public static ChannelEntry Claude() => new()
        {
            Id = "claude", Name = "Claude", Protocol = ProviderProtocol.Claude,
            BaseUrl = "https://api.anthropic.com",
            Models = new List<string> { "claude-sonnet-4-20250514", "claude-opus-4-6" },
            ApiVersion = "2023-06-01",
            EnvVarName = "ANTHROPIC_API_KEY", UseEnvVar = true
        };

        public static ChannelEntry OpenAI() => new()
        {
            Id = "openai", Name = "OpenAI", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.openai.com/v1",
            Models = new List<string> { "gpt-4o", "gpt-4o-mini", "o1" },
            EnvVarName = "OPENAI_API_KEY", UseEnvVar = true
        };

        public static ChannelEntry Gemini() => new()
        {
            Id = "gemini", Name = "Gemini", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            Models = new List<string> { "gemini-2.0-flash", "gemini-2.5-pro" },
            EnvVarName = "GEMINI_API_KEY", UseEnvVar = true
        };

        public static ChannelEntry DeepSeek() => new()
        {
            Id = "deepseek", Name = "DeepSeek", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.deepseek.com/v1",
            Models = new List<string> { "deepseek-chat", "deepseek-reasoner" },
            EnvVarName = "DEEPSEEK_API_KEY", UseEnvVar = true
        };

        /// <summary>
        /// 创建默认 Provider 列表（Claude + OpenAI + Gemini + DeepSeek）
        /// </summary>
        public static List<ChannelEntry> CreateDefaults() => new() { Claude(), OpenAI(), Gemini(), DeepSeek() };

        private static readonly HashSet<string> _presetIds = new() { "claude", "openai", "gemini", "deepseek" };

        /// <summary>
        /// 判断是否为内置预设 Provider
        /// </summary>
        public static bool IsPresetId(string id) => _presetIds.Contains(id);

        /// <summary>
        /// 所有可用预设（用于 Add Provider 菜单）
        /// 每次调用返回新实例，调用方可安全修改
        /// </summary>
        public static ChannelEntry[] All => new[] { Claude(), OpenAI(), Gemini(), DeepSeek() };
    }
}
