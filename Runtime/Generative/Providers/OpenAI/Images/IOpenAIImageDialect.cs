using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UniAI.Providers.OpenAI.Images
{
    public interface IOpenAIImageDialect
    {
        string GetEndpointPath(GenerateRequest request);
        bool UseMultipart(GenerateRequest request);
        string Validate(GenerateRequest request, ModelEntry model);
        JObject BuildJsonBody(string model, GenerateRequest request);
        IReadOnlyList<HttpMultipartFormPart> BuildMultipartParts(string model, GenerateRequest request);
        GenerateResult ParseResponse(string json, GenerateRequest request);
        object GetCapabilities(ModelEntry model, string modelId);
    }

    public interface IOpenAIImageDialectFactory
    {
        bool CanHandle(ModelEntry model);
        IOpenAIImageDialect Create(ModelEntry model);
    }
}
