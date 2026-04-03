using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 框架内部日志，支持日志级别和 API Key 脱敏
    /// </summary>
    internal static class AILogger
    {
        internal static AILogLevel LogLevel { get; set; } = AILogLevel.Info;

        private const string TAG = "[UniAI]";

        internal static void Verbose(string message)
        {
            if (LogLevel <= AILogLevel.Verbose)
                Debug.Log($"{TAG} {message}");
        }

        internal static void Info(string message)
        {
            if (LogLevel <= AILogLevel.Info)
                Debug.Log($"{TAG} {message}");
        }

        internal static void Warning(string message)
        {
            if (LogLevel <= AILogLevel.Warning)
                Debug.LogWarning($"{TAG} {message}");
        }

        internal static void Error(string message)
        {
            if (LogLevel <= AILogLevel.Error)
                Debug.LogError($"{TAG} {message}");
        }

        /// <summary>
        /// API Key 脱敏：仅显示前 8 位 + ***
        /// </summary>
        internal static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return "(empty)";
            return apiKey.Length <= 8 ? "***" : apiKey[..8] + "***";
        }
    }
}
