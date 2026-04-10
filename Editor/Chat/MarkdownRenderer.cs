using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 轻量 Markdown → IMGUI 渲染器
    /// </summary>
    public static class MarkdownRenderer
    {
        private static GUIStyle _normalStyle;
        private static GUIStyle _h1Style;
        private static GUIStyle _h2Style;
        private static GUIStyle _h3Style;
        private static GUIStyle _codeBlockStyle;
        private static GUIStyle _codeLabelStyle;
        private static GUIStyle _copyBtnStyle;
        private static GUIStyle _copiedLabelStyle;
        private static bool _stylesReady;

        private static readonly Color _codeBlockBg = new(0.10f, 0.10f, 0.10f);
        private static readonly Color _codeHeaderBg = new(0.14f, 0.14f, 0.14f);
        private static readonly Color _copiedFlashColor = new(0.3f, 0.85f, 0.4f);

        // "Copied!" toast state
        private static string _copiedBlockId;
        private static double _copiedTime;

        // ─── Block 解析缓存 ───
        // key = markdown 内容的 hashCode + length 组合，避免每帧重新解析
        private static readonly Dictionary<long, List<Block>> _blockCache = new();
        private const int BLOCK_CACHE_MAX = 64;
        private const int IMAGE_CACHE_MAX = 32;

        public static void Draw(string markdown, float width)
        {
            if (string.IsNullOrEmpty(markdown)) return;
            EnsureStyles();
            _codeBlockCounter = 0;

            var blocks = GetCachedBlocks(markdown);
            foreach (var block in blocks)
            {
                switch (block.Type)
                {
                    case BlockType.CodeBlock:
                        DrawCodeBlock(block.Content, block.Language, width);
                        break;
                    case BlockType.Heading1:
                        GUILayout.Label(block.Content, _h1Style);
                        break;
                    case BlockType.Heading2:
                        GUILayout.Label(block.Content, _h2Style);
                        break;
                    case BlockType.Heading3:
                        GUILayout.Label(block.Content, _h3Style);
                        break;
                    case BlockType.UnorderedList:
                        DrawListItem("\u2022 ", block.Content);
                        break;
                    case BlockType.OrderedList:
                        DrawListItem(block.Prefix, block.Content);
                        break;
                    case BlockType.Image:
                        DrawImageBlock(block.Content, block.ImageKey, width);
                        break;
                    case BlockType.Paragraph:
                        GUILayout.Label(block.Content, _normalStyle, GUILayout.MaxWidth(width));
                        break;
                }
            }
        }

        /// <summary>
        /// 清除所有缓存（会话切换/消息删除时调用）
        /// </summary>
        public static void InvalidateCache()
        {
            _blockCache.Clear();
            foreach (var tex in _imageCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _imageCache.Clear();
            _imageFailed.Clear();
            _imageKeySrcMap.Clear();
        }

        private static long ComputeCacheKey(string markdown)
        {
            // 用 hashCode + length 组合作为缓存 key，避免对 1MB+ 字符串反复操作
            return ((long)markdown.GetHashCode() << 32) | (uint)markdown.Length;
        }

        private static List<Block> GetCachedBlocks(string markdown)
        {
            long key = ComputeCacheKey(markdown);
            if (_blockCache.TryGetValue(key, out var cached))
                return cached;

            // 防止缓存无限增长
            if (_blockCache.Count >= BLOCK_CACHE_MAX)
                _blockCache.Clear();

            var blocks = ParseBlocks(markdown);
            _blockCache[key] = blocks;
            return blocks;
        }

        private static int _codeBlockCounter;

        private static void DrawCodeBlock(string code, string language, float width)
        {
            // Unique id for this code block (reset per Draw call via _codeBlockCounter)
            string blockId = $"cb_{_codeBlockCounter++}_{code?.GetHashCode()}";
            bool showCopied = _copiedBlockId == blockId
                && (EditorApplication.timeSinceStartup - _copiedTime) < 1.5;

            // Header bar with language + copy/copied button
            var headerRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
            if (headerRect.width > 1)
                EditorGUI.DrawRect(headerRect, _codeHeaderBg);

            GUILayout.Space(10);
            GUILayout.Label(string.IsNullOrEmpty(language) ? "代码" : language,
                _codeLabelStyle, GUILayout.Height(22));
            GUILayout.FlexibleSpace();

            if (showCopied)
            {
                GUILayout.Label("已复制!", _copiedLabelStyle, GUILayout.Height(18));
            }
            else
            {
                if (GUILayout.Button("复制", _copyBtnStyle, GUILayout.Width(44), GUILayout.Height(18)))
                {
                    EditorGUIUtility.systemCopyBuffer = code;
                    _copiedBlockId = blockId;
                    _copiedTime = EditorApplication.timeSinceStartup;
                }
            }

            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            // Code content — deeper black background
            var codeRect = EditorGUILayout.BeginVertical();
            if (codeRect.width > 1)
                EditorGUI.DrawRect(codeRect, _codeBlockBg);

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(code, _codeBlockStyle, GUILayout.MaxWidth(width - 28));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);

            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private static void DrawListItem(string prefix, string content)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label(prefix + FormatInline(content), _normalStyle);
            EditorGUILayout.EndHorizontal();
        }

        // ─── Image Block ───

        private const float IMAGE_MAX_WIDTH = 384f;

        // Texture 缓存：用短 key（imageKey）而不是完整的 data URL
        private static readonly Dictionary<string, Texture2D> _imageCache = new();
        private static readonly HashSet<string> _imageFailed = new();

        // src → imageKey 映射，保存下载按钮需要的原始 src
        private static readonly Dictionary<string, string> _imageKeySrcMap = new();

        private static void DrawImageBlock(string alt, string imageKey, float width)
        {
            var tex = LoadImageTextureByKey(imageKey);
            if (tex == null)
            {
                GUILayout.Label(string.IsNullOrEmpty(alt) ? "[图片加载失败]" : $"[图片加载失败: {alt}]",
                    _normalStyle, GUILayout.MaxWidth(width));
                return;
            }

            float displayWidth = Mathf.Min(tex.width, IMAGE_MAX_WIDTH, width - 8);
            float displayHeight = displayWidth * tex.height / Mathf.Max(1, tex.width);

            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(displayWidth, displayHeight,
                GUILayout.Width(displayWidth), GUILayout.Height(displayHeight));
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);

            // 下载按钮
            EditorGUILayout.BeginHorizontal(GUILayout.Width(displayWidth));
            if (GUILayout.Button("下载图片", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(18)))
            {
                _imageKeySrcMap.TryGetValue(imageKey, out var src);
                SaveImageToFile(src ?? imageKey, alt);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
        }

        private static void SaveImageToFile(string src, string alt)
        {
            string ext = GuessImageExtension(src);
            string defaultName = string.IsNullOrWhiteSpace(alt) ? "image" : SanitizeFileName(alt);
            string path = EditorUtility.SaveFilePanel("保存图片", "", $"{defaultName}.{ext}", ext);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                byte[] bytes = GetImageBytes(src);
                if (bytes == null || bytes.Length == 0)
                {
                    EditorUtility.DisplayDialog("保存失败", "无法读取图片数据。", "确定");
                    return;
                }
                File.WriteAllBytes(path, bytes);
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("保存失败", e.Message, "确定");
            }
        }

        private static byte[] GetImageBytes(string src)
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = src.IndexOf(',');
                if (comma > 0 && src.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) > 0)
                    return Convert.FromBase64String(src.Substring(comma + 1));
            }
            else if (src.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string abs = Path.Combine(Directory.GetCurrentDirectory(), src);
                if (File.Exists(abs)) return File.ReadAllBytes(abs);
            }
            else if (File.Exists(src))
            {
                return File.ReadAllBytes(src);
            }
            return null;
        }

        private static string GuessImageExtension(string src)
        {
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                int slash = src.IndexOf('/');
                int semi = src.IndexOf(';');
                if (slash > 0 && semi > slash)
                {
                    string mime = src.Substring(slash + 1, semi - slash - 1).ToLowerInvariant();
                    return mime switch
                    {
                        "jpeg" => "jpg",
                        "svg+xml" => "svg",
                        _ => mime
                    };
                }
            }
            string ext = Path.GetExtension(src);
            if (!string.IsNullOrEmpty(ext)) return ext.TrimStart('.').ToLowerInvariant();
            return "png";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// 从原始 src 生成短 imageKey（用于 Texture 缓存和 Block 存储）
        /// data URL → "data:{hashCode}:{length}"，其他 src 原样返回
        /// </summary>
        private static string MakeImageKey(string src)
        {
            if (string.IsNullOrEmpty(src)) return "";

            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                // 用 hash + length 生成短 key，避免在字典中存 1MB+ 的 key
                return $"data:{src.GetHashCode():x8}:{src.Length}";
            }

            return src;
        }

        /// <summary>
        /// 通过 imageKey 查找/加载 Texture。首次加载时需要原始 src（从 _imageKeySrcMap 取）
        /// </summary>
        private static Texture2D LoadImageTextureByKey(string imageKey)
        {
            if (string.IsNullOrEmpty(imageKey)) return null;
            if (_imageCache.TryGetValue(imageKey, out var cached) && cached != null) return cached;
            if (_imageFailed.Contains(imageKey)) return null;

            // 防止图片缓存无限增长
            if (_imageCache.Count >= IMAGE_CACHE_MAX)
            {
                foreach (var tex in _imageCache.Values)
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                _imageCache.Clear();
                _imageFailed.Clear();
            }

            // 从映射表取原始 src
            if (!_imageKeySrcMap.TryGetValue(imageKey, out var src))
                src = imageKey; // 非 data URL 的 key 就是原始 src

            try
            {
                byte[] bytes = null;

                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = src.IndexOf(',');
                    if (comma > 0 && src.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        string b64 = src.Substring(comma + 1);
                        bytes = Convert.FromBase64String(b64);
                    }
                }
                else if (src.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
                    if (assetTex != null)
                    {
                        _imageCache[imageKey] = assetTex;
                        return assetTex;
                    }
                }
                else if (File.Exists(src))
                {
                    bytes = File.ReadAllBytes(src);
                }

                if (bytes != null && bytes.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        tex.hideFlags = HideFlags.HideAndDontSave;
                        _imageCache[imageKey] = tex;

                        // Texture 加载成功后释放 src 映射中的原始 data URL 引用，
                        // 只保留 SaveImageToFile 需要的原始 src
                        // （下载时再从 src 解码，不影响渲染性能）
                        return tex;
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            catch
            {
                // fall through to failure mark
            }

            _imageFailed.Add(imageKey);
            return null;
        }

        /// <summary>
        /// 行内格式: **bold**, `code`
        /// </summary>
        private static string FormatInline(string text)
        {
            // Bold: **text**
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            // Inline code: `text` — 用 monospace 风格不好在 richText 中表达，这里加 <b> 标记
            text = Regex.Replace(text, @"`(.+?)`", "<b><color=#e6db74>$1</color></b>");
            return text;
        }

        // ─── Block Parser ───

        private enum BlockType
        {
            Paragraph,
            CodeBlock,
            Heading1,
            Heading2,
            Heading3,
            UnorderedList,
            OrderedList,
            Image
        }

        private struct Block
        {
            public BlockType Type;
            /// <summary>
            /// 文本内容（Paragraph/Heading/List: 渲染文本；Image: alt 文本）。
            /// 已在解析阶段剥离图片 markdown，Paragraph 不含任何 ![...](...) 语法。
            /// </summary>
            public string Content;
            public string Language;
            /// <summary>OrderedList: 序号前缀</summary>
            public string Prefix;
            /// <summary>Image: 短 imageKey（非原始 data URL），用于 Texture 缓存查找</summary>
            public string ImageKey;
        }

        private static readonly Regex _imageRegex = new(@"!\[(.*?)\]\((.+?)\)", RegexOptions.Compiled);

        private static List<Block> ParseBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = markdown.Split('\n');
            int i = 0;

            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Code block
                if (trimmed.StartsWith("```"))
                {
                    string lang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                    i++;
                    var codeLines = new List<string>();
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        codeLines.Add(lines[i]);
                        i++;
                    }
                    if (i < lines.Length) i++; // skip closing ```
                    blocks.Add(new Block
                    {
                        Type = BlockType.CodeBlock,
                        Content = string.Join("\n", codeLines),
                        Language = lang
                    });
                    continue;
                }

                // Headings
                if (trimmed.StartsWith("### "))
                {
                    blocks.Add(new Block { Type = BlockType.Heading3, Content = trimmed.Substring(4) });
                    i++;
                    continue;
                }
                if (trimmed.StartsWith("## "))
                {
                    blocks.Add(new Block { Type = BlockType.Heading2, Content = trimmed.Substring(3) });
                    i++;
                    continue;
                }
                if (trimmed.StartsWith("# "))
                {
                    blocks.Add(new Block { Type = BlockType.Heading1, Content = trimmed.Substring(2) });
                    i++;
                    continue;
                }

                // Unordered list
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    blocks.Add(new Block { Type = BlockType.UnorderedList, Content = trimmed.Substring(2) });
                    i++;
                    continue;
                }

                // Ordered list
                var olMatch = Regex.Match(trimmed, @"^(\d+)\.\s(.+)$");
                if (olMatch.Success)
                {
                    blocks.Add(new Block
                    {
                        Type = BlockType.OrderedList,
                        Content = olMatch.Groups[2].Value,
                        Prefix = olMatch.Groups[1].Value + ". "
                    });
                    i++;
                    continue;
                }

                // Empty line
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                // Paragraph — merge consecutive non-empty lines
                var paragraphLines = new List<string> { trimmed };
                i++;
                while (i < lines.Length
                    && !string.IsNullOrWhiteSpace(lines[i])
                    && !lines[i].TrimStart().StartsWith("```")
                    && !lines[i].TrimStart().StartsWith("#")
                    && !lines[i].TrimStart().StartsWith("- ")
                    && !lines[i].TrimStart().StartsWith("* ")
                    && !Regex.IsMatch(lines[i].TrimStart(), @"^\d+\.\s"))
                {
                    paragraphLines.Add(lines[i].TrimStart());
                    i++;
                }

                // 在解析阶段将段落中的内联图片拆分为独立 Block
                string paragraphText = string.Join(" ", paragraphLines);
                SplitParagraphWithImages(paragraphText, blocks);
            }

            return blocks;
        }

        /// <summary>
        /// 将可能含有 ![alt](src) 的段落文本拆分为 Paragraph + Image Block 序列。
        /// 图片的 data URL 在此阶段转为短 imageKey 存入 Block，原始 src 存入 _imageKeySrcMap。
        /// 后续渲染只操作短 key，不再接触 1MB+ 的原始字符串。
        /// </summary>
        private static void SplitParagraphWithImages(string text, List<Block> blocks)
        {
            var match = _imageRegex.Match(text);
            if (!match.Success)
            {
                // 无图片，直接作为纯文本段落（应用行内格式）
                blocks.Add(new Block { Type = BlockType.Paragraph, Content = FormatInline(text) });
                return;
            }

            int pos = 0;
            while (match.Success)
            {
                // 图片前的文本
                if (match.Index > pos)
                {
                    string before = text.Substring(pos, match.Index - pos);
                    if (!string.IsNullOrWhiteSpace(before))
                        blocks.Add(new Block { Type = BlockType.Paragraph, Content = FormatInline(before) });
                }

                // 图片 Block — 用短 key
                string alt = match.Groups[1].Value;
                string src = match.Groups[2].Value;
                string imageKey = MakeImageKey(src);

                // 保存 imageKey → 原始 src 映射（供加载和下载使用）
                _imageKeySrcMap[imageKey] = src;

                blocks.Add(new Block { Type = BlockType.Image, Content = alt, ImageKey = imageKey });

                pos = match.Index + match.Length;
                match = match.NextMatch();
            }

            // 图片后的剩余文本
            if (pos < text.Length)
            {
                string after = text.Substring(pos);
                if (!string.IsNullOrWhiteSpace(after))
                    blocks.Add(new Block { Type = BlockType.Paragraph, Content = FormatInline(after) });
            }
        }

        // ─── Styles ───

        private static void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _normalStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true,
                fontSize = 13,
                padding = new RectOffset(4, 4, 2, 2)
            };

            _h1Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                wordWrap = true,
                padding = new RectOffset(4, 4, 6, 4)
            };

            _h2Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                wordWrap = true,
                padding = new RectOffset(4, 4, 4, 3)
            };

            _h3Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(4, 4, 3, 2)
            };

            _codeBlockStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false,
                richText = false,
                font = GetMonoFont(),
                fontSize = 12,
                padding = new RectOffset(4, 4, 0, 0)
            };

            _codeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            };
            _codeLabelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

            _copyBtnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter
            };

            _copiedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            _copiedLabelStyle.normal.textColor = _copiedFlashColor;
        }

        private static Font GetMonoFont()
        {
            // 尝试使用 Consolas（Windows）或系统 monospace
            var font = Font.CreateDynamicFontFromOSFont("Consolas", 12);
            return font ?? EditorStyles.label.font;
        }
    }
}
