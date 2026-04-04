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
        [JsonProperty("tools")] public List<ClaudeToolDef> Tools;
        [JsonProperty("tool_choice")] public object ToolChoice;
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
        [JsonProperty("partial_json")] public string PartialJson;
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

    // ─── Tool Models ───

    [Serializable]
    internal class ClaudeToolDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("input_schema")] public object InputSchema;
    }

    [Serializable]
    internal class ClaudeToolUseBlock
    {
        [JsonProperty("type")] public string Type = "tool_use";
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("input")] public object Input;
    }

    [Serializable]
    internal class ClaudeToolResultBlock
    {
        [JsonProperty("type")] public string Type = "tool_result";
        [JsonProperty("tool_use_id")] public string ToolUseId;
        [JsonProperty("content")] public string Content;
        [JsonProperty("is_error")] public bool IsError;
    }

    [Serializable]
    internal class ClaudeContentBlockStart
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("index")] public int Index;
        [JsonProperty("content_block")] public ClaudeContentBlockInfo ContentBlock;
    }

    [Serializable]
    internal class ClaudeContentBlockInfo
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
    }

}
