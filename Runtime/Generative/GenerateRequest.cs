using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// AI 资产生成请求
    /// </summary>
    public class GenerateRequest
    {
        /// <summary>生成提示词</summary>
        public string Prompt;

        /// <summary>目标资产类型</summary>
        public GenerativeAssetType AssetType;

        /// <summary>指定 Provider ID（为空时自动选择该类型的默认 Provider）</summary>
        public string ProviderId;

        /// <summary>负向提示词（不是所有 Provider 都支持）</summary>
        public string NegativePrompt;

        /// <summary>宽高比 "1:1" / "16:9" / "9:16" 等</summary>
        public string AspectRatio;

        /// <summary>生成数量（默认 1）</summary>
        public int Count = 1;

        /// <summary>Provider 特有参数（透传）</summary>
        public Dictionary<string, object> Parameters;
    }
}
