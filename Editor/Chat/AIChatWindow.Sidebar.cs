using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void DrawSidebar()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            string newFilter = EditorGUILayout.TextField(_history.SearchFilter,
                _searchFieldStyle, GUILayout.Height(20));
            if (newFilter != _history.SearchFilter)
                _history.SearchFilter = newFilter;
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);

            var groups = _history.GetGroupedSessions();
            foreach (var (group, items) in groups)
            {
                GUILayout.Label(group, _groupStyle);
                foreach (var session in items)
                {
                    bool isActive = _activeSession != null && _activeSession.Id == session.Id;
                    DrawSessionItem(session, isActive);
                }
                GUILayout.Space(4);
            }

            if (groups.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("还没有对话记录", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSessionItem(ChatSession session, bool isActive)
        {
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(26));
            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isActive ? _selectedItemBg : Color.clear);

            GUILayout.Space(PAD + 4);
            GUILayout.Label(TruncateTitle(session.Title, 20), _sessionItemStyle, GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowSessionContextMenu(session);
                else
                    SwitchToSession(session);
                Event.current.Use();
                Repaint();
            }
        }

        private void ShowSessionContextMenu(ChatSession session)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("删除"), false, () =>
            {
                if (_activeSession != null && _activeSession.Id == session.Id)
                    _activeSession = null;
                _history.Delete(session.Id);
                Repaint();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除全部"), false, () =>
            {
                _activeSession = null;
                _history.DeleteAll();
                Repaint();
            });
            menu.ShowAsContext();
        }
    }
}
