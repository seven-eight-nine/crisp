using System;
using System.Threading;

namespace Crisp.Runtime.Nodes;

/// <summary>
/// 非同期アクションノード。C# の async/await をビヘイビアツリーの tick 駆動モデルに橋渡しする。
///
/// 動作:
/// <list type="bullet">
///   <item><description>初回 tick: ファクトリから <see cref="IAsyncOperation"/> を生成し、内部に保持する</description></item>
///   <item><description>各 tick: 操作の完了状態をポーリングし、未完了なら <see cref="BtStatus.Running"/> を返す</description></item>
///   <item><description>完了時: 操作の結果（<see cref="BtStatus"/>）を返し、内部状態をクリアする</description></item>
///   <item><description>中断時: <see cref="CancellationTokenSource"/> 経由で操作をキャンセルする</description></item>
/// </list>
///
/// <see cref="Abort"/> は Reactive ノードの条件 false 化や Timeout によって呼び出され、
/// 実行中の非同期操作を安全にキャンセルする。
/// </summary>
public class AsyncActionNode : BtNode
{
    /// <summary>
    /// <see cref="IAsyncOperation"/> を生成するファクトリデリゲート。
    /// <see cref="CancellationToken"/> を受け取り、キャンセル可能な非同期操作を返す。
    /// </summary>
    private readonly Func<CancellationToken, IAsyncOperation> _factory;

    /// <summary>現在実行中の非同期操作。null の場合はアイドル状態。</summary>
    private IAsyncOperation? _current;

    /// <summary>現在の非同期操作に紐づくキャンセルトークンソース。</summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 新しい <see cref="AsyncActionNode"/> を生成する。
    /// </summary>
    /// <param name="factory">
    /// <see cref="CancellationToken"/> を受け取り <see cref="IAsyncOperation"/> を返すファクトリ。
    /// 各実行サイクルの開始時に呼び出される。
    /// </param>
    /// <param name="debugLabel">デバッグ用の表示ラベル。</param>
    public AsyncActionNode(Func<CancellationToken, IAsyncOperation> factory, string? debugLabel = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        DebugLabel = debugLabel;
    }

    public override string DebugNodeType => "async-action";

    public override string? DebugLabel { get; }

    /// <summary>
    /// 非同期アクションを tick する。
    /// 未開始なら操作を開始し、実行中なら完了をポーリングする。
    /// </summary>
    /// <param name="ctx">現在の tick コンテキスト。</param>
    /// <returns>
    /// 操作が未完了なら <see cref="BtStatus.Running"/>、
    /// 完了時は操作の結果（<see cref="BtStatus.Success"/> または <see cref="BtStatus.Failure"/>）。
    /// </returns>
    public override BtStatus Tick(TickContext ctx)
    {
        if (_current == null)
        {
            _cts = new CancellationTokenSource();
            _current = _factory(_cts.Token);
        }

        if (_current.IsCompleted)
        {
            var result = _current.GetResult();
            CleanUp();
            return Track(result);
        }

        return Track(BtStatus.Running);
    }

    /// <summary>
    /// 外的要因による強制キャンセル。
    /// Reactive ノードの条件 false 化、Timeout、親ノードの中断等で呼び出される。
    /// 実行中の非同期操作を <see cref="CancellationToken"/> 経由でキャンセルし、
    /// <see cref="IAsyncOperation.Cancel"/> も呼び出す。
    /// </summary>
    public override void Abort()
    {
        LastStatus = null;
        if (_current != null)
        {
            _cts?.Cancel();
            _current.Cancel();
        }
        CleanUp();
    }

    /// <summary>
    /// 正常終了後の状態初期化。
    /// Parallel 確定時やツリー再起動時に呼び出される。
    /// </summary>
    public override void Reset()
    {
        LastStatus = null;
        if (_current != null)
        {
            _cts?.Cancel();
            _current.Cancel();
        }
        CleanUp();
    }

    /// <summary>内部状態をクリアし、リソースを解放する。</summary>
    private void CleanUp()
    {
        _current = null;
        _cts?.Dispose();
        _cts = null;
    }
}
