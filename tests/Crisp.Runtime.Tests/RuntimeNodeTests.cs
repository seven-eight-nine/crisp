using Crisp.Runtime;
using Crisp.Runtime.Nodes;

namespace Crisp.Runtime.Tests;

/// <summary>
/// ランタイムノードのユニットテスト。
/// t-wada 式: 各ノードの基本動作を最小のケースで検証し、段階的に複雑なケースを追加する。
/// </summary>
public class RuntimeNodeTests
{
    private static readonly TickContext Ctx = new(DeltaTime: 0.016f);

    // ──────────────────────────────────────────────
    //  テストヘルパー: 指定した BtStatus を返すスタブノード
    // ──────────────────────────────────────────────

    private static ActionNode StubNode(BtStatus status) => new(() => status);

    /// <summary>Reset 呼び出し回数を追跡するノード。</summary>
    private class TrackingNode : BtNode
    {
        private readonly BtStatus _status;
        public int ResetCount { get; private set; }

        public TrackingNode(BtStatus status) => _status = status;
        public override BtStatus Tick(TickContext ctx) => _status;
        public override void Reset() => ResetCount++;
    }

    // ═══════════════════════════════════════════════
    //  1. ConditionNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void ConditionNode_trueでSuccess()
    {
        var node = new ConditionNode(() => true);
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void ConditionNode_falseでFailure()
    {
        var node = new ConditionNode(() => false);
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  2. ActionNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void ActionNode_戻り値をそのまま返す()
    {
        Assert.Equal(BtStatus.Running, new ActionNode(() => BtStatus.Running).Tick(Ctx));
        Assert.Equal(BtStatus.Success, new ActionNode(() => BtStatus.Success).Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  3. SelectorNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Selector_最初のSuccessで停止する()
    {
        var node = new SelectorNode(StubNode(BtStatus.Failure), StubNode(BtStatus.Success), StubNode(BtStatus.Failure));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void Selector_全Failureなら全体Failure()
    {
        var node = new SelectorNode(StubNode(BtStatus.Failure), StubNode(BtStatus.Failure));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    [Fact]
    public void Selector_Runningで中断し次tickで再開する()
    {
        var callCount = 0;
        var runOnce = new ActionNode(() =>
        {
            callCount++;
            return callCount == 1 ? BtStatus.Running : BtStatus.Success;
        });

        var node = new SelectorNode(StubNode(BtStatus.Failure), runOnce);

        Assert.Equal(BtStatus.Running, node.Tick(Ctx));
        // 2回目のtickでは Running を返した子から再開
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
        Assert.Equal(2, callCount);
    }

    // ═══════════════════════════════════════════════
    //  4. SequenceNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Sequence_全Successなら全体Success()
    {
        var node = new SequenceNode(StubNode(BtStatus.Success), StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void Sequence_Failureで即停止()
    {
        var node = new SequenceNode(StubNode(BtStatus.Success), StubNode(BtStatus.Failure), StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  5. InvertNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Invert_SuccessとFailureが反転される()
    {
        Assert.Equal(BtStatus.Failure, new InvertNode(StubNode(BtStatus.Success)).Tick(Ctx));
        Assert.Equal(BtStatus.Success, new InvertNode(StubNode(BtStatus.Failure)).Tick(Ctx));
    }

    [Fact]
    public void Invert_Runningはそのまま()
    {
        Assert.Equal(BtStatus.Running, new InvertNode(StubNode(BtStatus.Running)).Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  6. RepeatNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Repeat_指定回数成功したらSuccess()
    {
        var node = new RepeatNode(3, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void Repeat_子がFailureなら即Failure()
    {
        var node = new RepeatNode(3, StubNode(BtStatus.Failure));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  7. GuardNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Guard_条件trueなら子を評価する()
    {
        var node = new GuardNode(() => true, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void Guard_条件falseならFailure()
    {
        var node = new GuardNode(() => false, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  8. IfNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void If_条件trueならThen()
    {
        var node = new IfNode(() => true, StubNode(BtStatus.Success), StubNode(BtStatus.Failure));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void If_条件falseならElse()
    {
        var node = new IfNode(() => false, StubNode(BtStatus.Success), StubNode(BtStatus.Failure));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    [Fact]
    public void If_ElseなしでfalseならFailure()
    {
        var node = new IfNode(() => false, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }

    // ═══════════════════════════════════════════════
    //  9. TimeoutNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Timeout_時間内なら子を評価する()
    {
        var node = new TimeoutNode(1.0f, StubNode(BtStatus.Running));
        Assert.Equal(BtStatus.Running, node.Tick(new TickContext(0.5f)));
    }

    [Fact]
    public void Timeout_時間超過でFailure()
    {
        var node = new TimeoutNode(1.0f, StubNode(BtStatus.Running));
        Assert.Equal(BtStatus.Failure, node.Tick(new TickContext(1.5f)));
    }

    // ═══════════════════════════════════════════════
    //  10. CooldownNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void Cooldown_初回はそのまま子を評価する()
    {
        var node = new CooldownNode(1.0f, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Success, node.Tick(Ctx));
    }

    [Fact]
    public void Cooldown_Success後はクールダウン中Failure()
    {
        var node = new CooldownNode(1.0f, StubNode(BtStatus.Success));
        node.Tick(new TickContext(0.016f)); // Success → クールダウン開始
        Assert.Equal(BtStatus.Failure, node.Tick(new TickContext(0.5f))); // まだクールダウン中
    }

    // ═══════════════════════════════════════════════
    //  11. WhileNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void While_条件trueなら子を評価する()
    {
        var node = new WhileNode(() => true, StubNode(BtStatus.Running));
        Assert.Equal(BtStatus.Running, node.Tick(Ctx));
    }

    [Fact]
    public void While_条件falseならFailure()
    {
        var node = new WhileNode(() => false, StubNode(BtStatus.Success));
        Assert.Equal(BtStatus.Failure, node.Tick(Ctx));
    }
}

/// <summary>
/// U4 決定事項に基づく ParallelNode の Reset セマンティクステスト。
/// Parallel の確定時（Success / Failure）に全子がリセットされることを検証する。
/// </summary>
public class ParallelResetSemanticTests
{
    private static readonly TickContext Ctx = new(DeltaTime: 0.016f);

    /// <summary>Reset 呼び出し回数を追跡するノード。</summary>
    private class TrackingNode : BtNode
    {
        private readonly BtStatus _status;
        public int ResetCount { get; private set; }

        public TrackingNode(BtStatus status) => _status = status;
        public override BtStatus Tick(TickContext ctx) => _status;
        public override void Reset() => ResetCount++;
    }

    // ═══════════════════════════════════════════════
    //  :any ポリシー
    // ═══════════════════════════════════════════════

    [Fact]
    public void Any_RunningとSuccess_全体Successで全子Reset()
    {
        var running = new TrackingNode(BtStatus.Running);
        var success = new TrackingNode(BtStatus.Success);
        var node = new ParallelNode(new ParallelPolicy.Any(), running, success);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(1, running.ResetCount); // Running 子もリセットされる
        Assert.Equal(1, success.ResetCount);
    }

    [Fact]
    public void Any_RunningとFailure_全体Runningで子Resetなし()
    {
        var running = new TrackingNode(BtStatus.Running);
        var failure = new TrackingNode(BtStatus.Failure);
        var node = new ParallelNode(new ParallelPolicy.Any(), running, failure);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Running, result);
        Assert.Equal(0, running.ResetCount);
        Assert.Equal(0, failure.ResetCount);
    }

    [Fact]
    public void Any_FailureとFailure_全体Failureで全子Reset()
    {
        var f1 = new TrackingNode(BtStatus.Failure);
        var f2 = new TrackingNode(BtStatus.Failure);
        var node = new ParallelNode(new ParallelPolicy.Any(), f1, f2);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(1, f1.ResetCount);
        Assert.Equal(1, f2.ResetCount);
    }

    // ═══════════════════════════════════════════════
    //  :all ポリシー
    // ═══════════════════════════════════════════════

    [Fact]
    public void All_RunningとSuccess_全体Running()
    {
        var running = new TrackingNode(BtStatus.Running);
        var success = new TrackingNode(BtStatus.Success);
        var node = new ParallelNode(new ParallelPolicy.All(), running, success);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Running, result);
        Assert.Equal(0, running.ResetCount);
    }

    [Fact]
    public void All_RunningとFailure_全体Failureで全子Reset()
    {
        var running = new TrackingNode(BtStatus.Running);
        var failure = new TrackingNode(BtStatus.Failure);
        var node = new ParallelNode(new ParallelPolicy.All(), running, failure);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(1, running.ResetCount);
        Assert.Equal(1, failure.ResetCount);
    }

    [Fact]
    public void All_SuccessとSuccess_全体Successで全子Reset()
    {
        var s1 = new TrackingNode(BtStatus.Success);
        var s2 = new TrackingNode(BtStatus.Success);
        var node = new ParallelNode(new ParallelPolicy.All(), s1, s2);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(1, s1.ResetCount);
        Assert.Equal(1, s2.ResetCount);
    }

    // ═══════════════════════════════════════════════
    //  :n ポリシー
    // ═══════════════════════════════════════════════

    [Fact]
    public void N_2個Success_全体Successで全子Reset()
    {
        var s1 = new TrackingNode(BtStatus.Success);
        var s2 = new TrackingNode(BtStatus.Success);
        var running = new TrackingNode(BtStatus.Running);
        var node = new ParallelNode(new ParallelPolicy.N(2), s1, s2, running);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(1, s1.ResetCount);
        Assert.Equal(1, s2.ResetCount);
        Assert.Equal(1, running.ResetCount);
    }

    [Fact]
    public void N_Failure数が超過_全体Failureで全子Reset()
    {
        var f1 = new TrackingNode(BtStatus.Failure);
        var f2 = new TrackingNode(BtStatus.Failure);
        var running = new TrackingNode(BtStatus.Running);
        var node = new ParallelNode(new ParallelPolicy.N(2), f1, f2, running);

        // 3子中2個Failure → 残り1つが成功しても2には届かない → Failure
        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(1, f1.ResetCount);
    }

    // ═══════════════════════════════════════════════
    //  再突入テスト
    // ═══════════════════════════════════════════════

    [Fact]
    public void Parallel確定後の再突入で前回の状態が漏れない()
    {
        var tickCount = 0;
        // 1回目は Running、2回目以降は Success を返すノード
        var changingNode = new ActionNode(() =>
        {
            tickCount++;
            return tickCount == 1 ? BtStatus.Running : BtStatus.Success;
        });

        var quickCheck = new ActionNode(() => BtStatus.Success);
        var parallel = new ParallelNode(new ParallelPolicy.Any(), changingNode, quickCheck);

        // Tick #1: quickCheck=Success → :any で全体 Success → 全子 Reset
        var result1 = parallel.Tick(Ctx);
        Assert.Equal(BtStatus.Success, result1);

        // Tick #2（再突入）: changingNode は Reset 済みなので新たに評価
        // tickCount は外部変数なのでリセットされないが、ノード内部状態はリセット済み
        var result2 = parallel.Tick(Ctx);
        Assert.Equal(BtStatus.Success, result2);
    }
}
