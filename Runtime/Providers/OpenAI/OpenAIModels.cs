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
        [JsonProperty("tools")] public List<OpenAIToolDef> Tools;
        [JsonProperty("tool_choice")] public object ToolChoice;
        [JsonProperty("response_format")] public object ResponseFormat;
    }

    [Serializable]
    internal class OpenAIMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public object Content;
        [JsonProperty("tool_call_id")] public string ToolCallId;
        [JsonProperty("tool_calls")] public List<OpenAIToolCallMsg> ToolCallsOut;
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
        [JsonProperty("tool_calls")] public List<OpenAIToolCallMsg> ToolCalls;
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
        [JsonProperty("tool_calls")] public List<OpenAIStreamToolCall> ToolCalls;
    }

    // ─── Tool Models ───

    [Serializable]
    internal class OpenAIToolDef
    {
        [JsonProperty("type")] public string Type = "function";
        [JsonProperty("function")] public OpenAIFunctionDef Function;
    }

    [Serializable]
    internal class OpenAIFunctionDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("parameters")] public object Parameters;
    }

    [Serializable]
    internal class OpenAIToolCallMsg
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;
        [JsonProperty("function")] public OpenAIFunctionCall Function;
    }

    [Serializable]
    internal class OpenAIFunctionCall
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("arguments")] public string Arguments;
    }

    [Serializable]
    internal class OpenAIStreamToolCall
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("id")] public string Id;
        [JsonProperty("function")] public OpenAIFunctionCall Function;
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
