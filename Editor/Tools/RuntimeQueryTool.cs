using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 查询运行时游戏对象和组件数据的 Tool。
    /// 仅在 Play Mode 下有效。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Runtime Query", fileName = "RuntimeQueryTool")]
    public class RuntimeQueryTool : AIToolAsset
    {
        private const int MAX_RESULTS = 50;
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (!Application.isPlaying)
                return UniTask.FromResult("Error: Runtime query is only available in Play Mode. Please enter Play Mode first.");

            var args = JsonConvert.DeserializeObject<RuntimeQueryArgs>(arguments);
            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing required parameter 'action'.");

            string result = args.Action.ToLowerInvariant() switch
            {
                "scene" => QueryScene(args),
                "find" => FindGameObjects(args),
                "inspect" => InspectGameObject(args),
                "component" => InspectComponent(args),
                "find_type" => FindByComponentType(args),
                _ => $"Error: Unknown action '{args.Action}'. Available: scene, find, inspect, component, find_type"
            };

            return UniTask.FromResult(result);
        }

        // ─── scene: 列出当前场景层级 ───

        private static string QueryScene(RuntimeQueryArgs args)
        {
            var sb = new StringBuilder();
            int sceneCount = SceneManager.sceneCount;

            for (int s = 0; s < sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                sb.AppendLine($"Scene: {scene.name} (path: {scene.path})");

                var roots = scene.GetRootGameObjects();
                int depth = args.Depth > 0 ? args.Depth : 1;

                foreach (var root in roots)
                    AppendHierarchy(sb, root, 1, depth);
            }

            return sb.Length > 0 ? sb.ToString() : "No loaded scenes found.";
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

        // ─── find: 按名称查找 GameObject ───

        private static string FindGameObjects(RuntimeQueryArgs args)
        {
            if (string.IsNullOrEmpty(args.Name))
                return "Error: 'name' parameter required for find action.";

            // 精确查找
            var exact = GameObject.Find(args.Name);
            if (exact != null)
                return FormatGameObjectSummary(exact);

            // 模糊搜索
            var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var matches = new List<GameObject>();
            string lower = args.Name.ToLowerInvariant();

            foreach (var go in all)
            {
                if (go.name.ToLowerInvariant().Contains(lower))
                {
                    matches.Add(go);
                    if (matches.Count >= MAX_RESULTS) break;
                }
            }

            if (matches.Count == 0)
                return $"No GameObject found matching '{args.Name}'.";

            var sb = new StringBuilder($"Found {matches.Count} GameObjects matching '{args.Name}':\n");
            foreach (var go in matches)
                sb.AppendLine($"  - {GetFullPath(go)} (active: {go.activeInHierarchy})");

            return sb.ToString();
        }

        // ─── inspect: 查看 GameObject 的所有组件及字段 ───

        private static string InspectGameObject(RuntimeQueryArgs args)
        {
            if (string.IsNullOrEmpty(args.Name))
                return "Error: 'name' parameter required for inspect action.";

            var go = GameObject.Find(args.Name);
            if (go == null)
                return $"Error: GameObject '{args.Name}' not found. Use 'find' action to search first.";

            return FormatGameObjectDetail(go);
        }

        // ─── component: 查看指定组件的详细字段 ───

        private static string InspectComponent(RuntimeQueryArgs args)
        {
            if (string.IsNullOrEmpty(args.Name))
                return "Error: 'name' parameter required for component action.";
            if (string.IsNullOrEmpty(args.ComponentType))
                return "Error: 'component_type' parameter required for component action.";

            var go = GameObject.Find(args.Name);
            if (go == null)
                return $"Error: GameObject '{args.Name}' not found.";

            var components = go.GetComponents<Component>();
            Component target = null;
            string typeLower = args.ComponentType.ToLowerInvariant();

            foreach (var c in components)
            {
                if (c == null) continue;
                if (c.GetType().Name.ToLowerInvariant() == typeLower
                    || c.GetType().FullName?.ToLowerInvariant() == typeLower)
                {
                    target = c;
                    break;
                }
            }

            if (target == null)
                return $"Error: Component '{args.ComponentType}' not found on '{args.Name}'.";

            return FormatComponentDetail(target);
        }

        // ─── find_type: 按组件类型查找所有 GameObject ───

        private static string FindByComponentType(RuntimeQueryArgs args)
        {
            if (string.IsNullOrEmpty(args.ComponentType))
                return "Error: 'component_type' parameter required for find_type action.";

            // 在所有已加载程序集中查找类型
            Type componentType = FindType(args.ComponentType);
            if (componentType == null)
                return $"Error: Type '{args.ComponentType}' not found. Use full type name if ambiguous.";

            if (!typeof(Component).IsAssignableFrom(componentType))
                return $"Error: '{args.ComponentType}' is not a Component type.";

            var found = UnityEngine.Object.FindObjectsByType(componentType, FindObjectsSortMode.None);
            if (found.Length == 0)
                return $"No active GameObjects found with component '{args.ComponentType}'.";

            var sb = new StringBuilder($"Found {found.Length} objects with '{componentType.Name}':\n");
            int count = 0;

            foreach (var obj in found)
            {
                if (obj is Component comp)
                {
                    sb.AppendLine($"  - {GetFullPath(comp.gameObject)}");
                    count++;
                    if (count >= MAX_RESULTS)
                    {
                        sb.AppendLine($"  ... (truncated, {found.Length} total)");
                        break;
                    }
                }
            }

            return sb.ToString();
        }

        // ─── 格式化辅助 ───

        private static string FormatGameObjectSummary(GameObject go)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {GetFullPath(go)}");
            sb.AppendLine($"  Active: {go.activeInHierarchy}, Layer: {LayerMask.LayerToName(go.layer)}, Tag: {go.tag}");
            sb.AppendLine($"  Position: {go.transform.position}, Rotation: {go.transform.eulerAngles}");
            sb.AppendLine($"  Children: {go.transform.childCount}");
            sb.AppendLine("  Components:");

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine($"    - {c.GetType().Name}");
            }

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
                if (field.DeclaringType == typeof(MonoBehaviour) || field.DeclaringType == typeof(Behaviour)
                    || field.DeclaringType == typeof(Component) || field.DeclaringType == typeof(UnityEngine.Object))
                    continue;

                try
                {
                    object val = field.GetValue(component);
                    sb.AppendLine($"  {FormatAccessibility(field)} {field.FieldType.Name} {field.Name} = {FormatValue(val)}");
                }
                catch
                {
                    sb.AppendLine($"  {field.FieldType.Name} {field.Name} = <error reading>");
                }
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
                catch
                {
                    sb.AppendLine($"  {prop.PropertyType.Name} {prop.Name} = <error reading>");
                }
            }

            return sb.ToString();
        }

        private static void AppendFieldsSummary(StringBuilder sb, Component component)
        {
            var type = component.GetType();
            int count = 0;

            // 只显示 SerializeField 和 public 字段的摘要
            foreach (var field in type.GetFields(INSTANCE_FLAGS))
            {
                if (field.DeclaringType == typeof(MonoBehaviour) || field.DeclaringType == typeof(Behaviour)
                    || field.DeclaringType == typeof(Component) || field.DeclaringType == typeof(UnityEngine.Object))
                    continue;

                bool isPublic = field.IsPublic;
                bool isSerialized = field.GetCustomAttribute<SerializeField>() != null;
                if (!isPublic && !isSerialized) continue;

                try
                {
                    object val = field.GetValue(component);
                    sb.AppendLine($"  {field.Name} = {FormatValue(val)}");
                }
                catch
                {
                    sb.AppendLine($"  {field.Name} = <error>");
                }

                count++;
                if (count >= 20)
                {
                    sb.AppendLine("  ... (use 'component' action for full detail)");
                    break;
                }
            }
        }

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

            if (val is string s)
                return s.Length > 100 ? $"\"{s.Substring(0, 100)}...\"" : $"\"{s}\"";

            if (val is UnityEngine.Object uObj)
                return uObj == null ? "null (destroyed)" : $"{uObj.GetType().Name}({uObj.name})";

            if (val is System.Collections.ICollection collection)
                return $"[{collection.GetType().Name}, Count={collection.Count}]";

            if (val is System.Collections.IEnumerable enumerable && !(val is string))
                return $"[{val.GetType().Name}]";

            string str = val.ToString();
            return str.Length > 150 ? str.Substring(0, 150) + "..." : str;
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
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name.ToLowerInvariant() == lower || type.FullName?.ToLowerInvariant() == lower)
                            return type;
                    }
                }
                catch { /* 某些程序集可能无法反射 */ }
            }

            return null;
        }

        private class RuntimeQueryArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("name")] public string Name;
            [JsonProperty("component_type")] public string ComponentType;
            [JsonProperty("depth")] public int Depth;
        }
    }
}
