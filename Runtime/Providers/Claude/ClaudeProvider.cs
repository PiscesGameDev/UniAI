using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Newtonsoft.Json;

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

                await foreach (var line in AIHttpClient.PostStreamAsync(url, json, headers, linkedToken))
                {
                    var evt = parser.ParseLine(line);
                    if (evt == null) continue;

                    var chunk = ParseStreamEvent(evt);
                    if (chunk != null)
                        await writer.YieldAsync(chunk);
                }
            });
        }

        private ClaudeRequest BuildRequestBody(AIRequest request, bool stream)
        {
            var claudeMessages = new List<ClaudeMessage>();

            foreach (var msg in request.Messages)
            {
                var role = msg.Role == AIRole.User ? "user" : "assistant";
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

            return new ClaudeRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                System = request.SystemPrompt,
                Messages = claudeMessages,
                Stream = stream
            };
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

                return AIResponse.Success(
                    text,
                    resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.InputTokens,
                        OutputTokens = resp.Usage.OutputTokens
                    } : null,
                    resp.StopReason,
                    json
                );
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

        private static AIStreamChunk ParseStreamEvent(SSEEvent evt)
        {
            if (evt.Data == null || evt.Data == "[DONE]") return null;

            try
            {
                var baseEvent = JsonConvert.DeserializeObject<ClaudeStreamEvent>(evt.Data);

                switch (baseEvent?.Type)
                {
                    case "content_block_delta":
                    {
                        var delta = JsonConvert.DeserializeObject<ClaudeContentBlockDelta>(evt.Data);
                        if (delta?.Delta?.Type == "text_delta")
                            return new AIStreamChunk { DeltaText = delta.Delta.Text };
                        break;
                    }
                    case "message_delta":
                    {
                        var msgDelta = JsonConvert.DeserializeObject<ClaudeMessageDelta>(evt.Data);
                        return new AIStreamChunk
                        {
                            IsComplete = true,
                            Usage = msgDelta?.Usage != null ? new TokenUsage
                            {
                                OutputTokens = msgDelta.Usage.OutputTokens
                            } : null
                        };
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

            return null;
        }

        private static readonly JsonSerializerSettings _serializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };
    }
}
