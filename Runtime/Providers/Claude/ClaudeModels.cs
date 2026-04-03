using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UniAI.Providers.Claude
{
    // ─── Request Models ───

    [Serializable]
    internal class ClaudeRequest
    {
        [JsonProperty("model")] public string Model;
        [JsonProperty("max_tokens")] public int MaxTokens;
        [JsonProperty("temperature")] public float Temperature;
        [JsonProperty("system")] public string System;
        [JsonProperty("messages")] public List<ClaudeMessage> Messages;
        [JsonProperty("stream")] public bool Stream;
    }

    [Serializable]
    internal class ClaudeMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public object Content;
    }

    [Serializable]
    internal class ClaudeTextBlock
    {
        [JsonProperty("type")] public string Type = "text";
        [JsonProperty("text")] public string Text;
    }

    [Serializable]
    internal class ClaudeImageBlock
    {
        [JsonProperty("type")] public string Type = "image";
        [JsonProperty("source")] public ClaudeImageSource Source;
    }

    [Serializable]
    internal class ClaudeImageSource
    {
        [JsonProperty("type")] public string Type = "base64";
        [JsonProperty("media_type")] public string MediaType;
        [JsonProperty("data")] public string Data;
    }

    // ─── Response Models ───

    [Serializable]
    internal class ClaudeResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public List<ClaudeContentBlock> Content;
        [JsonProperty("stop_reason")] public string StopReason;
        [JsonProperty("usage")] public ClaudeUsage Usage;
    }

    [Serializable]
    internal class ClaudeContentBlock
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("text")] public string Text;
    }

    [Serializable]
    internal class ClaudeUsage
    {
        [JsonProperty("input_tokens")] public int InputTokens;
        [JsonProperty("output_tokens")] public int OutputTokens;
    }

    // ─── Stream Event Models ───

    [Serializable]
    internal class ClaudeStreamEvent
    {
        [JsonProperty("type")] public string Type;
    }

    [Serializable]
    internal class ClaudeContentBlockDelta
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("index")] public int Index;
        [JsonProperty("delta")] public ClaudeDelta Delta;
    }

    [Serializable]
    internal class ClaudeDelta
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("text")] public string Text;
    }

    [Serializable]
    internal class ClaudeMessageDelta
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("delta")] public ClaudeMessageDeltaData Delta;
        [JsonProperty("usage")] public ClaudeUsage Usage;
    }

    [Serializable]
    internal class ClaudeMessageDeltaData
    {
        [JsonProperty("stop_reason")] public string StopReason;
    }

    // ─── Error Models ───

    [Serializable]
    internal class ClaudeErrorResponse
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("error")] public ClaudeErrorDetail Error;
    }

    [Serializable]
    internal class ClaudeErrorDetail
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("message")] public string Message;
    }
}
