using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Unity Selection（当前选中的 GameObject 或资产）读写 Tool。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Selection", fileName = "SelectionTool")]
    public class SelectionTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            SelectionArgs args;
            try { args = JsonConvert.DeserializeObject<SelectionArgs>(arguments) ?? new SelectionArgs(); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            string action = string.IsNullOrEmpty(args.Action) ? "get" : args.Action.ToLowerInvariant();

            string result = action switch
            {
                "get" => Get(),
                "set" => Set(args),
                "clear" => Clear(),
                "get_assets" => GetAssets(),
                _ => $"Error: Unknown action '{args.Action}'."
            };

            return UniTask.FromResult(result);
        }

        private static string Get()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
                return "Selection is empty.";

            var sb = new StringBuilder($"Selection ({objects.Length} object(s)):\n");
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                if (obj is GameObject go)
                {
                    sb.AppendLine($"  [GameObject] {GetFullPath(go)} (scene: {(go.scene.IsValid() ? go.scene.name : "<prefab asset>")})");
                }
                else
                {
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                        sb.AppendLine($"  [{obj.GetType().Name}] {assetPath}");
                    else
                        sb.AppendLine($"  [{obj.GetType().Name}] {obj.name}");
                }
            }
            return sb.ToString();
        }

        private static string Set(SelectionArgs args)
        {
            var targets = new List<UnityEngine.Object>();

            // 场景 GameObject 路径
            if (args.Paths != null && args.Paths.Length > 0)
            {
                foreach (var p in args.Paths)
                {
                    var go = GameObject.Find(p);
                    if (go == null)
                    {
                        // 模糊匹配按名
                        string leaf = p.Contains('/') ? p.Substring(p.LastIndexOf('/') + 1) : p;
                        var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                        foreach (var g in all)
                            if (g.name == leaf) { go = g; break; }
                    }
                    if (go != null) targets.Add(go);
                }
            }

            // 资产路径
            if (args.AssetPaths != null && args.AssetPaths.Length > 0)
            {
                foreach (var p in args.AssetPaths)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                    if (asset != null) targets.Add(asset);
                }
            }

            if (targets.Count == 0)
                return "Error: No valid targets found in 'paths' or 'asset_paths'.";

            Selection.objects = targets.ToArray();
            return $"Selected {targets.Count} object(s).";
        }

        private static string Clear()
        {
            Selection.objects = Array.Empty<UnityEngine.Object>();
            return "Selection cleared.";
        }

        private static string GetAssets()
        {
            string[] guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
                return "No assets selected in Project window.";

            var sb = new StringBuilder($"Selected assets ({guids.Length}):\n");
            foreach (var guid in guids)
                sb.AppendLine($"  - {AssetDatabase.GUIDToAssetPath(guid)}");
            return sb.ToString();
        }

        private static string GetFullPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }

        private class SelectionArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("paths")] public string[] Paths;
            [JsonProperty("asset_paths")] public string[] AssetPaths;
        }
    }
}
