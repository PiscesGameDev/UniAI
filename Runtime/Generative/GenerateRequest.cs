using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// AI asset generation request.
    /// </summary>
    public class GenerateRequest
    {
        /// <summary>Generation/editing prompt.</summary>
        public string Prompt;

        /// <summary>Target asset type.</summary>
        public GenerativeAssetType AssetType;

        /// <summary>Optional provider id. Empty means use the default provider for the asset type.</summary>
        public string ProviderId;

        /// <summary>Negative prompt. Not every provider supports this.</summary>
        public string NegativePrompt;

        /// <summary>Convenience aspect ratio such as "1:1", "16:9", or "9:16".</summary>
        public string AspectRatio;

        /// <summary>Provider-native size such as "auto", "1024x1024", or "1536x1024".</summary>
        public string Size;

        /// <summary>Provider-native quality such as "auto", "low", "medium", or "high".</summary>
        public string Quality;

        /// <summary>Output format, for example "png", "jpeg", or "webp".</summary>
        public string OutputFormat;

        /// <summary>Output compression quality when supported, usually 0-100.</summary>
        public int? OutputCompression;

        /// <summary>Background preference, for example "auto", "opaque", or "transparent".</summary>
        public string Background;

        /// <summary>Image operation. Auto selects edit when input images or masks exist.</summary>
        public GenerateImageOperation ImageOperation = GenerateImageOperation.Auto;

        /// <summary>Reference/input images for image editing.</summary>
        public List<GenerateImageInput> InputImages;

        /// <summary>Optional mask image for local edits/inpainting.</summary>
        public GenerateImageInput MaskImage;

        /// <summary>Number of generated assets. Default is 1.</summary>
        public int Count = 1;

        /// <summary>Provider-specific escape hatch.</summary>
        public Dictionary<string, object> Parameters;
    }

    public enum GenerateImageOperation
    {
        Auto,
        Generate,
        Edit
    }

    public class GenerateImageInput
    {
        public byte[] Data;
        public string FileName;
        public string MediaType = "image/png";

        public GenerateImageInput() { }

        public GenerateImageInput(byte[] data, string fileName, string mediaType = "image/png")
        {
            Data = data;
            FileName = fileName;
            MediaType = string.IsNullOrEmpty(mediaType) ? "image/png" : mediaType;
        }
    }
}
