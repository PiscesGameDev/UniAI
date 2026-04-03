using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI.Providers.Claude
{
    /// <summary>
    /// Claude Messages API 实现
    /// </summary>
    public class ClaudeProvider : IAIProvider
    {
        public string Name => "Claude";

        private readonly ClaudeConfig _config;
        private readonly int _timeoutSeconds;

        public ClaudeProvider(ClaudeConfig config, int timeoutSeconds = 60)
        {
            _config = config;
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            var url = $"{_config.BaseUrl.TrimEnd('/')}/v1/messages";
            var body = BuildRequestBody(request, stream: false);
            var json = JsonConvert.SerializeObject(body, Formatting.None, _serializerSettings);
            var headers = BuildHeaders();

            AILogger.Verbose($"Claude SendAsync model={body.Model}");

            var result = await AIHttpClient.PostJsonAsync(url, json, headers, _timeoutSeconds, ct);

            if (!result.IsSuccess)
                return ParseError(result);

            return ParseResponse(result.Body);
        }

        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var url = $"{_config.BaseUrl.TrimEnd('/')}/v1/messages";
                var body = BuildRequestBody(request, stream: true);
                var json = JsonConvert.SerializeObject(body, Formatting.None, _serializerSettings);
                var headers = BuildHeaders();

                AILogger.Verbose($"Claude StreamAsync model={body.Model}");

                var parser = new SSEParser();

                // 流式 Tool 调用累积状态
                string _currentToolId = null;
                string _currentToolName = null;
                string _toolJsonAccumulator = "";

                await foreach (var line in AIHttpClient.PostStreamAsync(url, json, headers, linkedToken))
                {
                    var evt = parser.ParseLine(line);
                    if (evt == null) continue;

                    if (evt.Data == null || evt.Data == "[DONE]") continue;

                    try
                    {
                        var baseEvent = JsonConvert.DeserializeObject<ClaudeStreamEvent>(evt.Data);

                        switch (baseEvent?.Type)
                        {
                            case "content_block_start":
                            {
                                var blockStart = JsonConvert.DeserializeObject<ClaudeContentBlockStart>(evt.Data);
                                if (blockStart?.ContentBlock?.Type == "tool_use")
                                {
                                    _currentToolId = blockStart.ContentBlock.Id;
                                    _currentToolName = blockStart.ContentBlock.Name;
                                    _toolJsonAccumulator = "";
                                }
                                break;
                            }
                            case "content_block_delta":
                            {
                                var delta = JsonConvert.DeserializeObject<ClaudeContentBlockDelta>(evt.Data);
                                if (delta?.Delta?.Type == "text_delta")
                                {
                                    await writer.YieldAsync(new AIStreamChunk { DeltaText = delta.Delta.Text });
                                }
                                else if (delta?.Delta?.Type == "input_json_delta")
                                {
                                    var jsonDelta = JsonConvert.DeserializeObject<ClaudeInputJsonDelta>(
                                        JsonConvert.SerializeObject(delta.Delta));
                                    if (jsonDelta?.PartialJson != null)
                                        _toolJsonAccumulator += jsonDelta.PartialJson;
                                }
                                break;
                            }
                            case "content_block_stop":
                            {
                                if (_currentToolId != null)
                                {
                                    await writer.YieldAsync(new AIStreamChunk
                                    {
                                        ToolCall = new AIToolCall
                                        {
                                            Id = _currentToolId,
                                            Name = _currentToolName,
                                            Arguments = _toolJsonAccumulator
                                        }
                                    });
                                    _currentToolId = null;
                                    _currentToolName = null;
                                    _toolJsonAccumulator = "";
                                }
                                break;
                            }
                            case "message_delta":
                            {
                                var msgDelta = JsonConvert.DeserializeObject<ClaudeMessageDelta>(evt.Data);
                                await writer.YieldAsync(new AIStreamChunk
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
                    catch (Exception e)
                    {
                        AILogger.Warning($"Failed to parse stream event: {e.Message}");
                    }
                }
            });
        }

        private ClaudeRequest BuildRequestBody(AIRequest request, bool stream)
        {
            var claudeMessages = new List<ClaudeMessage>();

            foreach (var msg in request.Messages)
            {
                var role = msg.Role == AIRole.User ? "user" : "assistant";

                // 检查是否包含 Tool 相关内容
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

            var claudeRequest = new ClaudeRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                System = request.SystemPrompt,
                Messages = claudeMessages,
                Stream = stream
            };

            // Tools
            if (request.Tools?.Count > 0)
            {
                claudeRequest.Tools = request.Tools.Select(t => new ClaudeToolDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = string.IsNullOrEmpty(t.ParametersSchema) ? new object()
                        : JsonConvert.DeserializeObject(t.ParametersSchema)
                }).ToList();

                // ToolChoice
                if (!string.IsNullOrEmpty(request.ToolChoice))
                {
                    claudeRequest.ToolChoice = request.ToolChoice switch
                    {
                        "auto" => new { type = "auto" },
                        "any" => new { type = "any" },
                        "none" => null, // 不发送 tool_choice
                        _ => (object)new { type = "tool", name = request.ToolChoice }
                    };
                }
            }

            return claudeRequest;
        }

        private Dictionary<string, string> BuildHeaders() => new()
        {
            { "x-api-key", _config.ApiKey },
            { "anthropic-version", _config.ApiVersion }
        };

        private static AIResponse ParseResponse(string json)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<ClaudeResponse>(json);
                var text = resp.Content?
                    .Where(b => b.Type == "text")
                    .Select(b => b.Text)
                    .FirstOrDefault() ?? "";

                // 解析 Tool 调用
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

                var response = AIResponse.Success(
                    text,
                    resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.InputTokens,
                        OutputTokens = resp.Usage.OutputTokens
                    } : null,
                    resp.StopReason,
                    json
                );
                response.ToolCalls = toolCalls;
                return response;
            }
            catch (Exception e)
            {
                AILogger.Error($"Failed to parse Claude response: {e.Message}");
                return AIResponse.Fail($"Parse error: {e.Message}", json);
            }
        }

        private static AIResponse ParseError(HttpResult result)
        {
            string errorMsg = result.Error;
            if (!string.IsNullOrEmpty(result.Body))
            {
                try
                {
                    var err = JsonConvert.DeserializeObject<ClaudeErrorResponse>(result.Body);
                    if (err?.Error != null)
                        errorMsg = $"{err.Error.Type}: {err.Error.Message}";
                }
                catch { /* use original error */ }
            }
            return AIResponse.Fail($"HTTP {result.StatusCode}: {errorMsg}", result.Body);
        }

        private static readonly JsonSerializerSettings _serializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };
    }
}
