namespace Crisp.Runtime.Nodes;

/// <summary>
/// ガードノード。条件が true なら子を評価、false なら Failure を返す。
/// </summary>
public class GuardNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _child;

    public GuardNode(Func<bool> condition, BtNode child)
    {
        _condition = condition;
        _child = child;
    }

    public override BtStatus Tick(TickContext ctx) =>
        _condition() ? _child.Tick(ctx) : BtStatus.Failure;

    public override void Reset() => _child.Reset();
}

/// <summary>
/// if ノード。条件が true なら Then を、false なら Else（または Failure）を返す。
/// </summary>
public class IfNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _then;
    private readonly BtNode? _else;

    public IfNode(Func<bool> condition, BtNode then, BtNode? @else = null)
    {
        _condition = condition;
        _then = then;
        _else = @else;
    }

    public override BtStatus Tick(TickContext ctx) =>
        _condition() ? _then.Tick(ctx) : (_else?.Tick(ctx) ?? BtStatus.Failure);

    public override void Reset()
    {
        _then.Reset();
        _else?.Reset();
    }
}

/// <summary>
/// 反転ノード。Success ↔ Failure を反転する。Running はそのまま。
/// </summary>
public class InvertNode : BtNode
{
    private readonly BtNode _child;

    public InvertNode(BtNode child) => _child = child;

    public override BtStatus Tick(TickContext ctx) => _child.Tick(ctx) switch
    {
        BtStatus.Success => BtStatus.Failure,
        BtStatus.Failure => BtStatus.Success,
        var s => s, // Running はそのまま
    };

    public override void Reset() => _child.Reset();
}

/// <summary>
/// リピートノード。子ノードを指定回数繰り返し評価する。
/// 子が Failure を返した場合は即座に Failure を返し、カウンタをリセットする。
/// 子が Running を返した場合はそのまま Running を返す（次 tick で続行）。
/// </summary>
public class RepeatNode : BtNode
{
    private readonly int _count;
    private readonly BtNode _child;
    private int _current;

    public RepeatNode(int count, BtNode child)
    {
        _count = count;
        _child = child;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        while (_current < _count)
        {
            var status = _child.Tick(ctx);
            if (status == BtStatus.Running) return BtStatus.Running;
            if (status == BtStatus.Failure) { Reset(); return BtStatus.Failure; }
            _current++;
        }
        Reset();
        return BtStatus.Success;
    }

    public override void Reset()
    {
        _current = 0;
        _child.Reset();
    }
}

/// <summary>
/// タイムアウトノード。指定秒数以内に子が完了しなければ Failure を返す。
/// U1 決定事項: DeltaTime は <see cref="TickContext"/> から取得する。
/// </summary>
public class TimeoutNode : BtNode
{
    private readonly float _seconds;
    private readonly BtNode _child;
    private float _elapsed;

    public TimeoutNode(float seconds, BtNode child)
    {
        _seconds = seconds;
        _child = child;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        _elapsed += ctx.DeltaTime;
        if (_elapsed >= _seconds)
        {
            Reset();
            return BtStatus.Failure;
        }
        var status = _child.Tick(ctx);
        if (status != BtStatus.Running)
            Reset();
        return status;
    }

    public override void Reset()
    {
        _elapsed = 0;
        _child.Reset();
    }
}

/// <summary>
/// クールダウンノード。子が Success を返した後、指定秒数が経過するまで Failure を返す。
/// U1 決定事項: DeltaTime は <see cref="TickContext"/> から取得する。
/// </summary>
public class CooldownNode : BtNode
{
    private readonly float _seconds;
    private readonly BtNode _child;
    private float _remaining;

    public CooldownNode(float seconds, BtNode child)
    {
        _seconds = seconds;
        _child = child;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        _remaining -= ctx.DeltaTime;
        if (_remaining > 0) return BtStatus.Failure;
        var status = _child.Tick(ctx);
        if (status == BtStatus.Success) _remaining = _seconds;
        return status;
    }

    public override void Reset()
    {
        _remaining = 0;
        _child.Reset();
    }
}

/// <summary>
/// while ノード。条件が true の間、body を毎 tick 評価する。
/// 条件が false なら Failure を返す。
/// </summary>
public class WhileNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _body;

    public WhileNode(Func<bool> condition, BtNode body)
    {
        _condition = condition;
        _body = body;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        if (!_condition()) return BtStatus.Failure;
        return _body.Tick(ctx);
    }

    public override void Reset() => _body.Reset();
}
