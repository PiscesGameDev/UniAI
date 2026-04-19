using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Debug = UnityEngine.Debug;

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
        /// 每次 <see cref="SendAsync(AIRequest,CancellationToken)"/> / <see cref="StreamAsync"/> 完成时触发，
        /// 携带可观察的请求级指标（耗时 / tokens / 错误）。消费方应自行做异常处理，
        /// 事件处理中抛出的异常会被吞掉不影响主流程。
        /// </summary>
        public event Action<AIRequestMetrics> OnRequestCompleted;

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
        public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            AIResponse response;
            try
            {
                if (!IsRouted)
                    response = await _provider.SendAsync(request, ct);
                else if (string.IsNullOrEmpty(request.Model))
                    response = AIResponse.Fail("request.Model is required in routed mode.");
                else
                    response = await ChannelManager.SendAsync(_config, request.Model, request, ct);
            }
            catch (Exception e)
            {
                sw.Stop();
                EmitCompleted(request, sw.ElapsedMilliseconds, null, e.Message, false);
                throw;
            }

            sw.Stop();
            EmitCompleted(request, sw.ElapsedMilliseconds, response?.Usage, response?.Error, response?.IsSuccess ?? false);
            return response;
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
                return InstrumentStream(request, _provider.StreamAsync(request, ct));

            if (string.IsNullOrEmpty(request.Model))
                return ChannelManager.ErrorStream("request.Model is required in routed mode.");

            return InstrumentStream(request, ChannelManager.StreamAsync(_config, request.Model, request, ct));
        }

        private IUniTaskAsyncEnumerable<AIStreamChunk> InstrumentStream(
            AIRequest request, IUniTaskAsyncEnumerable<AIStreamChunk> inner)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, ct) =>
            {
                var sw = Stopwatch.StartNew();
                TokenUsage usage = null;
                string error = null;
                bool success = true;

                try
                {
                    await foreach (var chunk in inner.WithCancellation(ct))
                    {
                        if (chunk?.Usage != null) usage = chunk.Usage;
                        if (!string.IsNullOrEmpty(chunk?.Error))
                        {
                            error = chunk.Error;
                            success = false;
                        }
                        await writer.YieldAsync(chunk);
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    success = false;
                    sw.Stop();
                    EmitCompleted(request, sw.ElapsedMilliseconds, usage, error, success);
                    throw;
                }

                sw.Stop();
                EmitCompleted(request, sw.ElapsedMilliseconds, usage, error, success);
            });
        }

        private void EmitCompleted(AIRequest request, long durationMs, TokenUsage usage, string error, bool success)
        {
            if (OnRequestCompleted == null) return;

            try
            {
                OnRequestCompleted(new AIRequestMetrics
                {
                    Model = request?.Model,
                    DurationMs = durationMs,
                    InputTokens = usage?.InputTokens ?? 0,
                    OutputTokens = usage?.OutputTokens ?? 0,
                    Error = error,
                    Success = success
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIClient] OnRequestCompleted handler threw: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 单次请求的可观察指标。通过 <see cref="AIClient.OnRequestCompleted"/> 事件向消费方暴露。
    /// </summary>
    public sealed class AIRequestMetrics
    {
        public string Model;
        public long DurationMs;
        public int InputTokens;
        public int OutputTokens;
        public int TotalTokens => InputTokens + OutputTokens;
        public string Error;
        public bool Success;
    }
}
