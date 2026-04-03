using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UniAI
{
    /// <summary>
    /// Prompt 模板 — 支持 {{变量名}} 替换
    /// </summary>
    public class PromptTemplate
    {
        private readonly string _template;

        private static readonly Regex VariablePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        private PromptTemplate(string template)
        {
            _template = template;
        }

        /// <summary>
        /// 从文件加载模板
        /// </summary>
        public static PromptTemplate FromFile(string path)
        {
            var content = File.ReadAllText(path);
            return new PromptTemplate(content);
        }

        /// <summary>
        /// 从 Unity Resources 加载模板（不带扩展名）
        /// </summary>
        public static PromptTemplate FromResources(string resourcePath)
        {
            var textAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(resourcePath);
            if (textAsset == null)
            {
                AILogger.Error($"Prompt template not found in Resources: {resourcePath}");
                return new PromptTemplate("");
            }
            return new PromptTemplate(textAsset.text);
        }

        /// <summary>
        /// 从字符串创建模板
        /// </summary>
        public static PromptTemplate FromString(string template)
        {
            return new PromptTemplate(template);
        }

        /// <summary>
        /// 渲染模板，替换 {{变量名}} 为实际值
        /// </summary>
        public string Render(Dictionary<string, string> variables)
        {
            if (variables == null || variables.Count == 0)
                return _template;

            return VariablePattern.Replace(_template, match =>
            {
                var key = match.Groups[1].Value;
                return variables.TryGetValue(key, out var value) ? value : match.Value;
            });
        }

        /// <summary>
        /// 获取模板中所有变量名
        /// </summary>
        public List<string> GetVariableNames()
        {
            var names = new List<string>();
            foreach (Match match in VariablePattern.Matches(_template))
            {
                var name = match.Groups[1].Value;
                if (!names.Contains(name))
                    names.Add(name);
            }
            return names;
        }
    }
}
