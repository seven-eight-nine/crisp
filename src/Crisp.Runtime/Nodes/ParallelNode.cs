namespace Crisp.Runtime.Nodes;

/// <summary>
/// パラレルノード。全子ノードを毎 tick 評価し、ポリシーに応じて結果を判定する。
///
/// U4 決定事項（Reset セマンティクス）:
/// <list type="bullet">
///   <item><description>Parallel が Running を返す場合 → 子の状態を維持して続行</description></item>
///   <item><description>Parallel が Success / Failure を返す場合 → 全子を Reset（Running 含む）</description></item>
/// </list>
///
/// これにより、長時間アクション（移動・アニメーション等）が不要になった時に確実に中断され、
/// 再突入時の状態漏れが原理的に発生しない。
/// </summary>
public class ParallelNode : BtNode
{
    private readonly BtNode[] _children;
    private readonly ParallelPolicy _policy;

    public ParallelNode(ParallelPolicy policy, params BtNode[] children)
    {
        _policy = policy;
        _children = children;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        var successCount = 0;
        var failureCount = 0;
        var runningCount = 0;

        foreach (var child in _children)
        {
            switch (child.Tick(ctx))
            {
                case BtStatus.Success: successCount++; break;
                case BtStatus.Failure: failureCount++; break;
                case BtStatus.Running: runningCount++; break;
            }
        }

        var result = _policy switch
        {
            ParallelPolicy.All =>
                failureCount > 0 ? BtStatus.Failure :
                runningCount > 0 ? BtStatus.Running :
                BtStatus.Success,

            ParallelPolicy.Any =>
                successCount > 0 ? BtStatus.Success :
                runningCount > 0 ? BtStatus.Running :
                BtStatus.Failure,

            ParallelPolicy.N(var n) =>
                successCount >= n ? BtStatus.Success :
                failureCount > _children.Length - n ? BtStatus.Failure :
                BtStatus.Running,

            _ => throw new InvalidOperationException($"未知の ParallelPolicy: {_policy}"),
        };

        // U4: 確定時（Running 以外）は全子を Reset
        if (result != BtStatus.Running)
        {
            foreach (var child in _children)
                child.Reset();
        }

        return result;
    }

    public override void Reset()
    {
        foreach (var child in _children) child.Reset();
    }
}
