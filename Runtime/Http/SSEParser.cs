namespace UniAI
{
    /// <summary>
    /// SSE 事件
    /// </summary>
    internal class SSEEvent
    {
        public string EventType { get; set; }
        public string Data { get; set; }
    }

    /// <summary>
    /// SSE 协议解析器，将原始行解析为结构化事件
    /// </summary>
    internal class SSEParser
    {
        private string _currentEventType;
        private string _currentData;

        /// <summary>
        /// 解析一行 SSE 数据。当收到空行时返回完整事件，否则返回 null
        /// </summary>
        internal SSEEvent ParseLine(string line)
        {
            // 空行 = 事件分隔符，派发当前事件
            if (string.IsNullOrEmpty(line))
            {
                if (_currentData == null) return null;

                var evt = new SSEEvent
                {
                    EventType = _currentEventType ?? "message",
                    Data = _currentData
                };

                _currentEventType = null;
                _currentData = null;
                return evt;
            }

            // 注释行
            if (line.StartsWith(':')) return null;

            int colonIndex = line.IndexOf(':');
            string field, value;

            if (colonIndex < 0)
            {
                field = line;
                value = "";
            }
            else
            {
                field = line[..colonIndex];
                value = line[(colonIndex + 1)..];
                // 规范要求跳过冒号后的第一个空格
                if (value.Length > 0 && value[0] == ' ')
                    value = value[1..];
            }

            switch (field)
            {
                case "event":
                    _currentEventType = value;
                    break;
                case "data":
                    _currentData = _currentData == null ? value : _currentData + "\n" + value;
                    break;
                // id, retry 等字段暂不处理
            }

            return null;
        }

        /// <summary>
        /// 重置解析器状态
        /// </summary>
        internal void Reset()
        {
            _currentEventType = null;
            _currentData = null;
        }
    }
}
