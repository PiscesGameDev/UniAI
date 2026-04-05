using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;

namespace UniAI.Providers
{
    /// <summary>
    /// 故障转移 Provider — 持有多个 Provider，依次尝试直到成功
    /// </summary>
    internal class FallbackProvider : IAIProvider
    {
        public string Name => _providers[0].Name;

        private readonly List<IAIProvider> _providers;

        public FallbackProvider(List<IAIProvider> providers)
        {
            if (providers == null || providers.Count == 0)
                throw new ArgumentException("At least one provider is required.", nameof(providers));
            _providers = providers;
        }

        public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            AIResponse lastFailure = null;

            for (int i = 0; i < _providers.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var response = await _providers[i].SendAsync(request, ct);
                    if (response.IsSuccess)
                        return response;

                    lastFailure = response;
                    AILogger.Warning($"FallbackProvider: provider[{i}] ({_providers[i].Name}) failed: {response.Error}, trying next...");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    AILogger.Warning($"FallbackProvider: provider[{i}] ({_providers[i].Name}) exception: {e.Message}, trying next...");
                    lastFailure = AIResponse.Fail(e.Message);
                }
            }

            return lastFailure ?? AIResponse.Fail("All providers failed.");
        }

        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            // 只有一个 Provider 时直接透传
            if (_providers.Count == 1)
                return _providers[0].StreamAsync(request, ct);

            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                for (int i = 0; i < _providers.Count; i++)
                {
                    linkedToken.ThrowIfCancellationRequested();
                    bool hasYielded = false;

                    try
                    {
                        await foreach (var chunk in _providers[i].StreamAsync(request, linkedToken))
                        {
                            hasYielded = true;
                            await writer.YieldAsync(chunk);
                        }

                        // 流式正常结束
                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        if (hasYielded)
                        {
                            // 已经向消费者输出了部分数据，不能切换 Provider（会导致响应拼接错乱）
                            AILogger.Error($"FallbackProvider stream: provider[{i}] ({_providers[i].Name}) failed mid-stream: {e.Message}");
                            await writer.YieldAsync(new AIStreamChunk { IsComplete = true });
                            return;
                        }

                        AILogger.Warning($"FallbackProvider stream: provider[{i}] ({_providers[i].Name}) failed before streaming: {e.Message}, trying next...");
                    }
                }

                // 所有 Provider 都失败（均在流开始前失败）
                await writer.YieldAsync(new AIStreamChunk
                {
                    IsComplete = true
                });
            });
        }
    }
}
