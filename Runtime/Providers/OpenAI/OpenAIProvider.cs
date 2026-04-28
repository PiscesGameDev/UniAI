using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// OpenAI Chat Completions API 实现（兼容所有 OpenAI 兼容接口）
    /// </summary>
    public class OpenAIProvider : ProviderBase
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
            if (!RequestContainsImageInput(request))
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
            var messages = ConvertMessages(request, dialect);

            var openAIRequest = new OpenAIRequest
            {
                Model = modelId,
                Messages = messages,
                MaxTokens = request.MaxTokens,
                Temperature = dialect.ShouldOmitTemperature(request) ? null : request.Temperature,
                Stream = stream
            };

            if (request.Tools?.Count > 0)
                BuildToolDefs(request, openAIRequest);

            BuildResponseFormat(request, openAIRequest);

            return openAIRequest;
        }

        // ────────────────────────── 消息转换 ──────────────────────────

        private string ResolveModelId(AIRequest request)
            => string.IsNullOrEmpty(request.Model) ? Config.Model : request.Model;

        private static bool RequestContainsImageInput(AIRequest request)
        {
            if (request?.Messages == null)
                return false;

            foreach (var msg in request.Messages)
            {
                if (msg?.Contents == null)
                    continue;

                if (msg.Contents.Any(c => c is AIImageContent))
                    return true;
            }

            return false;
        }

        private static List<OpenAIMessage> ConvertMessages(AIRequest request, IOpenAIChatDialect dialect)
        {
            var messages = new List<OpenAIMessage>();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new OpenAIMessage
                {
                    Role = "system",
                    Content = request.SystemPrompt
                });
            }

            foreach (var msg in request.Messages)
            {
                // Tool result → role="tool" message
                if (msg.Contents.Count == 1 && msg.Contents[0] is AIToolResultContent toolResult)
                {
                    messages.Add(new OpenAIMessage
                    {
                        Role = "tool",
                        Content = toolResult.Content,
                        ToolCallId = toolResult.ToolUseId
                    });
                    continue;
                }

                // Assistant with tool_use → assistant message with tool_calls
                if (msg.Role == AIRole.Assistant && msg.Contents.Any(c => c is AIToolUseContent))
                {
                    var textPart = msg.Contents.OfType<AITextContent>().FirstOrDefault()?.Text;
                    var toolCalls = msg.Contents.OfType<AIToolUseContent>().Select(tu => new OpenAIToolCallMsg
                    {
                        Id = tu.Id,
                        Type = "function",
                        Function = new OpenAIFunctionCall { Name = tu.Name, Arguments = tu.Arguments ?? "{}" }
                    }).ToList();

                    var assistantMessage = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = textPart,
                        ToolCallsOut = toolCalls
                    };
                    dialect.ApplyAssistantMessageExtras(assistantMessage, msg, hasToolCalls: true);
                    messages.Add(assistantMessage);
                    continue;
                }

                // Normal message
                var role = msg.Role == AIRole.User ? "user" : "assistant";
                bool hasMultiContent = msg.Contents.Any(c => c is AIImageContent or AIFileContent);

                object content;
                if (hasMultiContent)
                {
                    content = msg.Contents.Select<AIContent, object>(c =>
                    {
                        if (c is AITextContent text)
                            return new OpenAITextPart { Text = text.Text };
                        if (c is AIImageContent img)
                            return new OpenAIImagePart
                            {
                                ImageUrl = new OpenAIImageUrl
                                {
                                    Url = $"data:{img.MediaType};base64,{Convert.ToBase64String(img.Data)}"
                                }
                            };
                        if (c is AIFileContent file)
                            return new OpenAITextPart { Text = $"[File: {file.FileName}]\n{file.Text}" };
                        return null;
                    }).Where(x => x != null).ToList();
                }
                else
                {
                    content = msg.Contents.FirstOrDefault() is AITextContent t ? t.Text : "";
                }

                var openAIMessage = new OpenAIMessage { Role = role, Content = content };
                if (msg.Role == AIRole.Assistant)
                    dialect.ApplyAssistantMessageExtras(openAIMessage, msg, hasToolCalls: false);
                messages.Add(openAIMessage);
            }

            return messages;
        }

        private static void BuildToolDefs(AIRequest request, OpenAIRequest openAIRequest)
        {
            openAIRequest.Tools = request.Tools.Select(t => new OpenAIToolDef
            {
                Function = new OpenAIFunctionDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = string.IsNullOrEmpty(t.ParametersSchema) ? new object()
                        : JsonConvert.DeserializeObject(t.ParametersSchema)
                }
            }).ToList();

            if (!string.IsNullOrEmpty(request.ToolChoice))
            {
                openAIRequest.ToolChoice = request.ToolChoice switch
                {
                    "auto" => "auto",
                    "any" => "required",
                    "none" => "none",
                    _ => new { type = "function", function = new { name = request.ToolChoice } }
                };
            }
        }

        private static void BuildResponseFormat(AIRequest request, OpenAIRequest openAIRequest)
        {
            var format = request.ResponseFormat;
            if (format == null || format.Type == ResponseFormatType.Text)
                return;

            if (format.Type == ResponseFormatType.JsonObject)
            {
                openAIRequest.ResponseFormat = new { type = "json_object" };
            }
            else if (format.Type == ResponseFormatType.JsonSchema)
            {
                openAIRequest.ResponseFormat = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = format.Name,
                        schema = JsonConvert.DeserializeObject(format.Schema),
                        strict = format.Strict
                    }
                };
            }
        }

        // ────────────────────────── 流式事件处理 ──────────────────────────

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

            // 处理 tool_calls 增量
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

            // 文本增量 — 即使同 chunk 也带了 finish_reason 也要先发出
            var deltaText = choice.Delta?.Content;
            if (!string.IsNullOrEmpty(deltaText))
                await emit(new AIStreamChunk { DeltaText = deltaText });

            if (choice.FinishReason != null)
            {
                // 输出累积的 tool calls
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

        // ────────────────────────── 响应解析 ──────────────────────────

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

    internal interface IOpenAIChatDialect
    {
        bool ShouldOmitTemperature(AIRequest request);
        void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls);
        string GetReasoningContent(OpenAIResponseMessage message);
        string GetReasoningDelta(OpenAIStreamDelta delta);
    }

    internal sealed class DefaultOpenAIChatDialect : IOpenAIChatDialect
    {
        public static readonly DefaultOpenAIChatDialect Instance = new();

        private DefaultOpenAIChatDialect() { }

        public bool ShouldOmitTemperature(AIRequest request) => false;

        public void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls) { }

        public string GetReasoningContent(OpenAIResponseMessage message) => null;

        public string GetReasoningDelta(OpenAIStreamDelta delta) => null;
    }

    internal sealed class DeepSeekThinkingDialect : IOpenAIChatDialect
    {
        public static readonly DeepSeekThinkingDialect Instance = new();

        private DeepSeekThinkingDialect() { }

        public bool ShouldOmitTemperature(AIRequest request) => true;

        public void ApplyAssistantMessageExtras(OpenAIMessage target, AIMessage source, bool hasToolCalls)
        {
            if (!hasToolCalls || string.IsNullOrEmpty(source?.ReasoningContent))
                return;

            target.ReasoningContent = source.ReasoningContent;
        }

        public string GetReasoningContent(OpenAIResponseMessage message) => message?.ReasoningContent;

        public string GetReasoningDelta(OpenAIStreamDelta delta) => delta?.ReasoningContent;
    }

    internal static class OpenAIChatDialectRegistry
    {
        public static IOpenAIChatDialect Resolve(string modelId)
        {
            var model = ModelRegistry.Get(modelId);
            if (model == null)
                return DefaultOpenAIChatDialect.Instance;

            if (model.AdapterId == "deepseek.openai_chat.thinking"
                || (model.Behavior & ModelBehavior.RequiresReasoningReplayForToolCalls) != 0)
            {
                return DeepSeekThinkingDialect.Instance;
            }

            return DefaultOpenAIChatDialect.Instance;
        }
    }
}
