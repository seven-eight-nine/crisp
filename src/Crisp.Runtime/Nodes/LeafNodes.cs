namespace Crisp.Runtime.Nodes;

/// <summary>
/// 条件ノード。ラムダ式を評価し、true なら Success、false なら Failure を返す。
/// check ノードの生成コードで使用される。
/// </summary>
public class ConditionNode : BtNode
{
    private readonly Func<bool> _condition;

    public ConditionNode(Func<bool> condition, string? debugLabel = null)
    {
        _condition = condition;
        DebugLabel = debugLabel;
    }

    public override string DebugNodeType => "check";
    public override string? DebugLabel { get; }

    public override BtStatus Tick(TickContext ctx) =>
        Track(_condition() ? BtStatus.Success : BtStatus.Failure);
}

/// <summary>
/// アクションノード。ラムダ式を評価し、BtStatus をそのまま返す。
/// アクション呼び出しの生成コードで使用される。
/// </summary>
public class ActionNode : BtNode
{
    private readonly Func<BtStatus> _action;

    public ActionNode(Func<BtStatus> action, string? debugLabel = null)
    {
        _action = action;
        DebugLabel = debugLabel;
    }

    public override string? DebugLabel { get; }

    public override BtStatus Tick(TickContext ctx) => Track(_action());
}
