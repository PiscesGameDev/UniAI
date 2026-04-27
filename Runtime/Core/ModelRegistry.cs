using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 模型查询注册表，由内置预设和用户自定义模型组成。
    /// 查找优先级：用户自定义模型 > 内置预设 > null。
    /// </summary>
    public static class ModelRegistry
    {
        private const int DEFAULT_CONTEXT_WINDOW = 8192;

        private static readonly Dictionary<string, ModelEntry> _builtIn = BuildBuiltInModels();

        /// <summary>
        /// 查询模型元信息。查找优先级：用户自定义模型 > 内置预设 > null。
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
        /// 查询模型能力。未知模型默认视为 Chat。
        /// </summary>
        public static ModelCapability GetCapabilities(string modelId)
        {
            return Get(modelId)?.Capabilities ?? ModelCapability.Chat;
        }

        /// <summary>
        /// 判断模型是否支持指定能力。
        /// </summary>
        public static bool HasCapability(string modelId, ModelCapability cap)
        {
            return (GetCapabilities(modelId) & cap) != 0;
        }

        /// <summary>
        /// 查询模型使用的 API 端点类型。未知模型默认走 ChatCompletions。
        /// </summary>
        public static ModelEndpoint GetEndpoint(string modelId)
        {
            return Get(modelId)?.Endpoint ?? ModelEndpoint.ChatCompletions;
        }

        /// <summary>
        /// 根据端点类型获取相对于 baseUrl 的 API 路径。
        /// </summary>
        public static string GetEndpointPath(ModelEndpoint endpoint) => endpoint switch
        {
            ModelEndpoint.ChatCompletions => "/chat/completions",
            ModelEndpoint.Embeddings => "/embeddings",
            ModelEndpoint.ImageGenerations => "/images/generations",
            ModelEndpoint.ImageEdits => "/images/edits",
            ModelEndpoint.AudioGenerations => "/audio/generations",
            ModelEndpoint.VideoGenerations => "/video/generations",
            ModelEndpoint.Rerank => "/services/rerank/text-rerank/text-rerank",
            _ => "/chat/completions"
        };

        /// <summary>
        /// 根据模型 ID 获取 API 端点路径。
        /// </summary>
        public static string GetEndpointPath(string modelId)
            => GetEndpointPath(GetEndpoint(modelId));

        /// <summary>
        /// 查询模型上下文窗口大小（tokens）。
        /// 优先级：ModelEntry.ContextWindow（>0）> 前缀兜底 > 默认值。
        /// </summary>
        public static int GetContextWindow(string modelId)
        {
            var entry = Get(modelId);
            if (entry is { ContextWindow: > 0 }) return entry.ContextWindow;

            if (string.IsNullOrEmpty(modelId)) return DEFAULT_CONTEXT_WINDOW;

            string lower = modelId.ToLowerInvariant();
            foreach (var fallback in BuiltInPresetCatalog.ContextWindowFallbacks)
            {
                if (lower.StartsWith(fallback.Prefix))
                    return fallback.ContextWindow;
            }

            return DEFAULT_CONTEXT_WINDOW;
        }

        /// <summary>
        /// 获取所有内置模型查询条目。
        /// </summary>
        public static IReadOnlyDictionary<string, ModelEntry> BuiltInModels => _builtIn;

        private static Dictionary<string, ModelEntry> BuildBuiltInModels()
        {
            var result = new Dictionary<string, ModelEntry>();
            foreach (var preset in BuiltInPresetCatalog.Models)
            {
                if (preset == null || string.IsNullOrEmpty(preset.Id))
                    continue;

                result[preset.Id] = preset.ToModelEntry();
            }

            return result;
        }
    }
}
