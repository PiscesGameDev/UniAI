namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void SendMessage()
        {
            string text = _inputText.Trim();
            _inputText = "";

            if (_controller == null) return;

            if (_controller.ActiveSession == null)
                CreateNewSession();

            _controller.SendMessage(text, _contextSlots);
        }

        private void CancelStream()
        {
            _controller?.CancelStream();
        }
    }
}
