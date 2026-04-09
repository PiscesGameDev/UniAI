using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Unity Selection 聚合工具：get / set / clear / get_assets。
    /// </summary>
    [UniAITool(
        Name = "manage_selection",
        Group = ToolGroups.Editor,
        Description = "Unity Selection. Actions: 'get' (current scene+asset selection), 'set' (scene paths + asset paths), 'clear', 'get_assets' (Project window selection).",
        Actions = new[] { "get", "set", "clear", "get_assets" })]
    internal static class ManageSelection
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
                    "get" => Get(),
                    "set" => Set(args),
                    "clear" => Clear(),
                    "get_assets" => GetAssets(),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            return UniTask.FromResult(result);
        }

        public class SetArgs
        {
            [ToolParam(Description = "Scene GameObject paths/names.", Required = false)]
            public string[] Paths;
            [ToolParam(Description = "Asset paths (Project).", Required = false)]
            public string[] AssetPaths;
        }

        // ─── 实现 ───

        private static object Get()
        {
            var objects = Selection.objects;
            var list = new List<object>();
            if (objects != null)
            {
                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    if (obj is GameObject go)
                    {
                        list.Add(new
                        {
                            kind = "GameObject",
                            name = GetFullPath(go),
                            scene = go.scene.IsValid() ? go.scene.name : null
                        });
                    }
                    else
                    {
                        string assetPath = AssetDatabase.GetAssetPath(obj);
                        list.Add(new
                        {
                            kind = obj.GetType().Name,
                            name = string.IsNullOrEmpty(assetPath) ? obj.name : assetPath
                        });
                    }
                }
            }
            return ToolResponse.Success(new { count = list.Count, selection = list });
        }

        private static object Set(JObject args)
        {
            var targets = new List<UnityEngine.Object>();

            if (args["paths"] is JArray paths)
            {
                foreach (var token in paths)
                {
                    var p = token.ToObject<string>();
                    if (string.IsNullOrEmpty(p)) continue;
                    var go = GameObject.Find(p);
                    if (go == null)
                    {
                        string leaf = p.Contains('/') ? p.Substring(p.LastIndexOf('/') + 1) : p;
                        var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                        foreach (var g in all)
                            if (g.name == leaf) { go = g; break; }
                    }
                    if (go != null) targets.Add(go);
                }
            }

            if (args["assetPaths"] is JArray assetPaths)
            {
                foreach (var token in assetPaths)
                {
                    var p = token.ToObject<string>();
                    if (string.IsNullOrEmpty(p)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                    if (asset != null) targets.Add(asset);
                }
            }

            if (targets.Count == 0)
                return ToolResponse.Error("No valid targets found in 'paths' or 'assetPaths'.");

            Selection.objects = targets.ToArray();
            return ToolResponse.Success(new { selected = targets.Count });
        }

        private static object Clear()
        {
            Selection.objects = Array.Empty<UnityEngine.Object>();
            return ToolResponse.Success(new { cleared = true });
        }

        private static object GetAssets()
        {
            string[] guids = Selection.assetGUIDs ?? Array.Empty<string>();
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            return ToolResponse.Success(new { count = paths.Length, assets = paths });
        }

        private static string GetFullPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
            return sb.ToString();
        }
    }
}
