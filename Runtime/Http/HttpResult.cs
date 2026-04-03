namespace UniAI
{
    /// <summary>
    /// HTTP 请求结果
    /// </summary>
    internal class HttpResult
    {
        public bool IsSuccess { get; set; }
        public long StatusCode { get; set; }
        public string Body { get; set; }
        public string Error { get; set; }

        internal static HttpResult Success(long statusCode, string body) => new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Body = body
        };

        internal static HttpResult Fail(long statusCode, string error, string body = null) => new()
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Error = error,
            Body = body
        };
    }
}
