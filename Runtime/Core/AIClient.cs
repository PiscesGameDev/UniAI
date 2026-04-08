using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniAI.Providers;
using UniAI.Providers.Claude;
using UniAI.Providers.OpenAI;

namespace UniAI
{
    /// <summary>
    /// AI 客户端 — 框架的唯一入口
    /// </summary>
    public class AIClient
    {
        private readonly IAIProvider _provider;

        /// <summary>
        /// 当前 Provider 名称
        /// </summary>
        public string ProviderName => _provider.Name;

        /// <summary>
        /// 使用指定 Provider 创建客户端
        /// </summary>
        public AIClient(IAIProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 从配置创建客户端（使用 ActiveProvider 的 DefaultModel）
        /// </summary>
        public static AIClient Create(AIConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var entry = config.GetActiveProvider()
                ?? throw new InvalidOperationException("No provider configured in AIConfig.");

            return Create(entry, entry.DefaultModel, config.General);
        }

        /// <summary>
        /// 从配置 + 模型名创建客户端（自动路由到对应渠道，支持多渠道故障转移）
        /// </summary>
        public static AIClient Create(AIConfig config, string modelId)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(modelId)) throw new ArgumentNullException(nameof(modelId));

            var providers = config.FindProvidersForModel(modelId);
            if (providers.Count == 0)
                throw new InvalidOperationException($"No provider found for model '{modelId}'.");

            var general = config.General ?? new GeneralConfig();

            if (providers.Count == 1)
            {
                return Create(providers[0], modelId, general);
            }

            // 多渠道 → 使用 FallbackProvider
            var innerProviders = new List<IAIProvider>();
            foreach (var entry in providers)
            {
                var apiKey = entry.ApiKey;
                if (string.IsNullOrEmpty(apiKey)) continue;
                innerProviders.Add(CreateProvider(entry, modelId, general));
            }

            if (innerProviders.Count == 0)
                throw new InvalidOperationException($"No provider with API key found for model '{modelId}'.");

            if (innerProviders.Count == 1)
                return new AIClient(innerProviders[0]);

            var fallback = new FallbackProvider(innerProviders);

            AILogger.Info($"AIClient created with {innerProviders.Count} fallback providers for model: {modelId}");
            return new AIClient(fallback);
        }

        /// <summary>
        /// 从单个 ChannelEntry + 指定模型创建客户端
        /// </summary>
        public static AIClient Create(ChannelEntry entry, string modelId = null, GeneralConfig general = null)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            general ??= new GeneralConfig();
            modelId ??= entry.DefaultModel;

            var provider = CreateProvider(entry, modelId, general);

            AILogger.Info($"AIClient created with provider: {entry.Name} ({entry.Protocol}), model: {modelId}");

            return new AIClient(provider);
        }

        /// <summary>
        /// 创建具体的 IAIProvider 实例
        /// </summary>
        private static IAIProvider CreateProvider(ChannelEntry entry, string modelId, GeneralConfig general)
        {
            return entry.Protocol switch
            {
                ProviderProtocol.Claude => new ClaudeProvider(
                    new ClaudeConfig
                    {
                        ApiKey = entry.ApiKey,
                        BaseUrl = entry.BaseUrl,
                        Model = modelId ?? entry.DefaultModel,
                        ApiVersion = entry.ApiVersion ?? "2023-06-01"
                    },
                    general.TimeoutSeconds),
                ProviderProtocol.OpenAI => new OpenAIProvider(
                    new OpenAIConfig
                    {
                        ApiKey = entry.ApiKey,
                        BaseUrl = entry.BaseUrl,
                        Model = modelId ?? entry.DefaultModel
                    },
                    general.TimeoutSeconds),
                _ => throw new NotSupportedException(
                    $"Protocol '{entry.Protocol}' is not supported. Use AIClient(IAIProvider) for custom protocols.")
            };
        }

        /// <summary>
        /// 发送请求获取完整响应
        /// </summary>
        public UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            return _provider.SendAsync(request, ct);
        }

        /// <summary>
        /// 发送请求并自动反序列化为 T（结构化输出）
        /// </summary>
        public async UniTask<AITypedResponse<T>> SendAsync<T>(AIRequest request, CancellationToken ct = default)
        {
            var response = await _provider.SendAsync(request, ct);
            return AITypedResponse<T>.FromResponse(response);
        }

        /// <summary>
        /// 流式发送请求
        /// </summary>
        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            return _provider.StreamAsync(request, ct);
        }

        /// <summary>
        /// 便捷 Chat：直接发送单条消息获取完整响应，不经过 Agent
        /// </summary>
        public UniTask<AIResponse> ChatAsync(string userMessage, string systemPrompt = null, CancellationToken ct = default)
        {
            var request = new AIRequest
            {
                SystemPrompt = systemPrompt,
                Messages = { AIMessage.User(userMessage) }
            };
            return SendAsync(request, ct);
        }

        /// <summary>
        /// 便捷 Chat：发送单条消息并自动反序列化为 T（结构化输出）
        /// </summary>
        public UniTask<AITypedResponse<T>> ChatAsync<T>(
            string userMessage, AIResponseFormat responseFormat,
            string systemPrompt = null, CancellationToken ct = default)
        {
            var request = new AIRequest
            {
                SystemPrompt = systemPrompt,
                ResponseFormat = responseFormat,
                Messages = { AIMessage.User(userMessage) }
            };
            return SendAsync<T>(request, ct);
        }

        /// <summary>
        /// 便捷 Chat：流式响应
        /// </summary>
        public IUniTaskAsyncEnumerable<AIStreamChunk> ChatStreamAsync(string userMessage, string systemPrompt = null, CancellationToken ct = default)
        {
            var request = new AIRequest
            {
                SystemPrompt = systemPrompt,
                Messages = { AIMessage.User(userMessage) }
            };
            return StreamAsync(request, ct);
        }
    }
}
