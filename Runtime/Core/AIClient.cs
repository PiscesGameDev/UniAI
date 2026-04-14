using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// AI 客户端 — 框架的唯一入口。
    /// 路由模式: Create(config) — 不绑定模型，发送时从 request.Model 路由，委托 ChannelManager 处理缓存和故障转移。
    /// 直连模式: Create(entry, modelId, general) 或 new AIClient(provider) — 用于测试连接等场景。
    /// </summary>
    public class AIClient
    {
        // 路由模式
        private readonly AIConfig _config;

        // 直连模式
        private readonly IAIProvider _provider;

        private bool IsRouted => _config != null;

        /// <summary>
        /// 当前 Provider 名称（直连模式返回 Provider 名称，路由模式返回 "ChannelManager"）
        /// </summary>
        public string ProviderName => IsRouted ? "ChannelManager" : _provider.Name;

        /// <summary>
        /// 路由模式 — 只记录 config，不创建 Provider
        /// </summary>
        private AIClient(AIConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 直连模式 — 使用指定 Provider
        /// </summary>
        public AIClient(IAIProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 从配置创建客户端（路由模式，模型在发送时从 request.Model 解析）
        /// </summary>
        public static AIClient Create(AIConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            AILogger.Info("AIClient created in routed mode");
            return new AIClient(config);
        }

        /// <summary>
        /// 从单个 ChannelEntry + 指定模型创建客户端（直连模式，用于测试连接等）
        /// </summary>
        public static AIClient Create(ChannelEntry entry, string modelId = null, GeneralConfig general = null)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            general ??= new GeneralConfig();
            modelId ??= entry.DefaultModel;

            var provider = ChannelManager.CreateProvider(entry, modelId, general);

            AILogger.Info($"AIClient created in direct mode: {entry.Name} ({entry.Protocol}), model: {modelId}");

            return new AIClient(provider);
        }

        /// <summary>
        /// 发送请求获取完整响应（路由模式下 request.Model 必须指定）
        /// </summary>
        public UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            if (!IsRouted)
                return _provider.SendAsync(request, ct);

            if (string.IsNullOrEmpty(request.Model))
                return UniTask.FromResult(AIResponse.Fail("request.Model is required in routed mode."));

            return ChannelManager.SendAsync(_config, request.Model, request, ct);
        }

        /// <summary>
        /// 发送请求并自动反序列化为 T（结构化输出）
        /// </summary>
        public async UniTask<AITypedResponse<T>> SendAsync<T>(AIRequest request, CancellationToken ct = default)
        {
            var response = await SendAsync(request, ct);
            return AITypedResponse<T>.FromResponse(response);
        }

        /// <summary>
        /// 流式发送请求（路由模式下 request.Model 必须指定）
        /// </summary>
        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            if (!IsRouted)
                return _provider.StreamAsync(request, ct);

            if (string.IsNullOrEmpty(request.Model))
                return ChannelManager.ErrorStream("request.Model is required in routed mode.");

            return ChannelManager.StreamAsync(_config, request.Model, request, ct);
        }

        /// <summary>
        /// 便捷 Chat：直接发送单条消息获取完整响应
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
