using System;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 场景聚合工具：Hierarchy + Transform + 组件 + 场景 IO。
    /// 对象操作在 Edit/Play Mode 均可用；场景 IO（save/open/new）仅 Edit Mode。
    /// </summary>
    [UniAITool(
        Name = "manage_scene",
        Group = ToolGroups.Scene,
        Description =
            "Scene operations (Edit & Play Mode). Actions: 'create_empty', 'create_primitive', 'create_camera', 'create_light', " +
            "'destroy', 'set_transform', 'set_active', 'set_parent', 'rename', " +
            "'add_component', 'remove_component', 'set_property', " +
            "'save_scene' (edit only), 'open_scene' (edit only), 'new_scene' (edit only).",
        Actions = new[]
        {
            "create_empty", "create_primitive", "create_camera", "create_light",
            "destroy", "set_transform", "set_active", "set_parent", "rename",
            "add_component", "remove_component", "set_property",
            "save_scene", "open_scene", "new_scene"
        })]
    internal static class ManageScene
    {
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult<object>(ToolResponse.Error("Missing required parameter 'action'."));

            object result;
            try
            {
                result = action switch
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
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex)
            {
                result = ToolResponse.Error(ex.Message);
            }

            return UniTask.FromResult(result);
        }

        // ─── 通用 args 形参 ───

        public class CreateEmptyArgs
        {
            [ToolParam(Description = "Name of the new GameObject.", Required = false)]
            public string Name;
            [ToolParam(Description = "Optional parent path or name.", Required = false)]
            public string Parent;
            [ToolParam(Description = "Local position [x,y,z].", Required = false)]
            public float[] Position;
            [ToolParam(Description = "Local euler rotation [x,y,z].", Required = false)]
            public float[] Rotation;
            [ToolParam(Description = "Local scale [x,y,z].", Required = false)]
            public float[] Scale;
        }

        public class CreatePrimitiveArgs : CreateEmptyArgs
        {
            [ToolParam(Description = "Primitive type: Cube/Sphere/Capsule/Cylinder/Plane/Quad.")]
            public string Primitive;
        }

        public class CreateLightArgs : CreateEmptyArgs
        {
            [ToolParam(Description = "Light type: Directional/Point/Spot/Area.", Required = false)]
            public string LightType;
        }

        public class DestroyArgs
        {
            [ToolParam(Description = "Hierarchy path or name of the target GameObject.")]
            public string Path;
        }

        public class SetTransformArgs : DestroyArgs
        {
            [ToolParam(Description = "Position [x,y,z].", Required = false)]
            public float[] Position;
            [ToolParam(Description = "Euler rotation [x,y,z].", Required = false)]
            public float[] Rotation;
            [ToolParam(Description = "Scale [x,y,z].", Required = false)]
            public float[] Scale;
            [ToolParam(Description = "'world' or 'local' (default 'local').", Required = false)]
            public string Space;
        }

        public class SetActiveArgs : DestroyArgs
        {
            [ToolParam(Description = "true/false.")]
            public bool Value;
        }

        public class SetParentArgs : DestroyArgs
        {
            [ToolParam(Description = "New parent path. Empty/null = move to root.", Required = false)]
            public string Parent;
        }

        public class RenameArgs : DestroyArgs
        {
            [ToolParam(Description = "New name.")]
            public string Name;
        }

        public class AddComponentArgs : DestroyArgs
        {
            [ToolParam(Description = "Component type name (e.g. 'Rigidbody', 'UnityEngine.BoxCollider').")]
            public string ComponentType;
        }

        public class RemoveComponentArgs : AddComponentArgs { }

        public class SetPropertyArgs : AddComponentArgs
        {
            [ToolParam(Description = "Property or field name on the component.")]
            public string Property;
            [ToolParam(Description = "Value to assign (any JSON token).")]
            public object Value;
        }

        public class OpenSceneArgs
        {
            [ToolParam(Description = "Scene asset path (must start with 'Assets/').")]
            public string ScenePath;
        }

        // ─── 创建 ───

        private static object CreateEmpty(JObject args)
        {
            var go = new GameObject(StringOr(args["name"], "GameObject"));
            AttachParent(go, (string)args["parent"]);
            ApplyTransform(go, args);
            SceneEdit.RegisterCreated(go, "UniAI: create_empty");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go) }, "Empty GameObject created.");
        }

        private static object CreatePrimitive(JObject args)
        {
            var prim = (string)args["primitive"];
            if (string.IsNullOrEmpty(prim))
                return ToolResponse.Error("'primitive' parameter required (Cube/Sphere/Capsule/Cylinder/Plane/Quad).");
            if (!Enum.TryParse<PrimitiveType>(prim, true, out var type))
                return ToolResponse.Error($"Unknown primitive '{prim}'.");

            var go = GameObject.CreatePrimitive(type);
            var name = (string)args["name"];
            if (!string.IsNullOrEmpty(name)) go.name = name;
            AttachParent(go, (string)args["parent"]);
            ApplyTransform(go, args);
            SceneEdit.RegisterCreated(go, "UniAI: create_primitive");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), primitive = type.ToString() });
        }

        private static object CreateCamera(JObject args)
        {
            var go = new GameObject(StringOr(args["name"], "Camera"));
            go.AddComponent<Camera>();
            AttachParent(go, (string)args["parent"]);
            ApplyTransform(go, args);
            SceneEdit.RegisterCreated(go, "UniAI: create_camera");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go) }, "Camera created.");
        }

        private static object CreateLight(JObject args)
        {
            var go = new GameObject(StringOr(args["name"], "Light"));
            var light = go.AddComponent<Light>();
            var lt = (string)args["lightType"];
            if (!string.IsNullOrEmpty(lt) && Enum.TryParse<LightType>(lt, true, out var parsed))
                light.type = parsed;
            AttachParent(go, (string)args["parent"]);
            ApplyTransform(go, args);
            SceneEdit.RegisterCreated(go, "UniAI: create_light");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), lightType = light.type.ToString() });
        }

        // ─── 修改 ───

        private static object Destroy(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            string path = GetFullPath(go);
            var scene = go.scene;
            SceneEdit.DestroyObject(go);
            SceneEdit.MarkDirty(scene);
            return ToolResponse.Success(new { path }, "Destroyed.");
        }

        private static object SetTransform(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            SceneEdit.RecordObject(go.transform, "UniAI: set_transform");

            bool world = string.Equals((string)args["space"], "world", StringComparison.OrdinalIgnoreCase);
            var position = ReadFloats(args["position"]);
            var rotation = ReadFloats(args["rotation"]);
            var scale = ReadFloats(args["scale"]);

            if (position != null && position.Length >= 3)
            {
                var pos = new Vector3(position[0], position[1], position[2]);
                if (world) go.transform.position = pos;
                else go.transform.localPosition = pos;
            }
            if (rotation != null && rotation.Length >= 3)
            {
                var rot = new Vector3(rotation[0], rotation[1], rotation[2]);
                if (world) go.transform.eulerAngles = rot;
                else go.transform.localEulerAngles = rot;
            }
            if (scale != null && scale.Length >= 3)
                go.transform.localScale = new Vector3(scale[0], scale[1], scale[2]);

            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), space = world ? "world" : "local" });
        }

        private static object SetActive(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            var valueToken = args["value"];
            if (valueToken == null) return ToolResponse.Error("'value' (bool) required.");
            bool active = valueToken.ToObject<bool>();
            SceneEdit.RecordObject(go, "UniAI: set_active");
            go.SetActive(active);
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), active });
        }

        private static object SetParent(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);

            Transform parent = null;
            var parentPath = (string)args["parent"];
            if (!string.IsNullOrEmpty(parentPath))
            {
                if (!TryLocate(parentPath, out var parentGo, out var pErr)) return ToolResponse.Error($"Parent: {pErr}");
                parent = parentGo.transform;
            }

            SceneEdit.SetTransformParent(go.transform, parent, "UniAI: set_parent");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new
            {
                path = GetFullPath(go),
                parent = parent == null ? "<root>" : GetFullPath(parent.gameObject)
            });
        }

        private static object Rename(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");
            SceneEdit.RecordObject(go, "UniAI: rename");
            string oldName = go.name;
            go.name = name;
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { oldName, newName = name });
        }

        // ─── 组件 ───

        private static object AddComponent(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            var typeName = (string)args["componentType"];
            if (string.IsNullOrEmpty(typeName)) return ToolResponse.Error("'componentType' required.");

            var type = FindType(typeName);
            if (type == null) return ToolResponse.Error($"Type '{typeName}' not found.");
            if (!typeof(Component).IsAssignableFrom(type)) return ToolResponse.Error($"'{typeName}' is not a Component.");

            var comp = SceneEdit.AddComponent(go, type);
            if (comp == null) return ToolResponse.Error($"Failed to add component '{typeName}'.");
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), component = type.Name });
        }

        private static object RemoveComponent(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            var typeName = (string)args["componentType"];
            if (string.IsNullOrEmpty(typeName)) return ToolResponse.Error("'componentType' required.");

            var target = FindComponent(go, typeName);
            if (target == null) return ToolResponse.Error($"Component '{typeName}' not found on {go.name}.");
            if (target is Transform) return ToolResponse.Error("Cannot remove Transform component.");

            SceneEdit.DestroyObject(target);
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new { path = GetFullPath(go), removed = typeName });
        }

        private static object SetProperty(JObject args)
        {
            if (!TryLocate((string)args["path"], out var go, out var err)) return ToolResponse.Error(err);
            var typeName = (string)args["componentType"];
            var propName = (string)args["property"];
            var valueToken = args["value"];

            if (string.IsNullOrEmpty(typeName)) return ToolResponse.Error("'componentType' required.");
            if (string.IsNullOrEmpty(propName)) return ToolResponse.Error("'property' required.");
            if (valueToken == null) return ToolResponse.Error("'value' required.");

            var target = FindComponent(go, typeName);
            if (target == null) return ToolResponse.Error($"Component '{typeName}' not found.");

            var type = target.GetType();
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            var field = prop == null ? type.GetField(propName, INSTANCE_FLAGS) : null;

            Type memberType = prop?.PropertyType ?? field?.FieldType;
            if (memberType == null) return ToolResponse.Error($"Property or field '{propName}' not found on {type.Name}.");

            object converted;
            try { converted = ConvertJToken(valueToken, memberType); }
            catch (Exception ex) { return ToolResponse.Error($"Cannot convert value to {memberType.Name}: {ex.Message}"); }

            SceneEdit.RecordObject(target, "UniAI: set_property");
            if (prop != null && prop.CanWrite) prop.SetValue(target, converted);
            else if (field != null) field.SetValue(target, converted);
            else return ToolResponse.Error($"'{propName}' is read-only.");

            if (!Application.isPlaying) EditorUtility.SetDirty(target);
            SceneEdit.MarkDirty(go);
            return ToolResponse.Success(new
            {
                path = GetFullPath(go),
                component = type.Name,
                property = propName,
                value = FormatValue(converted)
            });
        }

        // ─── 场景 IO ───

        private static object SaveScene()
        {
            if (Application.isPlaying)
                return ToolResponse.Error("save_scene is only available in Edit Mode.");
            bool ok = EditorSceneManager.SaveOpenScenes();
            return ok ? ToolResponse.Success(new { saved = true }, "Open scenes saved.") : ToolResponse.Error("Failed to save scenes.");
        }

        private static object OpenScene(JObject args)
        {
            if (Application.isPlaying)
                return ToolResponse.Error("open_scene is only available in Edit Mode.");
            var scenePath = (string)args["scenePath"];
            if (string.IsNullOrEmpty(scenePath)) return ToolResponse.Error("'scenePath' required.");
            if (!scenePath.StartsWith("Assets/")) return ToolResponse.Error("scenePath must start with 'Assets/'.");

            if (EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return scene.IsValid()
                ? ToolResponse.Success(new { scenePath })
                : ToolResponse.Error($"Failed to open scene '{scenePath}'.");
        }

        private static object NewScene()
        {
            if (Application.isPlaying)
                return ToolResponse.Error("new_scene is only available in Edit Mode.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            return scene.IsValid()
                ? ToolResponse.Success(new { created = true }, "New scene created.")
                : ToolResponse.Error("Failed to create new scene.");
        }

        // ─── 辅助 ───

        private static string StringOr(JToken token, string fallback)
        {
            var s = (string)token;
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

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

        private static Component FindComponent(GameObject go, string typeName)
        {
            string lower = typeName.ToLowerInvariant();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name.ToLowerInvariant() == lower || c.GetType().FullName?.ToLowerInvariant() == lower)
                    return c;
            }
            return null;
        }

        private static void AttachParent(GameObject go, string parentPath)
        {
            if (string.IsNullOrEmpty(parentPath)) return;
            if (!TryLocate(parentPath, out var parent, out _)) return;
            go.transform.SetParent(parent.transform, false);
        }

        private static void ApplyTransform(GameObject go, JObject args)
        {
            var pos = ReadFloats(args["position"]);
            var rot = ReadFloats(args["rotation"]);
            var scl = ReadFloats(args["scale"]);
            if (pos != null && pos.Length >= 3) go.transform.localPosition = new Vector3(pos[0], pos[1], pos[2]);
            if (rot != null && rot.Length >= 3) go.transform.localEulerAngles = new Vector3(rot[0], rot[1], rot[2]);
            if (scl != null && scl.Length >= 3) go.transform.localScale = new Vector3(scl[0], scl[1], scl[2]);
        }

        private static float[] ReadFloats(JToken token)
        {
            if (token is JArray arr)
            {
                var result = new float[arr.Count];
                for (int i = 0; i < arr.Count; i++) result[i] = arr[i].ToObject<float>();
                return result;
            }
            return null;
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
    }
}
