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

            [ToolParam(Description = "Aspect ratio for images: '1:1', '16:9', '9:16'.", Required = false)]
            public string AspectRatio;

            [ToolParam(Description = "Provider-native image size such as 'auto', '1024x1024', '1536x1024'.", Required = false)]
            public string Size;

            [ToolParam(Description = "Image quality such as 'auto', 'low', 'medium', or 'high'.", Required = false)]
            public string Quality;

            [ToolParam(Description = "Output format such as 'png', 'jpeg', or 'webp'.", Required = false)]
            public string OutputFormat;

            [ToolParam(Description = "Output compression, usually 0-100 when supported.", Required = false)]
            public int OutputCompression;

            [ToolParam(Description = "Background preference such as 'auto', 'opaque', or 'transparent'.", Required = false)]
            public string Background;

            [ToolParam(Description = "Input image asset/file paths for image editing. Use paths under Assets/.", Required = false)]
            public string[] InputImages;

            [ToolParam(Description = "Optional mask image asset/file path for image editing. Use a path under Assets/.", Required = false)]
            public string MaskImage;

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

            // Model metadata determines whether this model can produce generated assets.
            var entry = ModelRegistry.Get(model);
            var capabilities = entry?.Capabilities ?? ModelCapability.Chat;
            if (!IsGenerativeModel(capabilities))
                return ToolResponse.Error($"Model '{model}' does not support asset generation. Use 'list_models' to see available generative models.");

            // Find compatible channels, then let the runtime router choose a provider.
            var config = AIConfigManager.LoadConfig();
            var channels = config.FindChannelsForModel(model);
            if (channels.Count == 0)
                return ToolResponse.Error($"Model '{model}' not found in any enabled channel. Add it to a channel's model list first.");

            var route = GenerativeProviderRouter.Resolve(channels, entry, model, config.General);
            if (route.Provider == null)
                return ToolResponse.Error(route.Error);

            GenerateResult result;
            try
            {
                if (GenerativeProviderRouter.HasImageGenerationCapability(capabilities))
                    result = await GenerateImage(route.Provider, args, ct);
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
            return SaveAssets(result, prompt, model, route.Channel.Name, (string)args["savePath"]);
        }

        private static async UniTask<GenerateResult> GenerateImage(
            IGenerativeAssetProvider provider, JObject args, CancellationToken ct)
        {
            var aspectRatio = (string)args["aspectRatio"];
            int count = (int?)args["count"] ?? 1;
            var inputImages = LoadInputImages(args["inputImages"] as JArray);
            var maskImage = LoadInputImage((string)args["maskImage"]);

            var request = new GenerateRequest
            {
                Prompt = (string)args["prompt"],
                AssetType = GenerativeAssetType.Image,
                AspectRatio = aspectRatio,
                Size = (string)args["size"],
                Quality = (string)args["quality"],
                OutputFormat = (string)args["outputFormat"],
                OutputCompression = (int?)args["outputCompression"],
                Background = (string)args["background"],
                InputImages = inputImages,
                MaskImage = maskImage,
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
                    adapterId = entry?.AdapterId ?? "",
                    behavior = (entry?.Behavior ?? ModelBehavior.None).ToString(),
                    behaviorTags = entry?.BehaviorTags ?? new List<string>(),
                    behaviorOptions = FormatModelBehaviorOptions(entry?.BehaviorOptions),
                    description = entry?.Description ?? "",
                    channels
                };

                if (!IsGenerativeModel(capabilities))
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

        private static List<GenerateImageInput> LoadInputImages(JArray paths)
        {
            if (paths == null || paths.Count == 0)
                return null;

            var result = new List<GenerateImageInput>();
            foreach (var item in paths)
            {
                var input = LoadInputImage((string)item);
                if (input != null)
                    result.Add(input);
            }

            return result.Count > 0 ? result : null;
        }

        private static List<object> FormatModelBehaviorOptions(IReadOnlyList<ModelBehaviorOption> options)
        {
            var result = new List<object>();
            if (options == null)
                return result;

            foreach (var option in options)
            {
                if (option == null)
                    continue;

                result.Add(new { key = option.Key, value = option.Value });
            }

            return result;
        }

        private static GenerateImageInput LoadInputImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (!File.Exists(path))
                throw new FileNotFoundException($"Image file not found: {path}", path);

            return new GenerateImageInput(
                File.ReadAllBytes(path),
                Path.GetFileName(path),
                GuessImageMediaType(path));
        }

        private static string GuessImageMediaType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };
        }

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

        private static bool IsGenerativeModel(ModelCapability capabilities)
        {
            return GenerativeProviderRouter.IsGenerativeModel(capabilities);
        }
    }
}
