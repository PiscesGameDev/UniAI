using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Unity 菜单项聚合工具：execute / list。
    /// </summary>
    [UniAITool(
        Name = "manage_menu",
        Group = ToolGroups.Editor,
        Description =
            "Unity editor menu items. Actions: 'execute' (invoke [MenuItem] path), 'list' (enumerate registered menu items, optional filter).",
        Actions = new[] { "execute", "list" })]
    internal static class ManageMenu
    {
        private const int MAX_RESULTS = 200;

        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult(ToolResponse.Error("Missing 'action'."));

            object result;
            try
            {
                result = action switch
                {
                    "execute" => Execute(args),
                    "list" => List(args),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex)
            {
                result = ToolResponse.Error(ex.Message);
            }

            return UniTask.FromResult(result);
        }

        public class ExecuteArgs
        {
            [ToolParam(Description = "Menu path (e.g. 'Window/General/Console').")]
            public string MenuPath;
        }

        public class ListArgs
        {
            [ToolParam(Description = "Optional substring filter (case-insensitive).", Required = false)]
            public string Filter;
        }

        // ─── 实现 ───

        private static object Execute(JObject args)
        {
            var menuPath = (string)args["menuPath"];
            if (string.IsNullOrEmpty(menuPath)) return ToolResponse.Error("'menuPath' required.");

            bool ok = EditorApplication.ExecuteMenuItem(menuPath);
            return ok
                ? ToolResponse.Success(new { menuPath }, "Menu item executed.")
                : ToolResponse.Error($"Failed to execute '{menuPath}'. Not found or validate returned false.");
        }

        private static object List(JObject args)
        {
            string filter = ((string)args["filter"])?.ToLowerInvariant();
            var found = new List<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var method in methods)
                    {
                        var attrs = method.GetCustomAttributes(typeof(MenuItem), false);
                        foreach (var a in attrs)
                        {
                            var mi = (MenuItem)a;
                            if (mi.menuItem == null) continue;
                            if (filter != null && !mi.menuItem.ToLowerInvariant().Contains(filter)) continue;
                            found.Add(mi.menuItem);
                            if (found.Count >= MAX_RESULTS) goto done;
                        }
                    }
                }
            }

            done:
            found.Sort(StringComparer.Ordinal);
            return ToolResponse.Success(new
            {
                total = found.Count,
                truncated = found.Count >= MAX_RESULTS,
                menuItems = found
            });
        }
    }
}
