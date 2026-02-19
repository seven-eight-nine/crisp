using System.Collections.Generic;

namespace Crisp.Runtime.Nodes;

/// <summary>
/// セレクターノード。子ノードを順に評価し、最初に Success または Running を返すものを採用する。
/// 全ての子が Failure なら Failure を返す。
///
/// Running 状態の記憶:
/// 前回 Running を返した子のインデックスを記憶し、次の tick ではそこから再開する。
/// </summary>
public class SelectorNode : BtNode
{
    private readonly BtNode[] _children;
    private int _runningIndex = -1;

    public SelectorNode(params BtNode[] children)
    {
        _children = children;
    }

    public override IReadOnlyList<BtNode> DebugChildren => _children;

    public override BtStatus Tick(TickContext ctx)
    {
        var start = _runningIndex >= 0 ? _runningIndex : 0;
        for (var i = start; i < _children.Length; i++)
        {
            var status = _children[i].Tick(ctx);
            switch (status)
            {
                case BtStatus.Success:
                    _runningIndex = -1;
                    return Track(BtStatus.Success);
                case BtStatus.Running:
                    _runningIndex = i;
                    return Track(BtStatus.Running);
                // Failure → 次の子へ
            }
        }
        _runningIndex = -1;
        return Track(BtStatus.Failure);
    }

    public override void Reset()
    {
        LastStatus = null;
        _runningIndex = -1;
        foreach (var child in _children) child.Reset();
    }
}
