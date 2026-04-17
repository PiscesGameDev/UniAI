using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// 从参数类反射生成 JSON Schema。
    /// 支持的类型：string / int / long / float / double / bool / enum / List&lt;T&gt; / 嵌套 object。
    /// 读取 <see cref="ToolParamAttribute"/> 填充 description / required / default / enum。
    /// </summary>
    internal static class ToolSchemaGenerator
    {
        /// <summary>
        /// 为参数类生成 JSON Schema。
        /// </summary>
        public static JObject Generate(Type argsType)
        {
            if (argsType == null)
                return BuildEmptyObject();

            return BuildObjectSchema(argsType);
        }

        /// <summary>
        /// 合并多个 action 分支的参数类为单个 Schema：根部添加 <c>action</c> 必填字段，其它字段并集。
        /// 这是为 HasActions=true 的工具设计的简化策略——不使用 oneOf，直接把所有 action 的参数合并为可选字段。
        /// LLM 通过 action 枚举 + description 判断该传哪些字段。
        /// </summary>
        public static JObject GenerateForActions(string[] actions, IReadOnlyDictionary<string, Type> actionArgsTypes)
        {
            var root = new JObject { ["type"] = "object" };
            var properties = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "Operation to perform.",
                    ["enum"] = new JArray(actions ?? Array.Empty<string>())
                }
            };
            var required = new JArray { "action" };

            if (actionArgsTypes != null)
            {
                // 合并所有 action 的参数字段为并集，全部标记为可选（根据实际 action 按需提供）
                foreach (var kv in actionArgsTypes)
                {
                    if (kv.Value == null) continue;
                    var sub = BuildObjectSchema(kv.Value);
                    if (sub?["properties"] is not JObject subProps) continue;

                    foreach (var prop in subProps.Properties())
                    {
                        if (properties.ContainsKey(prop.Name)) continue;
                        properties[prop.Name] = prop.Value.DeepClone();
                    }
                }
            }

            root["properties"] = properties;
            root["required"] = required;
            return root;
        }

        private static JObject BuildEmptyObject()
        {
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            };
        }

        private static JObject BuildObjectSchema(Type type)
        {
            var properties = new JObject();
            var required = new JArray();

            foreach (var member in GetSerializableMembers(type))
            {
                var memberType = GetMemberType(member);
                if (memberType == null) continue;

                var attr = member.GetCustomAttribute<ToolParamAttribute>();
                var name = ResolveMemberName(member);
                var propSchema = BuildTypeSchema(memberType);

                if (attr != null)
                {
                    if (!string.IsNullOrEmpty(attr.Description))
                        propSchema["description"] = attr.Description;
                    if (!string.IsNullOrEmpty(attr.DefaultValue))
                        propSchema["default"] = attr.DefaultValue;
                    if (attr.Enum is { Length: > 0 })
                        propSchema["enum"] = new JArray(attr.Enum);
                    if (attr.Required && !IsNullable(memberType))
                        required.Add(name);
                }

                properties[name] = propSchema;
            }

            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (required.Count > 0)
                schema["required"] = required;
            return schema;
        }

        private static JObject BuildTypeSchema(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (underlying == typeof(string))
                return new JObject { ["type"] = "string" };
            if (underlying == typeof(bool))
                return new JObject { ["type"] = "boolean" };
            if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte))
                return new JObject { ["type"] = "integer" };
            if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
                return new JObject { ["type"] = "number" };

            if (underlying.IsEnum)
            {
                return new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(Enum.GetNames(underlying))
                };
            }

            if (underlying.IsArray)
            {
                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = BuildTypeSchema(underlying.GetElementType())
                };
            }

            if (underlying.IsGenericType)
            {
                var def = underlying.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>))
                {
                    return new JObject
                    {
                        ["type"] = "array",
                        ["items"] = BuildTypeSchema(underlying.GetGenericArguments()[0])
                    };
                }
            }

            // 嵌套 object（递归）
            if (underlying.IsClass || underlying.IsValueType)
                return BuildObjectSchema(underlying);

            return new JObject { ["type"] = "string" };
        }

        private static IEnumerable<MemberInfo> GetSerializableMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var f in type.GetFields(flags))
            {
                if (f.IsStatic || f.IsInitOnly) continue;
                if (f.IsPrivate && f.GetCustomAttribute<ToolParamAttribute>() == null) continue;
                yield return f;
            }
            foreach (var p in type.GetProperties(flags))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                yield return p;
            }
        }

        private static Type GetMemberType(MemberInfo member) => member switch
        {
            FieldInfo f => f.FieldType,
            PropertyInfo p => p.PropertyType,
            _ => null
        };

        private static string ResolveMemberName(MemberInfo member)
        {
            // 与 Newtonsoft.Json 默认行为一致：camelCase 与原名都可反序列化，
            // 此处直接返回 PascalCase 下的 camelCase 形式（首字母小写）。
            var name = member.Name;
            if (string.IsNullOrEmpty(name)) return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static bool IsNullable(Type type)
        {
            if (!type.IsValueType) return true;
            return Nullable.GetUnderlyingType(type) != null;
        }
    }
}
