using System;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 编辑器下修改场景（Hierarchy + Transform + 组件 + 场景 IO）的 Tool。
    /// 所有写操作支持 Undo 并标脏场景。仅 Edit Mode 下生效。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Scene Edit", fileName = "SceneEditTool")]
    public class SceneEditTool : AIToolAsset
    {
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (Application.isPlaying)
                return UniTask.FromResult("Error: SceneEditTool is only available in Edit Mode. Please exit Play Mode first.");

            SceneEditArgs args;
            try { args = JsonConvert.DeserializeObject<SceneEditArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing required parameter 'action'.");

            string result;
            try
            {
                result = args.Action.ToLowerInvariant() switch
                {
                    "create_empty" => CreateEmpty(args),
                    "create_primitive" => CreatePrimitive(args),
                    "create_camera" => CreateCamera(args),
                    "create_light" => CreateLight(args),
                    "destroy" => Destroy(args),
                    "set_transform" => SetTransform(args),
                    "set_active" => SetActive(args),
                    "set_parent" => SetParent(args),
                    "rename" => Rename(args),
                    "add_component" => AddComponent(args),
                    "remove_component" => RemoveComponent(args),
                    "set_property" => SetProperty(args),
                    "save_scene" => SaveScene(),
                    "open_scene" => OpenScene(args),
                    "new_scene" => NewScene(),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
            }

            return UniTask.FromResult(result);
        }

        // ─── 创建 ───

        private static string CreateEmpty(SceneEditArgs args)
        {
            var go = new GameObject(string.IsNullOrEmpty(args.Name) ? "GameObject" : args.Name);
            AttachParent(go, args.Parent);
            ApplyTransform(go, args);
            Undo.RegisterCreatedObjectUndo(go, "UniAI: create_empty");
            MarkDirty(go);
            return $"Created empty GameObject: {GetFullPath(go)}";
        }

        private static string CreatePrimitive(SceneEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Primitive))
                return "Error: 'primitive' parameter required (Cube/Sphere/Capsule/Cylinder/Plane/Quad).";

            if (!Enum.TryParse<PrimitiveType>(args.Primitive, true, out var type))
                return $"Error: Unknown primitive '{args.Primitive}'.";

            var go = GameObject.CreatePrimitive(type);
            if (!string.IsNullOrEmpty(args.Name)) go.name = args.Name;
            AttachParent(go, args.Parent);
            ApplyTransform(go, args);
            Undo.RegisterCreatedObjectUndo(go, "UniAI: create_primitive");
            MarkDirty(go);
            return $"Created primitive {type}: {GetFullPath(go)}";
        }

        private static string CreateCamera(SceneEditArgs args)
        {
            var go = new GameObject(string.IsNullOrEmpty(args.Name) ? "Camera" : args.Name);
            go.AddComponent<Camera>();
            AttachParent(go, args.Parent);
            ApplyTransform(go, args);
            Undo.RegisterCreatedObjectUndo(go, "UniAI: create_camera");
            MarkDirty(go);
            return $"Created Camera: {GetFullPath(go)}";
        }

        private static string CreateLight(SceneEditArgs args)
        {
            var go = new GameObject(string.IsNullOrEmpty(args.Name) ? "Light" : args.Name);
            var light = go.AddComponent<Light>();
            if (!string.IsNullOrEmpty(args.LightType) && Enum.TryParse<LightType>(args.LightType, true, out var lt))
                light.type = lt;
            AttachParent(go, args.Parent);
            ApplyTransform(go, args);
            Undo.RegisterCreatedObjectUndo(go, "UniAI: create_light");
            MarkDirty(go);
            return $"Created Light ({light.type}): {GetFullPath(go)}";
        }

        // ─── 修改 ───

        private static string Destroy(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            string path = GetFullPath(go);
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return $"Destroyed GameObject: {path}";
        }

        private static string SetTransform(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            Undo.RecordObject(go.transform, "UniAI: set_transform");

            bool world = string.Equals(args.Space, "world", StringComparison.OrdinalIgnoreCase);

            if (args.Position != null && args.Position.Length >= 3)
            {
                var pos = new Vector3(args.Position[0], args.Position[1], args.Position[2]);
                if (world) go.transform.position = pos;
                else go.transform.localPosition = pos;
            }
            if (args.Rotation != null && args.Rotation.Length >= 3)
            {
                var rot = new Vector3(args.Rotation[0], args.Rotation[1], args.Rotation[2]);
                if (world) go.transform.eulerAngles = rot;
                else go.transform.localEulerAngles = rot;
            }
            if (args.Scale != null && args.Scale.Length >= 3)
            {
                go.transform.localScale = new Vector3(args.Scale[0], args.Scale[1], args.Scale[2]);
            }

            MarkDirty(go);
            return $"Updated transform of {GetFullPath(go)} (space={(world ? "world" : "local")})";
        }

        private static string SetActive(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (args.Value == null) return "Error: 'value' (bool) required for set_active.";
            bool active = args.Value.ToObject<bool>();
            Undo.RecordObject(go, "UniAI: set_active");
            go.SetActive(active);
            MarkDirty(go);
            return $"Set active={active} on {GetFullPath(go)}";
        }

        private static string SetParent(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;

            Transform parent = null;
            if (!string.IsNullOrEmpty(args.Parent))
            {
                if (!TryLocate(args.Parent, out var parentGo, out var pErr)) return $"Parent: {pErr}";
                parent = parentGo.transform;
            }

            Undo.SetTransformParent(go.transform, parent, "UniAI: set_parent");
            MarkDirty(go);
            return $"Set parent of {go.name} to {(parent == null ? "<root>" : GetFullPath(parent.gameObject))}";
        }

        private static string Rename(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (string.IsNullOrEmpty(args.Name)) return "Error: 'name' parameter required for rename.";
            Undo.RecordObject(go, "UniAI: rename");
            string oldName = go.name;
            go.name = args.Name;
            MarkDirty(go);
            return $"Renamed '{oldName}' → '{args.Name}'";
        }

        // ─── 组件 ───

        private static string AddComponent(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (string.IsNullOrEmpty(args.ComponentType)) return "Error: 'component_type' required.";

            var type = FindType(args.ComponentType);
            if (type == null) return $"Error: Type '{args.ComponentType}' not found.";
            if (!typeof(Component).IsAssignableFrom(type)) return $"Error: '{args.ComponentType}' is not a Component.";

            var comp = Undo.AddComponent(go, type);
            if (comp == null) return $"Error: Failed to add component '{args.ComponentType}'.";
            MarkDirty(go);
            return $"Added component {type.Name} to {GetFullPath(go)}";
        }

        private static string RemoveComponent(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (string.IsNullOrEmpty(args.ComponentType)) return "Error: 'component_type' required.";

            string lower = args.ComponentType.ToLowerInvariant();
            Component target = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name.ToLowerInvariant() == lower || c.GetType().FullName?.ToLowerInvariant() == lower)
                {
                    target = c;
                    break;
                }
            }

            if (target == null) return $"Error: Component '{args.ComponentType}' not found on {go.name}.";
            if (target is Transform) return "Error: Cannot remove Transform component.";

            Undo.DestroyObjectImmediate(target);
            MarkDirty(go);
            return $"Removed component {args.ComponentType} from {GetFullPath(go)}";
        }

        private static string SetProperty(SceneEditArgs args)
        {
            if (!TryLocate(args.Path, out var go, out var err)) return err;
            if (string.IsNullOrEmpty(args.ComponentType)) return "Error: 'component_type' required.";
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            string lower = args.ComponentType.ToLowerInvariant();
            Component target = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name.ToLowerInvariant() == lower || c.GetType().FullName?.ToLowerInvariant() == lower)
                {
                    target = c;
                    break;
                }
            }
            if (target == null) return $"Error: Component '{args.ComponentType}' not found.";

            var type = target.GetType();
            var prop = type.GetProperty(args.Property, BindingFlags.Public | BindingFlags.Instance);
            var field = prop == null ? type.GetField(args.Property, INSTANCE_FLAGS) : null;

            Type memberType = prop?.PropertyType ?? field?.FieldType;
            if (memberType == null)
                return $"Error: Property or field '{args.Property}' not found on {type.Name}.";

            object converted;
            try { converted = ConvertJToken(args.Value, memberType); }
            catch (Exception ex) { return $"Error: Cannot convert value to {memberType.Name}: {ex.Message}"; }

            Undo.RecordObject(target, "UniAI: set_property");
            if (prop != null && prop.CanWrite) prop.SetValue(target, converted);
            else if (field != null) field.SetValue(target, converted);
            else return $"Error: '{args.Property}' is read-only.";

            EditorUtility.SetDirty(target);
            MarkDirty(go);
            return $"Set {type.Name}.{args.Property} = {FormatValue(converted)} on {GetFullPath(go)}";
        }

        // ─── 场景 IO ───

        private static string SaveScene()
        {
            bool ok = EditorSceneManager.SaveOpenScenes();
            return ok ? "Saved open scenes." : "Error: Failed to save scenes.";
        }

        private static string OpenScene(SceneEditArgs args)
        {
            if (string.IsNullOrEmpty(args.ScenePath)) return "Error: 'scene_path' required.";
            if (!args.ScenePath.StartsWith("Assets/")) return "Error: scene_path must start with 'Assets/'.";

            if (EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            var scene = EditorSceneManager.OpenScene(args.ScenePath, OpenSceneMode.Single);
            return scene.IsValid() ? $"Opened scene: {args.ScenePath}" : $"Error: Failed to open scene '{args.ScenePath}'.";
        }

        private static string NewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            return scene.IsValid() ? "Created new scene." : "Error: Failed to create new scene.";
        }

        // ─── 辅助 ───

        private static bool TryLocate(string path, out GameObject go, out string error)
        {
            go = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Error: 'path' parameter required to locate GameObject.";
                return false;
            }

            // 精确匹配
            go = GameObject.Find(path);
            if (go != null) return true;

            // 按名模糊
            string leaf = path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;
            var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var g in all)
            {
                if (g.name == leaf) { go = g; return true; }
            }

            error = $"Error: GameObject '{path}' not found.";
            return false;
        }

        private static void AttachParent(GameObject go, string parentPath)
        {
            if (string.IsNullOrEmpty(parentPath)) return;
            if (!TryLocate(parentPath, out var parent, out _)) return;
            go.transform.SetParent(parent.transform, false);
        }

        private static void ApplyTransform(GameObject go, SceneEditArgs args)
        {
            if (args.Position != null && args.Position.Length >= 3)
                go.transform.localPosition = new Vector3(args.Position[0], args.Position[1], args.Position[2]);
            if (args.Rotation != null && args.Rotation.Length >= 3)
                go.transform.localEulerAngles = new Vector3(args.Rotation[0], args.Rotation[1], args.Rotation[2]);
            if (args.Scale != null && args.Scale.Length >= 3)
                go.transform.localScale = new Vector3(args.Scale[0], args.Scale[1], args.Scale[2]);
        }

        private static void MarkDirty(GameObject go)
        {
            if (go == null) return;
            var scene = go.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
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

        private static Type FindType(string typeName)
        {
            string lower = typeName.ToLowerInvariant();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in assembly.GetTypes())
                    {
                        if (t.Name.ToLowerInvariant() == lower || t.FullName?.ToLowerInvariant() == lower)
                            return t;
                    }
                }
                catch { }
            }
            return null;
        }

        private static object ConvertJToken(JToken token, Type targetType)
        {
            if (targetType == typeof(Vector3))
            {
                var v = ReadFloatArray(token, 3);
                return new Vector3(v[0], v[1], v[2]);
            }
            if (targetType == typeof(Vector2))
            {
                var v = ReadFloatArray(token, 2);
                return new Vector2(v[0], v[1]);
            }
            if (targetType == typeof(Vector4))
            {
                var v = ReadFloatArray(token, 4);
                return new Vector4(v[0], v[1], v[2], v[3]);
            }
            if (targetType == typeof(Color))
            {
                var v = ReadFloatArray(token, 4, defaultAlpha: 1f);
                return new Color(v[0], v[1], v[2], v[3]);
            }
            if (targetType == typeof(Quaternion))
            {
                var v = ReadFloatArray(token, 3);
                return Quaternion.Euler(v[0], v[1], v[2]);
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                string path = token.ToObject<string>();
                if (string.IsNullOrEmpty(path)) return null;
                return AssetDatabase.LoadAssetAtPath(path, targetType);
            }
            if (targetType.IsEnum)
            {
                if (token.Type == JTokenType.String)
                    return Enum.Parse(targetType, token.ToObject<string>(), true);
                return Enum.ToObject(targetType, token.ToObject<int>());
            }
            return token.ToObject(targetType);
        }

        private static float[] ReadFloatArray(JToken token, int count, float defaultAlpha = 0f)
        {
            var result = new float[count];
            if (count == 4) result[3] = defaultAlpha;

            if (token is JArray arr)
            {
                for (int i = 0; i < Math.Min(arr.Count, count); i++)
                    result[i] = arr[i].ToObject<float>();
            }
            else if (token is JObject obj)
            {
                string[] keys = count == 4 ? new[] { "r", "g", "b", "a" } : new[] { "x", "y", "z", "w" };
                for (int i = 0; i < count; i++)
                {
                    if (obj[keys[i]] != null) result[i] = obj[keys[i]].ToObject<float>();
                    else if (count == 4 && obj[new[] { "x", "y", "z", "w" }[i]] != null)
                        result[i] = obj[new[] { "x", "y", "z", "w" }[i]].ToObject<float>();
                }
            }
            return result;
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is UnityEngine.Object uo) return uo == null ? "null" : $"{uo.GetType().Name}({uo.name})";
            return val.ToString();
        }

        private class SceneEditArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("path")] public string Path;
            [JsonProperty("parent")] public string Parent;
            [JsonProperty("name")] public string Name;
            [JsonProperty("primitive")] public string Primitive;
            [JsonProperty("light_type")] public string LightType;
            [JsonProperty("position")] public float[] Position;
            [JsonProperty("rotation")] public float[] Rotation;
            [JsonProperty("scale")] public float[] Scale;
            [JsonProperty("space")] public string Space;
            [JsonProperty("component_type")] public string ComponentType;
            [JsonProperty("property")] public string Property;
            [JsonProperty("value")] public JToken Value;
            [JsonProperty("scene_path")] public string ScenePath;
        }
    }
}
