using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private float CalcInputAreaHeight(float width)
        {
            if (!_stylesReady) return 80f;
            float actionBarH = _showActionBar ? 24f : 0f;
            float textH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                // 与 DrawInputArea 中的 CalcHeight 保持一致: PAD*2 + 加号按钮(22+4) + 发送按钮(4+60) + PAD
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 90);
                textH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            return 6 + actionBarH + 2 + textH + 8 + 6;
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

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            string plusIcon = _showActionBar ? "▼" : "+";
            if (GUILayout.Button(plusIcon, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(INPUT_MIN_HEIGHT)))
                _showActionBar = !_showActionBar;

            GUILayout.Space(4);

            GUI.SetNextControlName("ChatInput");
            float inputH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 90);
                inputH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            _inputText = EditorGUILayout.TextArea(_inputText, _inputStyle,
                GUILayout.Height(inputH), GUILayout.ExpandWidth(true));

            GUILayout.Space(4);

            if (_isStreaming)
            {
                if (GUILayout.Button("■ 停止", GUILayout.Width(60), GUILayout.Height(inputH)))
                    CancelStream();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_inputText));
                if (GUILayout.Button("发送", GUILayout.Width(60), GUILayout.Height(inputH)))
                    SendMessage();
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
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

            if (!_isStreaming && !string.IsNullOrWhiteSpace(_inputText))
            {
                Event.current.Use();
                SendMessage();
            }
        }
    }
}
