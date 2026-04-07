using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI.Providers.Claude
{
    /// <summary>
    /// Claude Messages API 实现
    /// </summary>
    public class ClaudeProvider : ProviderBase
    {
        public override string Name => "Claude";

        private readonly ClaudeConfig _config;

        public ClaudeProvider(ClaudeConfig config, int timeoutSeconds = 60)
            : base(timeoutSeconds)
        {
            _config = config;
        }

        protected override string BuildUrl() => $"{_config.BaseUrl.TrimEnd('/')}/v1/messages";

        protected override Dictionary<string, string> BuildHeaders() => new()
        {
            { "x-api-key", _config.ApiKey },
            { "anthropic-version", _config.ApiVersion }
        };

        protected override string GetModelFromBody(object body) => ((ClaudeRequest)body).Model;

        protected override object BuildRequestBody(AIRequest request, bool stream)
        {
            var claudeMessages = ConvertMessages(request.Messages);

            var claudeRequest = new ClaudeRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                System = request.SystemPrompt,
                Messages = claudeMessages,
                Stream = stream
            };

            if (request.Tools?.Count > 0)
                BuildToolDefs(request, claudeRequest);

            return claudeRequest;
        }

        // ────────────────────────── 消息转换 ──────────────────────────

        private static List<ClaudeMessage> ConvertMessages(List<AIMessage> messages)
        {
            var claudeMessages = new List<ClaudeMessage>();

            foreach (var msg in messages)
            {
                var role = msg.Role == AIRole.User ? "user" : "assistant";
                bool hasToolContent = msg.Contents.Any(c => c is AIToolUseContent or AIToolResultContent);

                if (hasToolContent)
                {
                    var contentBlocks = new List<object>();
                    foreach (var c in msg.Contents)
                    {
                        switch (c)
                        {
                            case AITextContent text:
                                contentBlocks.Add(new ClaudeTextBlock { Text = text.Text });
                                break;
                            case AIToolUseContent toolUse:
                                contentBlocks.Add(new ClaudeToolUseBlock
                                {
                                    Id = toolUse.Id,
                                    Name = toolUse.Name,
                                    Input = string.IsNullOrEmpty(toolUse.Arguments) ? new object()
                                        : JsonConvert.DeserializeObject(toolUse.Arguments)
                                });
                                break;
                            case AIToolResultContent toolResult:
                                contentBlocks.Add(new ClaudeToolResultBlock
                                {
                                    ToolUseId = toolResult.ToolUseId,
                                    Content = toolResult.Content,
                                    IsError = toolResult.IsError
                                });
                                break;
                        }
                    }
                    claudeMessages.Add(new ClaudeMessage { Role = role, Content = contentBlocks });
                }
                else
                {
                    bool hasMultipleContentTypes = msg.Contents.Count > 1 ||
                        msg.Contents.Any(c => c is AIImageContent);

                    object content;
                    if (hasMultipleContentTypes)
                    {
                        content = msg.Contents.Select<AIContent, object>(c =>
                        {
                            if (c is AITextContent text)
                                return new ClaudeTextBlock { Text = text.Text };
                            if (c is AIImageContent img)
                                return new ClaudeImageBlock
                                {
                                    Source = new ClaudeImageSource
                                    {
                                        MediaType = img.MediaType,
                                        Data = Convert.ToBase64String(img.Data)
                                    }
                                };
                            return null;
                        }).Where(x => x != null).ToList();
                    }
                    else
                    {
                        content = msg.Contents.FirstOrDefault() is AITextContent t ? t.Text : "";
                    }

                    claudeMessages.Add(new ClaudeMessage { Role = role, Content = content });
                }
            }

            return claudeMessages;
        }

        private static void BuildToolDefs(AIRequest request, ClaudeRequest claudeRequest)
        {
            claudeRequest.Tools = request.Tools.Select(t => new ClaudeToolDef
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = string.IsNullOrEmpty(t.ParametersSchema) ? new object()
                    : JsonConvert.DeserializeObject(t.ParametersSchema)
            }).ToList();

            if (!string.IsNullOrEmpty(request.ToolChoice))
            {
                claudeRequest.ToolChoice = request.ToolChoice switch
                {
                    "auto" => new { type = "auto" },
                    "any" => new { type = "any" },
                    "none" => null,
                    _ => (object)new { type = "tool", name = request.ToolChoice }
                };
            }
        }

        // ────────────────────────── 流式事件处理 ──────────────────────────

        private class ClaudeStreamState
        {
            public string CurrentToolId;
            public string CurrentToolName;
            public string ToolJsonAccumulator = "";
        }

        protected override object CreateStreamState() => new ClaudeStreamState();

        protected override async UniTask ProcessStreamEvent(SSEEvent evt, object streamState, EmitChunk emit)
        {
            var state = (ClaudeStreamState)streamState;
            var baseEvent = JsonConvert.DeserializeObject<ClaudeStreamEvent>(evt.Data);

            switch (baseEvent?.Type)
            {
                case "content_block_start":
                {
                    var blockStart = JsonConvert.DeserializeObject<ClaudeContentBlockStart>(evt.Data);
                    if (blockStart?.ContentBlock?.Type == "tool_use")
                    {
                        state.CurrentToolId = blockStart.ContentBlock.Id;
                        state.CurrentToolName = blockStart.ContentBlock.Name;
                        state.ToolJsonAccumulator = "";
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var delta = JsonConvert.DeserializeObject<ClaudeContentBlockDelta>(evt.Data);
                    if (delta?.Delta?.Type == "text_delta")
                    {
                        await emit(new AIStreamChunk { DeltaText = delta.Delta.Text });
                    }
                    else if (delta?.Delta?.Type == "input_json_delta")
                    {
                        if (delta.Delta.PartialJson != null)
                            state.ToolJsonAccumulator += delta.Delta.PartialJson;
                    }
                    break;
                }
                case "content_block_stop":
                {
                    if (state.CurrentToolId != null)
                    {
                        await emit(new AIStreamChunk
                        {
                            ToolCall = new AIToolCall
                            {
                                Id = state.CurrentToolId,
                                Name = state.CurrentToolName,
                                Arguments = state.ToolJsonAccumulator
                            }
                        });
                        state.CurrentToolId = null;
                        state.CurrentToolName = null;
                        state.ToolJsonAccumulator = "";
                    }
                    break;
                }
                case "message_delta":
                {
                    var msgDelta = JsonConvert.DeserializeObject<ClaudeMessageDelta>(evt.Data);
                    await emit(new AIStreamChunk
                    {
                        IsComplete = true,
                        Usage = msgDelta?.Usage != null ? new TokenUsage
                        {
                            OutputTokens = msgDelta.Usage.OutputTokens
                        } : null
                    });
                    break;
                }
                case "error":
                {
                    var err = JsonConvert.DeserializeObject<ClaudeErrorResponse>(evt.Data);
                    AILogger.Error($"Stream error: {err?.Error?.Message}");
                    break;
                }
            }
        }

        // ────────────────────────── 响应解析 ──────────────────────────

        protected override AIResponse ParseResponse(string json)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<ClaudeResponse>(json);
                var text = resp.Content?
                    .Where(b => b.Type == "text")
                    .Select(b => b.Text)
                    .FirstOrDefault() ?? "";

                List<AIToolCall> toolCalls = null;
                var rawContent = JObject.Parse(json)?["content"] as JArray;
                if (rawContent != null)
                {
                    foreach (var block in rawContent)
                    {
                        if (block["type"]?.ToString() == "tool_use")
                        {
                            toolCalls ??= new List<AIToolCall>();
                            toolCalls.Add(new AIToolCall
                            {
                                Id = block["id"]?.ToString(),
                                Name = block["name"]?.ToString(),
                                Arguments = block["input"]?.ToString(Formatting.None)
                            });
                        }
                    }
                }

                return AIResponse.Success(
                    text,
                    resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.InputTokens,
                        OutputTokens = resp.Usage.OutputTokens
                    } : null,
                    resp.StopReason,
                    json,
                    toolCalls
                );
            }
            catch (Exception e)
            {
                AILogger.Error($"Failed to parse Claude response: {e.Message}");
                return AIResponse.Fail($"Parse error: {e.Message}", json);
            }
        }

        protected override string TryParseErrorBody(string body)
        {
            try
            {
                var err = JsonConvert.DeserializeObject<ClaudeErrorResponse>(body);
                if (err?.Error != null)
                    return $"{err.Error.Type}: {err.Error.Message}";
            }
            catch { /* use original error */ }
            return null;
        }
    }
}
