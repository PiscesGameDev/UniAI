using System;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 执行 Unity 编辑器菜单项的 Tool。可触发任何 [MenuItem] 注册的菜单（含项目自定义扩展）。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Menu Item", fileName = "MenuItemTool")]
    public class MenuItemTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            MenuItemArgs args;
            try { args = JsonConvert.DeserializeObject<MenuItemArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing 'action'.");

            string result = args.Action.ToLowerInvariant() switch
            {
                "execute" => Execute(args),
                "list" => ListMenuItems(args),
                _ => $"Error: Unknown action '{args.Action}'."
            };

            return UniTask.FromResult(result);
        }

        private static string Execute(MenuItemArgs args)
        {
            if (string.IsNullOrEmpty(args.MenuPath))
                return "Error: 'menu_path' required (e.g. 'Window/General/Console').";

            bool ok = EditorApplication.ExecuteMenuItem(args.MenuPath);
            return ok
                ? $"Executed menu item: {args.MenuPath}"
                : $"Error: Failed to execute '{args.MenuPath}'. The menu item may not exist or has a Validate function that returned false.";
        }

        private static string ListMenuItems(MenuItemArgs args)
        {
            string filter = args.Filter?.ToLowerInvariant();
            var found = new System.Collections.Generic.List<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                    catch { continue; }

                    foreach (var method in methods)
                    {
                        var attrs = method.GetCustomAttributes(typeof(MenuItem), false);
                        foreach (var a in attrs)
                        {
                            var mi = (MenuItem)a;
                            if (mi.menuItem == null) continue;
                            if (filter != null && !mi.menuItem.ToLowerInvariant().Contains(filter)) continue;
                            found.Add(mi.menuItem);
                            if (found.Count >= 200) break;
                        }
                        if (found.Count >= 200) break;
                    }
                    if (found.Count >= 200) break;
                }
                if (found.Count >= 200) break;
            }

            if (found.Count == 0)
                return filter == null ? "No menu items found." : $"No menu items matching '{args.Filter}'.";

            found.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder($"Found {found.Count} menu item(s){(found.Count >= 200 ? " (truncated)" : "")}:\n");
            foreach (var m in found) sb.AppendLine($"  {m}");
            return sb.ToString();
        }

        private class MenuItemArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("menu_path")] public string MenuPath;
            [JsonProperty("filter")] public string Filter;
        }
    }
}
