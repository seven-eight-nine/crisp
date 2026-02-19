using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Runtime.Nodes;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F1: Reactive Reevaluation のユニットテスト。
/// t-wada 式 TDD に基づき、最小のケースから段階的に複雑さを増す。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>Lexer: "reactive" / "reactive-select" が Identifier トークンとして認識される</description></item>
///   <item><description>Parser: CstReactive / CstReactiveSelect への変換</description></item>
///   <item><description>CstToAstLowering: AstReactive / AstReactiveSelect への変換</description></item>
///   <item><description>AstToIrLowering: IrReactive / IrReactiveSelect への変換</description></item>
///   <item><description>CSharpEmitter: ReactiveNode / ReactiveSelectorNode の構築式</description></item>
///   <item><description>IrSerializer/Deserializer: ラウンドトリップ</description></item>
///   <item><description>Evaluator: IR インタープリタでの評価</description></item>
///   <item><description>Runtime: ReactiveNode / ReactiveSelectorNode の実行時動作</description></item>
/// </list>
/// </summary>
public class ReactiveTests
{
    // ──────────────────────────────────────────────
    //  テスト用コンテキストクラス
    // ──────────────────────────────────────────────

    /// <summary>条件とアクションを持つテスト用エージェント。</summary>
    private class ReactiveAgent
    {
        public bool IsReady { get; set; } = true;
        public bool IsAlive { get; set; } = true;
        public bool HasEnemy { get; set; } = true;
        public int CallCount { get; private set; }

        public BtStatus Attack()
        {
            CallCount++;
            return BtStatus.Success;
        }

        public BtStatus Patrol() => BtStatus.Success;
        public BtStatus Flee() => BtStatus.Running;
        public BtStatus Wait() => BtStatus.Running;
    }

    // ──────────────────────────────────────────────
    //  IR ノード構築ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = TypeRef.Bool };

    /// <summary>真偽値リテラルを構築する。</summary>
    private static IrLiteral BoolLit(bool value)
        => new(value, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

    // ──────────────────────────────────────────────
    //  Runtime テスト用ヘルパー
    // ──────────────────────────────────────────────

    private static readonly TickContext Ctx = new(DeltaTime: 0.016f);

    /// <summary>指定した BtStatus を返すスタブノード。</summary>
    private static ActionNode StubNode(BtStatus status) => new(() => status);

    /// <summary>Abort 呼び出し回数を追跡するノード。</summary>
    private class AbortTrackingNode : BtNode
    {
        private readonly Func<BtStatus> _tick;
        public int AbortCount { get; private set; }
        public int ResetCount { get; private set; }
        public int TickCount { get; private set; }

        public AbortTrackingNode(BtStatus status) : this(() => status) { }
        public AbortTrackingNode(Func<BtStatus> tick) => _tick = tick;

        public override BtStatus Tick(TickContext ctx)
        {
            TickCount++;
            return _tick();
        }

        public override void Abort()
        {
            AbortCount++;
        }

        public override void Reset()
        {
            ResetCount++;
        }
    }

    // ═══════════════════════════════════════════════
    //  1. Lexer: "reactive" / "reactive-select" のトークン化
    // ═══════════════════════════════════════════════

    [Fact]
    public void reactiveがIdentifierトークンとしてレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("reactive")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("reactive", tokens[0].Text);
    }

    [Fact]
    public void reactiveSelectがIdentifierトークンとしてレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("reactive-select")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("reactive-select", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  2. Parser: CstReactive / CstReactiveSelect
    // ═══════════════════════════════════════════════

    [Fact]
    public void reactiveノードをパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (reactive .IsReady (seq (.Attack))))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var reactive = Assert.IsType<CstReactive>(tree.Body[0]);
        Assert.IsType<CstMemberAccess>(reactive.Condition);
        Assert.IsType<CstSequence>(reactive.Body);
    }

    [Fact]
    public void reactiveノードの条件がメンバーアクセスである()
    {
        var (program, _) = Parser.Parse("(tree T (reactive .IsReady .Patrol))");

        var tree = Assert.Single(program.Trees);
        var reactive = Assert.IsType<CstReactive>(tree.Body[0]);
        var condition = Assert.IsType<CstMemberAccess>(reactive.Condition);
        Assert.Equal("IsReady", condition.Segments[0]);
    }

    [Fact]
    public void reactiveノードの条件が二項式である()
    {
        var (program, diagnostics) = Parser.Parse("(tree T (reactive (< .Health 30) .Flee))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var reactive = Assert.IsType<CstReactive>(tree.Body[0]);
        Assert.IsType<CstBinaryExpr>(reactive.Condition);
    }

    [Fact]
    public void reactiveSelectノードをパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (reactive-select .A .B))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var rsel = Assert.IsType<CstReactiveSelect>(tree.Body[0]);
        Assert.Equal(2, rsel.Children_.Length);
    }

    [Fact]
    public void reactiveSelectノードの子が複数ある場合()
    {
        var (program, diagnostics) = Parser.Parse("(tree T (reactive-select .A .B .C))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var rsel = Assert.IsType<CstReactiveSelect>(tree.Body[0]);
        Assert.Equal(3, rsel.Children_.Length);
    }

    // ═══════════════════════════════════════════════
    //  3. CstToAstLowering: AstReactive / AstReactiveSelect
    // ═══════════════════════════════════════════════

    [Fact]
    public void CstReactiveがAstReactiveに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (reactive .IsReady .Patrol))");
        var trees = CstToAstLowering.Lower(program);

        // Assert
        var tree = trees[0];
        var reactive = Assert.IsType<AstReactive>(tree.Body[0]);
        Assert.IsType<AstMemberAccessExpr>(reactive.Condition);
        Assert.IsType<AstActionCall>(reactive.Body);
    }

    [Fact]
    public void CstReactiveの条件と本体が正しく変換される()
    {
        var (program, _) = Parser.Parse("(tree T (reactive .IsAlive (seq .Attack .Patrol)))");
        var trees = CstToAstLowering.Lower(program);

        var reactive = Assert.IsType<AstReactive>(trees[0].Body[0]);
        var condition = Assert.IsType<AstMemberAccessExpr>(reactive.Condition);
        Assert.Equal("IsAlive", condition.Path.Segments[0]);
        var body = Assert.IsType<AstSequence>(reactive.Body);
        Assert.Equal(2, body.Children_.Length);
    }

    [Fact]
    public void CstReactiveSelectがAstReactiveSelectに変換される()
    {
        var (program, _) = Parser.Parse("(tree T (reactive-select .A .B .C))");
        var trees = CstToAstLowering.Lower(program);

        var rsel = Assert.IsType<AstReactiveSelect>(trees[0].Body[0]);
        Assert.Equal(3, rsel.Children_.Length);
    }

    // ═══════════════════════════════════════════════
    //  4. AstToIrLowering: IrReactive / IrReactiveSelect
    // ═══════════════════════════════════════════════

    [Fact]
    public void AstReactiveがIrReactiveに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (reactive .IsReady .Patrol))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var irTree = irTrees[0];
        var reactive = Assert.IsType<IrReactive>(irTree.Body[0]);
        var condition = Assert.IsType<IrMemberLoad>(reactive.Condition);
        Assert.Equal("IsReady", condition.Ref.Chain[0]);
        Assert.IsType<IrAction>(reactive.Body);
    }

    [Fact]
    public void AstReactiveの本体がseqの場合IrSequenceに変換される()
    {
        var (program, _) = Parser.Parse("(tree T (reactive .IsAlive (seq .Attack .Patrol)))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        var reactive = Assert.IsType<IrReactive>(irTrees[0].Body[0]);
        Assert.IsType<IrSequence>(reactive.Body);
    }

    [Fact]
    public void AstReactiveSelectがIrReactiveSelectに変換される()
    {
        var (program, _) = Parser.Parse("(tree T (reactive-select .A .B))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        var rsel = Assert.IsType<IrReactiveSelect>(irTrees[0].Body[0]);
        Assert.Equal(2, rsel.Children.Length);
    }

    [Fact]
    public void IrReactiveSelectの子がIrActionに変換される()
    {
        var (program, _) = Parser.Parse("(tree T (reactive-select .A .B .C))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        var rsel = Assert.IsType<IrReactiveSelect>(irTrees[0].Body[0]);
        Assert.Equal(3, rsel.Children.Length);
        Assert.All(rsel.Children, child => Assert.IsType<IrAction>(child));
    }

    // ═══════════════════════════════════════════════
    //  5. CSharpEmitter: ReactiveNode / ReactiveSelectorNode
    // ═══════════════════════════════════════════════

    [Fact]
    public void IrReactiveがReactiveNode構築式に変換される()
    {
        // Arrange
        var node = new IrReactive(Member("IsReady"), Action("Attack"));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert
        Assert.Equal(
            "new ReactiveNode(() => this.IsReady, new ActionNode(() => this.Attack(), \"Attack()\"))",
            result);
    }

    [Fact]
    public void IrReactiveの条件がメンバーチェーンの場合()
    {
        var node = new IrReactive(Member("Target", "IsAlive"), Action("Attack"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal(
            "new ReactiveNode(() => this.Target.IsAlive, new ActionNode(() => this.Attack(), \"Attack()\"))",
            result);
    }

    [Fact]
    public void IrReactiveSelectがReactiveSelectorNode構築式に変換される()
    {
        // Arrange
        var node = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            Action("A"), Action("B")));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert
        Assert.Contains("new ReactiveSelectorNode(", result);
        Assert.Contains("new ActionNode(() => this.A(), \"A()\")", result);
        Assert.Contains("new ActionNode(() => this.B(), \"B()\")", result);
    }

    // ═══════════════════════════════════════════════
    //  6. IrSerializer ラウンドトリップ
    // ═══════════════════════════════════════════════

    [Fact]
    public void IrReactiveが正しく直列化される()
    {
        // Arrange
        var node = new IrReactive(Member("IsReady"), Action("Attack"));

        // Act
        var result = IrSerializer.Serialize(node);

        // Assert
        Assert.Contains("ir-reactive", result);
        Assert.Contains("ir-member-load", result);
        Assert.Contains("\"IsReady\"", result);
        Assert.Contains("ir-action", result);
        Assert.Contains("\"Attack\"", result);
    }

    [Fact]
    public void IrReactiveSelectが正しく直列化される()
    {
        var node = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            Action("A"), Action("B")));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-reactive-select", result);
        Assert.Contains("\"A\"", result);
        Assert.Contains("\"B\"", result);
    }

    [Fact]
    public void IrReactiveのラウンドトリップが等価である()
    {
        // Arrange
        var original = new IrReactive(
            new IrMemberLoad(new MemberRef(ImmutableArray.Create("IsReady")))
                { TypeRef = TypeRef.Bool },
            new IrAction(
                new MethodRef("this", "Attack", ImmutableArray<string>.Empty),
                ImmutableArray<IrExpr>.Empty)
            { TypeRef = TypeRef.BtStatus });

        // Act: Serialize → Deserialize → Serialize
        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        // Assert
        Assert.Equal(text, roundTripped);
        Assert.IsType<IrReactive>(deserialized);
    }

    [Fact]
    public void IrReactiveSelectのラウンドトリップが等価である()
    {
        var original = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            new IrAction(
                new MethodRef("this", "A", ImmutableArray<string>.Empty),
                ImmutableArray<IrExpr>.Empty)
            { TypeRef = TypeRef.BtStatus },
            new IrAction(
                new MethodRef("this", "B", ImmutableArray<string>.Empty),
                ImmutableArray<IrExpr>.Empty)
            { TypeRef = TypeRef.BtStatus }));

        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        Assert.Equal(text, roundTripped);
        Assert.IsType<IrReactiveSelect>(deserialized);
    }

    [Fact]
    public void IrReactiveを逆直列化できる()
    {
        // Arrange: 手書きのS式
        var text = "(ir-reactive\n  (ir-member-load (\"IsReady\") :type \"System.Boolean\")\n  (ir-action \"this\" \"Attack\" () :type \"Crisp.Runtime.BtStatus\"))";

        // Act
        var node = IrDeserializer.Deserialize(text);

        // Assert
        var reactive = Assert.IsType<IrReactive>(node);
        var cond = Assert.IsType<IrMemberLoad>(reactive.Condition);
        Assert.Equal("IsReady", cond.Ref.Chain[0]);
        Assert.IsType<IrAction>(reactive.Body);
    }

    [Fact]
    public void IrReactiveSelectを逆直列化できる()
    {
        var text = "(ir-reactive-select\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"B\" () :type \"Crisp.Runtime.BtStatus\"))";

        var node = IrDeserializer.Deserialize(text);

        var rsel = Assert.IsType<IrReactiveSelect>(node);
        Assert.Equal(2, rsel.Children.Length);
    }

    // ═══════════════════════════════════════════════
    //  7. Evaluator: IR インタープリタでの reactive 評価
    // ═══════════════════════════════════════════════

    [Fact]
    public void Reactive_条件trueでボディを評価する()
    {
        // Arrange
        var agent = new ReactiveAgent { IsReady = true };
        var eval = new Evaluator(agent);
        var node = new IrReactive(Member("IsReady"), Action("Attack"));

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Reactive_条件falseでFailureを返す()
    {
        var agent = new ReactiveAgent { IsReady = false };
        var eval = new Evaluator(agent);
        var node = new IrReactive(Member("IsReady"), Action("Attack"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void Reactive_条件trueでボディがRunningならRunningを返す()
    {
        var agent = new ReactiveAgent { IsReady = true };
        var eval = new Evaluator(agent);
        var node = new IrReactive(Member("IsReady"), Action("Wait"));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void ReactiveSelect_先頭から評価して最初の非Failureを返す()
    {
        // Arrange: Attack は Success を返す
        var agent = new ReactiveAgent();
        var eval = new Evaluator(agent);
        var node = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            Action("Attack"), Action("Patrol")));

        // Act
        var result = eval.Tick(node);

        // Assert: 1つ目の子が Success なのでそれが返る
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void ReactiveSelect_全Failureなら全体Failure()
    {
        // Arrange: Flee は Running だが、全部 Failure のケースを作る
        var agent = new ReactiveAgent { IsReady = false, IsAlive = false };
        var eval = new Evaluator(agent);
        // 条件チェックの子を2つ持つ reactive-select
        var node = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            new IrCondition(Member("IsReady")),
            new IrCondition(Member("IsAlive"))));

        var result = eval.Tick(node);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void ReactiveSelect_毎tick先頭から再評価する()
    {
        // Arrange: 2つ目の子で Running を返した後、1つ目の子が Success を返すケースをテスト
        var agent = new ReactiveAgent { IsReady = false, HasEnemy = true };
        var eval = new Evaluator(agent);
        var node = new IrReactiveSelect(ImmutableArray.Create<IrNode>(
            new IrCondition(Member("IsReady")),
            new IrCondition(Member("HasEnemy"))));

        // 1st tick: IsReady=false → skip, HasEnemy=true → Success
        var result1 = eval.Tick(node);
        Assert.Equal(BtStatus.Success, result1);

        // 2nd tick: IsReady=true → Success (先頭から再評価するため1つ目で止まる)
        agent.IsReady = true;
        var result2 = eval.Tick(node);
        Assert.Equal(BtStatus.Success, result2);
    }

    // ═══════════════════════════════════════════════
    //  8. Runtime: ReactiveNode の実行時動作
    // ═══════════════════════════════════════════════

    [Fact]
    public void ReactiveNode_条件trueなら子をtickする()
    {
        var body = new AbortTrackingNode(BtStatus.Success);
        var node = new ReactiveNode(() => true, body);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(1, body.TickCount);
    }

    [Fact]
    public void ReactiveNode_条件falseならFailureを返す()
    {
        var body = new AbortTrackingNode(BtStatus.Success);
        var node = new ReactiveNode(() => false, body);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(0, body.TickCount); // body は tick されない
    }

    [Fact]
    public void ReactiveNode_条件trueでボディRunningならRunning()
    {
        var body = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveNode(() => true, body);

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void ReactiveNode_ボディRunning中に条件falseになるとAbortが呼ばれる()
    {
        // Arrange: 条件を途中で false に切り替え可能にする
        var conditionValue = true;
        var body = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveNode(() => conditionValue, body);

        // Act: 1st tick → Running
        var result1 = node.Tick(Ctx);
        Assert.Equal(BtStatus.Running, result1);
        Assert.Equal(0, body.AbortCount);

        // Act: 条件を false にして 2nd tick → Failure & Abort
        conditionValue = false;
        var result2 = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Failure, result2);
        Assert.Equal(1, body.AbortCount); // Abort が呼ばれたことを確認
    }

    [Fact]
    public void ReactiveNode_ボディが完了していればAbortは呼ばれない()
    {
        // Arrange: ボディは最初は Success を返す（Running 状態にならない）
        var conditionValue = true;
        var body = new AbortTrackingNode(BtStatus.Success);
        var node = new ReactiveNode(() => conditionValue, body);

        // Act: 1st tick → Success（Running にならない）
        node.Tick(Ctx);

        // 条件を false にして 2nd tick
        conditionValue = false;
        var result = node.Tick(Ctx);

        // Assert: Abort は呼ばれない（Running 中ではなかったため）
        Assert.Equal(BtStatus.Failure, result);
        Assert.Equal(0, body.AbortCount);
    }

    [Fact]
    public void ReactiveNode_Resetで子もリセットされる()
    {
        var body = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveNode(() => true, body);

        node.Tick(Ctx); // body を Running 状態にする
        node.Reset();

        Assert.Equal(1, body.ResetCount);
    }

    [Fact]
    public void ReactiveNode_外部Abortで子もAbortされる()
    {
        var body = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveNode(() => true, body);

        node.Tick(Ctx); // body を Running 状態にする
        node.Abort();

        Assert.Equal(1, body.AbortCount);
    }

    // ═══════════════════════════════════════════════
    //  9. Runtime: ReactiveSelectorNode の実行時動作
    // ═══════════════════════════════════════════════

    [Fact]
    public void ReactiveSelectorNode_最初のSuccessで停止する()
    {
        var node = new ReactiveSelectorNode(
            StubNode(BtStatus.Failure),
            StubNode(BtStatus.Success),
            StubNode(BtStatus.Failure));

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void ReactiveSelectorNode_全Failureなら全体Failure()
    {
        var node = new ReactiveSelectorNode(
            StubNode(BtStatus.Failure),
            StubNode(BtStatus.Failure));

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void ReactiveSelectorNode_Runningを返す()
    {
        var node = new ReactiveSelectorNode(
            StubNode(BtStatus.Failure),
            StubNode(BtStatus.Running));

        var result = node.Tick(Ctx);

        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void ReactiveSelectorNode_上位子が復活したら下位のRunning子をAbortする()
    {
        // Arrange: 1st tick では child0=Failure, child1=Running
        //          2nd tick では child0=Success → child1 を Abort
        var tickCount = 0;
        var child0 = new AbortTrackingNode(() =>
        {
            tickCount++;
            return tickCount <= 1 ? BtStatus.Failure : BtStatus.Success;
        });
        var child1 = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveSelectorNode(child0, child1);

        // Act: 1st tick → child0=Failure, child1=Running → 全体 Running
        var result1 = node.Tick(Ctx);
        Assert.Equal(BtStatus.Running, result1);
        Assert.Equal(0, child1.AbortCount);

        // Act: 2nd tick → child0=Success → child1 を Abort
        var result2 = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Success, result2);
        Assert.Equal(1, child1.AbortCount); // 下位 Running 子が Abort された
    }

    [Fact]
    public void ReactiveSelectorNode_毎tick先頭から再評価する()
    {
        // Arrange: child0 は毎回 tick される（通常の select と違い先頭から再評価）
        var child0 = new AbortTrackingNode(BtStatus.Failure);
        var child1 = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveSelectorNode(child0, child1);

        // Act: 2回 tick
        node.Tick(Ctx);
        node.Tick(Ctx);

        // Assert: child0 が毎 tick 評価されている
        Assert.Equal(2, child0.TickCount);
    }

    [Fact]
    public void ReactiveSelectorNode_Resetで全子がリセットされる()
    {
        var child0 = new AbortTrackingNode(BtStatus.Failure);
        var child1 = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveSelectorNode(child0, child1);

        node.Tick(Ctx);
        node.Reset();

        Assert.Equal(1, child0.ResetCount);
        Assert.Equal(1, child1.ResetCount);
    }

    [Fact]
    public void ReactiveSelectorNode_外部Abortで全Running子がAbortされる()
    {
        var child0 = new AbortTrackingNode(BtStatus.Failure);
        var child1 = new AbortTrackingNode(BtStatus.Running);
        var node = new ReactiveSelectorNode(child0, child1);

        node.Tick(Ctx); // child1 が Running 状態になる
        node.Abort();

        Assert.Equal(1, child1.AbortCount);
        Assert.Equal(0, child0.AbortCount); // Failure だった child0 は Abort されない
    }

    // ═══════════════════════════════════════════════
    //  10. End-to-end パイプライン: ソースから IR 評価まで
    // ═══════════════════════════════════════════════

    [Fact]
    public void ソースからreactiveをパースしてIRまで変換できる()
    {
        // Arrange & Act: 完全なパイプライン
        var source = "(tree Combat (reactive .IsReady (seq (.Attack) (.Patrol))))";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var tree = irTrees[0];
        Assert.Equal("Combat", tree.Name);
        var reactive = Assert.IsType<IrReactive>(tree.Body[0]);
        Assert.IsType<IrMemberLoad>(reactive.Condition);
        Assert.IsType<IrSequence>(reactive.Body);
    }

    [Fact]
    public void ソースからreactiveSelectをパースしてIRまで変換できる()
    {
        var source = "(tree Patrol (reactive-select (check .HasEnemy) (.Patrol) (.Wait)))";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        var tree = irTrees[0];
        var rsel = Assert.IsType<IrReactiveSelect>(tree.Body[0]);
        Assert.Equal(3, rsel.Children.Length);
    }
}
