using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UniAI.Providers.OpenAI
{
    // ─── Request Models ───

    [Serializable]
    internal class OpenAIRequest
    {
        [JsonProperty("model")] public string Model;
        [JsonProperty("messages")] public List<OpenAIMessage> Messages;
        [JsonProperty("max_tokens")] public int MaxTokens;
        [JsonProperty("temperature")] public float Temperature;
        [JsonProperty("stream")] public bool Stream;
    }

    [Serializable]
    internal class OpenAIMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public object Content;
    }

    [Serializable]
    internal class OpenAITextPart
    {
        [JsonProperty("type")] public string Type = "text";
        [JsonProperty("text")] public string Text;
    }

    [Serializable]
    internal class OpenAIImagePart
    {
        [JsonProperty("type")] public string Type = "image_url";
        [JsonProperty("image_url")] public OpenAIImageUrl ImageUrl;
    }

    [Serializable]
    internal class OpenAIImageUrl
    {
        [JsonProperty("url")] public string Url;
    }

    // ─── Response Models ───

    [Serializable]
    internal class OpenAIResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("choices")] public List<OpenAIChoice> Choices;
        [JsonProperty("usage")] public OpenAIUsage Usage;
    }

    [Serializable]
    internal class OpenAIChoice
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("message")] public OpenAIResponseMessage Message;
        [JsonProperty("finish_reason")] public string FinishReason;
    }

    [Serializable]
    internal class OpenAIResponseMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
    }

    [Serializable]
    internal class OpenAIUsage
    {
        [JsonProperty("prompt_tokens")] public int PromptTokens;
        [JsonProperty("completion_tokens")] public int CompletionTokens;
        [JsonProperty("total_tokens")] public int TotalTokens;
    }

    // ─── Stream Models ───

    [Serializable]
    internal class OpenAIStreamResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("choices")] public List<OpenAIStreamChoice> Choices;
        [JsonProperty("usage")] public OpenAIUsage Usage;
    }

    [Serializable]
    internal class OpenAIStreamChoice
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("delta")] public OpenAIStreamDelta Delta;
        [JsonProperty("finish_reason")] public string FinishReason;
    }

    [Serializable]
    internal class OpenAIStreamDelta
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
    }

    // ─── Error Models ───

    [Serializable]
    internal class OpenAIErrorResponse
    {
        [JsonProperty("error")] public OpenAIErrorDetail Error;
    }

    [Serializable]
    internal class OpenAIErrorDetail
    {
        [JsonProperty("message")] public string Message;
        [JsonProperty("type")] public string Type;
        [JsonProperty("code")] public string Code;
    }
}
