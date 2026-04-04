using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
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
            var messages = new List<OpenAIMessage>();

            // System prompt
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

            var openAIRequest = new OpenAIRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                Messages = messages,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                Stream = stream
            };

            // Tools
            if (request.Tools?.Count > 0)
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

                // ToolChoice
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

            return openAIRequest;
        }

        public override IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var url = BuildUrl();
                var body = (OpenAIRequest)BuildRequestBody(request, stream: true);
                var json = JsonConvert.SerializeObject(body, Formatting.None, SerializerSettings);
                var headers = BuildHeaders();

                AILogger.Verbose($"OpenAI StreamAsync model={body.Model}");

                var parser = new SSEParser();

                // 流式 Tool 调用累积
                var toolCallAccumulators = new Dictionary<int, (string Id, string Name, string Args)>();

                await foreach (var line in AIHttpClient.PostStreamAsync(url, json, headers, linkedToken))
                {
                    var evt = parser.ParseLine(line);
                    if (evt == null) continue;

                    if (evt.Data == null || evt.Data == "[DONE]")
                    {
                        break;
                    }

                    try
                    {
                        var resp = JsonConvert.DeserializeObject<OpenAIStreamResponse>(evt.Data);
                        var choice = resp?.Choices?.FirstOrDefault();
                        if (choice == null) continue;

                        // 处理 tool_calls 增量
                        if (choice.Delta?.ToolCalls != null)
                        {
                            foreach (var tc in choice.Delta.ToolCalls)
                            {
                                if (!toolCallAccumulators.ContainsKey(tc.Index))
                                    toolCallAccumulators[tc.Index] = (tc.Id ?? "", tc.Function?.Name ?? "", "");

                                var current = toolCallAccumulators[tc.Index];
                                if (!string.IsNullOrEmpty(tc.Id))
                                    current.Id = tc.Id;
                                if (!string.IsNullOrEmpty(tc.Function?.Name))
                                    current.Name = tc.Function.Name;
                                if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                                    current.Args += tc.Function.Arguments;
                                toolCallAccumulators[tc.Index] = current;
                            }
                            continue;
                        }

                        if (choice.FinishReason != null)
                        {
                            // 输出累积的 tool calls
                            foreach (var kvp in toolCallAccumulators)
                            {
                                var (id, name, args) = kvp.Value;
                                await writer.YieldAsync(new AIStreamChunk
                                {
                                    ToolCall = new AIToolCall { Id = id, Name = name, Arguments = args }
                                });
                            }

                            await writer.YieldAsync(new AIStreamChunk
                            {
                                IsComplete = true,
                                Usage = resp.Usage != null ? new TokenUsage
                                {
                                    InputTokens = resp.Usage.PromptTokens,
                                    OutputTokens = resp.Usage.CompletionTokens
                                } : null
                            });
                            break;
                        }

                        var deltaText = choice.Delta?.Content;
                        if (deltaText != null)
                            await writer.YieldAsync(new AIStreamChunk { DeltaText = deltaText });
                    }
                    catch (Exception e)
                    {
                        AILogger.Warning($"Failed to parse OpenAI stream event: {e.Message}");
                    }
                }
            });
        }

        protected override AIResponse ParseResponse(string json)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<OpenAIResponse>(json);
                var choice = resp.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? "";
                var finishReason = choice?.FinishReason;

                // 解析 Tool 调用
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
