using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 材质球聚合工具：create / set_shader / set_color / set_float / set_int / set_vector / set_texture。
    /// </summary>
    [UniAITool(
        Name = "manage_material",
        Group = ToolGroups.Asset,
        Description =
            "Material asset operations. Actions: 'create' (new .mat with shader), " +
            "'set_shader', 'set_color', 'set_float', 'set_int', 'set_vector', 'set_texture'.",
        Actions = new[] { "create", "set_shader", "set_color", "set_float", "set_int", "set_vector", "set_texture" })]
    internal static class ManageMaterial
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
                    "create" => Create(args),
                    "set_shader" => SetShader(args),
                    "set_color" => SetColor(args),
                    "set_float" => SetFloat(args),
                    "set_int" => SetInt(args),
                    "set_vector" => SetVector(args),
                    "set_texture" => SetTexture(args),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            EditorAgentGuard.NotifyAssetsModified();
            return UniTask.FromResult(result);
        }

        public class CreateArgs
        {
            [ToolParam(Description = "Material asset path (must end with '.mat').")]
            public string Path;
            [ToolParam(Description = "Shader name (default 'Standard').", Required = false)]
            public string Shader;
        }

        public class SetShaderArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader name (e.g. 'Universal Render Pipeline/Lit').")]
            public string Shader;
        }

        public class SetColorArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader property name (e.g. '_BaseColor').")]
            public string Property;
            [ToolParam(Description = "Color as [r,g,b,a].")]
            public object Value;
        }

        public class SetFloatArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader property name.")]
            public string Property;
            [ToolParam(Description = "Float value.")]
            public float Value;
        }

        public class SetIntArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader property name.")]
            public string Property;
            [ToolParam(Description = "Integer value.")]
            public int Value;
        }

        public class SetVectorArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader property name.")]
            public string Property;
            [ToolParam(Description = "Vector as [x,y,z,w].")]
            public object Value;
        }

        public class SetTextureArgs
        {
            [ToolParam(Description = "Material asset path.")]
            public string Path;
            [ToolParam(Description = "Shader property name (e.g. '_BaseMap').")]
            public string Property;
            [ToolParam(Description = "Texture asset path.")]
            public string TexturePath;
        }

        // ─── 实现 ───

        private static object Create(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            if (!path.StartsWith("Assets/")) return ToolResponse.Error("path must start with 'Assets/'.");
            if (!path.EndsWith(".mat")) return ToolResponse.Error("path must end with '.mat'.");

            string shaderName = string.IsNullOrEmpty((string)args["shader"]) ? "Standard" : (string)args["shader"];
            var shader = Shader.Find(shaderName);
            if (shader == null) return ToolResponse.Error($"Shader '{shaderName}' not found.");

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            if (!Application.isPlaying) AssetDatabase.SaveAssetIfDirty(mat);
            return ToolResponse.Success(new { path, shader = shaderName });
        }

        private static object SetShader(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var shaderName = (string)args["shader"];
            if (string.IsNullOrEmpty(shaderName)) return ToolResponse.Error("'shader' required.");

            var shader = Shader.Find(shaderName);
            if (shader == null) return ToolResponse.Error($"Shader '{shaderName}' not found.");

            SceneEdit.RecordObject(mat, "UniAI: set_shader");
            mat.shader = shader;
            Save(mat);
            return ToolResponse.Success(new { path, shader = shaderName });
        }

        private static object SetColor(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var prop = (string)args["property"];
            if (string.IsNullOrEmpty(prop)) return ToolResponse.Error("'property' required.");
            if (args["value"] == null) return ToolResponse.Error("'value' required.");

            var color = ParseColor(args["value"]);
            SceneEdit.RecordObject(mat, "UniAI: set_color");
            mat.SetColor(prop, color);
            Save(mat);
            return ToolResponse.Success(new { path, property = prop, color = color.ToString() });
        }

        private static object SetFloat(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var prop = (string)args["property"];
            if (string.IsNullOrEmpty(prop)) return ToolResponse.Error("'property' required.");
            if (args["value"] == null) return ToolResponse.Error("'value' required.");

            float v = args["value"].ToObject<float>();
            SceneEdit.RecordObject(mat, "UniAI: set_float");
            mat.SetFloat(prop, v);
            Save(mat);
            return ToolResponse.Success(new { path, property = prop, value = v });
        }

        private static object SetInt(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var prop = (string)args["property"];
            if (string.IsNullOrEmpty(prop)) return ToolResponse.Error("'property' required.");
            if (args["value"] == null) return ToolResponse.Error("'value' required.");

            int v = args["value"].ToObject<int>();
            SceneEdit.RecordObject(mat, "UniAI: set_int");
            mat.SetInt(prop, v);
            Save(mat);
            return ToolResponse.Success(new { path, property = prop, value = v });
        }

        private static object SetVector(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var prop = (string)args["property"];
            if (string.IsNullOrEmpty(prop)) return ToolResponse.Error("'property' required.");
            if (args["value"] == null) return ToolResponse.Error("'value' required.");

            var v = ParseVector4(args["value"]);
            SceneEdit.RecordObject(mat, "UniAI: set_vector");
            mat.SetVector(prop, v);
            Save(mat);
            return ToolResponse.Success(new { path, property = prop, value = v.ToString() });
        }

        private static object SetTexture(JObject args)
        {
            if (!LoadMaterial(args, out var mat, out var path, out var err)) return ToolResponse.Error(err);
            var prop = (string)args["property"];
            var texPath = (string)args["texturePath"];
            if (string.IsNullOrEmpty(prop)) return ToolResponse.Error("'property' required.");
            if (string.IsNullOrEmpty(texPath)) return ToolResponse.Error("'texturePath' required.");

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex == null) return ToolResponse.Error($"Texture not found at '{texPath}'.");

            SceneEdit.RecordObject(mat, "UniAI: set_texture");
            mat.SetTexture(prop, tex);
            Save(mat);
            return ToolResponse.Success(new { path, property = prop, texturePath = texPath });
        }

        // ─── 辅助 ───

        private static bool LoadMaterial(JObject args, out Material mat, out string path, out string error)
        {
            mat = null;
            path = (string)args["path"];
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "'path' required."; return false; }
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { error = $"Material not found at '{path}'."; return false; }
            return true;
        }

        private static void Save(Material mat)
        {
            SceneEdit.SetAssetDirty(mat);
            // Play Mode 下不写盘，避免触发资产管线对运行中状态的影响。
            if (!Application.isPlaying) AssetDatabase.SaveAssetIfDirty(mat);
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
                return new Color(
                    obj["r"]?.ToObject<float>() ?? 0,
                    obj["g"]?.ToObject<float>() ?? 0,
                    obj["b"]?.ToObject<float>() ?? 0,
                    obj["a"]?.ToObject<float>() ?? 1);
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
                return new Vector4(
                    obj["x"]?.ToObject<float>() ?? 0,
                    obj["y"]?.ToObject<float>() ?? 0,
                    obj["z"]?.ToObject<float>() ?? 0,
                    obj["w"]?.ToObject<float>() ?? 0);
            }
            return Vector4.zero;
        }
    }
}
