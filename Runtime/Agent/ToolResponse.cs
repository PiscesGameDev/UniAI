namespace UniAI
{
    /// <summary>
    /// 工具返回值的统一包装。工具内部返回这些对象，由 Runner 序列化为 JSON 字符串回传给 LLM。
    /// 结构保持最简：<c>success</c> + <c>data</c> / <c>error</c>，LLM 容易理解。
    /// </summary>
    public static class ToolResponse
    {
        /// <summary>
        /// 成功响应。
        /// </summary>
        /// <param name="data">返回的数据对象（任意可序列化对象）。</param>
        /// <param name="message">可选的成功消息。</param>
        public static object Success(object data = null, string message = null)
            => new SuccessResponse { Success = true, Data = data, Message = message };

        /// <summary>
        /// 错误响应。
        /// </summary>
        /// <param name="error">错误描述。</param>
        /// <param name="code">可选的错误码。</param>
        public static object Error(string error, string code = null)
            => new ErrorResponse { Success = false, Error = error, Code = code };

        private sealed class SuccessResponse
        {
            public bool Success { get; set; }
            public object Data { get; set; }
            public string Message { get; set; }
        }

        private sealed class ErrorResponse
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public string Code { get; set; }
        }
    }
}
