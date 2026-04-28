using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// OpenAI Chat Completions API provider.
    /// </summary>
    public class OpenAIProvider : JsonSseProviderBase
    {
        public override string Name => "OpenAI";

        public OpenAIProvider(ProviderConfig config) : base(config) { }

        protected override string BuildUrl() => $"{Config.BaseUrl.TrimEnd('/')}/chat/completions";

        protected override Dictionary<string, string> BuildHeaders() => new()
        {
            { "Authorization", $"Bearer {Config.ApiKey}" }
        };

        protected override string GetModelFromBody(object body) => ((OpenAIRequest)body).Model;

        protected override string ValidateRequest(AIRequest request)
        {
            if (!OpenAIRequestConverter.ContainsImageInput(request))
                return null;

            var modelId = ResolveModelId(request);
            var modelEntry = ModelRegistry.Get(modelId);
            if (modelEntry == null || modelEntry.HasCapability(ModelCapability.VisionInput))
                return null;

            return $"Model '{modelId}' does not support image input on this OpenAI-compatible provider. Remove image attachments or use a model with VisionInput.";
        }

        protected override object BuildRequestBody(AIRequest request, bool stream)
        {
            var modelId = ResolveModelId(request);
            var dialect = OpenAIChatDialectRegistry.Resolve(modelId);
            var messages = OpenAIRequestConverter.ConvertMessages(request, dialect);

            var openAIRequest = new OpenAIRequest
            {
                Model = modelId,
                Messages = messages,
                MaxTokens = request.MaxTokens,
                Temperature = dialect.ShouldOmitTemperature(request) ? null : request.Temperature,
                Stream = stream
            };

            if (request.Tools?.Count > 0)
                OpenAIRequestConverter.BuildToolDefs(request, openAIRequest);

            OpenAIRequestConverter.BuildResponseFormat(request, openAIRequest);

            return openAIRequest;
        }

        private string ResolveModelId(AIRequest request)
            => string.IsNullOrEmpty(request.Model) ? Config.Model : request.Model;

        private class OpenAIStreamState
        {
            public readonly IOpenAIChatDialect Dialect;
            public readonly Dictionary<int, (string Id, string Name, string Args)> ToolCallAccumulators = new();
            public string ReasoningContent = "";

            public OpenAIStreamState(IOpenAIChatDialect dialect)
            {
                Dialect = dialect;
            }
        }

        protected override object CreateStreamState(object requestBody)
        {
            var modelId = (requestBody as OpenAIRequest)?.Model;
            return new OpenAIStreamState(OpenAIChatDialectRegistry.Resolve(modelId));
        }

        protected override async UniTask ProcessStreamEvent(SSEEvent evt, object streamState, EmitChunk emit)
        {
            var state = (OpenAIStreamState)streamState;
            var resp = JsonConvert.DeserializeObject<OpenAIStreamResponse>(evt.Data);
            var choice = resp?.Choices?.FirstOrDefault();
            if (choice == null) return;

            var reasoningDelta = state.Dialect.GetReasoningDelta(choice.Delta);
            if (!string.IsNullOrEmpty(reasoningDelta))
            {
                state.ReasoningContent += reasoningDelta;
                await emit(new AIStreamChunk { ReasoningDelta = reasoningDelta });
            }

            if (choice.Delta?.ToolCalls != null)
            {
                foreach (var tc in choice.Delta.ToolCalls)
                {
                    if (!state.ToolCallAccumulators.ContainsKey(tc.Index))
                        state.ToolCallAccumulators[tc.Index] = (tc.Id ?? "", tc.Function?.Name ?? "", "");

                    var current = state.ToolCallAccumulators[tc.Index];
                    if (!string.IsNullOrEmpty(tc.Id))
                        current.Id = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Function?.Name))
                        current.Name = tc.Function.Name;
                    if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                        current.Args += tc.Function.Arguments;
                    state.ToolCallAccumulators[tc.Index] = current;
                }

                if (choice.FinishReason == null)
                    return;
            }

            var deltaText = choice.Delta?.Content;
            if (!string.IsNullOrEmpty(deltaText))
                await emit(new AIStreamChunk { DeltaText = deltaText });

            if (choice.FinishReason != null)
            {
                foreach (var kvp in state.ToolCallAccumulators)
                {
                    var (id, name, args) = kvp.Value;
                    await emit(new AIStreamChunk
                    {
                        ToolCall = new AIToolCall { Id = id, Name = name, Arguments = args }
                    });
                }

                await emit(new AIStreamChunk
                {
                    IsComplete = true,
                    ReasoningContent = string.IsNullOrEmpty(state.ReasoningContent)
                        ? null
                        : state.ReasoningContent,
                    Usage = resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.PromptTokens,
                        OutputTokens = resp.Usage.CompletionTokens
                    } : null
                });
            }
        }

        protected override bool OnStreamDone(object streamState, EmitChunk emit) => true;

        protected override AIResponse ParseResponse(string json, object requestBody)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<OpenAIResponse>(json);
                var choice = resp.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? "";
                var finishReason = choice?.FinishReason;
                var dialect = OpenAIChatDialectRegistry.Resolve((requestBody as OpenAIRequest)?.Model);
                var reasoningContent = dialect.GetReasoningContent(choice?.Message);

                List<AIToolCall> toolCalls = null;
                if (choice?.Message?.ToolCalls != null)
                {
                    toolCalls = choice.Message.ToolCalls.Select(tc => new AIToolCall
                    {
                        Id = tc.Id,
                        Name = tc.Function?.Name,
                        Arguments = tc.Function?.Arguments
                    }).ToList();
                }

                return AIResponse.Success(
                    text,
                    resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.PromptTokens,
                        OutputTokens = resp.Usage.CompletionTokens
                    } : null,
                    finishReason,
                    json,
                    toolCalls,
                    reasoningContent
                );
            }
            catch (Exception e)
            {
                AILogger.Error($"Failed to parse OpenAI response: {e.Message}");
                return AIResponse.Fail($"Parse error: {e.Message}", json);
            }
        }

        protected override string TryParseErrorBody(string body)
        {
            try
            {
                var err = JsonConvert.DeserializeObject<OpenAIErrorResponse>(body);
                if (err?.Error != null)
                    return $"{err.Error.Type}: {err.Error.Message}";
            }
            catch { /* use original error */ }
            return null;
        }
    }
}
