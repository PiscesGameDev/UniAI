using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 可复用的模型选择 IMGUI 组件，封装 ModelSelector + Popup 绘制
    /// </summary>
    public class ModelSelectorGUI
    {
        private readonly ModelSelector _selector;

        /// <summary>
        /// 底层 ModelSelector 实例，供需要 Runtime 类型的 API 使用
        /// </summary>
        public ModelSelector Selector => _selector;

        public string CurrentModelId => _selector.CurrentModelId;
        public string[] ModelNames => _selector.ModelNames;
        public int SelectedModelIndex => _selector.SelectedModelIndex;

        public ModelSelectorGUI(string lastModelId = null)
        {
            _selector = new ModelSelector(lastModelId);
        }

        public void RebuildCache(AIConfig config) => _selector.RebuildCache(config);

        /// <summary>
        /// 绘制模型选择 Popup，返回 true 表示用户切换了模型
        /// </summary>
        public bool Draw(float popupWidth = 220f)
        {
            var modelNames = _selector.ModelNames;
            if (modelNames == null || modelNames.Length == 0)
                return false;

            GUILayout.Label("模型:", EditorStyles.miniLabel, GUILayout.Width(36));
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
