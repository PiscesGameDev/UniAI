using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 模型注册表 — 内置预设 + 用户自定义，提供模型能力查询与端点路由。
    /// 查找优先级：用户自定义 > 内置预设 > null。
    /// </summary>
    public static class ModelRegistry
    {
        
        private const int DEFAULT_CONTEXT_WINDOW = 8192;

        private static readonly Dictionary<string, ModelEntry> _builtIn = new();

        /// <summary>
        /// 模型前缀 → 上下文窗口兜底表（未在注册表中精确命中时使用）。
        /// 按前缀长度降序排列以优先匹配更具体的前缀。
        /// </summary>
        private static readonly (string prefix, int contextWindow)[] _contextWindowFallback =
        {
            // Claude
            ("claude-opus", 200000),
            ("claude-sonnet", 200000),
            ("claude-haiku", 200000),
            ("claude-3", 200000),
            ("claude", 100000),

            // OpenAI
            ("gpt-4o", 128000),
            ("gpt-4-turbo", 128000),
            ("gpt-4-1", 1047576),
            ("gpt-4.1", 1047576),
            ("o1", 200000),
            ("o3", 200000),
            ("o4", 200000),
            ("gpt-4", 8192),
            ("gpt-3.5-turbo", 16385),

            // Google Gemini
            ("gemini-2.5", 1000000),
            ("gemini-2.0", 1048576),
            ("gemini-1.5-pro", 2097152),
            ("gemini-1.5-flash", 1048576),
            ("gemini", 32768),

            // DeepSeek
            ("deepseek-chat", 64000),
            ("deepseek-reasoner", 64000),
            ("deepseek-coder", 64000),
            ("deepseek", 32768),

            // Qwen
            ("qwen-turbo", 131072),
            ("qwen-plus", 131072),
            ("qwen-max", 32768),
            ("qwen", 8192),
        };

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

            var settings = UniAISettings.Instance;
            if (settings?.CustomModels != null)
            {
                foreach (var entry in settings.CustomModels)
                {
                    if (entry.Id == modelId) return entry;
                }
            }

            return _builtIn.GetValueOrDefault(modelId);
        }

        /// <summary>
        /// 查询模型能力集。未知模型兜底为 Chat。
        /// </summary>
        public static ModelCapability GetCapabilities(string modelId)
        {
            return Get(modelId)?.Capabilities ?? ModelCapability.Chat;
        }

        /// <summary>
        /// 判断模型是否支持指定能力
        /// </summary>
        public static bool HasCapability(string modelId, ModelCapability cap)
        {
            return (GetCapabilities(modelId) & cap) != 0;
        }

        /// <summary>
        /// 查询模型的 API 端点类型。未知模型兜底为 ChatCompletions。
        /// </summary>
        public static ModelEndpoint GetEndpoint(string modelId)
        {
            return Get(modelId)?.Endpoint ?? ModelEndpoint.ChatCompletions;
        }

        /// <summary>
        /// 根据端点类型获取 API 路径（相对于 baseUrl）
        /// </summary>
        public static string GetEndpointPath(ModelEndpoint endpoint) => endpoint switch
        {
            ModelEndpoint.ChatCompletions => "/chat/completions",
            ModelEndpoint.ImageGenerations => "/images/generations",
            ModelEndpoint.ImageEdits => "/images/edits",
            ModelEndpoint.AudioGenerations => "/audio/generations",
            ModelEndpoint.VideoGenerations => "/video/generations",
            _ => "/chat/completions"
        };

        /// <summary>
        /// 根据模型名获取 API 端点路径
        /// </summary>
        public static string GetEndpointPath(string modelId)
            => GetEndpointPath(GetEndpoint(modelId));

        /// <summary>
        /// 查询模型的上下文窗口大小（tokens）。
        /// 优先级：ModelEntry.ContextWindow（>0）> 前缀兜底表 > DEFAULT_CONTEXT_WINDOW。
        /// </summary>
        public static int GetContextWindow(string modelId)
        {
            var entry = Get(modelId);
            if (entry != null && entry.ContextWindow > 0) return entry.ContextWindow;

            if (string.IsNullOrEmpty(modelId)) return DEFAULT_CONTEXT_WINDOW;

            string lower = modelId.ToLowerInvariant();
            foreach (var (prefix, contextWindow) in _contextWindowFallback)
            {
                if (lower.StartsWith(prefix)) return contextWindow;
            }
            return DEFAULT_CONTEXT_WINDOW;
        }

        /// <summary>
        /// 获取所有内置预设模型
        /// </summary>
        public static IReadOnlyDictionary<string, ModelEntry> BuiltInModels => _builtIn;

        // ─── 内置预设 ───

        private static void RegisterPresets()
        {
            // ─── OpenAI ───
            Add("gpt-4o", "OpenAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 128000);
            Add("gpt-4o-mini", "OpenAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 128000);
            Add("gpt-4.1", "OpenAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 1047576);
            Add("o1", "OpenAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);
            Add("o3-mini", "OpenAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);
            Add("dall-e-3", "OpenAI",
                ModelCapability.ImageGen, ModelEndpoint.ImageGenerations,
                "Text-to-Image, 专用图片生成端点");

            // ─── Anthropic ───
            Add("claude-opus-4-6", "Anthropic",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);
            Add("claude-sonnet-4-6", "Anthropic",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);
            Add("claude-sonnet-4-20250514", "Anthropic",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);
            Add("claude-haiku-4-5-20251001", "Anthropic",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 200000);

            // ─── Google ───
            Add("gemini-2.0-flash", "Google",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 1048576);
            Add("gemini-2.5-pro", "Google",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 1000000);
            Add("gemini-2.5-flash", "Google",
                ModelCapability.Chat | ModelCapability.ImageGen, ModelEndpoint.ChatCompletions,
                "多模态，支持聊天与图片生成，统一走 Chat 端点",
                contextWindow: 1000000);
            Add("gemini-2.5-flash-image", "Google",
                ModelCapability.ImageGen, ModelEndpoint.ChatCompletions,
                "图片生成专用变体，走 Chat 端点");
            Add("gemini-3-pro-image-preview", "Google",
                ModelCapability.ImageGen, ModelEndpoint.ChatCompletions,
                "图片生成预览版");
            Add("gemini-3.1-flash-image-preview", "Google",
                ModelCapability.ImageGen, ModelEndpoint.ChatCompletions,
                "图片生成预览版");

            // ─── DeepSeek ───
            Add("deepseek-chat", "DeepSeek",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 64000);
            Add("deepseek-reasoner", "DeepSeek",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 64000);

            // ─── Meta (Llama) ───
            Add("llama-3.3-70b", "Meta",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 128000);
            Add("llama-3.3-8b", "Meta",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 128000);
            Add("llama-4-405b", "Meta",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 128000);

            // ─── xAI (Grok) ───
            Add("grok-2", "xAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 131072);
            Add("grok-3-preview", "xAI",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 131072);

            // ─── Alibaba (Qwen) ───
            Add("qwen-max-2025", "Alibaba",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 32768);
            Add("qwen-plus", "Alibaba",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 131072);
            Add("qwen-vl-max", "Alibaba",
                ModelCapability.Chat, ModelEndpoint.ChatCompletions,
                contextWindow: 32768);

            // ─── 专业图像生成 ───
            Add("flux-1-pro", "Black Forest Labs",
                ModelCapability.ImageGen, ModelEndpoint.ImageGenerations,
                "高质量 Text-to-Image");
            Add("flux-1-schnell", "Black Forest Labs",
                ModelCapability.ImageGen, ModelEndpoint.ImageGenerations,
                "快速 Text-to-Image");

            Add("sd-3.5-large", "Stability AI",
                ModelCapability.ImageGen | ModelCapability.ImageEdit, ModelEndpoint.ImageGenerations,
                "支持生成与 Inpainting");
            Add("sd-xl-1.0", "Stability AI",
                ModelCapability.ImageGen | ModelCapability.ImageEdit, ModelEndpoint.ImageGenerations,
                "支持生成与 Inpainting");
        }

        private static void Add(string id, string vendor,
            ModelCapability capabilities, ModelEndpoint endpoint,
            string description = null, int contextWindow = 0)
        {
            _builtIn[id] = new ModelEntry(id, vendor, capabilities, endpoint, description, contextWindow);
        }
    }
}
