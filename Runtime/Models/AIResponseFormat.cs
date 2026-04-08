using Newtonsoft.Json;

namespace UniAI
{
    /// <summary>
    /// 响应格式类型
    /// </summary>
    public enum ResponseFormatType
    {
        Text,
        JsonObject,
        JsonSchema
    }

    /// <summary>
    /// AI 响应格式约束
    /// </summary>
    public class AIResponseFormat
    {
        /// <summary>
        /// 格式类型
        /// </summary>
        public ResponseFormatType Type { get; set; }

        /// <summary>
        /// Schema 名称（JsonSchema 模式必填）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// JSON Schema 字符串（JsonSchema 模式必填）
        /// </summary>
        public string Schema { get; set; }

        /// <summary>
        /// OpenAI strict 模式（默认 true）
        /// </summary>
        public bool Strict { get; set; } = true;

        /// <summary>
        /// 纯文本格式（默认行为）
        /// </summary>
        public static readonly AIResponseFormat Text = new() { Type = ResponseFormatType.Text };

        /// <summary>
        /// JSON 对象格式（要求返回合法 JSON，无 Schema 约束）
        /// </summary>
        public static readonly AIResponseFormat Json = new() { Type = ResponseFormatType.JsonObject };

        /// <summary>
        /// 带 JSON Schema 约束的结构化输出
        /// </summary>
        public static AIResponseFormat JsonWithSchema(string name, string jsonSchema, bool strict = true) => new()
        {
            Type = ResponseFormatType.JsonSchema,
            Name = name,
            Schema = jsonSchema,
            Strict = strict
        };
    }

    /// <summary>
    /// 带反序列化数据的泛型响应
    /// </summary>
    public class AITypedResponse<T>
    {
        /// <summary>
        /// 是否成功（HTTP + 反序列化均成功）
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 反序列化后的数据
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// 原始响应文本
        /// </summary>
        public string RawText { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Token 使用量
        /// </summary>
        public TokenUsage Usage { get; set; }

        /// <summary>
        /// 从 AIResponse 构造泛型响应，自动反序列化 Text → T
        /// </summary>
        public static AITypedResponse<T> FromResponse(AIResponse response)
        {
            if (!response.IsSuccess)
            {
                return new AITypedResponse<T>
                {
                    IsSuccess = false,
                    RawText = response.Text,
                    Error = response.Error,
                    Usage = response.Usage
                };
            }

            try
            {
                var data = JsonConvert.DeserializeObject<T>(response.Text);
                return new AITypedResponse<T>
                {
                    IsSuccess = true,
                    Data = data,
                    RawText = response.Text,
                    Usage = response.Usage
                };
            }
            catch (JsonException e)
            {
                return new AITypedResponse<T>
                {
                    IsSuccess = false,
                    RawText = response.Text,
                    Error = $"JSON deserialization failed: {e.Message}",
                    Usage = response.Usage
                };
            }
        }
    }
}
