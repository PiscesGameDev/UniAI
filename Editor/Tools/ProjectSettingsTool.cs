using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 项目设置读写 Tool：Tags / Layers / Physics / Time / Quality。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Project Settings", fileName = "ProjectSettingsTool")]
    public class ProjectSettingsTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            ProjectSettingsArgs args;
            try { args = JsonConvert.DeserializeObject<ProjectSettingsArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing 'action'.");

            string result;
            try
            {
                result = args.Action.ToLowerInvariant() switch
                {
                    "list_tags" => ListTags(),
                    "add_tag" => AddTag(args),
                    "remove_tag" => RemoveTag(args),
                    "list_layers" => ListLayers(),
                    "set_layer" => SetLayer(args),
                    "get_physics" => GetPhysics(),
                    "set_physics" => SetPhysics(args),
                    "get_time" => GetTime(),
                    "set_time" => SetTime(args),
                    "get_quality" => GetQuality(),
                    "set_quality" => SetQuality(args),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex) { result = $"Error: {ex.Message}"; }

            return UniTask.FromResult(result);
        }

        // ─── Tags ───

        private static string ListTags()
        {
            var tags = InternalEditorUtility.tags;
            var sb = new StringBuilder($"Tags ({tags.Length}):\n");
            foreach (var t in tags) sb.AppendLine($"  - {t}");
            return sb.ToString();
        }

        private static string AddTag(ProjectSettingsArgs args)
        {
            if (string.IsNullOrEmpty(args.Name)) return "Error: 'name' required.";

            foreach (var t in InternalEditorUtility.tags)
                if (t == args.Name) return $"Tag '{args.Name}' already exists.";

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tagsProp = tagManager.FindProperty("tags");
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = args.Name;
            tagManager.ApplyModifiedProperties();
            return $"Added tag: {args.Name}";
        }

        private static string RemoveTag(ProjectSettingsArgs args)
        {
            if (string.IsNullOrEmpty(args.Name)) return "Error: 'name' required.";

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tagsProp = tagManager.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == args.Name)
                {
                    tagsProp.DeleteArrayElementAtIndex(i);
                    tagManager.ApplyModifiedProperties();
                    return $"Removed tag: {args.Name}";
                }
            }
            return $"Error: Tag '{args.Name}' not found.";
        }

        // ─── Layers ───

        private static string ListLayers()
        {
            var sb = new StringBuilder("Layers:\n");
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name)) sb.AppendLine($"  {i}: {name}");
            }
            return sb.ToString();
        }

        private static string SetLayer(ProjectSettingsArgs args)
        {
            if (args.Index < 0 || args.Index > 31) return "Error: 'index' must be 0-31.";
            if (args.Index < 8) return "Error: Layers 0-7 are built-in and cannot be renamed.";
            if (string.IsNullOrEmpty(args.Name)) return "Error: 'name' required.";

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProp = tagManager.FindProperty("layers");
            layersProp.GetArrayElementAtIndex(args.Index).stringValue = args.Name;
            tagManager.ApplyModifiedProperties();
            return $"Set layer {args.Index} = '{args.Name}'";
        }

        // ─── Physics ───

        private static string GetPhysics()
        {
            var sb = new StringBuilder("Physics:\n");
            sb.AppendLine($"  gravity: {Physics.gravity}");
            sb.AppendLine($"  bounceThreshold: {Physics.bounceThreshold}");
            sb.AppendLine($"  defaultSolverIterations: {Physics.defaultSolverIterations}");
            sb.AppendLine($"  defaultSolverVelocityIterations: {Physics.defaultSolverVelocityIterations}");
            sb.AppendLine($"  sleepThreshold: {Physics.sleepThreshold}");
            sb.AppendLine($"  defaultContactOffset: {Physics.defaultContactOffset}");
            return sb.ToString();
        }

        private static string SetPhysics(ProjectSettingsArgs args)
        {
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            switch (args.Property)
            {
                case "gravity":
                    var g = args.Value as JArray ?? JArray.FromObject(args.Value);
                    Physics.gravity = new Vector3(g[0].ToObject<float>(), g[1].ToObject<float>(), g[2].ToObject<float>());
                    break;
                case "bounceThreshold": Physics.bounceThreshold = args.Value.ToObject<float>(); break;
                case "defaultSolverIterations": Physics.defaultSolverIterations = args.Value.ToObject<int>(); break;
                case "defaultSolverVelocityIterations": Physics.defaultSolverVelocityIterations = args.Value.ToObject<int>(); break;
                case "sleepThreshold": Physics.sleepThreshold = args.Value.ToObject<float>(); break;
                case "defaultContactOffset": Physics.defaultContactOffset = args.Value.ToObject<float>(); break;
                default: return $"Error: Unknown physics property '{args.Property}'.";
            }
            return $"Set Physics.{args.Property}";
        }

        // ─── Time ───

        private static string GetTime()
        {
            var sb = new StringBuilder("Time:\n");
            sb.AppendLine($"  fixedDeltaTime: {Time.fixedDeltaTime}");
            sb.AppendLine($"  maximumDeltaTime: {Time.maximumDeltaTime}");
            sb.AppendLine($"  timeScale: {Time.timeScale}");
            return sb.ToString();
        }

        private static string SetTime(ProjectSettingsArgs args)
        {
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            float v = args.Value.ToObject<float>();
            switch (args.Property)
            {
                case "fixedDeltaTime": Time.fixedDeltaTime = v; break;
                case "maximumDeltaTime": Time.maximumDeltaTime = v; break;
                case "timeScale": Time.timeScale = v; break;
                default: return $"Error: Unknown time property '{args.Property}'.";
            }
            return $"Set Time.{args.Property} = {v}";
        }

        // ─── Quality ───

        private static string GetQuality()
        {
            var names = QualitySettings.names;
            var sb = new StringBuilder($"Quality (current: {QualitySettings.GetQualityLevel()} / {names[QualitySettings.GetQualityLevel()]}):\n");
            for (int i = 0; i < names.Length; i++)
                sb.AppendLine($"  {i}: {names[i]}");
            sb.AppendLine($"\nvSyncCount: {QualitySettings.vSyncCount}");
            sb.AppendLine($"antiAliasing: {QualitySettings.antiAliasing}");
            sb.AppendLine($"shadowDistance: {QualitySettings.shadowDistance}");
            return sb.ToString();
        }

        private static string SetQuality(ProjectSettingsArgs args)
        {
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            switch (args.Property)
            {
                case "level": QualitySettings.SetQualityLevel(args.Value.ToObject<int>()); break;
                case "vSyncCount": QualitySettings.vSyncCount = args.Value.ToObject<int>(); break;
                case "antiAliasing": QualitySettings.antiAliasing = args.Value.ToObject<int>(); break;
                case "shadowDistance": QualitySettings.shadowDistance = args.Value.ToObject<float>(); break;
                default: return $"Error: Unknown quality property '{args.Property}'.";
            }
            return $"Set QualitySettings.{args.Property}";
        }

        private class ProjectSettingsArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("name")] public string Name;
            [JsonProperty("index")] public int Index;
            [JsonProperty("property")] public string Property;
            [JsonProperty("value")] public JToken Value;
        }
    }
}
