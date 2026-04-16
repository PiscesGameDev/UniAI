using System;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 可复用的 ModelSelector IMGUI 封装。
    /// </summary>
    public class ModelSelectorGUI
    {
        private readonly ModelSelector _selector;

        /// <summary>
        /// 底层的 ModelSelector 实例。
        /// </summary>
        public ModelSelector Selector => _selector;

        public string CurrentModelId => _selector.CurrentModelId;
        public string[] ModelNames => _selector.ModelNames;
        public int SelectedModelIndex => _selector.SelectedModelIndex;

        public ModelSelectorGUI(string lastModelId = null, Func<string, bool> modelFilter = null)
        {
            _selector = new ModelSelector(lastModelId, modelFilter);
        }

        public void RebuildCache(AIConfig config) => _selector.RebuildCache(config);

        /// <summary>
        /// 绘制模型选择下拉框。
        /// </summary>
        public bool Draw(float popupWidth = 220f)
        {
            GUILayout.Label("模型:", EditorStyles.miniLabel, GUILayout.Width(36));

            var modelNames = _selector.ModelNames;
            if (modelNames == null || modelNames.Length == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Popup(0, new[] { "(无可用模型)" },
                    GUILayout.Width(popupWidth), GUILayout.Height(22));
                EditorGUI.EndDisabledGroup();
                return false;
            }

            int selectedIndex = _selector.SelectedModelIndex;
            int newIdx = EditorGUILayout.Popup(selectedIndex, modelNames,
                GUILayout.Width(popupWidth), GUILayout.Height(22));

            if (newIdx == selectedIndex)
                return false;

            _selector.Select(newIdx);
            return true;
        }

        public bool Select(int index) => _selector.Select(index);
        public string ResolveForAgent(AgentDefinition agent) => _selector.ResolveForAgent(agent);
        public void RestoreFromSession(ChatSession session) => _selector.RestoreFromSession(session);
        public string EnsureValid() => _selector.EnsureValid();
    }
}
