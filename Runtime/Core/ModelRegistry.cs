using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 模型注册表 — 内置预设 + 用户自定义，提供模型能力查询。
    /// 查找优先级：用户自定义 > 内置预设 > 兜底 Chat。
    /// </summary>
    public static class ModelRegistry
    {
        private static readonly Dictionary<string, ModelEntry> _builtIn = new();

        static ModelRegistry()
        {
            RegisterPresets();
        }

        /// <summary>
        /// 查询模型定义。优先级：用户自定义 > 内置预设 > null。
        /// </summary>
        public static ModelEntry Get(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return null;

            // 用户自定义优先
            var settings = UniAISettings.Instance;
            if (settings?.CustomModels != null)
            {
                foreach (var entry in settings.CustomModels)
                {
                    if (entry.Id == modelId) return entry;
                }
            }

            // 内置预设
            return _builtIn.GetValueOrDefault(modelId);
        }

        /// <summary>
        /// 查询模型能力。未知模型兜底为 Chat。
        /// </summary>
        public static ModelCapability GetCapability(string modelId)
        {
            return Get(modelId)?.Capability ?? ModelCapability.Chat;
        }

        /// <summary>
        /// 根据能力类型获取 API 端点路径（相对于 baseUrl）
        /// </summary>
        public static string GetEndpointPath(ModelCapability capability) => capability switch
        {
            ModelCapability.Chat => "/chat/completions",
            ModelCapability.ImageGen => "/images/generations",
            ModelCapability.AudioGen => "/audio/generations",
            ModelCapability.VideoGen => "/video/generations",
            _ => "/chat/completions"
        };

        /// <summary>
        /// 根据模型名获取 API 端点路径
        /// </summary>
        public static string GetEndpointPath(string modelId)
            => GetEndpointPath(GetCapability(modelId));

        /// <summary>
        /// 获取所有内置预设模型
        /// </summary>
        public static IReadOnlyDictionary<string, ModelEntry> BuiltInModels => _builtIn;

        // ─── 内置预设 ───

        private static void RegisterPresets()
        {
            // ─── OpenAI ───
            Add("gpt-4o", "GPT-4o (Omni)", "OpenAI", ModelCapability.Chat);
            Add("gpt-4o-mini", "GPT-4o Mini", "OpenAI", ModelCapability.Chat);
            Add("gpt-4.1", "GPT-4.1", "OpenAI", ModelCapability.Chat);
            Add("o1", "o1 (Reasoning)", "OpenAI", ModelCapability.Chat);
            Add("o3-mini", "o3-mini", "OpenAI", ModelCapability.Chat);
            Add("dall-e-3", "DALL·E 3", "OpenAI", ModelCapability.ImageGen);

            // ─── Anthropic ───
            Add("claude-opus-4-6", "Claude Opus 4.6", "Anthropic", ModelCapability.Chat);
            Add("claude-sonnet-4-6", "Claude Sonnet 4.6", "Anthropic", ModelCapability.Chat);
            Add("claude-sonnet-4-20250514", "Claude Sonnet 4", "Anthropic", ModelCapability.Chat);
            Add("claude-haiku-4-5-20251001", "Claude Haiku 4.5", "Anthropic", ModelCapability.Chat);

            // ─── Google ───
            Add("gemini-2.0-flash", "Gemini 2.0 Flash", "Google", ModelCapability.Chat);
            Add("gemini-2.5-pro", "Gemini 2.5 Pro", "Google", ModelCapability.Chat);
            Add("gemini-2.5-flash", "Gemini 2.5 Flash", "Google", ModelCapability.Chat);
            Add("gemini-2.5-flash-image", "Gemini 2.5 Flash Image", "Google", ModelCapability.ImageGen);
            Add("gemini-3-pro-image-preview", "Gemini 3 Pro Image", "Google", ModelCapability.ImageGen);
            Add("gemini-3.1-flash-image-preview", "Gemini 3.1 Flash Image", "Google", ModelCapability.ImageGen);

            // ─── DeepSeek ───
            Add("deepseek-chat", "DeepSeek V3", "DeepSeek", ModelCapability.Chat);
            Add("deepseek-reasoner", "DeepSeek R1", "DeepSeek", ModelCapability.Chat);

            // ─── Meta (Llama) ───
            Add("llama-3.3-70b", "Llama 3.3 70B", "Meta", ModelCapability.Chat);
            Add("llama-3.3-8b", "Llama 3.3 8B", "Meta", ModelCapability.Chat);
            Add("llama-4-405b", "Llama 4 405B", "Meta", ModelCapability.Chat);

            // ─── xAI (Grok) ───
            Add("grok-2", "Grok 2", "xAI", ModelCapability.Chat);
            Add("grok-3-preview", "Grok 3 Preview", "xAI", ModelCapability.Chat);

            // ─── Alibaba (Qwen) ───
            Add("qwen-max-2025", "Qwen Max", "Alibaba", ModelCapability.Chat);
            Add("qwen-plus", "Qwen Plus", "Alibaba", ModelCapability.Chat);
            Add("qwen-vl-max", "Qwen VL Max", "Alibaba", ModelCapability.Chat);

            // ─── 专业图像生成 (ImageGen) ───
            // FLUX 在 2025-2026 年是高质量生成的首选
            Add("flux-1-pro", "FLUX.1 Pro", "Black Forest Labs", ModelCapability.ImageGen);
            Add("flux-1-schnell", "FLUX.1 Schnell", "Black Forest Labs", ModelCapability.ImageGen);

            // Stable Diffusion 依然是本地或中转站的主力
            Add("sd-3.5-large", "SD 3.5 Large", "Stability AI", ModelCapability.ImageGen);
            Add("sd-xl-1.0", "SDXL 1.0", "Stability AI", ModelCapability.ImageGen);
        }

        private static void Add(string id, string displayName, string vendor, ModelCapability capability)
        {
            _builtIn[id] = new ModelEntry(id, displayName, vendor, capability);
        }
    }
}
