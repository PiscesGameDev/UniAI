using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// 工具处理器委托签名。
    /// </summary>
    public delegate UniTask<object> ToolHandlerDelegate(JObject args, CancellationToken ct);

    /// <summary>
    /// 单个已注册工具的完整信息。
    /// </summary>
    public sealed class ToolHandlerInfo
    {
        public string Name { get; }
        public string Group { get; }
        public AITool Definition { get; }
        public bool RequiresPolling { get; }
        public int MaxPollSeconds { get; }
        public bool IsBuiltIn { get; }
        public ToolHandlerDelegate Invoke { get; }

        public ToolHandlerInfo(string name, string group, AITool definition, bool requiresPolling, int maxPollSeconds, bool isBuiltIn, ToolHandlerDelegate invoke)
        {
            Name = name;
            Group = group;
            Definition = definition;
            RequiresPolling = requiresPolling;
            MaxPollSeconds = maxPollSeconds;
            IsBuiltIn = isBuiltIn;
            Invoke = invoke;
        }
    }

    /// <summary>
    /// UniAI 工具注册表：通过反射扫描所有 <see cref="UniAIToolAttribute"/> 标记的 static 类，
    /// 建立 name → handler 映射。Agent 按 <see cref="AgentDefinition.ToolGroups"/> 取用。
    /// </summary>
    public static class UniAIToolRegistry
    {
        private static readonly Dictionary<string, ToolHandlerInfo> _handlers =
            new(StringComparer.Ordinal);
        private static readonly object _initLock = new();
        private static bool _initialized;

        /// <summary>
        /// 强制重新扫描（Editor 编译后自动调用）。
        /// </summary>
        public static void Reset()
        {
            lock (_initLock)
            {
                _handlers.Clear();
                _initialized = false;
            }
        }

        /// <summary>
        /// 扫描并注册所有带 <see cref="UniAIToolAttribute"/> 的 static 类。
        /// 多次调用只执行一次。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                DiscoverHandlers();
                _initialized = true;
            }
        }

        /// <summary>
        /// 按分组获取工具定义（发给 LLM 的 <see cref="AITool"/>）。
        /// groups 为空时返回所有工具。
        /// </summary>
        public static IReadOnlyList<AITool> GetDefinitions(IEnumerable<string> groups)
        {
            Initialize();
            if (groups == null)
                return _handlers.Values.Select(h => h.Definition).ToList();

            var set = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                return Array.Empty<AITool>();

            return _handlers.Values
                .Where(h => set.Contains(h.Group))
                .Select(h => h.Definition)
                .ToList();
        }

        /// <summary>
        /// 按分组获取完整的处理器信息。
        /// </summary>
        public static IReadOnlyList<ToolHandlerInfo> GetHandlers(IEnumerable<string> groups)
        {
            Initialize();
            if (groups == null)
                return _handlers.Values.ToList();

            var set = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                return Array.Empty<ToolHandlerInfo>();

            return _handlers.Values
                .Where(h => set.Contains(h.Group))
                .ToList();
        }

        /// <summary>
        /// 按名称查找处理器。
        /// </summary>
        public static bool TryGet(string name, out ToolHandlerInfo handler)
        {
            Initialize();
            return _handlers.TryGetValue(name, out handler);
        }

        /// <summary>
        /// 所有已发现的分组名（去重、有序）。供 Editor UI 下拉。
        /// </summary>
        public static IReadOnlyList<string> AllGroups
        {
            get
            {
                Initialize();
                return _handlers.Values
                    .Select(h => h.Group)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>
        /// 所有已注册的处理器（用于 Editor Tools Tab 调试展示）。
        /// </summary>
        public static IReadOnlyList<ToolHandlerInfo> AllHandlers
        {
            get
            {
                Initialize();
                return _handlers.Values.ToList();
            }
        }

        // ────────────────────────── 内部实现 ──────────────────────────

        private static void DiscoverHandlers()
        {
            int count = 0;
            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(SafeGetTypes)
                    .Where(t => t.IsClass && t.IsAbstract && t.IsSealed); // static class

                foreach (var type in allTypes)
                {
                    var attr = type.GetCustomAttribute<UniAIToolAttribute>();
                    if (attr == null) continue;

                    if (TryRegister(type, attr))
                        count++;
                }

                AILogger.Info($"UniAIToolRegistry: discovered {count} tool(s)");
            }
            catch (Exception ex)
            {
                AILogger.Error($"UniAIToolRegistry: discovery failed: {ex.Message}");
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        private static bool IsBuiltInAssembly(string assemblyName)
        {
            return assemblyName == "UniAI" || assemblyName == "UniAI.Editor";
        }

        private static bool TryRegister(Type type, UniAIToolAttribute attr)
        {
            var name = string.IsNullOrEmpty(attr.Name) ? ToSnakeCase(type.Name) : attr.Name;
            bool isBuiltIn = IsBuiltInAssembly(type.Assembly.GetName().Name);

            if (_handlers.ContainsKey(name))
            {
                AILogger.Warning($"UniAIToolRegistry: duplicate tool name '{name}' from {type.FullName}, overriding previous registration");
            }

            var method = type.GetMethod(
                "HandleAsync",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(JObject), typeof(CancellationToken) },
                null);

            if (method == null)
            {
                AILogger.Warning($"UniAIToolRegistry: {type.FullName} is marked with [UniAITool] but lacks 'public static UniTask<object> HandleAsync(JObject, CancellationToken)'");
                return false;
            }

            if (method.ReturnType != typeof(UniTask<object>))
            {
                AILogger.Warning($"UniAIToolRegistry: {type.FullName}.HandleAsync must return UniTask<object>, got {method.ReturnType.Name}");
                return false;
            }

            ToolHandlerDelegate invoker;
            try
            {
                invoker = (ToolHandlerDelegate)Delegate.CreateDelegate(typeof(ToolHandlerDelegate), method);
            }
            catch (Exception ex)
            {
                AILogger.Error($"UniAIToolRegistry: failed to bind delegate for {type.FullName}: {ex.Message}");
                return false;
            }

            var schema = BuildSchema(type, attr);
            var definition = new AITool
            {
                Name = name,
                Description = attr.Description ?? string.Empty,
                ParametersSchema = schema.ToString(Newtonsoft.Json.Formatting.None)
            };

            _handlers[name] = new ToolHandlerInfo(
                name,
                attr.Group ?? ToolGroups.Core,
                definition,
                attr.RequiresPolling,
                attr.MaxPollSeconds,
                isBuiltIn,
                invoker);
            return true;
        }

        private static JObject BuildSchema(Type type, UniAIToolAttribute attr)
        {
            // 约定：工具类内部嵌套一个 public/internal class XxxArgs，ToolParam 标记字段。
            // 单功能工具：查找名为 "Args" 的嵌套类；找不到则返回空 object schema。
            // action 工具：查找每个 action 对应的 "<Action>Args" 嵌套类（PascalCase），全部合并。
            if (attr.HasActions)
            {
                var actions = attr.Actions ?? Array.Empty<string>();
                var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in actions)
                {
                    var argsType = FindActionArgsType(type, a);
                    if (argsType != null)
                        map[a] = argsType;
                }
                return ToolSchemaGenerator.GenerateForActions(actions, map);
            }

            var singleArgs = type.GetNestedType("Args", BindingFlags.Public | BindingFlags.NonPublic);
            return ToolSchemaGenerator.Generate(singleArgs);
        }

        private static Type FindActionArgsType(Type toolType, string action)
        {
            if (string.IsNullOrEmpty(action)) return null;
            var pascal = ToPascalCase(action);
            return toolType.GetNestedType($"{pascal}Args", BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new System.Text.StringBuilder(input.Length + 4);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ToPascalCase(string snake)
        {
            if (string.IsNullOrEmpty(snake)) return snake;
            var parts = snake.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1));
            }
            return sb.ToString();
        }
    }
}
