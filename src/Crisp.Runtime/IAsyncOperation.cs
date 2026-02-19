namespace Crisp.Runtime;

/// <summary>
/// 非同期操作の抽象インターフェース。
/// 具体的な非同期ライブラリ（ValueTask, Task, UniTask 等）に依存しない
/// 非同期操作のポーリングモデルを提供する。
///
/// 非同期アクションは内部に未完了の非同期操作を保持し、
/// 各 tick で完了状態をポーリングし、完了時に <see cref="BtStatus"/> を返す。
///
/// 主な使用場面:
/// <list type="bullet">
///   <item><description>ゲームAIの移動・パスファインディング等の長時間アクション</description></item>
///   <item><description>ネットワーク通信を伴うAI判断</description></item>
///   <item><description>アニメーション完了待ち</description></item>
/// </list>
/// </summary>
public interface IAsyncOperation
{
    /// <summary>
    /// 非同期操作が完了しているかどうかを返す。
    /// 各 tick でポーリングされ、完了まで <c>false</c> を返し続ける。
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// 完了した非同期操作の結果を取得する。
    /// <see cref="IsCompleted"/> が <c>true</c> の場合にのみ呼び出すこと。
    /// </summary>
    /// <returns>非同期操作の最終結果。</returns>
    BtStatus GetResult();

    /// <summary>
    /// 実行中の非同期操作をキャンセルする。
    /// Reactive ノードの Abort や Timeout によって呼び出される。
    /// </summary>
    void Cancel();
}
