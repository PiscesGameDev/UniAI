using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 超时工具 — 为 UniTask 操作添加 CancelAfter 超时包装
    /// </summary>
    internal static class TimeoutHelper
    {
        /// <summary>
        /// 带超时执行一个异步函数。timeoutSeconds &lt;= 0 时等价于直接执行。
        /// 超时触发时抛出 OperationCanceledException（外层可用 `when (!outerCt.IsCancellationRequested)` 区分）。
        /// </summary>
        public static async UniTask<T> WithTimeout<T>(
            Func<CancellationToken, UniTask<T>> action,
            float timeoutSeconds,
            CancellationToken ct)
        {
            if (timeoutSeconds <= 0)
                return await action(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            return await action(cts.Token);
        }

        /// <summary>
        /// 非泛型版本，用于 UniTask（无返回值）操作。
        /// </summary>
        public static async UniTask WithTimeout(
            Func<CancellationToken, UniTask> action,
            float timeoutSeconds,
            CancellationToken ct)
        {
            if (timeoutSeconds <= 0)
            {
                await action(ct);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            await action(cts.Token);
        }
    }
}
