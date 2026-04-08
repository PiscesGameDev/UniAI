using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 材质球创建与 Shader 属性编辑 Tool。仅 Edit Mode 下生效。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Material Edit", fileName = "MaterialEditTool")]
    public class MaterialEditTool : AIToolAsset
    {
        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (Application.isPlaying)
                return UniTask.FromResult("Error: MaterialEditTool is only available in Edit Mode.");

            MaterialEditArgs args;
            try { args = JsonConvert.DeserializeObject<MaterialEditArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing required parameter 'action'.");

            string result;
            try
            {
                result = args.Action.ToLowerInvariant() switch
                {
                    "create" => Create(args),
                    "set_shader" => SetShader(args),
                    "set_color" => SetColor(args),
                    "set_float" => SetFloat(args),
                    "set_int" => SetInt(args),
                    "set_vector" => SetVector(args),
                    "set_texture" => SetTexture(args),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
            }

            return UniTask.FromResult(result);
        }

        private string Create(MaterialEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' (Assets/.../X.mat) required.";
            if (!args.Path.StartsWith("Assets/")) return "Error: path must start with 'Assets/'.";
            if (!args.Path.EndsWith(".mat")) return "Error: path must end with '.mat'.";

            string shaderName = string.IsNullOrEmpty(args.Shader) ? "Standard" : args.Shader;
            var shader = Shader.Find(shaderName);
            if (shader == null) return $"Error: Shader '{shaderName}' not found.";

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, args.Path);
            AssetDatabase.SaveAssetIfDirty(mat);
            NotifyFileModified();
            return $"Created material: {args.Path} (shader: {shaderName})";
        }

        private string SetShader(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Shader)) return "Error: 'shader' required.";

            var shader = Shader.Find(args.Shader);
            if (shader == null) return $"Error: Shader '{args.Shader}' not found.";

            Undo.RecordObject(mat, "UniAI: set_shader");
            mat.shader = shader;
            SaveMaterial(mat);
            return $"Set shader of {args.Path} → {args.Shader}";
        }

        private string SetColor(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required (e.g. _BaseColor, _Color).";
            if (args.Value == null) return "Error: 'value' required (e.g. [1, 0, 0, 1]).";

            var color = ParseColor(args.Value);
            Undo.RecordObject(mat, "UniAI: set_color");
            mat.SetColor(args.Property, color);
            SaveMaterial(mat);
            return $"Set {args.Property} = {color} on {args.Path}";
        }

        private string SetFloat(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            float v = args.Value.ToObject<float>();
            Undo.RecordObject(mat, "UniAI: set_float");
            mat.SetFloat(args.Property, v);
            SaveMaterial(mat);
            return $"Set {args.Property} = {v} on {args.Path}";
        }

        private string SetInt(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required.";

            int v = args.Value.ToObject<int>();
            Undo.RecordObject(mat, "UniAI: set_int");
            mat.SetInt(args.Property, v);
            SaveMaterial(mat);
            return $"Set {args.Property} = {v} on {args.Path}";
        }

        private string SetVector(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required.";
            if (args.Value == null) return "Error: 'value' required (e.g. [x, y, z, w]).";

            var v = ParseVector4(args.Value);
            Undo.RecordObject(mat, "UniAI: set_vector");
            mat.SetVector(args.Property, v);
            SaveMaterial(mat);
            return $"Set {args.Property} = {v} on {args.Path}";
        }

        private string SetTexture(MaterialEditArgs args)
        {
            if (!LoadMaterial(args.Path, out var mat, out var err)) return err;
            if (string.IsNullOrEmpty(args.Property)) return "Error: 'property' required (e.g. _BaseMap, _MainTex).";
            if (string.IsNullOrEmpty(args.TexturePath)) return "Error: 'texture_path' required.";

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(args.TexturePath);
            if (tex == null) return $"Error: Texture not found at '{args.TexturePath}'.";

            Undo.RecordObject(mat, "UniAI: set_texture");
            mat.SetTexture(args.Property, tex);
            SaveMaterial(mat);
            return $"Set {args.Property} = {args.TexturePath} on {args.Path}";
        }

        // ─── 辅助 ───

        private static bool LoadMaterial(string path, out Material mat, out string error)
        {
            mat = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Error: 'path' required.";
                return false;
            }

            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                error = $"Error: Material not found at '{path}'.";
                return false;
            }
            return true;
        }

        private void SaveMaterial(Material mat)
        {
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            NotifyFileModified();
        }

        private static Color ParseColor(JToken token)
        {
            if (token is JArray arr)
            {
                float r = arr.Count > 0 ? arr[0].ToObject<float>() : 0;
                float g = arr.Count > 1 ? arr[1].ToObject<float>() : 0;
                float b = arr.Count > 2 ? arr[2].ToObject<float>() : 0;
                float a = arr.Count > 3 ? arr[3].ToObject<float>() : 1;
                return new Color(r, g, b, a);
            }
            if (token is JObject obj)
            {
                float r = obj["r"]?.ToObject<float>() ?? 0;
                float g = obj["g"]?.ToObject<float>() ?? 0;
                float b = obj["b"]?.ToObject<float>() ?? 0;
                float a = obj["a"]?.ToObject<float>() ?? 1;
                return new Color(r, g, b, a);
            }
            return Color.white;
        }

        private static Vector4 ParseVector4(JToken token)
        {
            if (token is JArray arr)
            {
                float x = arr.Count > 0 ? arr[0].ToObject<float>() : 0;
                float y = arr.Count > 1 ? arr[1].ToObject<float>() : 0;
                float z = arr.Count > 2 ? arr[2].ToObject<float>() : 0;
                float w = arr.Count > 3 ? arr[3].ToObject<float>() : 0;
                return new Vector4(x, y, z, w);
            }
            if (token is JObject obj)
            {
                float x = obj["x"]?.ToObject<float>() ?? 0;
                float y = obj["y"]?.ToObject<float>() ?? 0;
                float z = obj["z"]?.ToObject<float>() ?? 0;
                float w = obj["w"]?.ToObject<float>() ?? 0;
                return new Vector4(x, y, z, w);
            }
            return Vector4.zero;
        }

        private class MaterialEditArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("path")] public string Path;
            [JsonProperty("shader")] public string Shader;
            [JsonProperty("property")] public string Property;
            [JsonProperty("value")] public JToken Value;
            [JsonProperty("texture_path")] public string TexturePath;
        }
    }
}
