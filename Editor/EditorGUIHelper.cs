using System;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Editor 窗口共享的颜色常量和绘制辅助方法
    /// </summary>
    internal static class EditorGUIHelper
    {
        // ─── Shared Colors ───

        internal static readonly Color WindowBg = new(0.15f, 0.15f, 0.15f);
        internal static readonly Color CardBg = new(0.22f, 0.22f, 0.22f);
        internal static readonly Color LeftPanelBg = new(0.18f, 0.18f, 0.18f);
        internal static readonly Color ItemBg = new(0.24f, 0.24f, 0.24f);
        internal static readonly Color ItemSelectedBg = new(0.30f, 0.30f, 0.33f);
        internal static readonly Color SeparatorColor = new(0.12f, 0.12f, 0.12f);
        internal static readonly Color SectionBg = new(0.21f, 0.21f, 0.21f);
        internal static readonly Color AccentColor = new(0.2f, 0.5f, 0.9f);
        internal static readonly Color BorderColor = new(0.35f, 0.35f, 0.35f);

        // ─── Drawing Helpers ───

        /// <summary>
        /// 绘制带背景色的 Section 容器
        /// </summary>
        internal static void DrawSection(float pad, Action drawContent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            var r = EditorGUILayout.BeginVertical();
            if (r.width > 1)
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), SectionBg);
            GUILayout.Space(8);
            drawContent();
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制带背景色 + 边框的 Box 容器
        /// </summary>
        internal static void DrawBox(float pad, Action drawContent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            var r = EditorGUILayout.BeginVertical();
            if (r.width > 1)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), SectionBg);
                // 绘制 1px 边框
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), BorderColor);                 // top
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), BorderColor);           // bottom
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), BorderColor);                 // left
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), BorderColor);          // right
            }
            GUILayout.Space(8);
            drawContent();
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制水平分隔线
        /// </summary>
        internal static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, SeparatorColor);
        }

        // ─── Badge Drawing ───

        private static GUIStyle _badgeStyle;

        private static GUIStyle BadgeStyle
        {
            get
            {
                if (_badgeStyle == null)
                {
                    _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(4, 4, 1, 1),
                        fontSize = 9
                    };
                }
                return _badgeStyle;
            }
        }

        /// <summary>
        /// 在指定 Rect 位置绘制 Badge（带半透明背景 + 彩色文字）
        /// </summary>
        internal static void DrawBadge(Rect rect, string text, Color color)
        {
            var bgColor = new Color(color.r, color.g, color.b, 0.15f);
            EditorGUI.DrawRect(rect, bgColor);

            var old = BadgeStyle.normal.textColor;
            BadgeStyle.normal.textColor = color;
            GUI.Label(rect, text, BadgeStyle);
            BadgeStyle.normal.textColor = old;
        }

        /// <summary>
        /// 在当前 GUILayout 位置内联绘制 Badge
        /// </summary>
        internal static void DrawBadgeInline(string text, Color color)
        {
            var bgColor = new Color(color.r, color.g, color.b, 0.15f);
            var content = new GUIContent(text);
            var rect = GUILayoutUtility.GetRect(content, BadgeStyle, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(rect, bgColor);

            var old = BadgeStyle.normal.textColor;
            BadgeStyle.normal.textColor = color;
            GUI.Label(rect, text, BadgeStyle);
            BadgeStyle.normal.textColor = old;
        }

        // ─── Shared Styles ───

        private static GUIStyle _miniIconBtnStyle;

        internal static GUIStyle MiniIconBtnStyle
        {
            get
            {
                if (_miniIconBtnStyle == null)
                {
                    _miniIconBtnStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        padding = new RectOffset(2, 2, 2, 2),
                        fixedHeight = 18,
                        fixedWidth = 22
                    };
                }
                return _miniIconBtnStyle;
            }
        }

        private static GUIStyle _detailLabelStyle;

        internal static GUIStyle DetailLabelStyle
        {
            get
            {
                if (_detailLabelStyle == null)
                {
                    _detailLabelStyle = new GUIStyle(EditorStyles.miniLabel);
                    _detailLabelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
                }
                return _detailLabelStyle;
            }
        }
    }
}
