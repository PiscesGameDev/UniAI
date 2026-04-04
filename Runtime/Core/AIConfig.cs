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
    /// 模型路由信息 — 模型名 + 首选渠道
    /// </summary>
    public struct ModelRoute
    {
        /// <summary>
        /// 模型名称（如 "claude-opus-4-6"）
        /// </summary>
        public string ModelId;

        /// <summary>
        /// 首选渠道（优先级最高的）
        /// </summary>
        public ProviderEntry Provider;
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

        /// <summary>
        /// 获取所有可用模型（扁平化、去重，同名模型取优先级最高的渠道）
        /// </summary>
        public List<ModelRoute> GetAllModels()
        {
            var result = new List<ModelRoute>();
            var seen = new HashSet<string>();

            foreach (var provider in Providers)
            {
                if (provider.Models == null) continue;
                foreach (var modelId in provider.Models)
                {
                    if (string.IsNullOrEmpty(modelId)) continue;
                    if (seen.Add(modelId))
                    {
                        result.Add(new ModelRoute { ModelId = modelId, Provider = provider });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 查找所有支持指定模型的渠道（按列表顺序 = 优先级）
        /// </summary>
        public List<ProviderEntry> FindProvidersForModel(string modelId)
        {
            var result = new List<ProviderEntry>();
            if (string.IsNullOrEmpty(modelId)) return result;

            foreach (var provider in Providers)
            {
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
    public class ProviderEntry
    {
        public string Id;
        public string Name;
        public ProviderProtocol Protocol;
        public string ApiKey;
        public string BaseUrl;

        /// <summary>
        /// 该渠道支持的模型列表
        /// </summary>
        public List<string> Models = new();

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
        /// 默认模型（Models 列表中的第一个）
        /// </summary>
        public string DefaultModel => Models?.Count > 0 ? Models[0] : null;

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
            var envKey = Environment.GetEnvironmentVariable(EnvVarName);
            return !string.IsNullOrEmpty(envKey) && ApiKey == envKey;
        }
    }

    // ─── Provider 级别配置（供直接构造 Provider 使用）───

    [Serializable]
    public class ClaudeConfig
    {
        public string ApiKey;
        public string BaseUrl = "https://api.anthropic.com";
        public string Model;
        public string ApiVersion = "2023-06-01";
    }

    [Serializable]
    public class OpenAIConfig
    {
        public string ApiKey;
        public string BaseUrl = "https://api.openai.com/v1";
        public string Model;
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
            BaseUrl = "https://api.anthropic.com",
            Models = new List<string> { "claude-sonnet-4-20250514", "claude-opus-4-6" },
            ApiVersion = "2023-06-01", IconName = "provider-claude", EnvVarName = "ANTHROPIC_API_KEY"
        };

        public static ProviderEntry OpenAI() => new()
        {
            Id = "openai", Name = "OpenAI", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.openai.com/v1",
            Models = new List<string> { "gpt-4o", "gpt-4o-mini", "o1" },
            IconName = "provider-openai", EnvVarName = "OPENAI_API_KEY"
        };

        public static ProviderEntry Gemini() => new()
        {
            Id = "gemini", Name = "Gemini", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            Models = new List<string> { "gemini-2.0-flash", "gemini-2.5-pro" },
            IconName = "provider-gemini", EnvVarName = "GEMINI_API_KEY"
        };

        public static ProviderEntry DeepSeek() => new()
        {
            Id = "deepseek", Name = "DeepSeek", Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.deepseek.com/v1",
            Models = new List<string> { "deepseek-chat", "deepseek-reasoner" },
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
