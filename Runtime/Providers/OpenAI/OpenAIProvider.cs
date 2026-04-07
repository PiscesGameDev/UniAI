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

        private readonly OpenAIConfig _config;

        public OpenAIProvider(OpenAIConfig config, int timeoutSeconds = 60)
            : base(timeoutSeconds)
        {
            _config = config;
        }

        protected override string BuildUrl() => $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";

        protected override Dictionary<string, string> BuildHeaders() => new()
        {
            { "Authorization", $"Bearer {_config.ApiKey}" }
        };

        protected override string GetModelFromBody(object body) => ((OpenAIRequest)body).Model;

        protected override object BuildRequestBody(AIRequest request, bool stream)
        {
            var messages = ConvertMessages(request);

            var openAIRequest = new OpenAIRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                Messages = messages,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                Stream = stream
            };

            if (request.Tools?.Count > 0)
                BuildToolDefs(request, openAIRequest);

            return openAIRequest;
        }

        // ────────────────────────── 消息转换 ──────────────────────────

        private static List<OpenAIMessage> ConvertMessages(AIRequest request)
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

                    messages.Add(new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = textPart,
                        ToolCallsOut = toolCalls
                    });
                    continue;
                }

                // Normal message
                var role = msg.Role == AIRole.User ? "user" : "assistant";
                bool hasImage = msg.Contents.Any(c => c is AIImageContent);

                object content;
                if (hasImage)
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
                        return null;
                    }).Where(x => x != null).ToList();
                }
                else
                {
                    content = msg.Contents.FirstOrDefault() is AITextContent t ? t.Text : "";
                }

                messages.Add(new OpenAIMessage { Role = role, Content = content });
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
                    _ => (object)new { type = "function", function = new { name = request.ToolChoice } }
                };
            }
        }

        // ────────────────────────── 流式事件处理 ──────────────────────────

        private class OpenAIStreamState
        {
            public readonly Dictionary<int, (string Id, string Name, string Args)> ToolCallAccumulators = new();
        }

        protected override object CreateStreamState() => new OpenAIStreamState();

        protected override async UniTask ProcessStreamEvent(SSEEvent evt, object streamState, EmitChunk emit)
        {
            var state = (OpenAIStreamState)streamState;
            var resp = JsonConvert.DeserializeObject<OpenAIStreamResponse>(evt.Data);
            var choice = resp?.Choices?.FirstOrDefault();
            if (choice == null) return;

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
                return;
            }

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
                    Usage = resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.PromptTokens,
                        OutputTokens = resp.Usage.CompletionTokens
                    } : null
                });
                return;
            }

            var deltaText = choice.Delta?.Content;
            if (deltaText != null)
                await emit(new AIStreamChunk { DeltaText = deltaText });
        }

        protected override bool OnStreamDone(object streamState, EmitChunk emit) => true;

        // ────────────────────────── 响应解析 ──────────────────────────

        protected override AIResponse ParseResponse(string json)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<OpenAIResponse>(json);
                var choice = resp.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? "";
                var finishReason = choice?.FinishReason;

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
                    toolCalls
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
