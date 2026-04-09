using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 预制体生命周期聚合工具：create_from_gameobject / instantiate / unpack / apply_overrides / revert_overrides。
    /// </summary>
    [UniAITool(
        Name = "manage_prefab",
        Group = ToolGroups.Asset,
        Description =
            "Prefab lifecycle. Actions: 'create_from_gameobject' (save scene GO as prefab asset), " +
            "'instantiate' (place prefab in active scene), 'unpack', 'apply_overrides', 'revert_overrides'.",
        Actions = new[] { "create_from_gameobject", "instantiate", "unpack", "apply_overrides", "revert_overrides" })]
    internal static class ManagePrefab
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
                    "create_from_gameobject" => CreateFromGameObject(args),
                    "instantiate" => Instantiate(args),
                    "unpack" => Unpack(args),
                    "apply_overrides" => ApplyOverrides(args),
                    "revert_overrides" => RevertOverrides(args),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            EditorAgentGuard.NotifyAssetsModified();
            return UniTask.FromResult(result);
        }

        public class CreateFromGameObjectArgs
        {
            [ToolParam(Description = "Scene GameObject path/name to convert into a prefab.")]
            public string Path;
            [ToolParam(Description = "Destination prefab path (must end with '.prefab').")]
            public string SavePath;
        }

        public class InstantiateArgs
        {
            [ToolParam(Description = "Prefab asset path.")]
            public string SavePath;
            [ToolParam(Description = "Optional name override for the instance.", Required = false)]
            public string Name;
            [ToolParam(Description = "Optional parent path/name.", Required = false)]
            public string Parent;
            [ToolParam(Description = "Optional local position [x,y,z].", Required = false)]
            public float[] Position;
        }

        public class UnpackArgs
        {
            [ToolParam(Description = "Scene GameObject path/name (must be a prefab instance).")]
            public string Path;
            [ToolParam(Description = "'completely' or 'outermost_root' (default 'outermost_root').", Required = false)]
            public string Mode;
        }

        public class ApplyOverridesArgs
        {
            [ToolParam(Description = "Scene GameObject path/name (prefab instance).")]
            public string Path;
        }

        public class RevertOverridesArgs : ApplyOverridesArgs { }

        // ─── 实现 ───

        private static object CreateFromGameObject(JObject args)
        {
            var path = (string)args["path"];
            var savePath = (string)args["savePath"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            if (string.IsNullOrEmpty(savePath)) return ToolResponse.Error("'savePath' required.");
            if (!savePath.StartsWith("Assets/")) return ToolResponse.Error("savePath must start with 'Assets/'.");
            if (!savePath.EndsWith(".prefab")) return ToolResponse.Error("savePath must end with '.prefab'.");

            if (!TryLocate(path, out var go, out var err)) return ToolResponse.Error(err);

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.AutomatedAction);
            if (prefab == null) return ToolResponse.Error($"Failed to create prefab at '{savePath}'.");

            SceneEdit.MarkDirty(go.scene);
            return ToolResponse.Success(new { savePath, source = GetFullPath(go) });
        }

        private static object Instantiate(JObject args)
        {
            var savePath = (string)args["savePath"];
            if (string.IsNullOrEmpty(savePath)) return ToolResponse.Error("'savePath' required.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(savePath);
            if (prefab == null) return ToolResponse.Error($"Prefab not found at '{savePath}'.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return ToolResponse.Error("InstantiatePrefab returned null.");

            var name = (string)args["name"];
            if (!string.IsNullOrEmpty(name)) instance.name = name;

            var parent = (string)args["parent"];
            if (!string.IsNullOrEmpty(parent) && TryLocate(parent, out var parentGo, out _))
                instance.transform.SetParent(parentGo.transform, false);

            if (args["position"] is JArray pos && pos.Count >= 3)
                instance.transform.localPosition = new Vector3(pos[0].ToObject<float>(), pos[1].ToObject<float>(), pos[2].ToObject<float>());

            SceneEdit.RegisterCreated(instance, "UniAI: instantiate_prefab");
            SceneEdit.MarkDirty(instance.scene);
            return ToolResponse.Success(new { savePath, instance = GetFullPath(instance) });
        }

        private static object Unpack(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return ToolResponse.Error($"'{go.name}' is not a prefab instance.");

            var mode = string.Equals((string)args["mode"], "completely", StringComparison.OrdinalIgnoreCase)
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.AutomatedAction);
            SceneEdit.MarkDirty(go.scene);
            return ToolResponse.Success(new { path = GetFullPath(go), mode = mode.ToString() });
        }

        private static object ApplyOverrides(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return ToolResponse.Error($"'{go.name}' is not a prefab instance.");
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            return ToolResponse.Success(new { path = GetFullPath(go) }, "Applied to source prefab.");
        }

        private static object RevertOverrides(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return ToolResponse.Error($"'{go.name}' is not a prefab instance.");
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            SceneEdit.MarkDirty(go.scene);
            return ToolResponse.Success(new { path = GetFullPath(go) }, "Reverted overrides.");
        }

        // ─── 辅助 ───

        private static bool TryLocate(string path, out GameObject go, out string error)
        {
            go = null;
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "'path' required."; return false; }

            go = GameObject.Find(path);
            if (go != null) return true;

            string leaf = path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;
            var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var g in all)
            {
                if (g.name == leaf) { go = g; return true; }
            }
            error = $"GameObject '{path}' not found.";
            return false;
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
