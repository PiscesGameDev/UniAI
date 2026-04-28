using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UniAI.Providers.OpenAI
{
    // ─── Request Models ───

    [Serializable]
    public class OpenAIRequest
    {
        [JsonProperty("model")] public string Model;
        [JsonProperty("messages")] public List<OpenAIMessage> Messages;
        [JsonProperty("max_tokens")] public int MaxTokens;
        [JsonProperty("temperature")] public float? Temperature;
        [JsonProperty("stream")] public bool Stream;
        [JsonProperty("tools")] public List<OpenAIToolDef> Tools;
        [JsonProperty("tool_choice")] public object ToolChoice;
        [JsonProperty("response_format")] public object ResponseFormat;
    }

    [Serializable]
    public class OpenAIMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public object Content;
        [JsonProperty("tool_call_id")] public string ToolCallId;
        [JsonProperty("tool_calls")] public List<OpenAIToolCallMsg> ToolCallsOut;
        [JsonProperty("reasoning_content")] public string ReasoningContent;
    }

    [Serializable]
    public class OpenAITextPart
    {
        [JsonProperty("type")] public string Type = "text";
        [JsonProperty("text")] public string Text;
    }

    [Serializable]
    public class OpenAIImagePart
    {
        [JsonProperty("type")] public string Type = "image_url";
        [JsonProperty("image_url")] public OpenAIImageUrl ImageUrl;
    }

    [Serializable]
    public class OpenAIImageUrl
    {
        [JsonProperty("url")] public string Url;
    }

    // ─── Response Models ───

    [Serializable]
    public class OpenAIResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("choices")] public List<OpenAIChoice> Choices;
        [JsonProperty("usage")] public OpenAIUsage Usage;
    }

    [Serializable]
    public class OpenAIChoice
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("message")] public OpenAIResponseMessage Message;
        [JsonProperty("finish_reason")] public string FinishReason;
    }

    [Serializable]
    public class OpenAIResponseMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
        [JsonProperty("reasoning_content")] public string ReasoningContent;
        [JsonProperty("tool_calls")] public List<OpenAIToolCallMsg> ToolCalls;
    }

    [Serializable]
    public class OpenAIUsage
    {
        [JsonProperty("prompt_tokens")] public int PromptTokens;
        [JsonProperty("completion_tokens")] public int CompletionTokens;
        [JsonProperty("total_tokens")] public int TotalTokens;
    }

    // ─── Stream Models ───

    [Serializable]
    public class OpenAIStreamResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("choices")] public List<OpenAIStreamChoice> Choices;
        [JsonProperty("usage")] public OpenAIUsage Usage;
    }

    [Serializable]
    public class OpenAIStreamChoice
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("delta")] public OpenAIStreamDelta Delta;
        [JsonProperty("finish_reason")] public string FinishReason;
    }

    [Serializable]
    public class OpenAIStreamDelta
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
        [JsonProperty("reasoning_content")] public string ReasoningContent;
        [JsonProperty("tool_calls")] public List<OpenAIStreamToolCall> ToolCalls;
    }

    // ─── Tool Models ───

    [Serializable]
    public class OpenAIToolDef
    {
        [JsonProperty("type")] public string Type = "function";
        [JsonProperty("function")] public OpenAIFunctionDef Function;
    }

    [Serializable]
    public class OpenAIFunctionDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("parameters")] public object Parameters;
    }

    [Serializable]
    public class OpenAIToolCallMsg
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;
        [JsonProperty("function")] public OpenAIFunctionCall Function;
    }

    [Serializable]
    public class OpenAIFunctionCall
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("arguments")] public string Arguments;
    }

    [Serializable]
    public class OpenAIStreamToolCall
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("id")] public string Id;
        [JsonProperty("function")] public OpenAIFunctionCall Function;
    }

    // ─── Error Models ───

    [Serializable]
    public class OpenAIErrorResponse
    {
        [JsonProperty("error")] public OpenAIErrorDetail Error;
    }

    [Serializable]
    public class OpenAIErrorDetail
    {
        [JsonProperty("message")] public string Message;
        [JsonProperty("type")] public string Type;
        [JsonProperty("code")] public string Code;
    }
}
