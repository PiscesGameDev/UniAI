namespace UniAI
{
    /// <summary>
    /// 内容块基类
    /// </summary>
    public abstract class AIContent
    {
        public abstract string Type { get; }
    }

    /// <summary>
    /// 文本内容
    /// </summary>
    public class AITextContent : AIContent
    {
        public override string Type => "text";
        public string Text { get; set; }

        public AITextContent(string text) => Text = text;
    }

    /// <summary>
    /// 图片内容（base64）
    /// </summary>
    public class AIImageContent : AIContent
    {
        public override string Type => "image";
        public byte[] Data { get; set; }
        public string MediaType { get; set; }

        public AIImageContent(byte[] data, string mediaType = "image/png")
        {
            Data = data;
            MediaType = mediaType;
        }
    }
}
