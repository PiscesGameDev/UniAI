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

        internal static readonly Color LeftPanelBg = new(0.18f, 0.18f, 0.18f);
        internal static readonly Color ItemBg = new(0.24f, 0.24f, 0.24f);
        internal static readonly Color ItemSelectedBg = new(0.30f, 0.30f, 0.33f);
        internal static readonly Color SeparatorColor = new(0.12f, 0.12f, 0.12f);
        internal static readonly Color SectionBg = new(0.21f, 0.21f, 0.21f);

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
        /// 绘制水平分隔线
        /// </summary>
        internal static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, SeparatorColor);
        }
    }
}
