using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 内置提供商渠道、模型预设和上下文回退的单一数据源
    /// </summary>
    public static class BuiltInPresetCatalog
    {
        public const string ChannelClaude = "Claude";
        public const string ChannelOpenAI = "OpenAI";
        public const string ChannelGemini = "Gemini";
        public const string ChannelDeepSeek = "DeepSeek";

        public readonly struct ContextWindowFallback
        {
            public readonly string Prefix;
            public readonly int ContextWindow;

            public ContextWindowFallback(string prefix, int contextWindow)
            {
                Prefix = prefix;
                ContextWindow = contextWindow;
            }
        }

        private static readonly ChannelPreset[] _channels =
        {
            new ChannelPreset(
                ChannelClaude,
                ProviderProtocol.Claude,
                "https://api.anthropic.com",
                "ANTHROPIC_API_KEY",
                apiVersion: "2023-06-01"),
            new ChannelPreset(
                ChannelOpenAI,
                ProviderProtocol.OpenAI,
                "https://api.openai.com/v1",
                "OPENAI_API_KEY"),
            new ChannelPreset(
                ChannelGemini,
                ProviderProtocol.OpenAI,
                "https://generativelanguage.googleapis.com/v1beta/openai",
                "GEMINI_API_KEY"),
            new ChannelPreset(
                ChannelDeepSeek,
                ProviderProtocol.OpenAI,
                "https://api.deepseek.com/v1",
                "DEEPSEEK_API_KEY"),
        };

        private static readonly ModelPreset[] _models =
        {
            // OpenAI 模型
            Chat("gpt-5.5", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 1050000, ChannelOpenAI),
            Chat("gpt-5.4", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 1050000, ChannelOpenAI),
            Chat("gpt-5.2", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 400000, ChannelOpenAI),
            Chat("gpt-5.1", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 400000, ChannelOpenAI),
            Chat("gpt-5", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 400000, ChannelOpenAI),
            Chat("gpt-4.1", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 1047576, ChannelOpenAI),
            Chat("gpt-4o", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 128000, ChannelOpenAI),
            Chat("gpt-4o-mini", "OpenAI", ModelCapability.Chat | ModelCapability.VisionInput, 128000, ChannelOpenAI),
            Chat("o1", "OpenAI", ModelCapability.Chat, 200000, ChannelOpenAI),
            Chat("o3-mini", "OpenAI", ModelCapability.Chat, 200000),
            new(
                "dall-e-3",
                "OpenAI",
                ModelCapability.ImageGen,
                ModelEndpoint.ImageGenerations,
                "Text-to-Image, 专用图片生成端点"),
            new(
                "gpt-image-2",
                "OpenAI",
                ModelCapability.ImageGen | ModelCapability.ImageEdit,
                ModelEndpoint.ImageGenerations,
                "GPT Image 2, 图片生成与编辑模型"),

            // Anthropic 模型
            Chat("claude-sonnet-4-20250514", "Anthropic", ModelCapability.Chat | ModelCapability.VisionInput, 200000, ChannelClaude),
            Chat("claude-opus-4-6", "Anthropic", ModelCapability.Chat | ModelCapability.VisionInput, 200000, ChannelClaude),
            Chat("claude-opus-4-7", "Anthropic", ModelCapability.Chat | ModelCapability.VisionInput, 200000),
            Chat("claude-sonnet-4-6", "Anthropic", ModelCapability.Chat | ModelCapability.VisionInput, 200000),
            Chat("claude-haiku-4-5-20251001", "Anthropic", ModelCapability.Chat | ModelCapability.VisionInput, 200000),

            // Google 模型
            Chat("gemini-2.0-flash", "Google", ModelCapability.Chat | ModelCapability.VisionInput, 1048576, ChannelGemini),
            Chat("gemini-2.5-pro", "Google", ModelCapability.Chat | ModelCapability.VisionInput, 1000000, ChannelGemini),
            new(
                "gemini-2.5-flash",
                "Google",
                ModelCapability.Chat | ModelCapability.VisionInput | ModelCapability.ImageGen,
                ModelEndpoint.ChatCompletions,
                "多模态，支持聊天与图片生成，统一走 Chat 端点",
                1000000),
            new(
                "gemini-2.5-flash-image",
                "Google",
                ModelCapability.ImageGen,
                ModelEndpoint.ChatCompletions,
                "图片生成专用变体，走 Chat 端点"),
            new(
                "gemini-3-pro-image-preview",
                "Google",
                ModelCapability.ImageGen,
                ModelEndpoint.ChatCompletions,
                "图片生成预览版"),
            new(
                "gemini-3.1-flash-image-preview",
                "Google",
                ModelCapability.ImageGen,
                ModelEndpoint.ChatCompletions,
                "图片生成预览版"),

            // DeepSeek 模型
            Chat("deepseek-v4-flash", "DeepSeek", ModelCapability.Chat, 1000000, ChannelDeepSeek),
            Chat("deepseek-v4-pro", "DeepSeek", ModelCapability.Chat, 1000000, ChannelDeepSeek),

            // Meta（Llama）模型
            Chat("llama-3.3-70b", "Meta", ModelCapability.Chat, 128000),
            Chat("llama-3.3-8b", "Meta", ModelCapability.Chat, 128000),
            Chat("llama-4-405b", "Meta", ModelCapability.Chat, 128000),

            // xAI（Grok）模型
            Chat("grok-2", "xAI", ModelCapability.Chat, 131072),
            Chat("grok-3-preview", "xAI", ModelCapability.Chat, 131072),

            // 阿里巴巴（Qwen）
            Chat("qwen-turbo", "Alibaba", ModelCapability.Chat, 131072),
            Chat("qwen-plus", "Alibaba", ModelCapability.Chat, 131072),
            Chat("qwen-max", "Alibaba", ModelCapability.Chat, 32768),
            new(
                "qwen-max-longcontext",
                "Alibaba",
                ModelCapability.Chat,
                ModelEndpoint.ChatCompletions,
                "Long context variant",
                131072),
            new(
                "qwen-vl-max",
                "Alibaba",
                ModelCapability.Chat | ModelCapability.VisionInput,
                ModelEndpoint.ChatCompletions,
                "Vision-language model",
                131072),
            new(
                "qwen3-235b-a22b",
                "Alibaba",
                ModelCapability.Chat,
                ModelEndpoint.ChatCompletions,
                "Qwen3 series large model",
                129024),
            new(
                "text-embedding-v1",
                "Alibaba",
                ModelCapability.Embedding,
                ModelEndpoint.Embeddings,
                "Text embedding model",
                2048),
            new(
                "gte-rerank-v2",
                "Alibaba",
                ModelCapability.Rerank,
                ModelEndpoint.Rerank,
                "Text rerank model",
                4000),

            // 专业图像生成
            new(
                "flux-1-pro",
                "Black Forest Labs",
                ModelCapability.ImageGen,
                ModelEndpoint.ImageGenerations,
                "高质量 Text-to-Image"),
            new(
                "flux-1-schnell",
                "Black Forest Labs",
                ModelCapability.ImageGen,
                ModelEndpoint.ImageGenerations,
                "快速 Text-to-Image"),
            new(
                "sd-3.5-large",
                "Stability AI",
                ModelCapability.ImageGen | ModelCapability.ImageEdit,
                ModelEndpoint.ImageGenerations,
                "支持生成与 Inpainting"),
            new(
                "sd-xl-1.0",
                "Stability AI",
                ModelCapability.ImageGen | ModelCapability.ImageEdit,
                ModelEndpoint.ImageGenerations,
                "支持生成与 Inpainting"),
        };

        private static readonly ContextWindowFallback[] _contextWindowFallbacks =
        {
            // 上下文兜底：Claude
            new("claude-opus", 200000),
            new("claude-sonnet", 200000),
            new("claude-haiku", 200000),
            new("claude-3", 200000),
            new("claude", 100000),

            // 上下文兜底：OpenAI
            new("gpt-5.5", 1050000),
            new("gpt-5.4", 1050000),
            new("gpt-5", 400000),
            new("gpt-4o", 128000),
            new("gpt-4-turbo", 128000),
            new("gpt-4-1", 1047576),
            new("gpt-4.1", 1047576),
            new("o1", 200000),
            new("o3", 200000),
            new("o4", 200000),
            new("gpt-4", 8192),
            new("gpt-3.5-turbo", 16385),

            // 上下文兜底：Google Gemini
            new("gemini-2.5", 1000000),
            new("gemini-2.0", 1048576),
            new("gemini-1.5-pro", 2097152),
            new("gemini-1.5-flash", 1048576),
            new("gemini", 32768),

            // 上下文兜底：DeepSeek
            new("deepseek-v4-flash", 1000000),
            new("deepseek-v4-pro", 1000000),

            // 上下文兜底：Qwen
            new("qwen-turbo", 131072),
            new("qwen-plus", 131072),
            new("qwen-max", 32768),
            new("qwen-max-longcontext", 131072),
            new("qwen-vl-max", 131072),
            new("qwen3-235b-a22b", 129024),
            new("text-embedding-v1", 2048),
            new("gte-rerank-v2", 4000),
            new("qwen", 8192),
        };

        public static IReadOnlyList<ChannelPreset> Channels => _channels;
        public static IReadOnlyList<ModelPreset> Models => _models;
        public static IReadOnlyList<ContextWindowFallback> ContextWindowFallbacks => _contextWindowFallbacks;

        public static ChannelPreset GetChannel(string channelName)
        {
            foreach (var channel in _channels)
            {
                if (channel.Name == channelName)
                    return channel;
            }

            return null;
        }

        public static IReadOnlyList<string> GetDefaultModelIds(string channelName)
        {
            return CreateDefaultModelIds(channelName).ToArray();
        }

        public static List<string> CreateDefaultModelIds(string channelName)
        {
            var modelIds = new List<string>();
            foreach (var model in _models)
            {
                if (model.IsDefaultForChannel(channelName))
                    modelIds.Add(model.Id);
            }

            return modelIds;
        }

        public static ChannelEntry CreateChannelEntry(string channelName)
        {
            var preset = GetChannel(channelName);
            if (preset == null)
                throw new KeyNotFoundException($"Built-in channel preset '{channelName}' was not found.");

            return new ChannelEntry
            {
                Name = preset.Name,
                Protocol = preset.Protocol,
                BaseUrl = preset.BaseUrl,
                EnvVarName = preset.EnvVarName,
                UseEnvVar = preset.UseEnvVar,
                ApiVersion = preset.ApiVersion,
                Models = CreateDefaultModelIds(preset.Name)
            };
        }

        public static List<ChannelEntry> CreateDefaultChannelEntries()
        {
            var entries = new List<ChannelEntry>(_channels.Length);
            foreach (var channel in _channels)
            {
                entries.Add(CreateChannelEntry(channel.Name));
            }

            return entries;
        }

        private static ModelPreset Chat(
            string id,
            string vendor,
            ModelCapability capabilities,
            int contextWindow,
            params string[] defaultChannels)
        {
            return new ModelPreset(
                id,
                vendor,
                capabilities,
                ModelEndpoint.ChatCompletions,
                contextWindow: contextWindow,
                defaultChannels: defaultChannels);
        }
    }
}
