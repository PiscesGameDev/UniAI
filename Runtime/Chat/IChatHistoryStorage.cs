using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 聊天历史存储接口 — 抽象存储层，支持不同的持久化实现
    /// </summary>
    public interface IChatHistoryStorage
    {
        /// <summary>
        /// 加载所有会话
        /// </summary>
        List<ChatSession> LoadAll();

        /// <summary>
        /// 保存会话
        /// </summary>
        void Save(ChatSession session);

        /// <summary>
        /// 删除指定会话
        /// </summary>
        void Delete(string sessionId);

        /// <summary>
        /// 删除所有会话
        /// </summary>
        void DeleteAll();
    }
}
