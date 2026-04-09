using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 通用 ScriptableObject 编辑工具：list_fields / get / set。基于 SerializedObject。
    /// </summary>
    [UniAITool(
        Name = "manage_scriptable_object",
        Group = ToolGroups.Asset,
        Description =
            "Edit ScriptableObject assets via SerializedObject. Actions: " +
            "'list_fields' (enumerate visible serialized fields), 'get' (read property), 'set' (write property).",
        Actions = new[] { "list_fields", "get", "set" })]
    internal static class ManageScriptableObject
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
                    "list_fields" => ListFields(args),
                    "get" => Get(args),
                    "set" => Set(args),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            if (action == "set") EditorAgentGuard.NotifyAssetsModified();
            return UniTask.FromResult(result);
        }

        public class ListFieldsArgs
        {
            [ToolParam(Description = "ScriptableObject asset path.")]
            public string Path;
        }

        public class GetArgs : ListFieldsArgs
        {
            [ToolParam(Description = "Property name (SerializedProperty path).")]
            public string Property;
        }

        public class SetArgs : GetArgs
        {
            [ToolParam(Description = "Value to assign (typed by the SerializedProperty).")]
            public object Value;
        }

        // ─── 实现 ───

        private static object ListFields(JObject args)
        {
            if (!Load(args, out var so, out var path, out var err)) return ToolResponse.Error(err);

            var sobj = new SerializedObject(so);
            var prop = sobj.GetIterator();
            var list = new List<object>();

            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                list.Add(new { name = prop.name, type = prop.propertyType.ToString() });
            }

            return ToolResponse.Success(new { path, type = so.GetType().Name, fields = list });
        }

        private static object Get(JObject args)
        {
            if (!Load(args, out var so, out var path, out var err)) return ToolResponse.Error(err);
            var name = (string)args["property"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'property' required.");

            var sobj = new SerializedObject(so);
            var prop = sobj.FindProperty(name);
            if (prop == null) return ToolResponse.Error($"Property '{name}' not found on {so.GetType().Name}.");

            return ToolResponse.Success(new
            {
                path,
                property = name,
                type = prop.propertyType.ToString(),
                value = FormatProperty(prop)
            });
        }

        private static object Set(JObject args)
        {
            if (!Load(args, out var so, out var path, out var err)) return ToolResponse.Error(err);
            var name = (string)args["property"];
            var value = args["value"];
            if (string.IsNullOrEmpty(name)) return ToolResponse.Error("'property' required.");
            if (value == null) return ToolResponse.Error("'value' required.");

            var sobj = new SerializedObject(so);
            var prop = sobj.FindProperty(name);
            if (prop == null) return ToolResponse.Error($"Property '{name}' not found on {so.GetType().Name}.");

            SceneEdit.RecordObject(so, "UniAI: set SO property");
            try { AssignProperty(prop, value); }
            catch (Exception ex) { return ToolResponse.Error($"Cannot assign value to {prop.propertyType}: {ex.Message}"); }

            sobj.ApplyModifiedProperties();
            SceneEdit.SetAssetDirty(so);
            if (!Application.isPlaying) AssetDatabase.SaveAssetIfDirty(so);

            return ToolResponse.Success(new { path, property = name, value = FormatProperty(prop) });
        }

        // ─── 辅助 ───

        private static bool Load(JObject args, out ScriptableObject so, out string path, out string error)
        {
            so = null;
            path = (string)args["path"];
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "'path' required."; return false; }
            so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so == null) { error = $"ScriptableObject not found at '{path}'."; return false; }
            return true;
        }

        private static void AssignProperty(SerializedProperty prop, JToken value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = value.ToObject<int>(); break;
                case SerializedPropertyType.Float:
                    prop.floatValue = value.ToObject<float>(); break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value.ToObject<bool>(); break;
                case SerializedPropertyType.String:
                    prop.stringValue = value.ToObject<string>(); break;
                case SerializedPropertyType.Color:
                    var c = ReadFloats(value, 4, 1f);
                    prop.colorValue = new Color(c[0], c[1], c[2], c[3]);
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = ReadFloats(value, 2);
                    prop.vector2Value = new Vector2(v2[0], v2[1]);
                    break;
                case SerializedPropertyType.Vector3:
                    var v3 = ReadFloats(value, 3);
                    prop.vector3Value = new Vector3(v3[0], v3[1], v3[2]);
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = ReadFloats(value, 4);
                    prop.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]);
                    break;
                case SerializedPropertyType.Quaternion:
                    var q = ReadFloats(value, 3);
                    prop.quaternionValue = Quaternion.Euler(q[0], q[1], q[2]);
                    break;
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.String)
                    {
                        string s = value.ToObject<string>();
                        int idx = Array.IndexOf(prop.enumNames, s);
                        if (idx < 0) throw new Exception($"Enum value '{s}' not valid. Options: {string.Join(", ", prop.enumNames)}");
                        prop.enumValueIndex = idx;
                    }
                    else prop.enumValueIndex = value.ToObject<int>();
                    break;
                case SerializedPropertyType.ObjectReference:
                    string assetPath = value.ToObject<string>();
                    prop.objectReferenceValue = string.IsNullOrEmpty(assetPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = value.ToObject<int>(); break;
                case SerializedPropertyType.Rect:
                    var r = ReadFloats(value, 4);
                    prop.rectValue = new Rect(r[0], r[1], r[2], r[3]);
                    break;
                default:
                    throw new Exception($"Unsupported SerializedProperty type: {prop.propertyType}");
            }
        }

        private static string FormatProperty(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F4"),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Color => prop.colorValue.ToString(),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Vector4 => prop.vector4Value.ToString(),
                SerializedPropertyType.Quaternion => prop.quaternionValue.eulerAngles.ToString(),
                SerializedPropertyType.Enum => prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumNames.Length
                    ? prop.enumNames[prop.enumValueIndex] : prop.enumValueIndex.ToString(),
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue == null
                    ? "null" : AssetDatabase.GetAssetPath(prop.objectReferenceValue) ?? prop.objectReferenceValue.name,
                SerializedPropertyType.LayerMask => prop.intValue.ToString(),
                SerializedPropertyType.Rect => prop.rectValue.ToString(),
                _ => $"<{prop.propertyType}>"
            };
        }

        private static float[] ReadFloats(JToken token, int count, float defaultLast = 0f)
        {
            var result = new float[count];
            if (count == 4) result[3] = defaultLast;
            if (token is JArray arr)
            {
                for (int i = 0; i < Math.Min(arr.Count, count); i++)
                    result[i] = arr[i].ToObject<float>();
            }
            else if (token is JObject obj)
            {
                string[] keys = count == 4 && obj["r"] != null
                    ? new[] { "r", "g", "b", "a" }
                    : new[] { "x", "y", "z", "w" };
                for (int i = 0; i < count; i++)
                    if (obj[keys[i]] != null) result[i] = obj[keys[i]].ToObject<float>();
            }
            return result;
        }
    }
}
