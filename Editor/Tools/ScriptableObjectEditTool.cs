using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 通用 ScriptableObject 属性编辑 Tool：读取字段、列出可编辑属性、按名赋值。
    /// 基于 SerializedObject/SerializedProperty，自动处理序列化和标脏。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/ScriptableObject Edit", fileName = "ScriptableObjectEditTool")]
    public class ScriptableObjectEditTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (Application.isPlaying)
                return UniTask.FromResult("Error: ScriptableObjectEditTool is only available in Edit Mode.");

            SOEditArgs args;
            try { args = JsonConvert.DeserializeObject<SOEditArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing 'action'.");

            string action = args.Action.ToLowerInvariant();
            string result;
            try
            {
                result = action switch
                {
                    "list_fields" => ListFields(args),
                    "get" => Get(args),
                    "set" => Set(args),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex) { result = $"Error: {ex.Message}"; }

            if (action == "set" && !result.StartsWith("Error"))
                NotifyFileModified();

            return UniTask.FromResult(result);
        }

        private static string ListFields(SOEditArgs args)
        {
            if (!LoadAsset(args.Path, out var so, out var err)) return err;

            var sobj = new SerializedObject(so);
            var prop = sobj.GetIterator();
            var sb = new StringBuilder($"=== Fields of {args.Path} ({so.GetType().Name}) ===\n");

            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                sb.AppendLine($"  {prop.propertyType,-20} {prop.name}");
            }
            return sb.ToString();
        }

        private static string Get(SOEditArgs args)
        {
            if (!LoadAsset(args.Path, out var so, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";

            var sobj = new SerializedObject(so);
            var prop = sobj.FindProperty(args.Property);
            if (prop == null) return $"Error: Property '{args.Property}' not found on {so.GetType().Name}.";

            return $"{args.Property} = {FormatProperty(prop)}";
        }

        private static string Set(SOEditArgs args)
        {
            if (!LoadAsset(args.Path, out var so, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            var sobj = new SerializedObject(so);
            var prop = sobj.FindProperty(args.Property);
            if (prop == null) return $"Error: Property '{args.Property}' not found on {so.GetType().Name}.";

            Undo.RecordObject(so, "UniAI: set SO property");

            try { AssignProperty(prop, args.Value); }
            catch (Exception ex) { return $"Error: Cannot assign value to {prop.propertyType}: {ex.Message}"; }

            sobj.ApplyModifiedProperties();
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssetIfDirty(so);

            return $"Set {args.Property} = {FormatProperty(prop)} on {args.Path}";
        }

        // ─── 辅助 ───

        private static bool LoadAsset(string path, out ScriptableObject so, out string error)
        {
            so = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Error: 'path' required.";
                return false;
            }

            so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so == null)
            {
                error = $"Error: ScriptableObject not found at '{path}'.";
                return false;
            }
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
                    string path = value.ToObject<string>();
                    prop.objectReferenceValue = string.IsNullOrEmpty(path)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = value.ToObject<int>(); break;
                case SerializedPropertyType.Rect:
                    var r = ReadFloats(value, 4);
                    prop.rectValue = new Rect(r[0], r[1], r[2], r[3]);
                    break;
                case SerializedPropertyType.AnimationCurve:
                    throw new Exception("AnimationCurve not supported.");
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
                SerializedPropertyType.String => $"\"{prop.stringValue}\"",
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

        private class SOEditArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("path")] public string Path;
            [JsonProperty("property")] public string Property;
            [JsonProperty("value")] public JToken Value;
        }
    }
}
