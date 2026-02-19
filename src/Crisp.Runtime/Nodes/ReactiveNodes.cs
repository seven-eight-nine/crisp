namespace Crisp.Runtime.Nodes;

/// <summary>
/// リアクティブノード（F1: Reactive Reevaluation）。
/// 毎 tick 条件を再評価し、false になったら Running 中の子サブツリーを中断（Abort）する。
///
/// 従来の guard は Running の子を記憶して続行するが、reactive は毎 tick 条件を再評価し、
/// 条件が false になった時点で即座に子を中断する。
/// これにより「敵が消えたら攻撃を中断してパトロールに戻る」等のリアクティブな振る舞いを実現する。
///
/// セマンティクス:
/// <list type="number">
///   <item><description>condition を評価する</description></item>
///   <item><description>false → body 内の Running 中ノードに Abort() を呼び、Failure を返す</description></item>
///   <item><description>true → body を Tick し、結果をそのまま返す</description></item>
/// </list>
/// </summary>
public class ReactiveNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _body;

    /// <summary>body が前回 Running を返したかどうか。Abort が必要か判定するために追跡する。</summary>
    private bool _bodyRunning;

    /// <summary>
    /// 新しい <see cref="ReactiveNode"/> を生成する。
    /// </summary>
    /// <param name="condition">毎 tick 再評価される条件。false で子を中断する。</param>
    /// <param name="body">条件が true の間 tick される子ノード。</param>
    public ReactiveNode(Func<bool> condition, BtNode body)
    {
        _condition = condition;
        _body = body;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        // 毎 tick 条件を再評価
        if (!_condition())
        {
            // 条件が false → Running 中の body を中断
            if (_bodyRunning)
            {
                _body.Abort();
                _bodyRunning = false;
            }
            return BtStatus.Failure;
        }

        // 条件が true → body を tick
        var status = _body.Tick(ctx);
        _bodyRunning = status == BtStatus.Running;

        // body が完了（Success/Failure）したらリセット
        if (status != BtStatus.Running)
            _body.Reset();

        return status;
    }

    /// <summary>
    /// 外的要因による強制キャンセル。Running 中の body を Abort する。
    /// </summary>
    public override void Abort()
    {
        if (_bodyRunning)
            _body.Abort();
        _bodyRunning = false;
    }

    /// <summary>
    /// 正常終了後の状態初期化。body をリセットする。
    /// </summary>
    public override void Reset()
    {
        _body.Reset();
        _bodyRunning = false;
    }
}

/// <summary>
/// リアクティブセレクターノード（F1: Reactive Reevaluation）。
/// 毎 tick 先頭から子を再評価し、上位の子が Success/Running を返したら
/// 下位の Running 中の子を全て中断（Abort）する。
///
/// 通常の select との違い:
/// <list type="bullet">
///   <item><description>select: Running の子を記憶し、次 tick ではその子から再開する</description></item>
///   <item><description>reactive-select: 毎 tick 先頭から再評価。上位が復活したら下位を中断する</description></item>
/// </list>
///
/// Running子の追跡:
/// 複数の子が同時にRunning状態になり得る（例: parallel内のサブツリー等）ため、
/// 単一の lastRunningIndex ではなく Running集合を追跡する。
/// </summary>
public class ReactiveSelectorNode : BtNode
{
    private readonly BtNode[] _children;

    /// <summary>現在 Running 状態にある子のインデックス集合。</summary>
    private readonly HashSet<int> _runningIndices = new();

    /// <summary>
    /// 新しい <see cref="ReactiveSelectorNode"/> を生成する。
    /// </summary>
    /// <param name="children">優先度順の子ノード配列。先頭が最も優先度が高い。</param>
    public ReactiveSelectorNode(params BtNode[] children)
    {
        _children = children;
    }

    public override BtStatus Tick(TickContext ctx)
    {
        // 毎 tick 先頭から再評価
        for (int i = 0; i < _children.Length; i++)
        {
            var status = _children[i].Tick(ctx);

            if (status != BtStatus.Failure)
            {
                // 今回選ばれなかった Running 子を全て Abort
                foreach (var ri in _runningIndices)
                {
                    if (ri != i)
                        _children[ri].Abort();
                }
                _runningIndices.Clear();

                if (status == BtStatus.Running)
                    _runningIndices.Add(i);

                return status;
            }
        }

        // 全 Failure: 残っている Running を全て Abort
        foreach (var ri in _runningIndices)
            _children[ri].Abort();
        _runningIndices.Clear();

        return BtStatus.Failure;
    }

    /// <summary>
    /// 外的要因による強制キャンセル。全 Running 中の子を Abort する。
    /// </summary>
    public override void Abort()
    {
        foreach (var ri in _runningIndices)
            _children[ri].Abort();
        _runningIndices.Clear();
    }

    /// <summary>
    /// 正常終了後の状態初期化。全子をリセットする。
    /// </summary>
    public override void Reset()
    {
        foreach (var child in _children)
            child.Reset();
        _runningIndices.Clear();
    }
}
