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
    public class OpenAIProvider : IAIProvider
    {
        public string Name => "OpenAI";

        private readonly OpenAIConfig _config;
        private readonly int _timeoutSeconds;

        public OpenAIProvider(OpenAIConfig config, int timeoutSeconds = 60)
        {
            _config = config;
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            var url = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";
            var body = BuildRequestBody(request, stream: false);
            var json = JsonConvert.SerializeObject(body, Formatting.None, _serializerSettings);
            var headers = BuildHeaders();

            AILogger.Verbose($"OpenAI SendAsync model={body.Model}");

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

                var url = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";
                var body = BuildRequestBody(request, stream: true);
                var json = JsonConvert.SerializeObject(body, Formatting.None, _serializerSettings);
                var headers = BuildHeaders();

                AILogger.Verbose($"OpenAI StreamAsync model={body.Model}");

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

        private OpenAIRequest BuildRequestBody(AIRequest request, bool stream)
        {
            var messages = new List<OpenAIMessage>();

            // OpenAI 的 system prompt 作为 messages[0]
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

            return new OpenAIRequest
            {
                Model = string.IsNullOrEmpty(request.Model) ? _config.Model : request.Model,
                Messages = messages,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                Stream = stream
            };
        }

        private Dictionary<string, string> BuildHeaders() => new()
        {
            { "Authorization", $"Bearer {_config.ApiKey}" }
        };

        private static AIResponse ParseResponse(string json)
        {
            try
            {
                var resp = JsonConvert.DeserializeObject<OpenAIResponse>(json);
                var text = resp.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                var finishReason = resp.Choices?.FirstOrDefault()?.FinishReason;

                return AIResponse.Success(
                    text,
                    resp.Usage != null ? new TokenUsage
                    {
                        InputTokens = resp.Usage.PromptTokens,
                        OutputTokens = resp.Usage.CompletionTokens
                    } : null,
                    finishReason,
                    json
                );
            }
            catch (Exception e)
            {
                AILogger.Error($"Failed to parse OpenAI response: {e.Message}");
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
                    var err = JsonConvert.DeserializeObject<OpenAIErrorResponse>(result.Body);
                    if (err?.Error != null)
                        errorMsg = $"{err.Error.Type}: {err.Error.Message}";
                }
                catch { /* use original error */ }
            }
            return AIResponse.Fail($"HTTP {result.StatusCode}: {errorMsg}", result.Body);
        }

        private static AIStreamChunk ParseStreamEvent(SSEEvent evt)
        {
            if (evt.Data == null || evt.Data == "[DONE]")
                return new AIStreamChunk { IsComplete = true };

            try
            {
                var resp = JsonConvert.DeserializeObject<OpenAIStreamResponse>(evt.Data);
                var choice = resp?.Choices?.FirstOrDefault();
                if (choice == null) return null;

                if (choice.FinishReason != null)
                {
                    return new AIStreamChunk
                    {
                        IsComplete = true,
                        Usage = resp.Usage != null ? new TokenUsage
                        {
                            InputTokens = resp.Usage.PromptTokens,
                            OutputTokens = resp.Usage.CompletionTokens
                        } : null
                    };
                }

                var deltaText = choice.Delta?.Content;
                if (deltaText == null) return null;

                return new AIStreamChunk { DeltaText = deltaText };
            }
            catch (Exception e)
            {
                AILogger.Warning($"Failed to parse OpenAI stream event: {e.Message}");
                return null;
            }
        }

        private static readonly JsonSerializerSettings _serializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };
    }
}
