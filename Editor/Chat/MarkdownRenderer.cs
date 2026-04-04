using System.Collections.Generic;
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
        private static GUIStyle _inlineCodeStyle;
        private static GUIStyle _codeLabelStyle;
        private static GUIStyle _copyBtnStyle;
        private static GUIStyle _copiedLabelStyle;
        private static bool _stylesReady;

        private static readonly Color _codeBlockBg = new(0.10f, 0.10f, 0.10f);
        private static readonly Color _codeHeaderBg = new(0.14f, 0.14f, 0.14f);
        private static readonly Color _inlineCodeBg = new(0.18f, 0.18f, 0.18f);
        private static readonly Color _copiedFlashColor = new(0.3f, 0.85f, 0.4f);

        private static Texture2D _codeBlockBgTex;
        private static Texture2D _inlineCodeBgTex;

        // "Copied!" toast state
        private static string _copiedBlockId;
        private static double _copiedTime;

        public static void Draw(string markdown, float width)
        {
            if (string.IsNullOrEmpty(markdown)) return;
            EnsureStyles();
            _codeBlockCounter = 0;

            var blocks = ParseBlocks(markdown);
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
                    default:
                        DrawRichParagraph(block.Content, width);
                        break;
                }
            }
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

        private static void DrawRichParagraph(string text, float width)
        {
            string rich = FormatInline(text);
            GUILayout.Label(rich, _normalStyle, GUILayout.MaxWidth(width));
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
            OrderedList
        }

        private struct Block
        {
            public BlockType Type;
            public string Content;
            public string Language;
            public string Prefix;
        }

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
                blocks.Add(new Block { Type = BlockType.Paragraph, Content = string.Join(" ", paragraphLines) });
            }

            return blocks;
        }

        // ─── Styles ───

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        private static void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _codeBlockBgTex = MakeTex(_codeBlockBg);
            _inlineCodeBgTex = MakeTex(_inlineCodeBg);

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

            _inlineCodeStyle = new GUIStyle(EditorStyles.label)
            {
                font = GetMonoFont(),
                fontSize = 12,
                richText = false
            };
            _inlineCodeStyle.normal.background = _inlineCodeBgTex;

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
