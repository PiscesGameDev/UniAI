using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 项目设置聚合工具：Tags / Layers / Physics / Time / Quality。
    /// </summary>
    [UniAITool(
        Name = "manage_project_settings",
        Group = ToolGroups.Editor,
        Description =
            "Project settings read/write. Actions: 'list_tags', 'add_tag', 'remove_tag', " +
            "'list_layers', 'set_layer', 'get_physics', 'set_physics', " +
            "'get_time', 'set_time', 'get_quality', 'set_quality'.",
        Actions = new[]
        {
            "list_tags", "add_tag", "remove_tag",
            "list_layers", "set_layer",
            "get_physics", "set_physics",
            "get_time", "set_time",
            "get_quality", "set_quality"
        })]
    internal static class ManageProjectSettings
    {
        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult<object>(ToolResponse.Error("Missing 'action'."));

            object result;
            try
            {
                result = action switch
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
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            return UniTask.FromResult(result);
        }

        public class TagNameArgs
        {
            [ToolParam(Description = "Tag name.")]
            public string Name;
        }

        public class SetLayerArgs
        {
            [ToolParam(Description = "Layer index (8-31; 0-7 are built-in).")]
            public int Index;
            [ToolParam(Description = "Layer name.")]
            public string Name;
        }

        public class SetPhysicsArgs
        {
            [ToolParam(Description = "One of: gravity, bounceThreshold, defaultSolverIterations, defaultSolverVelocityIterations, sleepThreshold, defaultContactOffset.")]
            public string Property;
            [ToolParam(Description = "Value (float/int or [x,y,z] for gravity).")]
            public object Value;
        }

        public class SetTimeArgs
        {
            [ToolParam(Description = "One of: fixedDeltaTime, maximumDeltaTime, timeScale.")]
            public string Property;
            [ToolParam(Description = "Float value.")]
            public float Value;
        }

        public class SetQualityArgs
        {
            [ToolParam(Description = "One of: level, vSyncCount, antiAliasing, shadowDistance.")]
            public string Property;
            [ToolParam(Description = "Numeric value.")]
            public object Value;
        }

        // ─── Tags ───

        private static object ListTags()
        {
            return ToolResponse.Success(new { tags = InternalEditorUtility.tags });
        }

        private static SerializedObject LoadTagManager()
        {
            return new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        }

        private static object AddTag(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");

            foreach (var t in InternalEditorUtility.tags)
                if (t == name) return ToolResponse.Success(new { name }, "Tag already exists.");

            var tagManager = LoadTagManager();
            var tagsProp = tagManager.FindProperty("tags");
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = name;
            tagManager.ApplyModifiedProperties();
            return ToolResponse.Success(new { name }, "Tag added.");
        }

        private static object RemoveTag(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");

            var tagManager = LoadTagManager();
            var tagsProp = tagManager.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == name)
                {
                    tagsProp.DeleteArrayElementAtIndex(i);
                    tagManager.ApplyModifiedProperties();
                    return ToolResponse.Success(new { name }, "Tag removed.");
                }
            }
            return ToolResponse.Error($"Tag '{name}' not found.");
        }

        // ─── Layers ───

        private static object ListLayers()
        {
            var list = new List<object>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name)) list.Add(new { index = i, name });
            }
            return ToolResponse.Success(new { layers = list });
        }

        private static object SetLayer(JObject args)
        {
            int index = (int?)args["index"] ?? -1;
            var name = (string)args["name"];
            if (index < 0 || index > 31) return ToolResponse.Error("'index' must be 0-31.");
            if (index < 8) return ToolResponse.Error("Layers 0-7 are built-in and cannot be renamed.");
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");

            var tagManager = LoadTagManager();
            var layersProp = tagManager.FindProperty("layers");
            layersProp.GetArrayElementAtIndex(index).stringValue = name;
            tagManager.ApplyModifiedProperties();
            return ToolResponse.Success(new { index, name });
        }

        // ─── Physics ───

        private static object GetPhysics()
        {
            return ToolResponse.Success(new
            {
                gravity = new[] { Physics.gravity.x, Physics.gravity.y, Physics.gravity.z },
                bounceThreshold = Physics.bounceThreshold,
                defaultSolverIterations = Physics.defaultSolverIterations,
                defaultSolverVelocityIterations = Physics.defaultSolverVelocityIterations,
                sleepThreshold = Physics.sleepThreshold,
                defaultContactOffset = Physics.defaultContactOffset
            });
        }

        private static object SetPhysics(JObject args)
        {
            var property = (string)args["property"];
            var value = args["value"];
            if (string.IsNullOrEmpty(property)) return ToolResponse.Error("'property' required.");
            if (value == null) return ToolResponse.Error("'value' required.");

            switch (property)
            {
                case "gravity":
                    var g = value as JArray ?? JArray.FromObject(value);
                    Physics.gravity = new Vector3(g[0].ToObject<float>(), g[1].ToObject<float>(), g[2].ToObject<float>());
                    break;
                case "bounceThreshold": Physics.bounceThreshold = value.ToObject<float>(); break;
                case "defaultSolverIterations": Physics.defaultSolverIterations = value.ToObject<int>(); break;
                case "defaultSolverVelocityIterations": Physics.defaultSolverVelocityIterations = value.ToObject<int>(); break;
                case "sleepThreshold": Physics.sleepThreshold = value.ToObject<float>(); break;
                case "defaultContactOffset": Physics.defaultContactOffset = value.ToObject<float>(); break;
                default: return ToolResponse.Error($"Unknown physics property '{property}'.");
            }
            return ToolResponse.Success(new { property }, "Physics property updated.");
        }

        // ─── Time ───

        private static object GetTime()
        {
            return ToolResponse.Success(new
            {
                fixedDeltaTime = Time.fixedDeltaTime,
                maximumDeltaTime = Time.maximumDeltaTime,
                timeScale = Time.timeScale
            });
        }

        private static object SetTime(JObject args)
        {
            var property = (string)args["property"];
            var value = args["value"];
            if (string.IsNullOrEmpty(property)) return ToolResponse.Error("'property' required.");
            if (value == null) return ToolResponse.Error("'value' required.");

            float v = value.ToObject<float>();
            switch (property)
            {
                case "fixedDeltaTime": Time.fixedDeltaTime = v; break;
                case "maximumDeltaTime": Time.maximumDeltaTime = v; break;
                case "timeScale": Time.timeScale = v; break;
                default: return ToolResponse.Error($"Unknown time property '{property}'.");
            }
            return ToolResponse.Success(new { property, value = v });
        }

        // ─── Quality ───

        private static object GetQuality()
        {
            var names = QualitySettings.names;
            int level = QualitySettings.GetQualityLevel();
            return ToolResponse.Success(new
            {
                currentLevel = level,
                currentName = names[level],
                levels = names,
                vSyncCount = QualitySettings.vSyncCount,
                antiAliasing = QualitySettings.antiAliasing,
                shadowDistance = QualitySettings.shadowDistance
            });
        }

        private static object SetQuality(JObject args)
        {
            var property = (string)args["property"];
            var value = args["value"];
            if (string.IsNullOrEmpty(property)) return ToolResponse.Error("'property' required.");
            if (value == null) return ToolResponse.Error("'value' required.");

            switch (property)
            {
                case "level": QualitySettings.SetQualityLevel(value.ToObject<int>()); break;
                case "vSyncCount": QualitySettings.vSyncCount = value.ToObject<int>(); break;
                case "antiAliasing": QualitySettings.antiAliasing = value.ToObject<int>(); break;
                case "shadowDistance": QualitySettings.shadowDistance = value.ToObject<float>(); break;
                default: return ToolResponse.Error($"Unknown quality property '{property}'.");
            }
            return ToolResponse.Success(new { property }, "Quality setting updated.");
        }
    }
}
