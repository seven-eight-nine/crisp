using System.Collections.Generic;

namespace Crisp.Runtime.Nodes;

/// <summary>
/// デバッグプロキシノード。実際のノードをラップし、
/// tick の前後に <see cref="IDebugSink"/> へイベントを通知する。
///
/// Source Generator がデバッグモード時に、各ノードをこの DebugProxyNode で包む
/// 生成コードを出力する。これにより:
/// <list type="bullet">
///   <item><description>各ノードの評価開始/終了をトレースできる</description></item>
///   <item><description>ブレークポイント設定によるステップ実行が可能</description></item>
///   <item><description>Abort イベントの追跡ができる</description></item>
/// </list>
///
/// デバッグプロパティ（DebugChildren, DebugNodeType, DebugLabel）は
/// 内部ノードに透過的に委譲する。
///
/// <c>nodeId</c> は <c>IrNode.Id</c>（= <c>CstNode.Id</c>）と一致し、
/// エディタ上の位置との対応付けに使用する。
/// </summary>
public class DebugProxyNode : BtNode
{
    /// <summary>ラップされた実際のノード。</summary>
    private readonly BtNode _inner;

    /// <summary>
    /// ノードの一意識別子。<c>IrNode.Id</c> と一致する。
    /// ブレークポイント設定・実行トレースで使用する。
    /// </summary>
    private readonly int _nodeId;

    /// <summary>デバッグイベントの通知先。</summary>
    private readonly IDebugSink _sink;

    /// <summary>
    /// 新しい <see cref="DebugProxyNode"/> を生成する。
    /// </summary>
    /// <param name="inner">ラップする実際のノード。</param>
    /// <param name="nodeId">ノードの一意識別子（IrNode.Id）。</param>
    /// <param name="sink">デバッグイベントの通知先。</param>
    public DebugProxyNode(BtNode inner, int nodeId, IDebugSink sink)
    {
        _inner = inner;
        _nodeId = nodeId;
        _sink = sink;
    }

    public override IReadOnlyList<BtNode> DebugChildren => _inner.DebugChildren;

    public override string DebugNodeType => _inner.DebugNodeType;

    public override string? DebugLabel => _inner.DebugLabel;

    /// <summary>
    /// デバッグプロキシの tick。
    /// 内部ノードの tick 前後に OnBeforeTick/OnAfterTick を通知する。
    /// ブレークポイントが設定されている場合は WaitForContinue で一時停止する。
    /// </summary>
    /// <param name="ctx">現在の tick コンテキスト。</param>
    /// <returns>内部ノードの評価結果。</returns>
    public override BtStatus Tick(TickContext ctx)
    {
        _sink.OnBeforeTick(_nodeId, ctx);

        if (_sink.ShouldBreak(_nodeId))
            _sink.WaitForContinue();

        var status = _inner.Tick(ctx);

        _sink.OnAfterTick(_nodeId, status, ctx);
        return Track(status);
    }

    /// <summary>
    /// 内部ノードの Abort を呼び出し、OnAbort イベントを通知する。
    /// </summary>
    public override void Abort()
    {
        LastStatus = null;
        _inner.Abort();
        _sink.OnAbort(_nodeId);
    }

    /// <summary>
    /// 内部ノードの Reset を呼び出す。
    /// </summary>
    public override void Reset()
    {
        LastStatus = null;
        _inner.Reset();
    }
}
