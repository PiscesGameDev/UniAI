using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    // ─────────────────── JSON-RPC 2.0 ───────────────────

    internal class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc = "2.0";
        [JsonProperty("method")] public string Method;
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)] public object Params;
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)] public int? Id;
    }

    internal class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc;
        [JsonProperty("id")] public int? Id;
        [JsonProperty("result")] public JToken Result;
        [JsonProperty("error")] public JsonRpcError Error;
    }

    internal class JsonRpcError
    {
        [JsonProperty("code")] public int Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("data")] public JToken Data;
    }

    // ─────────────────── MCP 协议模型 ───────────────────

    /// <summary>
    /// MCP Tool 定义（来自 tools/list 响应）
    /// </summary>
    public class McpToolDefinition
    {
        public string Name;
        public string Description;
        /// <summary>
        /// 原始 JSON Schema 字符串，直接映射为 AITool.ParametersSchema
        /// </summary>
        public string InputSchemaJson;
    }

    /// <summary>
    /// MCP Tool 调用结果（tools/call 响应）
    /// </summary>
    public class McpToolResult
    {
        public List<McpContent> Content = new();
        public bool IsError;
    }

    /// <summary>
    /// MCP 内容块
    /// </summary>
    public class McpContent
    {
        public string Type;   // "text" | "image" | "resource"
        public string Text;
        public string Data;   // base64（image）
        public string MimeType;
    }

    /// <summary>
    /// MCP Resource 定义（resources/list 响应）
    /// </summary>
    public class McpResourceDefinition
    {
        public string Uri;
        public string Name;
        public string Description;
        public string MimeType;
    }

    /// <summary>
    /// MCP Resource 内容（resources/read 响应）
    /// </summary>
    public class McpResourceContent
    {
        public string Uri;
        public string MimeType;
        public string Text;
        public string Blob;
    }

    /// <summary>
    /// MCP Server 元信息（initialize 响应）
    /// </summary>
    public class McpServerInfo
    {
        public string Name;
        public string Version;
        public string ProtocolVersion;
    }
}
