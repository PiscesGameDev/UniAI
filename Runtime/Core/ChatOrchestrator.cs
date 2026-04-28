using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 对话编排设置。
    /// </summary>
    public class ChatOrchestratorSettings
    {
        public float ToolTimeoutSeconds = 30f;
        public bool McpAutoConnect = true;
        public bool McpResourceInjection = true;
    }

    /// <summary>
    /// 对话生命周期编排器。
    /// 只负责串联 runtime、MCP、上下文准备、Runner 和 session 策略。
    /// </summary>
    public class ChatOrchestrator : IDisposable
    {
        public event Action<bool> OnStreamingChanged;
        public event Action OnScrollToBottom;
        public event Action OnStateChanged;

        private readonly ConversationRuntimeFactory _runtimeFactory = new();
        private readonly McpConversationInitializer _mcpInitializer = new();
        private readonly AgentEventSessionApplier _sessionApplier = new();

        private ChatOrchestratorDependencies _dependencies;
        private ConversationContextPreparer _contextPreparer;
        private ChatOrchestratorSettings _settings = new();
        private bool _isStreaming;
        private CancellationTokenSource _streamCts;

        public ChatOrchestrator()
        {
            _mcpInitializer.StatusChanged += () => OnStateChanged?.Invoke();
            Configure(null);
        }

        public bool IsStreaming => _isStreaming;
        public string McpStatus => _mcpInitializer.Status;

        public void Configure(ChatOrchestratorDependencies dependencies)
        {
            _dependencies = dependencies ?? new ChatOrchestratorDependencies();
            _dependencies.ContextProvider ??= NullConversationContextProvider.Instance;
            _dependencies.Persistence ??= NullChatSessionPersistence.Instance;
            _dependencies.TitlePolicy ??= NullChatTitlePolicy.Instance;
            _contextPreparer = new ConversationContextPreparer(_dependencies.ContextProvider);
        }

        public void EnsureRuntime(
            AIConfig config,
            ModelSelector modelSelector,
            AgentDefinition agent,
            ChatOrchestratorSettings settings = null)
        {
            _settings = settings ?? new ChatOrchestratorSettings();
            var runtime = _runtimeFactory.EnsureRuntime(config, modelSelector, agent, _settings);
            _mcpInitializer.EnsureStarted(runtime, _settings);
        }

        public void UpdateModel(ModelSelector modelSelector)
        {
            _runtimeFactory.UpdateModel(modelSelector);
        }

        public async UniTask StreamResponseAsync(ChatStreamRequest request, CancellationToken ct = default)
        {
            var session = request?.Session;
            if (session == null)
                return;

            var runtime = _runtimeFactory.Runtime;
            if (runtime?.Runner == null)
            {
                _sessionApplier.AddErrorMessage(session, "未配置 AI 提供商，请打开设置进行配置。");
                OnScrollToBottom?.Invoke();
                OnStateChanged?.Invoke();
                return;
            }

            BeginStreaming(ct);

            ChatMessage assistant = null;
            IDisposable guard = null;
            var linkedToken = _streamCts.Token;

            try
            {
                await _mcpInitializer.WaitReadyAsync();
                linkedToken.ThrowIfCancellationRequested();

                assistant = _sessionApplier.AddStreamingAssistant(session);
                OnScrollToBottom?.Invoke();
                OnStateChanged?.Invoke();

                if (runtime.HasTools)
                    guard = _dependencies.ToolExecutionGuardFactory?.Invoke();

                var modelId = string.IsNullOrEmpty(request.ModelId)
                    ? runtime.ModelId
                    : request.ModelId;
                var messages = await _contextPreparer.PrepareAsync(
                    session,
                    request.ContextSlots,
                    runtime,
                    request.Config,
                    modelId,
                    linkedToken);

                var requestOverride = new AIRequest { Model = modelId };
                await foreach (var evt in runtime.Runner.RunStreamAsync(messages, requestOverride, linkedToken))
                {
                    if (linkedToken.IsCancellationRequested)
                        break;

                    var result = _sessionApplier.Apply(session, assistant, evt);
                    if (result.ScrollToBottom)
                        OnScrollToBottom?.Invoke();
                    if (result.StateChanged)
                        OnStateChanged?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                _sessionApplier.MarkCanceled(assistant);
                OnStateChanged?.Invoke();
            }
            catch (Exception e)
            {
                _sessionApplier.MarkError(assistant, e.Message);
                AILogger.Warning($"Chat stream error: {e}");
                OnStateChanged?.Invoke();
            }
            finally
            {
                guard?.Dispose();
                _sessionApplier.Finish(assistant);
                EndStreaming();
                await PersistAndTitleAsync(session);
            }
        }

        public void CancelStream()
        {
            _streamCts?.Cancel();
        }

        public void Dispose()
        {
            CancelStream();
            _streamCts?.Dispose();
            _streamCts = null;
            _runtimeFactory.Dispose();
        }

        private void BeginStreaming(CancellationToken ct)
        {
            CancelStream();
            _streamCts?.Dispose();
            _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _isStreaming = true;
            OnStreamingChanged?.Invoke(true);
        }

        private void EndStreaming()
        {
            _isStreaming = false;
            OnStreamingChanged?.Invoke(false);
            _streamCts?.Dispose();
            _streamCts = null;
            OnStateChanged?.Invoke();
        }

        private async UniTask PersistAndTitleAsync(ChatSession session)
        {
            try
            {
                _dependencies.Persistence.Save(session);
                OnStateChanged?.Invoke();

                if (_dependencies.TitlePolicy.ShouldGenerateTitle(session))
                {
                    await _dependencies.TitlePolicy.GenerateTitleAsync(session, CancellationToken.None);
                    _dependencies.Persistence.Save(session);
                    OnStateChanged?.Invoke();
                }
            }
            catch (Exception e)
            {
                AILogger.Warning($"Chat session post-processing failed: {e.Message}");
            }
        }
    }
}
