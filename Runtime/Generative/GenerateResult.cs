using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// AI 资产生成结果
    /// </summary>
    public class GenerateResult
    {
        public bool IsSuccess;
        public string Error;
        public List<GeneratedAsset> Assets;

        public static GenerateResult Success(List<GeneratedAsset> assets)
            => new() { IsSuccess = true, Assets = assets };

        public static GenerateResult Fail(string error)
            => new() { IsSuccess = false, Error = error, Assets = new List<GeneratedAsset>() };
    }

    /// <summary>
    /// 单个生成的资产
    /// </summary>
    public class GeneratedAsset
    {
        /// <summary>原始二进制数据</summary>
        public byte[] Data;

        /// <summary>MIME 类型: image/png, audio/wav 等</summary>
        public string MediaType;

        /// <summary>建议扩展名: .png, .wav</summary>
        public string SuggestedExtension;

        /// <summary>Provider 返回的额外元数据</summary>
        public Dictionary<string, object> Metadata;
    }
}
