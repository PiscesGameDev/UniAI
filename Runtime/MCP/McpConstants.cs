namespace UniAI
{
    /// <summary>
    /// MCP JSON-RPC 方法名常量
    /// </summary>
    internal static class McpMethods
    {
        public const string Initialize = "initialize";
        public const string Initialized = "notifications/initialized";
        public const string ToolsList = "tools/list";
        public const string ToolsCall = "tools/call";
        public const string ResourcesList = "resources/list";
        public const string ResourcesRead = "resources/read";
    }

    /// <summary>
    /// MCP 内容块类型常量
    /// </summary>
    internal static class McpContentTypes
    {
        public const string Text = "text";
        public const string Image = "image";
        public const string Resource = "resource";
    }
}
