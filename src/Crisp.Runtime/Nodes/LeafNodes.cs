namespace Crisp.Runtime.Nodes;

/// <summary>
/// 条件ノード。ラムダ式を評価し、true なら Success、false なら Failure を返す。
/// check ノードの生成コードで使用される。
/// </summary>
public class ConditionNode : BtNode
{
    private readonly Func<bool> _condition;

    public ConditionNode(Func<bool> condition) => _condition = condition;

    public override BtStatus Tick(TickContext ctx) =>
        _condition() ? BtStatus.Success : BtStatus.Failure;
}

/// <summary>
/// アクションノード。ラムダ式を評価し、BtStatus をそのまま返す。
/// アクション呼び出しの生成コードで使用される。
/// </summary>
public class ActionNode : BtNode
{
    private readonly Func<BtStatus> _action;

    public ActionNode(Func<BtStatus> action) => _action = action;

    public override BtStatus Tick(TickContext ctx) => _action();
}
