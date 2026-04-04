using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace UniAI
{
    /// <summary>
    /// SSE 流式下载处理器
    /// 通过 DownloadHandlerScript 实现增量数据接收，写入 Channel 供异步消费
    /// </summary>
    internal class SSEDownloadHandler : DownloadHandlerScript
    {
        private readonly Channel<string> _channel;
        private readonly StringBuilder _buffer = new();

        internal SSEDownloadHandler(Channel<string> channel) : base(new byte[4096])
        {
            _channel = channel;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            var text = Encoding.UTF8.GetString(data, 0, dataLength);
            _buffer.Append(text);

            // 按行切分，将完整行写入 channel
            while (true)
            {
                int newlineIndex = IndexOfNewline(_buffer);
                if (newlineIndex < 0) break;

                var line = _buffer.ToString(0, newlineIndex).TrimEnd('\r');
                _buffer.Remove(0, newlineIndex + 1);

                _channel.Writer.TryWrite(line);
            }

            return true;
        }

        protected override void CompleteContent()
        {
            // 处理剩余 buffer
            if (_buffer.Length > 0)
            {
                _channel.Writer.TryWrite(_buffer.ToString().TrimEnd('\r'));
                _buffer.Clear();
            }

            _channel.Writer.TryComplete();
        }

        protected override float GetProgress() => 0f;

        private static int IndexOfNewline(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n') return i;
            }
            return -1;
        }
    }
}
