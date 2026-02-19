namespace Crisp.Runtime.Nodes;

/// <summary>
/// シーケンスノード。子ノードを順に評価し、全て Success なら Success を返す。
/// いずれかが Failure を返した時点で Failure を返す。
///
/// Running 状態の記憶:
/// 前回 Running を返した子のインデックスを記憶し、次の tick ではそこから再開する。
/// </summary>
public class SequenceNode : BtNode
{
    private readonly BtNode[] _children;
    private int _runningIndex = -1;

    public SequenceNode(params BtNode[] children)
    {
        _children = children;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        var start = _runningIndex >= 0 ? _runningIndex : 0;
        for (var i = start; i < _children.Length; i++)
        {
            var status = _children[i].Tick(ctx);
            switch (status)
            {
                case BtStatus.Failure:
                    _runningIndex = -1;
                    return BtStatus.Failure;
                case BtStatus.Running:
                    _runningIndex = i;
                    return BtStatus.Running;
                // Success → 次の子へ
            }
        }
        _runningIndex = -1;
        return BtStatus.Success;
    }

    public override void Reset()
    {
        _runningIndex = -1;
        foreach (var child in _children) child.Reset();
    }
}
