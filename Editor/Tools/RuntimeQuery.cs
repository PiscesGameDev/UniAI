using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Play Mode 运行时查询聚合工具：scene / find / inspect / component / find_type。
    /// </summary>
    [UniAITool(
        Name = "runtime_query",
        Group = ToolGroups.Runtime,
        Description =
            "Play-Mode reflection query. Actions: 'scene' (hierarchy), 'find' (by name), " +
            "'inspect' (GameObject detail), 'component' (single component fields), 'find_type' (by component type).",
        Actions = new[] { "scene", "find", "inspect", "component", "find_type" })]
    internal static class RuntimeQuery
    {
        private const int MAX_RESULTS = 50;
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            if (!Application.isPlaying)
                return UniTask.FromResult(ToolResponse.Error("runtime_query is only available in Play Mode."));

            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult(ToolResponse.Error("Missing 'action'."));

            object result;
            try
            {
                result = action switch
                {
                    "scene" => QueryScene(args),
                    "find" => FindGameObjects(args),
                    "inspect" => InspectGameObject(args),
                    "component" => InspectComponent(args),
                    "find_type" => FindByComponentType(args),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            return UniTask.FromResult(result);
        }

        public class SceneArgs
        {
            [ToolParam(Description = "Hierarchy depth (default 1).", Required = false)]
            public int Depth;
        }

        public class FindArgs
        {
            [ToolParam(Description = "GameObject name (exact or substring).")]
            public string Name;
        }

        public class InspectArgs : FindArgs { }

        public class ComponentArgs : FindArgs
        {
            [ToolParam(Description = "Component type name (short or full).")]
            public string ComponentType;
        }

        public class FindTypeArgs
        {
            [ToolParam(Description = "Component type name.")]
            public string ComponentType;
        }

        // ─── scene ───

        private static object QueryScene(JObject args)
        {
            int depth = (int?)args["depth"] ?? 1;
            if (depth <= 0) depth = 1;

            var sb = new StringBuilder();
            int sceneCount = SceneManager.sceneCount;
            for (int s = 0; s < sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                sb.AppendLine($"Scene: {scene.name} (path: {scene.path})");
                foreach (var root in scene.GetRootGameObjects())
                    AppendHierarchy(sb, root, 1, depth);
            }
            return ToolResponse.Success(new { hierarchy = sb.ToString() });
        }

        private static void AppendHierarchy(StringBuilder sb, GameObject go, int currentDepth, int maxDepth)
        {
            string indent = new(' ', currentDepth * 2);
            string activeFlag = go.activeSelf ? "" : " [inactive]";
            sb.AppendLine($"{indent}- {go.name}{activeFlag} ({go.GetComponents<Component>().Length} components, {go.transform.childCount} children)");
            if (currentDepth >= maxDepth) return;
            for (int i = 0; i < go.transform.childCount; i++)
                AppendHierarchy(sb, go.transform.GetChild(i).gameObject, currentDepth + 1, maxDepth);
        }

        // ─── find ───

        private static object FindGameObjects(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");

            var exact = GameObject.Find(name);
            if (exact != null)
                return ToolResponse.Success(new { matches = new[] { FormatSummary(exact) } });

            var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var matches = new List<object>();
            string lower = name.ToLowerInvariant();
            foreach (var go in all)
            {
                if (!go.name.ToLowerInvariant().Contains(lower)) continue;
                matches.Add(new { path = GetFullPath(go), active = go.activeInHierarchy });
                if (matches.Count >= MAX_RESULTS) break;
            }
            return ToolResponse.Success(new { count = matches.Count, matches });
        }

        // ─── inspect ───

        private static object InspectGameObject(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");

            var go = GameObject.Find(name);
            if (go == null) return ToolResponse.Error($"GameObject '{name}' not found.");

            return ToolResponse.Success(new { detail = FormatGameObjectDetail(go) });
        }

        // ─── component ───

        private static object InspectComponent(JObject args)
        {
            var name = (string)args["name"];
            var typeName = (string)args["componentType"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'name' required.");
            if (string.IsNullOrEmpty(typeName)) return ToolResponse.Error("'componentType' required.");

            var go = GameObject.Find(name);
            if (go == null) return ToolResponse.Error($"GameObject '{name}' not found.");

            string typeLower = typeName.ToLowerInvariant();
            Component target = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name.ToLowerInvariant() == typeLower
                    || c.GetType().FullName?.ToLowerInvariant() == typeLower)
                {
                    target = c;
                    break;
                }
            }
            if (target == null) return ToolResponse.Error($"Component '{typeName}' not found on '{name}'.");

            return ToolResponse.Success(new { detail = FormatComponentDetail(target) });
        }

        // ─── find_type ───

        private static object FindByComponentType(JObject args)
        {
            var typeName = (string)args["componentType"];
            if (string.IsNullOrEmpty(typeName)) return ToolResponse.Error("'componentType' required.");

            Type componentType = FindType(typeName);
            if (componentType == null) return ToolResponse.Error($"Type '{typeName}' not found.");
            if (!typeof(Component).IsAssignableFrom(componentType))
                return ToolResponse.Error($"'{typeName}' is not a Component type.");

            var found = UnityEngine.Object.FindObjectsByType(componentType, FindObjectsSortMode.None);
            int shown = Math.Min(found.Length, MAX_RESULTS);
            var paths = new string[shown];
            for (int i = 0; i < shown; i++)
                if (found[i] is Component comp) paths[i] = GetFullPath(comp.gameObject);

            return ToolResponse.Success(new
            {
                type = componentType.Name,
                total = found.Length,
                truncated = found.Length > MAX_RESULTS,
                paths
            });
        }

        // ─── 格式化 ───

        private static string FormatSummary(GameObject go)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {GetFullPath(go)}");
            sb.AppendLine($"  Active: {go.activeInHierarchy}, Layer: {LayerMask.LayerToName(go.layer)}, Tag: {go.tag}");
            sb.AppendLine($"  Position: {go.transform.position}, Rotation: {go.transform.eulerAngles}");
            sb.AppendLine($"  Children: {go.transform.childCount}");
            sb.AppendLine("  Components:");
            foreach (var c in go.GetComponents<Component>())
                if (c != null) sb.AppendLine($"    - {c.GetType().Name}");
            return sb.ToString();
        }

        private static string FormatGameObjectDetail(GameObject go)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {GetFullPath(go)} ===");
            sb.AppendLine($"Active: {go.activeInHierarchy}, Layer: {LayerMask.LayerToName(go.layer)}, Tag: {go.tag}");
            sb.AppendLine($"Position: {go.transform.position}");
            sb.AppendLine($"Rotation: {go.transform.eulerAngles}");
            sb.AppendLine($"Scale: {go.transform.localScale}");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine($"\n--- {c.GetType().Name} ---");
                AppendFieldsSummary(sb, c);
            }
            return sb.ToString();
        }

        private static string FormatComponentDetail(Component component)
        {
            var sb = new StringBuilder();
            var type = component.GetType();
            sb.AppendLine($"=== {type.Name} on {GetFullPath(component.gameObject)} ===");
            sb.AppendLine($"Type: {type.FullName}");

            sb.AppendLine("\n[Fields]");
            foreach (var field in type.GetFields(INSTANCE_FLAGS))
            {
                if (IsUnityBase(field.DeclaringType)) continue;
                try
                {
                    object val = field.GetValue(component);
                    sb.AppendLine($"  {FormatAccessibility(field)} {field.FieldType.Name} {field.Name} = {FormatValue(val)}");
                }
                catch { sb.AppendLine($"  {field.FieldType.Name} {field.Name} = <error reading>"); }
            }

            sb.AppendLine("\n[Properties]");
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    object val = prop.GetValue(component);
                    sb.AppendLine($"  {prop.PropertyType.Name} {prop.Name} = {FormatValue(val)}");
                }
                catch { sb.AppendLine($"  {prop.PropertyType.Name} {prop.Name} = <error reading>"); }
            }
            return sb.ToString();
        }

        private static void AppendFieldsSummary(StringBuilder sb, Component component)
        {
            var type = component.GetType();
            int count = 0;
            foreach (var field in type.GetFields(INSTANCE_FLAGS))
            {
                if (IsUnityBase(field.DeclaringType)) continue;
                bool isPublic = field.IsPublic;
                bool isSerialized = field.GetCustomAttribute<SerializeField>() != null;
                if (!isPublic && !isSerialized) continue;

                try
                {
                    object val = field.GetValue(component);
                    sb.AppendLine($"  {field.Name} = {FormatValue(val)}");
                }
                catch { sb.AppendLine($"  {field.Name} = <error>"); }

                if (++count >= 20) { sb.AppendLine("  ..."); break; }
            }
        }

        private static bool IsUnityBase(Type t) =>
            t == typeof(MonoBehaviour) || t == typeof(Behaviour) || t == typeof(Component) || t == typeof(UnityEngine.Object);

        private static string FormatAccessibility(FieldInfo field)
        {
            if (field.IsPublic) return "public";
            if (field.IsPrivate) return "private";
            if (field.IsFamily) return "protected";
            return "internal";
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is string s) return s.Length > 100 ? $"\"{s.Substring(0, 100)}...\"" : $"\"{s}\"";
            if (val is UnityEngine.Object uObj) return uObj == null ? "null (destroyed)" : $"{uObj.GetType().Name}({uObj.name})";
            if (val is System.Collections.ICollection coll) return $"[{coll.GetType().Name}, Count={coll.Count}]";
            if (val is System.Collections.IEnumerable) return $"[{val.GetType().Name}]";
            string str = val.ToString();
            return str.Length > 150 ? str.Substring(0, 150) + "..." : str;
        }

        private static string GetFullPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
            return sb.ToString();
        }

        private static Type FindType(string typeName)
        {
            string lower = typeName.ToLowerInvariant();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name.ToLowerInvariant() == lower || type.FullName?.ToLowerInvariant() == lower)
                            return type;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
