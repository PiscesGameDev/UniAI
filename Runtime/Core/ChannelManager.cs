using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using UniAI.Providers;

namespace UniAI
{
    /// <summary>
    /// 渠道管理器 — 全局静态，负责渠道查找、Provider 缓存和故障转移。
    /// 缓存独立于 AIClient 生命周期，所有调用者共享。
    /// </summary>
    public static class ChannelManager
    {
        private static readonly ChannelRouteSelector _selector = new();
        private static readonly ProviderCache _providerCache = new();

        /// <summary>
        /// 发送请求，内部处理渠道查找 + 缓存优先 + 故障转移
        /// </summary>
        public static async UniTask<AIResponse> SendAsync(
            AIConfig config, string modelId, AIRequest request, CancellationToken ct)
        {
            var candidates = _selector.BuildCandidates(config, modelId);
            if (candidates.Count == 0)
                return AIResponse.Fail($"No channel found for model '{modelId}'.");

            AIResponse lastFailure = null;

            foreach (var channel in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var provider = _providerCache.GetOrCreate(channel, modelId, config.General);

                try
                {
                    var response = await provider.SendAsync(request, ct);
                    if (response.IsSuccess)
                    {
                        _selector.MarkSuccess(modelId, channel);
                        return response;
                    }

                    lastFailure = response;
                    AILogger.Warning(
                        $"ChannelManager: channel '{channel.Name}' failed: {response.Error}, trying next...");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    lastFailure = AIResponse.Fail(e.Message);
                    AILogger.Warning(
                        $"ChannelManager: channel '{channel.Name}' exception: {e.Message}, trying next...");
                }
            }

            return lastFailure ?? AIResponse.Fail($"All channels failed for model '{modelId}'.");
        }

        /// <summary>
        /// 流式发送，支持缓存优先 + 故障转移（仅在 yield 数据前可切换）
        /// </summary>
        public static IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(
            AIConfig config, string modelId, AIRequest request, CancellationToken ct)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var candidates = _selector.BuildCandidates(config, modelId);
                if (candidates.Count == 0)
                {
                    await writer.YieldAsync(new AIStreamChunk
                    {
                        IsComplete = true,
                        Error = $"No channel found for model '{modelId}'."
                    });
                    return;
                }

                string lastError = null;

                foreach (var channel in candidates)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    var provider = _providerCache.GetOrCreate(channel, modelId, config.General);
                    bool hasYielded = false;

                    try
                    {
                        await foreach (var chunk in provider.StreamAsync(request, linkedToken))
                        {
                            if (!hasYielded)
                            {
                                hasYielded = true;
                                _selector.MarkSuccess(modelId, channel);
                            }

                            await writer.YieldAsync(chunk);
                        }

                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        lastError = e.Message;

                        if (hasYielded)
                        {
                            AILogger.Error(
                                $"ChannelManager stream: channel '{channel.Name}' failed mid-stream: {e.Message}");
                            await writer.YieldAsync(new AIStreamChunk
                            {
                                IsComplete = true,
                                Error = $"Channel '{channel.Name}' failed mid-stream: {e.Message}"
                            });
                            return;
                        }

                        AILogger.Warning(
                            $"ChannelManager stream: channel '{channel.Name}' failed before streaming: {e.Message}, trying next...");
                    }
                }

                await writer.YieldAsync(new AIStreamChunk
                {
                    IsComplete = true,
                    Error = $"All channels failed. Last error: {lastError ?? "unknown"}"
                });
            });
        }

        /// <summary>
        /// 从 ChannelEntry 创建 IAIProvider
        /// </summary>
        internal static IAIProvider CreateProvider(ChannelEntry entry, string modelId, GeneralConfig general)
        {
            return AIProviderFactoryRegistry.CreateProvider(entry, modelId, general);
        }

        /// <summary>
        /// 清除指定模型的路由缓存
        /// </summary>
        public static void Invalidate(string modelId)
        {
            _selector.Invalidate(modelId);
        }

        /// <summary>
        /// 清除所有缓存（路由缓存 + Provider 池）
        /// </summary>
        public static void InvalidateAll()
        {
            _selector.Clear();
            _providerCache.Clear();
        }

        /// <summary>
        /// 创建一个立即返回错误的流式枚举
        /// </summary>
        internal static IUniTaskAsyncEnumerable<AIStreamChunk> ErrorStream(string error)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, _) =>
            {
                await writer.YieldAsync(new AIStreamChunk { IsComplete = true, Error = error });
            });
        }

    }
}
