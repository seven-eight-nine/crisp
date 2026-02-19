using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Runtime.Nodes;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;
using ParallelPolicy = Crisp.Syntax.Ast.ParallelPolicy;

namespace Crisp.Syntax.Tests;

/// <summary>
/// Evaluator（IR インタープリタ）のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>IR ノードを直接構築して評価結果を検証する</description></item>
///   <item><description>テスト用コンテキストクラスでメンバーアクセス・メソッド呼び出しを検証する</description></item>
///   <item><description>ソースからの end-to-end パイプライン（Parse → AST → IR → 評価）でも検証する</description></item>
///   <item><description>ステートフルノード（Timeout, Cooldown）は複数 tick で状態遷移を検証する</description></item>
/// </list>
/// </summary>
public class EvaluatorTests
{
    // ──────────────────────────────────────────────
    //  テスト用コンテキストクラス
    // ──────────────────────────────────────────────

    /// <summary>基本的なアクションとプロパティを持つテスト用エージェント。</summary>
    private class SimpleAgent
    {
        public int Health { get; set; } = 100;
        public bool IsAlive { get; set; } = true;
        public float Speed { get; set; } = 5.0f;
        public string Name { get; set; } = "Agent";

        public BtStatus Patrol() => BtStatus.Success;
        public BtStatus Flee() => BtStatus.Success;
        public BtStatus Attack() => BtStatus.Failure;
        public BtStatus Wait() => BtStatus.Running;
        public BtStatus Heal(int amount) { Health += amount; return BtStatus.Success; }

        public float DistanceTo(string target) => 15.0f;
        public int GetHealth() => Health;
    }

    /// <summary>呼び出し回数を記録するテスト用エージェント。</summary>
    private class CountingAgent
    {
        public int CallCount { get; private set; }

        public BtStatus CountAndSucceed()
        {
            CallCount++;
            return BtStatus.Success;
        }

        public BtStatus FailOnThird()
        {
            CallCount++;
            return CallCount >= 3 ? BtStatus.Failure : BtStatus.Success;
        }
    }

    /// <summary>メンバーチェーンのテスト用。ネストしたオブジェクトを持つ。</summary>
    private class AgentWithTarget
    {
        public TargetInfo Target { get; set; } = new();
        public BtStatus Patrol() => BtStatus.Success;
    }

    /// <summary>ターゲット情報。メンバーチェーンの途中オブジェクト。</summary>
    private class TargetInfo
    {
        public float Distance { get; set; } = 10.0f;
        public string Name { get; set; } = "Enemy";
    }

    /// <summary>Running を返すことができるテスト用エージェント。タイムアウトテスト用。</summary>
    private class WaitingAgent
    {
        public bool IsDone { get; set; }
        public BtStatus WaitUntilDone() => IsDone ? BtStatus.Success : BtStatus.Running;
        public BtStatus AlwaysSucceed() => BtStatus.Success;
    }

    /// <summary>フィールドアクセスのテスト用エージェント。</summary>
    private class AgentWithField
    {
        public int Ammo = 50;
        public BtStatus Patrol() => BtStatus.Success;
    }

    /// <summary>BtNode を返すサブツリーメソッドを持つテスト用エージェント。</summary>
    private class SubtreeAgent
    {
        public BtStatus Patrol() => BtStatus.Success;

        public BtNode BuildAlwaysSucceed()
            => new ActionNode(() => BtStatus.Success);

        public BtNode BuildAlwaysFail()
            => new ActionNode(() => BtStatus.Failure);
    }

    // ──────────────────────────────────────────────
    //  IR ノード構築ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>引数付きアクションの IR ノードを構築する。</summary>
    private static IrAction ActionWithArgs(string name, params IrExpr[] args)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               args.ToImmutableArray())
        { TypeRef = TypeRef.BtStatus };

    /// <summary>条件ノードを構築する。</summary>
    private static IrCondition Condition(IrExpr expr) => new(expr);

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = new TypeRef("unknown") };

    /// <summary>整数リテラルを構築する。</summary>
    private static IrLiteral IntLit(int value)
        => new(value, LiteralKind.Int) { TypeRef = TypeRef.Int };

    /// <summary>浮動小数点リテラルを構築する。</summary>
    private static IrLiteral FloatLit(float value)
        => new(value, LiteralKind.Float) { TypeRef = TypeRef.Float };

    /// <summary>真偽値リテラルを構築する。</summary>
    private static IrLiteral BoolLit(bool value)
        => new(value, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

    /// <summary>文字列リテラルを構築する。</summary>
    private static IrLiteral StringLit(string value)
        => new(value, LiteralKind.String) { TypeRef = TypeRef.String };

    /// <summary>null リテラルを構築する。</summary>
    private static IrLiteral NullLit()
        => new(null, LiteralKind.Null) { TypeRef = new TypeRef("null") };

    /// <summary>メソッド呼び出し式（式位置）を構築する。</summary>
    private static IrCall Call(string name, TypeRef returnType, params IrExpr[] args)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               args.ToImmutableArray())
        { TypeRef = returnType };

    // ═══════════════════════════════════════════════
    //  1. 最小のアクション評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void Successを返すアクションが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(Action("Patrol"));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Failureを返すアクションが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(Action("Attack"));

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void Runningを返すアクションが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(Action("Wait"));

        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void 引数付きアクションが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(ActionWithArgs("Heal", IntLit(20)));

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(120, agent.Health);
    }

    // ═══════════════════════════════════════════════
    //  2. 条件の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void 条件がtrueの場合Successを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(Condition(BoolLit(true)));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 条件がfalseの場合Failureを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        var result = eval.Tick(Condition(BoolLit(false)));

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void メンバーアクセスによる条件が評価される()
    {
        var agent = new SimpleAgent { IsAlive = true };
        var eval = new Evaluator(agent);

        var result = eval.Tick(Condition(Member("IsAlive")));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 比較式による条件が評価される()
    {
        var agent = new SimpleAgent { Health = 20 };
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Lt, Member("Health"), IntLit(30))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  3. セレクターの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void セレクターは最初の成功した子を返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("Patrol"), Action("Attack")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void セレクターは全子失敗でFailureを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("Attack"), Action("Attack")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void セレクターはRunningを返す子で停止する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("Attack"), Action("Wait"), Action("Patrol")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Running, result);
    }

    // ═══════════════════════════════════════════════
    //  4. シーケンスの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void シーケンスは全子成功でSuccessを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSequence(ImmutableArray.Create<IrNode>(
            Action("Patrol"), Action("Flee")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void シーケンスは最初の失敗で停止する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSequence(ImmutableArray.Create<IrNode>(
            Action("Attack"), Action("Patrol")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void シーケンスはRunningを返す子で停止する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrSequence(ImmutableArray.Create<IrNode>(
            Action("Patrol"), Action("Wait"), Action("Attack")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Running, result);
    }

    // ═══════════════════════════════════════════════
    //  5. 反転の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void 反転はSuccessをFailureにする()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrInvert(Action("Patrol"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void 反転はFailureをSuccessにする()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrInvert(Action("Attack"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 反転はRunningをそのまま通す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrInvert(Action("Wait"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Running, result);
    }

    // ═══════════════════════════════════════════════
    //  6. ガードの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void ガードは条件trueでボディを評価する()
    {
        var agent = new SimpleAgent { IsAlive = true };
        var eval = new Evaluator(agent);
        var node = new IrGuard(Member("IsAlive"), Action("Patrol"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void ガードは条件falseでFailureを返す()
    {
        var agent = new SimpleAgent { IsAlive = false };
        var eval = new Evaluator(agent);
        var node = new IrGuard(Member("IsAlive"), Action("Patrol"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    // ═══════════════════════════════════════════════
    //  7. If の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void Ifはtrue分岐を評価する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrIf(BoolLit(true), Action("Patrol"), Action("Attack"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Ifはfalse分岐を評価する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrIf(BoolLit(false), Action("Patrol"), Action("Attack"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void IfはElseなしでfalseの場合Failureを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrIf(BoolLit(false), Action("Patrol"), null);

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    // ═══════════════════════════════════════════════
    //  8. リピートの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void リピートは指定回数子を実行する()
    {
        var agent = new CountingAgent();
        var eval = new Evaluator(agent);
        var node = new IrRepeat(3, Action("CountAndSucceed"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(3, agent.CallCount);
    }

    [Fact]
    public void リピートは子がFailureで中断する()
    {
        var agent = new CountingAgent();
        var eval = new Evaluator(agent);
        // FailOnThird は3回目で Failure を返す
        var node = new IrRepeat(5, Action("FailOnThird"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(3, agent.CallCount);
    }

    // ═══════════════════════════════════════════════
    //  9. パラレルの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void パラレルAnyは一つ成功でSuccessを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrParallel(
            new ParallelPolicy.Any(),
            ImmutableArray.Create<IrNode>(
                Action("Attack"), Action("Patrol"), Action("Attack")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void パラレルAllは全成功でSuccessを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrParallel(
            new ParallelPolicy.All(),
            ImmutableArray.Create<IrNode>(
                Action("Patrol"), Action("Flee")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void パラレルAllは一つ失敗でFailureを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrParallel(
            new ParallelPolicy.All(),
            ImmutableArray.Create<IrNode>(
                Action("Patrol"), Action("Attack"), Action("Flee")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void パラレルAllはRunningがあればRunningを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrParallel(
            new ParallelPolicy.All(),
            ImmutableArray.Create<IrNode>(
                Action("Patrol"), Action("Wait")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void パラレルNはN個成功でSuccessを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var node = new IrParallel(
            new ParallelPolicy.N(2),
            ImmutableArray.Create<IrNode>(
                Action("Patrol"), Action("Attack"), Action("Flee")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void パラレルNはN個到達不可能でFailureを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // 3個中2個失敗 → 残り1個しかないが N=2 必要 → 不可能
        var node = new IrParallel(
            new ParallelPolicy.N(2),
            ImmutableArray.Create<IrNode>(
                Action("Patrol"), Action("Attack"), Action("Attack")));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    // ═══════════════════════════════════════════════
    //  10. While の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void Whileは条件falseでSuccessを返す()
    {
        var agent = new SimpleAgent { IsAlive = false };
        var eval = new Evaluator(agent);
        var node = new IrWhile(Member("IsAlive"), Action("Patrol"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Whileは条件trueでボディを評価する()
    {
        var agent = new SimpleAgent { IsAlive = true };
        var eval = new Evaluator(agent);
        var node = new IrWhile(Member("IsAlive"), Action("Patrol"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  11. メンバーアクセスの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void プロパティの値を読み取れる()
    {
        var agent = new SimpleAgent { Health = 42 };
        var eval = new Evaluator(agent);
        // (check (= .Health 42))
        var expr = new IrBinaryOp(BinaryOp.Eq, Member("Health"), IntLit(42))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void フィールドの値を読み取れる()
    {
        var agent = new AgentWithField { Ammo = 30 };
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Eq, Member("Ammo"), IntLit(30))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void メンバーチェーンで値を読み取れる()
    {
        var agent = new AgentWithTarget { Target = new TargetInfo { Distance = 5.0f } };
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Lt, Member("Target", "Distance"), FloatLit(10.0f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  12. リテラルの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数リテラルが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // (check (= 42 42))
        var expr = new IrBinaryOp(BinaryOp.Eq, IntLit(42), IntLit(42))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 浮動小数点リテラルが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Eq, FloatLit(3.14f), FloatLit(3.14f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 文字列リテラルが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Eq, StringLit("hello"), StringLit("hello"))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void nullリテラルが正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Eq, NullLit(), NullLit())
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  13. 二項演算の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数の加算が正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // (check (= (+ 10 20) 30))
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(10), IntLit(20))
            { TypeRef = TypeRef.Int };
        var expr = new IrBinaryOp(BinaryOp.Eq, add, IntLit(30))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 整数の減算が正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var sub = new IrBinaryOp(BinaryOp.Sub, IntLit(50), IntLit(20))
            { TypeRef = TypeRef.Int };
        var expr = new IrBinaryOp(BinaryOp.Eq, sub, IntLit(30))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 浮動小数点の乗算が正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var mul = new IrBinaryOp(BinaryOp.Mul, FloatLit(2.5f), FloatLit(4.0f))
            { TypeRef = TypeRef.Float };
        var expr = new IrBinaryOp(BinaryOp.Eq, mul, FloatLit(10.0f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 整数の除算が正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var div = new IrBinaryOp(BinaryOp.Div, IntLit(10), IntLit(3))
            { TypeRef = TypeRef.Int };
        // 整数除算: 10 / 3 = 3
        var expr = new IrBinaryOp(BinaryOp.Eq, div, IntLit(3))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 剰余演算が正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var mod = new IrBinaryOp(BinaryOp.Mod, IntLit(10), IntLit(3))
            { TypeRef = TypeRef.Int };
        // 10 % 3 = 1
        var expr = new IrBinaryOp(BinaryOp.Eq, mod, IntLit(1))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 比較演算Ltが正しく計算される()
    {
        var agent = new SimpleAgent { Health = 20 };
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Lt, Member("Health"), IntLit(30))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 比較演算Geが正しく計算される()
    {
        var agent = new SimpleAgent { Health = 100 };
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Ge, Member("Health"), IntLit(50))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 不等価演算Neが正しく計算される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrBinaryOp(BinaryOp.Ne, IntLit(1), IntLit(2))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  14. 単項演算の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void Not演算がtrueをfalseにする()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrUnaryOp(UnaryOp.Not, BoolLit(true))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void Not演算がfalseをtrueにする()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrUnaryOp(UnaryOp.Not, BoolLit(false))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Negate演算が整数を反転する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // (check (= (- 42) -42))
        var neg = new IrUnaryOp(UnaryOp.Negate, IntLit(42))
            { TypeRef = TypeRef.Int };
        var expr = new IrBinaryOp(BinaryOp.Eq, neg, IntLit(-42))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Negate演算が浮動小数点を反転する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var neg = new IrUnaryOp(UnaryOp.Negate, FloatLit(3.14f))
            { TypeRef = TypeRef.Float };
        var expr = new IrBinaryOp(BinaryOp.Eq, neg, FloatLit(-3.14f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  15. 論理演算の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void And演算は全てtrueでtrueを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrLogicOp(
            LogicOp.And,
            ImmutableArray.Create<IrExpr>(BoolLit(true), BoolLit(true)))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void And演算はいずれかfalseでfalseを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrLogicOp(
            LogicOp.And,
            ImmutableArray.Create<IrExpr>(BoolLit(true), BoolLit(false), BoolLit(true)))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void Or演算はいずれかtrueでtrueを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrLogicOp(
            LogicOp.Or,
            ImmutableArray.Create<IrExpr>(BoolLit(false), BoolLit(true)))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Or演算は全てfalseでfalseを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var expr = new IrLogicOp(
            LogicOp.Or,
            ImmutableArray.Create<IrExpr>(BoolLit(false), BoolLit(false)))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Failure, result);
    }

    // ═══════════════════════════════════════════════
    //  16. メソッド呼び出し式の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void メソッド呼び出し式が正しく評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // (check (> (.DistanceTo "target") 10.0))
        var call = Call("DistanceTo", TypeRef.Float, StringLit("target"));
        var expr = new IrBinaryOp(BinaryOp.Gt, call, FloatLit(10.0f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        // DistanceTo returns 15.0, 15.0 > 10.0 = true
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 引数なしメソッド呼び出し式が正しく評価される()
    {
        var agent = new SimpleAgent { Health = 75 };
        var eval = new Evaluator(agent);
        // (check (= (.GetHealth) 75))
        var call = Call("GetHealth", TypeRef.Int);
        var expr = new IrBinaryOp(BinaryOp.Eq, call, IntLit(75))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  17. 型変換の評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void IntからFloatへの型変換が正しく行われる()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        // (check (= (float 42) 42.0))
        var conv = new IrConvert(IntLit(42), TypeRef.Float)
            { TypeRef = TypeRef.Float };
        var expr = new IrBinaryOp(BinaryOp.Eq, conv, FloatLit(42.0f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void メンバーのIntからFloatへの変換で比較が正しく動作する()
    {
        var agent = new SimpleAgent { Health = 80 };
        var eval = new Evaluator(agent);
        // (check (< (float .Health) 100.0))
        var conv = new IrConvert(Member("Health"), TypeRef.Float)
            { TypeRef = TypeRef.Float };
        var expr = new IrBinaryOp(BinaryOp.Lt, conv, FloatLit(100.0f))
            { TypeRef = TypeRef.Bool };

        var result = eval.Tick(Condition(expr));

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  18. タイムアウトの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void タイムアウト内に完了する場合子の結果を返す()
    {
        var agent = new WaitingAgent { IsDone = true };
        var eval = new Evaluator(agent);
        var node = new IrTimeout(5.0f, Action("WaitUntilDone"));

        var result = eval.Tick(node, new TickContext(1.0f));

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void タイムアウト超過でFailureを返す()
    {
        var agent = new WaitingAgent { IsDone = false };
        var eval = new Evaluator(agent);
        var node = new IrTimeout(5.0f, Action("WaitUntilDone"));

        // Tick 1: DeltaTime=2.0, accumulated=2.0 → Running
        var result1 = eval.Tick(node, new TickContext(2.0f));
        Assert.Equal(BtStatus.Running, result1);

        // Tick 2: DeltaTime=2.0, accumulated=4.0 → Running
        var result2 = eval.Tick(node, new TickContext(2.0f));
        Assert.Equal(BtStatus.Running, result2);

        // Tick 3: DeltaTime=2.0, accumulated=6.0 → Failure (timeout exceeded)
        var result3 = eval.Tick(node, new TickContext(2.0f));
        Assert.Equal(BtStatus.Failure, result3);
    }

    [Fact]
    public void タイムアウト中に子が完了したらタイマーリセット()
    {
        var agent = new WaitingAgent { IsDone = false };
        var eval = new Evaluator(agent);
        var node = new IrTimeout(5.0f, Action("WaitUntilDone"));

        // Tick 1: Running
        eval.Tick(node, new TickContext(2.0f));

        // Tick 2: 子が完了 → タイマーリセット
        agent.IsDone = true;
        var result = eval.Tick(node, new TickContext(2.0f));
        Assert.Equal(BtStatus.Success, result);

        // Tick 3: 再び Running → タイマーは 0 からスタート
        agent.IsDone = false;
        var result3 = eval.Tick(node, new TickContext(2.0f));
        Assert.Equal(BtStatus.Running, result3);
    }

    // ═══════════════════════════════════════════════
    //  19. クールダウンの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void クールダウン中はFailureを返す()
    {
        var agent = new WaitingAgent();
        var eval = new Evaluator(agent);
        var node = new IrCooldown(3.0f, Action("AlwaysSucceed"));

        // Tick 1: 子が Success → クールダウン開始
        var result1 = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Success, result1);

        // Tick 2: クールダウン中（残り 2.0）→ Failure
        var result2 = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Failure, result2);

        // Tick 3: クールダウン中（残り 1.0）→ Failure
        var result3 = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Failure, result3);
    }

    [Fact]
    public void クールダウン終了後は子を再評価する()
    {
        var agent = new WaitingAgent();
        var eval = new Evaluator(agent);
        var node = new IrCooldown(2.0f, Action("AlwaysSucceed"));

        // Tick 1: Success → クールダウン開始
        eval.Tick(node, new TickContext(1.0f));

        // Tick 2: クールダウン中（残り 1.0）→ Failure
        var result2 = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Failure, result2);

        // Tick 3: クールダウン終了（残り 0.0）→ 子を再評価 → Success
        var result3 = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Success, result3);
    }

    // ═══════════════════════════════════════════════
    //  20. Reset の検証
    // ═══════════════════════════════════════════════

    [Fact]
    public void Resetで内部状態がクリアされる()
    {
        var agent = new WaitingAgent();
        var eval = new Evaluator(agent);
        var node = new IrCooldown(10.0f, Action("AlwaysSucceed"));

        // Success → クールダウン開始
        eval.Tick(node, new TickContext(1.0f));

        // クールダウン中
        var result = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Failure, result);

        // Reset でクリア
        eval.Reset();

        // クールダウンがクリアされたので再び Success
        var resultAfterReset = eval.Tick(node, new TickContext(1.0f));
        Assert.Equal(BtStatus.Success, resultAfterReset);
    }

    // ═══════════════════════════════════════════════
    //  21. ツリーの評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void ツリーのボディが順に評価される()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var tree = new IrTree("Test", ImmutableArray.Create<IrNode>(Action("Patrol")));

        var result = eval.Tick(tree, default);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void 空のツリーはSuccessを返す()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);
        var tree = new IrTree("Empty", ImmutableArray<IrNode>.Empty);

        var result = eval.Tick(tree, default);

        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════
    //  22. エラーケース
    // ═══════════════════════════════════════════════

    [Fact]
    public void 存在しないメソッドの呼び出しでMissingMethodExceptionが発生する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        Assert.Throws<MissingMethodException>(() =>
            eval.Tick(Action("NonExistentMethod")));
    }

    [Fact]
    public void 存在しないメンバーの読み取りでMissingMemberExceptionが発生する()
    {
        var agent = new SimpleAgent();
        var eval = new Evaluator(agent);

        Assert.Throws<MissingMemberException>(() =>
            eval.Tick(Condition(Member("NonExistentMember"))));
    }

    [Fact]
    public void nullコンテキストでArgumentNullExceptionが発生する()
    {
        Assert.Throws<ArgumentNullException>(() => new Evaluator(null!));
    }

    // ═══════════════════════════════════════════════
    //  23. End-to-End: ソースから IR 経由で評価
    // ═══════════════════════════════════════════════

    /// <summary>End-to-End 用テストコンテキスト。Pattern1 Minimal に対応する。</summary>
    private class CombatAgent
    {
        public int Health { get; set; } = 100;

        public BtStatus Flee() => BtStatus.Success;
        public BtStatus Patrol() => BtStatus.Success;
    }

    /// <summary>ソースを Parse → AST Lowering → IR Lowering して IrTree 配列を返す。</summary>
    private static System.Collections.Immutable.ImmutableArray<IrTree> CompileToIr(string source)
    {
        var (program, _) = Parser.Parse(source);
        var astTrees = CstToAstLowering.Lower(program);
        return AstToIrLowering.Lower(astTrees);
    }

    [Fact]
    public void Pattern1_Minimalが正しく評価される_HealthLow()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;
        var irTrees = CompileToIr(source);
        var agent = new CombatAgent { Health = 20 };
        var eval = new Evaluator(agent);

        // Health=20 < 30 → check Success → Flee Success → seq Success → select Success
        var result = eval.Tick(irTrees[0], default);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Pattern1_Minimalが正しく評価される_HealthHigh()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;
        var irTrees = CompileToIr(source);
        var agent = new CombatAgent { Health = 80 };
        var eval = new Evaluator(agent);

        // Health=80 >= 30 → check Failure → seq Failure → Patrol Success → select Success
        var result = eval.Tick(irTrees[0], default);

        Assert.Equal(BtStatus.Success, result);
    }

    // =================================================================
    //  サブツリー埋め込み（BtNode 戻りメソッド）
    // =================================================================

    [Fact]
    public void BtNodeを返すメソッドのサブツリーがSuccessを返す()
    {
        var agent = new SubtreeAgent();
        var eval = new Evaluator(agent);

        // BuildAlwaysSucceed() は ActionNode(() => Success) を返す
        var node = Action("BuildAlwaysSucceed");
        var result = eval.Tick(node, default);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void BtNodeを返すメソッドのサブツリーがFailureを返す()
    {
        var agent = new SubtreeAgent();
        var eval = new Evaluator(agent);

        // BuildAlwaysFail() は ActionNode(() => Failure) を返す
        var node = Action("BuildAlwaysFail");
        var result = eval.Tick(node, default);

        Assert.Equal(BtStatus.Failure, result);
    }
}
