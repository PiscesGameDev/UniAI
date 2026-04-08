using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 预制体生命周期 Tool：从 GameObject 创建、实例化、拆包、应用/回滚覆盖。
    /// 仅 Edit Mode 下生效。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Prefab Edit", fileName = "PrefabEditTool")]
    public class PrefabEditTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (Application.isPlaying)
                return UniTask.FromResult("Error: PrefabEditTool is only available in Edit Mode.");

            PrefabEditArgs args;
            try { args = JsonConvert.DeserializeObject<PrefabEditArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing required parameter 'action'.");

            string result;
            try
            {
                result = args.Action.ToLowerInvariant() switch
                {
                    "create_from_gameobject" => CreateFromGameObject(args),
                    "instantiate" => Instantiate(args),
                    "unpack" => Unpack(args),
                    "apply_overrides" => ApplyOverrides(args),
                    "revert_overrides" => RevertOverrides(args),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
            }

            return UniTask.FromResult(result);
        }

        private static string CreateFromGameObject(PrefabEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' (scene GameObject path) required.";
            if (string.IsNullOrEmpty(args.SavePath)) return "Error: 'save_path' (Assets/.../X.prefab) required.";
            if (!args.SavePath.StartsWith("Assets/")) return "Error: save_path must start with 'Assets/'.";
            if (!args.SavePath.EndsWith(".prefab")) return "Error: save_path must end with '.prefab'.";

            if (!TryLocate(args.Path, out var go, out var err)) return err;

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, args.SavePath, InteractionMode.AutomatedAction);
            if (prefab == null) return $"Error: Failed to create prefab at '{args.SavePath}'.";

            EditorSceneManager.MarkSceneDirty(go.scene);
            return $"Created prefab: {args.SavePath} (from {GetFullPath(go)})";
        }

        private static string Instantiate(PrefabEditArgs args)
        {
            if (string.IsNullOrEmpty(args.SavePath)) return "Error: 'save_path' (prefab asset path) required.";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(args.SavePath);
            if (prefab == null) return $"Error: Prefab not found at '{args.SavePath}'.";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return "Error: InstantiatePrefab returned null.";

            if (!string.IsNullOrEmpty(args.Name)) instance.name = args.Name;

            if (!string.IsNullOrEmpty(args.Parent))
            {
                if (TryLocate(args.Parent, out var parent, out _))
                    instance.transform.SetParent(parent.transform, false);
            }

            if (args.Position != null && args.Position.Length >= 3)
                instance.transform.localPosition = new Vector3(args.Position[0], args.Position[1], args.Position[2]);

            Undo.RegisterCreatedObjectUndo(instance, "UniAI: instantiate_prefab");
            EditorSceneManager.MarkSceneDirty(instance.scene);
            return $"Instantiated prefab '{args.SavePath}' as {GetFullPath(instance)}";
        }

        private static string Unpack(PrefabEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{go.name}' is not a prefab instance.";

            var mode = string.Equals(args.Mode, "completely", StringComparison.OrdinalIgnoreCase)
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return $"Unpacked prefab instance '{GetFullPath(go)}' ({mode})";
        }

        private static string ApplyOverrides(PrefabEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{go.name}' is not a prefab instance.";

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            return $"Applied overrides of '{GetFullPath(go)}' to source prefab.";
        }

        private static string RevertOverrides(PrefabEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{go.name}' is not a prefab instance.";

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return $"Reverted overrides of '{GetFullPath(go)}'.";
        }

        // ─── 辅助 ───

        private static bool TryLocate(string path, out GameObject go, out string error)
        {
            go = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Error: 'path' parameter required.";
                return false;
            }

            go = GameObject.Find(path);
            if (go != null) return true;

            string leaf = path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;
            var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var g in all)
            {
                if (g.name == leaf) { go = g; return true; }
            }

            error = $"Error: GameObject '{path}' not found.";
            return false;
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

        private class PrefabEditArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("path")] public string Path;
            [JsonProperty("save_path")] public string SavePath;
            [JsonProperty("parent")] public string Parent;
            [JsonProperty("name")] public string Name;
            [JsonProperty("position")] public float[] Position;
            [JsonProperty("mode")] public string Mode;
        }
    }
}
