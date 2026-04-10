using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// AI 资产生成与保存工具 — 统一的生成式资产管理入口。
    /// 根据 ModelRegistry 确定模型能力，路由到正确的 API 端点，生成后保存到 Assets/ 目录。
    /// </summary>
    [UniAITool(
        Name = "manage_generate",
        Group = ToolGroups.Generate,
        Description =
            "Generate and save AI-created assets. Actions: " +
            "'generate' (create image/audio/video from text prompt — model determines capability, e.g. 'dall-e-3' for images), " +
            "'list_models' (list all models with their capabilities — shows which models can generate images/audio/etc).",
        Actions = new[] { "generate", "list_models" },
        RequiresPolling = true,
        MaxPollSeconds = 120)]
    internal static class ManageGenerate
    {
        private const string GENERATED_DIR = "Assets/Generated";

        public static async UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return ToolResponse.Error("Missing required parameter 'action'.");

            return action switch
            {
                "generate" => await Generate(args, ct),
                "list_models" => ListModels(),
                _ => ToolResponse.Error($"Unknown action '{action}'.")
            };
        }

        // ─── Args ───

        public class GenerateArgs
        {
            [ToolParam(Description = "Text prompt describing what to generate.")]
            public string Prompt;

            [ToolParam(Description = "Model name (e.g. 'dall-e-3', 'gemini-imagen-3'). Must be in a channel's model list. Model capability (image/audio/video) is auto-detected from ModelRegistry.")]
            public string Model;

            [ToolParam(Description = "Aspect ratio for images: '1:1', '16:9', '9:16'.", Required = false, DefaultValue = "1:1")]
            public string AspectRatio;

            [ToolParam(Description = "Number of assets to generate (default 1).", Required = false)]
            public int Count;

            [ToolParam(Description = "Save path under Assets/ (e.g. 'Assets/Generated/my_image.png'). Auto-generated if empty.", Required = false)]
            public string SavePath;
        }

        public class ListModelsArgs { }

        // ─── Actions ───

        private static async UniTask<object> Generate(JObject args, CancellationToken ct)
        {
            var prompt = (string)args["prompt"];
            if (string.IsNullOrEmpty(prompt))
                return ToolResponse.Error("'prompt' is required.");

            var model = (string)args["model"];
            if (string.IsNullOrEmpty(model))
                return ToolResponse.Error("'model' is required. Use 'list_models' to see available models and their capabilities.");

            // 查询模型能力 — 必须支持至少一种生成能力
            var capabilities = ModelRegistry.GetCapabilities(model);
            if (capabilities == ModelCapability.Chat)
                return ToolResponse.Error($"Model '{model}' is a chat-only model. Use 'list_models' to see available generative models.");

            // 查找渠道
            var config = AIConfigManager.LoadConfig();
            var channels = config.FindChannelsForModel(model);
            if (channels.Count == 0)
                return ToolResponse.Error($"Model '{model}' not found in any enabled channel. Add it to a channel's model list first.");

            var channel = channels[0];
            var apiKey = channel.GetEffectiveApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return ToolResponse.Error($"Channel '{channel.Name}' has no API key configured.");

            // 根据能力路由到对应生成器
            GenerateResult result;
            try
            {
                if (ModelRegistry.HasCapability(model, ModelCapability.ImageGen))
                    result = await GenerateImage(channel, apiKey, model, args, ct);
                else
                    result = GenerateResult.Fail($"Model '{model}' capabilities ({capabilities}) are not yet supported for generation.");
            }
            catch (OperationCanceledException)
            {
                return ToolResponse.Error("Generation cancelled.");
            }
            catch (Exception ex)
            {
                return ToolResponse.Error($"Generation failed: {ex.Message}");
            }

            if (!result.IsSuccess)
                return ToolResponse.Error($"Generation failed: {result.Error}");

            if (result.Assets == null || result.Assets.Count == 0)
                return ToolResponse.Error("Generation succeeded but returned no assets.");

            // 保存到 Assets/
            return SaveAssets(result, prompt, model, channel.Name, (string)args["savePath"]);
        }

        private static async UniTask<GenerateResult> GenerateImage(
            ChannelEntry channel, string apiKey, string model, JObject args, CancellationToken ct)
        {
            var aspectRatio = (string)args["aspectRatio"] ?? "1:1";
            int count = (int?)args["count"] ?? 1;

            var provider = new OpenAIImageProvider(
                apiKey, channel.BaseUrl, model,
                providerId: $"image-{channel.Id}-{model}",
                displayName: $"{channel.Name} ({model})");

            var request = new GenerateRequest
            {
                Prompt = (string)args["prompt"],
                AssetType = GenerativeAssetType.Image,
                AspectRatio = aspectRatio,
                Count = count
            };

            return await provider.GenerateAsync(request, ct);
        }

        private static object SaveAssets(GenerateResult result, string prompt, string model, string channelName, string savePath)
        {
            EnsureGeneratedDir();
            var savedAssets = new List<object>();

            for (int i = 0; i < result.Assets.Count; i++)
            {
                var asset = result.Assets[i];
                var filePath = ResolveFilePath(savePath, asset.SuggestedExtension, i, result.Assets.Count);

                var fullPath = Path.GetFullPath(filePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(fullPath, asset.Data);

                savedAssets.Add(new
                {
                    path = filePath,
                    mediaType = asset.MediaType,
                    sizeBytes = asset.Data.Length,
                    metadata = asset.Metadata
                });
            }

            AssetDatabase.Refresh();

            return ToolResponse.Success(new
            {
                generatedAssets = savedAssets,
                prompt,
                model,
                channel = channelName,
                count = savedAssets.Count
            });
        }

        private static object ListModels()
        {
            var config = AIConfigManager.LoadConfig();
            var channelModels = new Dictionary<string, List<string>>();

            if (config?.ChannelEntries != null)
            {
                foreach (var ch in config.ChannelEntries)
                {
                    if (!ch.Enabled || ch.Models == null) continue;
                    foreach (var modelId in ch.Models)
                    {
                        if (!channelModels.ContainsKey(modelId))
                            channelModels[modelId] = new List<string>();
                        channelModels[modelId].Add(ch.Name);
                    }
                }
            }

            // 合并渠道模型 + 注册表信息，按能力分组
            var generative = new List<object>();
            var chat = new List<object>();

            foreach (var (modelId, channels) in channelModels)
            {
                var entry = ModelRegistry.Get(modelId);
                var capabilities = entry?.Capabilities ?? ModelCapability.Chat;
                var info = new
                {
                    model = modelId,
                    vendor = entry?.Vendor ?? "Unknown",
                    capabilities = capabilities.ToString(),
                    endpoint = (entry?.Endpoint ?? ModelEndpoint.ChatCompletions).ToString(),
                    description = entry?.Description ?? "",
                    channels
                };

                if (capabilities == ModelCapability.Chat)
                    chat.Add(info);
                else
                    generative.Add(info);
            }

            return ToolResponse.Success(new
            {
                generativeModels = generative,
                chatModels = chat,
                hint = "Use 'generate' with a generative model (ImageGen/AudioGen/VideoGen). Chat models cannot generate assets."
            });
        }

        // ─── 辅助 ───

        private static string ResolveFilePath(string savePath, string extension, int index, int total)
        {
            if (!string.IsNullOrEmpty(savePath))
            {
                if (total > 1 && index > 0)
                {
                    var dir = Path.GetDirectoryName(savePath);
                    var name = Path.GetFileNameWithoutExtension(savePath);
                    var ext = Path.GetExtension(savePath);
                    return Path.Combine(dir ?? GENERATED_DIR, $"{name}_{index}{ext}");
                }
                return savePath;
            }

            extension ??= ".png";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var suffix = total > 1 ? $"_{index}" : "";
            return $"{GENERATED_DIR}/img_{timestamp}{suffix}{extension}";
        }

        private static void EnsureGeneratedDir()
        {
            if (!AssetDatabase.IsValidFolder(GENERATED_DIR))
            {
                AssetDatabase.CreateFolder("Assets", "Generated");
            }
        }
    }
}
