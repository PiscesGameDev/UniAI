namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void ExecuteQuickAction(ContextCollector.ContextSlot requiredSlot, string message)
        {
            _controller?.ExecuteQuickAction(requiredSlot, message, ref _contextSlots);
        }

        private void CreateNewSession(AgentDefinition agent = null)
        {
            _controller?.CreateNewSession(agent);
            _chatScroll = UnityEngine.Vector2.zero;
            RefreshAIAvatar();
            UnityEngine.GUI.FocusControl("ChatInput");
        }

        private void SwitchToSession(ChatSession session)
        {
            _controller?.SwitchToSession(session);
            _chatScroll.y = float.MaxValue;
            RefreshAIAvatar();
        }
    }
}
