using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private static readonly string[] _textFileExtensions = { ".cs", ".json", ".txt", ".xml", ".yaml", ".yml", ".md", ".csv", ".cfg", ".ini", ".log", ".shader", ".hlsl", ".cginc", ".compute" };

        private float CalcInputAreaHeight(float width)
        {
            if (!_stylesReady) return 80f;
            float actionBarH = _showActionBar ? 24f : 0f;
            float attachBarH = _pendingAttachments.Count > 0 ? 40f : 0f;
            float textH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 116);
                textH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            return 6 + actionBarH + 2 + attachBarH + textH + 8 + 6;
        }

        private void DrawInputArea(float width)
        {
            EditorGUI.DrawRect(new Rect(0, 0, width, CalcInputAreaHeight(width)), _inputBg);

            GUILayout.Space(6);

            if (_showActionBar)
            {
                var barRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
                if (barRect.width > 1)
                    EditorGUI.DrawRect(barRect, _contextBarBg);

                GUILayout.Space(PAD);

                DrawContextToggle("选中对象", ContextCollector.ContextSlot.Selection);
                DrawContextToggle("控制台", ContextCollector.ContextSlot.Console);
                DrawContextToggle("工程资源", ContextCollector.ContextSlot.Project);

                GUILayout.Space(8);
                GUILayout.Label("|", EditorStyles.miniLabel, GUILayout.Width(6));
                GUILayout.Space(4);

                foreach (var (label, icon, slot, message) in _quickActions)
                {
                    if (GUILayout.Button(icon + " " + label, _quickActionStyle, GUILayout.Height(18)))
                        ExecuteQuickAction(slot, message);
                    GUILayout.Space(2);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Space(PAD);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            // 附件预览条
            if (_pendingAttachments.Count > 0)
                DrawAttachmentPreviewBar(width);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            string plusIcon = _showActionBar ? "▼" : "+";
            if (GUILayout.Button(plusIcon, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(INPUT_MIN_HEIGHT)))
                _showActionBar = !_showActionBar;

            GUILayout.Space(2);

            // 附件按钮
            if (GUILayout.Button(new GUIContent("@", "添加附件"), EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(INPUT_MIN_HEIGHT)))
                ShowAttachmentFilePicker();

            GUILayout.Space(4);

            GUI.SetNextControlName("ChatInput");
            float inputH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 116);
                inputH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            _inputText = EditorGUILayout.TextArea(_inputText, _inputStyle,
                GUILayout.Height(inputH), GUILayout.ExpandWidth(true));

            GUILayout.Space(4);

            bool isStreaming = _controller != null && _controller.IsStreaming;
            if (isStreaming)
            {
                if (GUILayout.Button("■ 停止", GUILayout.Width(60), GUILayout.Height(inputH)))
                    CancelStream();
            }
            else
            {
                bool hasContent = !string.IsNullOrWhiteSpace(_inputText) || _pendingAttachments.Count > 0;
                EditorGUI.BeginDisabledGroup(!hasContent);
                if (GUILayout.Button("发送", GUILayout.Width(60), GUILayout.Height(inputH)))
                    SendMessage();
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 拖拽检测（覆盖整个输入区）
            HandleDragAndDrop();
        }

        // ─── Attachment Preview Bar ───

        private void DrawAttachmentPreviewBar(float width)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(36));
            GUILayout.Space(PAD + 26);

            for (int i = _pendingAttachments.Count - 1; i >= 0; i--)
            {
                var att = _pendingAttachments[i];
                DrawAttachmentChip(att, i);
                GUILayout.Space(4);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawAttachmentChip(ChatAttachment att, int index)
        {
            var chipRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(32));
            if (chipRect.width > 1)
            {
                var bgRect = new Rect(chipRect.x - 2, chipRect.y, chipRect.width + 4, chipRect.height);
                DrawRoundedRect(bgRect, new Color(0.25f, 0.25f, 0.28f), 4f);
            }

            GUILayout.Space(4);

            if (att.Type == ChatAttachmentType.Image)
            {
                // 图片缩略图
                string thumbKey = $"att_{index}_{att.FileName}";
                if (_attachmentThumbnails.TryGetValue(thumbKey, out var thumb) && thumb != null)
                {
                    var thumbRect = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
                }
                else
                {
                    GUILayout.Label("[img]", EditorStyles.miniLabel, GUILayout.Width(28), GUILayout.Height(28));
                }
            }
            else
            {
                GUILayout.Label("F", EditorStyles.miniLabel, GUILayout.Width(16), GUILayout.Height(28));
            }

            GUILayout.Space(2);

            string displayName = att.FileName ?? "unknown";
            if (displayName.Length > 20)
                displayName = displayName.Substring(0, 17) + "...";
            GUILayout.Label(displayName, EditorStyles.miniLabel, GUILayout.Height(28));

            GUILayout.Space(2);

            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(18)))
            {
                string thumbKey = $"att_{index}_{att.FileName}";
                _attachmentThumbnails.Remove(thumbKey);
                _pendingAttachments.RemoveAt(index);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        // ─── File Picker ───

        private static readonly string _imageFilter = "Image files (png, jpg, tga, bmp, psd);*.png;*.jpg;*.jpeg;*.tga;*.bmp;*.psd";
        private static readonly string _textFilter = "Text files (cs, json, txt, xml, yaml, md, shader);*.cs;*.json;*.txt;*.xml;*.yaml;*.yml;*.md;*.csv;*.cfg;*.ini;*.log;*.shader;*.hlsl;*.cginc;*.compute";
        private static readonly string _allFilter = "All supported files;*.png;*.jpg;*.jpeg;*.tga;*.bmp;*.psd;*.cs;*.json;*.txt;*.xml;*.yaml;*.yml;*.md;*.csv;*.cfg;*.ini;*.log;*.shader;*.hlsl;*.cginc;*.compute";

        private void ShowAttachmentFilePicker()
        {
            string filter = $"{_allFilter},{_imageFilter},{_textFilter}";
            string path = EditorUtility.OpenFilePanelWithFilters("选择附件", Application.dataPath, ParseFilters(filter));
            if (string.IsNullOrEmpty(path)) return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string[] imageExts = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd" };

            if (imageExts.Contains(ext))
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    AddImageAttachment(tex, Path.GetFileNameWithoutExtension(path));
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            else
            {
                string content = File.ReadAllText(path);
                AddFileAttachment(Path.GetFileName(path), content);
            }

            Repaint();
        }

        /// <summary>
        /// 将逗号分隔的 "Label;ext1;ext2,Label2;ext3" 格式转为 OpenFilePanelWithFilters 所需的 string[]
        /// </summary>
        private static string[] ParseFilters(string filter)
        {
            var parts = filter.Split(',');
            var result = new System.Collections.Generic.List<string>();
            foreach (var part in parts)
            {
                var segments = part.Split(';');
                if (segments.Length < 2) continue;
                result.Add(segments[0]); // label
                // 合并扩展名：去掉 *. 前缀，用逗号分隔
                var exts = new System.Collections.Generic.List<string>();
                for (int i = 1; i < segments.Length; i++)
                    exts.Add(segments[i].TrimStart('*', '.'));
                result.Add(string.Join(",", exts));
            }
            return result.ToArray();
        }

        // ─── Drag & Drop ───

        private void HandleDragAndDrop()
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                return;

            if (evt.type == EventType.DragUpdated)
            {
                bool hasValid = DragAndDrop.objectReferences.Any(IsAcceptableDragObject);
                DragAndDrop.visualMode = hasValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                evt.Use();

                foreach (var obj in DragAndDrop.objectReferences)
                    TryAddAttachment(obj);

                Repaint();
            }
        }

        private static bool IsAcceptableDragObject(UnityEngine.Object obj)
        {
            if (obj is Texture2D || obj is Sprite || obj is RenderTexture || obj is TextAsset)
                return true;

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return false;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return _textFileExtensions.Contains(ext);
        }

        private void TryAddAttachment(UnityEngine.Object obj)
        {
            if (obj is Texture2D tex)
            {
                AddImageAttachment(tex, obj.name);
            }
            else if (obj is Sprite sprite)
            {
                AddImageAttachment(sprite.texture, obj.name);
            }
            else if (obj is RenderTexture rt)
            {
                var temp = RenderTextureToTexture2D(rt);
                if (temp != null)
                {
                    AddImageAttachment(temp, obj.name);
                    UnityEngine.Object.DestroyImmediate(temp);
                }
            }
            else if (obj is TextAsset textAsset)
            {
                AddFileAttachment(obj.name + ".txt", textAsset.text);
            }
            else
            {
                // 尝试作为文本文件读取
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) return;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (!_textFileExtensions.Contains(ext)) return;

                string content = File.ReadAllText(path);
                AddFileAttachment(Path.GetFileName(path), content);
            }
        }

        private void AddImageAttachment(Texture2D tex, string name)
        {
            // 确保纹理可读：使用 RenderTexture 中转
            Texture2D readable = MakeReadable(tex);
            byte[] png = readable.EncodeToPNG();
            if (readable != tex)
                UnityEngine.Object.DestroyImmediate(readable);

            if (png == null || png.Length == 0) return;

            string base64 = Convert.ToBase64String(png);
            var att = new ChatAttachment
            {
                Type = ChatAttachmentType.Image,
                FileName = name + ".png",
                Content = base64,
                MediaType = "image/png"
            };
            _pendingAttachments.Add(att);

            // 创建缩略图
            string thumbKey = $"att_{_pendingAttachments.Count - 1}_{att.FileName}";
            var thumb = new Texture2D(2, 2);
            thumb.LoadImage(png);
            _attachmentThumbnails[thumbKey] = thumb;
        }

        private void AddFileAttachment(string fileName, string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            _pendingAttachments.Add(new ChatAttachment
            {
                Type = ChatAttachmentType.File,
                FileName = fileName,
                Content = content
            });
        }

        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source.isReadable)
                return source;

            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            return tex;
        }

        private void DrawContextToggle(string label, ContextCollector.ContextSlot slot)
        {
            bool isOn = _contextSlots.HasFlag(slot);
            var style = isOn ? _contextToggleOnStyle : _contextToggleOffStyle;
            if (GUILayout.Button(label, style, GUILayout.Height(18)))
            {
                if (isOn) _contextSlots &= ~slot;
                else _contextSlots |= slot;
            }
        }

        private void HandleInputShortcuts()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (Event.current.keyCode != KeyCode.Return && Event.current.keyCode != KeyCode.KeypadEnter) return;
            if (Event.current.shift) return;
            if (GUI.GetNameOfFocusedControl() != "ChatInput") return;

            bool isStreaming = _controller != null && _controller.IsStreaming;
            bool hasContent = !string.IsNullOrWhiteSpace(_inputText) || _pendingAttachments.Count > 0;
            if (!isStreaming && hasContent)
            {
                Event.current.Use();
                SendMessage();
            }
        }
    }
}
