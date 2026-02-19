using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F3: Typed Blackboard のユニットテスト。
/// t-wada 式 TDD に基づき、最小のケースから段階的に複雑さを増す。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>Lexer: $ プレフィクスで BlackboardAccess トークンを認識する</description></item>
///   <item><description>Parser: CstBlackboardAccess を式位置で認識する</description></item>
///   <item><description>CstToAstLowering: CstBlackboardAccess → AstBlackboardAccessExpr (MemberPath)</description></item>
///   <item><description>AstToIrLowering: AstBlackboardAccessExpr → IrBlackboardLoad (MemberRef)</description></item>
///   <item><description>CSharpEmitter: IrBlackboardLoad → blackboard.MemberName の構築式</description></item>
///   <item><description>IrSerializer/Deserializer: ir-blackboard-load のラウンドトリップ</description></item>
///   <item><description>Evaluator: ブラックボードオブジェクトへのリフレクションアクセス</description></item>
///   <item><description>Runtime: IBtBlackboard マーカーインターフェース</description></item>
/// </list>
/// </summary>
public class BlackboardTests
{
    // ──────────────────────────────────────────────
    //  テスト用コンテキスト・ブラックボードクラス
    // ──────────────────────────────────────────────

    /// <summary>アクションを持つテスト用エージェント。</summary>
    private class TestAgent
    {
        public bool IsReady { get; set; } = true;

        public BtStatus Attack() => BtStatus.Success;
        public BtStatus Patrol() => BtStatus.Success;
    }

    /// <summary>テスト用ブラックボード。IBtBlackboard マーカーを実装する。</summary>
    private class TestBlackboard : IBtBlackboard
    {
        public bool IsAlarmTriggered { get; set; }
        public int EnemyCount { get; set; }
        public float DangerLevel { get; set; }
        public string TargetName { get; set; } = "";
        public NestedData World { get; set; } = new();
    }

    /// <summary>ネストしたプロパティのテスト用クラス。</summary>
    private class NestedData
    {
        public bool IsDayTime { get; set; } = true;
        public InnerData Region { get; set; } = new();
    }

    /// <summary>二重ネストのテスト用クラス。</summary>
    private class InnerData
    {
        public string Name { get; set; } = "Forest";
    }

    // ──────────────────────────────────────────────
    //  IR ノード構築ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>ブラックボードロード式を構築する。</summary>
    private static IrBlackboardLoad BbLoad(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = TypeRef.Bool };

    /// <summary>ブラックボードロード式を指定型で構築する。</summary>
    private static IrBlackboardLoad BbLoadTyped(TypeRef typeRef, params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = typeRef };

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = TypeRef.Bool };

    /// <summary>真偽値リテラルを構築する。</summary>
    private static IrLiteral BoolLit(bool value)
        => new(value, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

    /// <summary>整数リテラルを構築する。</summary>
    private static IrLiteral IntLit(int value)
        => new(value, LiteralKind.Int) { TypeRef = TypeRef.Int };

    // ===============================================
    //  1. Lexer: $ プレフィクスで BlackboardAccess トークンを認識
    // ===============================================

    [Fact]
    public void ドルドットで始まるトークンがBlackboardAccessとしてレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("$.Foo")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenKind.BlackboardAccess, tokens[0].Kind);
        Assert.Equal("$.Foo", tokens[0].Text);
    }

    [Fact]
    public void ドルドットの複数セグメントが単一トークンとしてレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("$.Foo.Bar.Baz")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenKind.BlackboardAccess, tokens[0].Kind);
        Assert.Equal("$.Foo.Bar.Baz", tokens[0].Text);
    }

    [Fact]
    public void BlackboardAccessトークンのスパンが正しい()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("$.IsAlarmTriggered")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(0, tokens[0].Span.Start);
        Assert.Equal(18, tokens[0].Span.Length);
    }

    [Fact]
    public void BlackboardAccessが他のトークンと混在してもレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("(check $.IsReady)")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(TokenKind.BlackboardAccess, tokens[2].Kind);
        Assert.Equal("$.IsReady", tokens[2].Text);
        Assert.Equal(TokenKind.RightParen, tokens[3].Kind);
    }

    // ===============================================
    //  2. Parser: CstBlackboardAccess を式位置で認識
    // ===============================================

    [Fact]
    public void BlackboardAccessをcheck条件としてパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (check $.IsAlarmTriggered))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var check = Assert.IsType<CstCheck>(tree.Body[0]);
        var bb = Assert.IsType<CstBlackboardAccess>(check.Expr);
        Assert.Equal("IsAlarmTriggered", bb.Segments[0]);
    }

    [Fact]
    public void BlackboardAccessをguard条件としてパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (guard $.IsReady (.Attack)))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var guard = Assert.IsType<CstGuard>(tree.Body[0]);
        var bb = Assert.IsType<CstBlackboardAccess>(guard.Condition);
        Assert.Single(bb.Segments);
        Assert.Equal("IsReady", bb.Segments[0]);
    }

    [Fact]
    public void 複数セグメントのBlackboardAccessをパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (check $.World.IsDayTime))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var check = Assert.IsType<CstCheck>(tree.Body[0]);
        var bb = Assert.IsType<CstBlackboardAccess>(check.Expr);
        Assert.Equal(2, bb.Segments.Length);
        Assert.Equal("World", bb.Segments[0]);
        Assert.Equal("IsDayTime", bb.Segments[1]);
    }

    [Fact]
    public void BlackboardAccessが二項式の左辺で使用できる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree T (check (> $.EnemyCount 0)))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var check = Assert.IsType<CstCheck>(tree.Body[0]);
        var binExpr = Assert.IsType<CstBinaryExpr>(check.Expr);
        Assert.IsType<CstBlackboardAccess>(binExpr.Lhs);
    }

    // ===============================================
    //  3. CstToAstLowering: CstBlackboardAccess → AstBlackboardAccessExpr
    // ===============================================

    [Fact]
    public void CstBlackboardAccessがAstBlackboardAccessExprに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (check $.IsAlarmTriggered))");
        var trees = CstToAstLowering.Lower(program);

        // Assert
        var tree = trees[0];
        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bb = Assert.IsType<AstBlackboardAccessExpr>(check.Expr);
        Assert.Single(bb.Path.Segments);
        Assert.Equal("IsAlarmTriggered", bb.Path.Segments[0]);
    }

    [Fact]
    public void 複数セグメントのBlackboardAccessのMemberPathが正しい()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (check $.World.Region.Name))");
        var trees = CstToAstLowering.Lower(program);

        // Assert
        var check = Assert.IsType<AstCheck>(trees[0].Body[0]);
        var bb = Assert.IsType<AstBlackboardAccessExpr>(check.Expr);
        Assert.Equal(3, bb.Path.Segments.Length);
        Assert.Equal("World", bb.Path.Segments[0]);
        Assert.Equal("Region", bb.Path.Segments[1]);
        Assert.Equal("Name", bb.Path.Segments[2]);
    }

    // ===============================================
    //  4. AstToIrLowering: AstBlackboardAccessExpr → IrBlackboardLoad
    // ===============================================

    [Fact]
    public void AstBlackboardAccessExprがIrBlackboardLoadに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (check $.IsAlarmTriggered))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var condition = Assert.IsType<IrCondition>(irTrees[0].Body[0]);
        var bbLoad = Assert.IsType<IrBlackboardLoad>(condition.Expr);
        Assert.Single(bbLoad.Ref.Chain);
        Assert.Equal("IsAlarmTriggered", bbLoad.Ref.Chain[0]);
    }

    [Fact]
    public void 複数セグメントのBlackboardAccessがIrBlackboardLoadのチェーンに正しく変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (check $.World.IsDayTime))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var condition = Assert.IsType<IrCondition>(irTrees[0].Body[0]);
        var bbLoad = Assert.IsType<IrBlackboardLoad>(condition.Expr);
        Assert.Equal(2, bbLoad.Ref.Chain.Length);
        Assert.Equal("World", bbLoad.Ref.Chain[0]);
        Assert.Equal("IsDayTime", bbLoad.Ref.Chain[1]);
    }

    [Fact]
    public void BlackboardAccessが二項式内でIrBlackboardLoadに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree T (check (> $.EnemyCount 0)))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var condition = Assert.IsType<IrCondition>(irTrees[0].Body[0]);
        var binOp = Assert.IsType<IrBinaryOp>(condition.Expr);
        Assert.IsType<IrBlackboardLoad>(binOp.Lhs);
    }

    // ===============================================
    //  5. CSharpEmitter: IrBlackboardLoad → blackboard.MemberName
    // ===============================================

    [Fact]
    public void IrBlackboardLoadが単一セグメントでblackboardアクセスに変換される()
    {
        // Arrange
        var node = BbLoad("IsAlarmTriggered");

        // Act
        var result = CSharpEmitter.EmitExpressionString(node);

        // Assert
        Assert.Equal("blackboard.IsAlarmTriggered", result);
    }

    [Fact]
    public void IrBlackboardLoadが複数セグメントでドットチェーンに変換される()
    {
        // Arrange
        var node = BbLoad("World", "IsDayTime");

        // Act
        var result = CSharpEmitter.EmitExpressionString(node);

        // Assert
        Assert.Equal("blackboard.World.IsDayTime", result);
    }

    [Fact]
    public void IrBlackboardLoadが3段チェーンで正しくエミットされる()
    {
        // Arrange
        var node = BbLoad("World", "Region", "Name");

        // Act
        var result = CSharpEmitter.EmitExpressionString(node);

        // Assert
        Assert.Equal("blackboard.World.Region.Name", result);
    }

    [Fact]
    public void IrBlackboardLoadがConditionNode内でラムダ式としてエミットされる()
    {
        // Arrange
        var node = new IrCondition(BbLoad("IsAlarmTriggered"));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert
        Assert.Equal("new ConditionNode(() => blackboard.IsAlarmTriggered, \"$.IsAlarmTriggered\")", result);
    }

    // ===============================================
    //  6. Evaluator: ブラックボードオブジェクトへのリフレクションアクセス
    // ===============================================

    [Fact]
    public void Evaluator_ブラックボードのboolプロパティにアクセスできる()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard { IsAlarmTriggered = true };
        var eval = new Evaluator(agent, bb);
        var node = new IrCondition(BbLoad("IsAlarmTriggered"));

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Evaluator_ブラックボードのboolプロパティがfalseならFailure()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard { IsAlarmTriggered = false };
        var eval = new Evaluator(agent, bb);
        var node = new IrCondition(BbLoad("IsAlarmTriggered"));

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void Evaluator_ブラックボードのネストプロパティにアクセスできる()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard();
        bb.World.IsDayTime = true;
        var eval = new Evaluator(agent, bb);
        var node = new IrCondition(BbLoad("World", "IsDayTime"));

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Evaluator_ブラックボードの二重ネストプロパティにアクセスできる()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard();
        bb.World.Region.Name = "Desert";
        var eval = new Evaluator(agent, bb);
        // 二項式で比較: $.World.Region.Name = "Desert"
        var bbLoad = BbLoadTyped(new TypeRef("unknown"), "World", "Region", "Name");
        var literal = new IrLiteral("Desert", LiteralKind.String) { TypeRef = TypeRef.String };
        var binOp = new IrBinaryOp(BinaryOp.Eq, bbLoad, literal) { TypeRef = TypeRef.Bool };
        var node = new IrCondition(binOp);

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Evaluator_ブラックボードのintプロパティで比較式が評価できる()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard { EnemyCount = 5 };
        var eval = new Evaluator(agent, bb);
        var bbLoad = BbLoadTyped(new TypeRef("unknown"), "EnemyCount");
        var binOp = new IrBinaryOp(BinaryOp.Gt, bbLoad, IntLit(3)) { TypeRef = TypeRef.Bool };
        var node = new IrCondition(binOp);

        // Act
        var result = eval.Tick(node);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void Evaluator_ブラックボード未設定でアクセスするとInvalidOperationException()
    {
        // Arrange
        var agent = new TestAgent();
        var eval = new Evaluator(agent); // blackboard = null
        var node = new IrCondition(BbLoad("IsAlarmTriggered"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => eval.Tick(node));
    }

    [Fact]
    public void Evaluator_存在しないブラックボードメンバーでMissingMemberException()
    {
        // Arrange
        var agent = new TestAgent();
        var bb = new TestBlackboard();
        var eval = new Evaluator(agent, bb);
        var node = new IrCondition(BbLoad("NonExistentProperty"));

        // Act & Assert
        Assert.Throws<MissingMemberException>(() => eval.Tick(node));
    }

    // ===============================================
    //  7. IrSerializer/Deserializer: ir-blackboard-load のラウンドトリップ
    // ===============================================

    [Fact]
    public void IrBlackboardLoadが正しく直列化される()
    {
        // Arrange
        var node = BbLoad("IsAlarmTriggered");

        // Act
        var result = IrSerializer.Serialize(
            new IrCondition(node));

        // Assert
        Assert.Contains("ir-blackboard-load", result);
        Assert.Contains("\"IsAlarmTriggered\"", result);
    }

    [Fact]
    public void 複数セグメントのIrBlackboardLoadが正しく直列化される()
    {
        // Arrange
        var node = BbLoad("World", "IsDayTime");

        // Act
        var result = IrSerializer.Serialize(
            new IrCondition(node));

        // Assert
        Assert.Contains("ir-blackboard-load", result);
        Assert.Contains("\"World\"", result);
        Assert.Contains("\"IsDayTime\"", result);
    }

    [Fact]
    public void IrBlackboardLoadのラウンドトリップが等価である()
    {
        // Arrange
        var original = new IrCondition(
            new IrBlackboardLoad(new MemberRef(ImmutableArray.Create("IsAlarmTriggered")))
                { TypeRef = TypeRef.Bool });

        // Act: Serialize -> Deserialize -> Serialize
        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        // Assert
        Assert.Equal(text, roundTripped);
        var cond = Assert.IsType<IrCondition>(deserialized);
        Assert.IsType<IrBlackboardLoad>(cond.Expr);
    }

    [Fact]
    public void 複数セグメントのIrBlackboardLoadのラウンドトリップが等価である()
    {
        // Arrange
        var original = new IrCondition(
            new IrBlackboardLoad(new MemberRef(ImmutableArray.Create("World", "Region", "Name")))
                { TypeRef = new TypeRef("string") });

        // Act
        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        // Assert
        Assert.Equal(text, roundTripped);
    }

    [Fact]
    public void IrBlackboardLoadを手書きS式から逆直列化できる()
    {
        // Arrange: 手書きのS式
        var text = "(ir-condition\n  (ir-blackboard-load (\"IsAlarmTriggered\") :type \"System.Boolean\"))";

        // Act
        var node = IrDeserializer.Deserialize(text);

        // Assert
        var cond = Assert.IsType<IrCondition>(node);
        var bbLoad = Assert.IsType<IrBlackboardLoad>(cond.Expr);
        Assert.Single(bbLoad.Ref.Chain);
        Assert.Equal("IsAlarmTriggered", bbLoad.Ref.Chain[0]);
        Assert.Equal("System.Boolean", bbLoad.TypeRef.FullName);
    }

    [Fact]
    public void 複数セグメントのIrBlackboardLoadを手書きS式から逆直列化できる()
    {
        // Arrange
        var text = "(ir-condition\n  (ir-blackboard-load (\"World\" \"IsDayTime\") :type \"System.Boolean\"))";

        // Act
        var node = IrDeserializer.Deserialize(text);

        // Assert
        var cond = Assert.IsType<IrCondition>(node);
        var bbLoad = Assert.IsType<IrBlackboardLoad>(cond.Expr);
        Assert.Equal(2, bbLoad.Ref.Chain.Length);
        Assert.Equal("World", bbLoad.Ref.Chain[0]);
        Assert.Equal("IsDayTime", bbLoad.Ref.Chain[1]);
    }

    // ===============================================
    //  8. Runtime: IBtBlackboard マーカーインターフェース
    // ===============================================

    [Fact]
    public void IBtBlackboardを実装したクラスがマーカーインターフェースを満たす()
    {
        // Arrange & Act
        var bb = new TestBlackboard();

        // Assert
        Assert.IsAssignableFrom<IBtBlackboard>(bb);
    }

    [Fact]
    public void IBtBlackboardはメソッドを持たないマーカーインターフェースである()
    {
        // Arrange & Act
        var methods = typeof(IBtBlackboard).GetMethods(
            System.Reflection.BindingFlags.DeclaredOnly
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance);

        // Assert
        Assert.Empty(methods);
    }

    // ===============================================
    //  9. End-to-end パイプライン: ソースから IR 評価まで
    // ===============================================

    [Fact]
    public void ソースからBlackboardAccessをパースしてIRまで変換できる()
    {
        // Arrange & Act: 完全なパイプライン
        var source = "(tree CombatAI (guard $.IsAlarmTriggered (.Attack)))";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var tree = irTrees[0];
        Assert.Equal("CombatAI", tree.Name);
        var guard = Assert.IsType<IrGuard>(tree.Body[0]);
        var bbLoad = Assert.IsType<IrBlackboardLoad>(guard.Condition);
        Assert.Equal("IsAlarmTriggered", bbLoad.Ref.Chain[0]);
        Assert.IsType<IrAction>(guard.Body);
    }

    [Fact]
    public void ソースからBlackboardAccessとMemberAccessが共存するツリーをパースできる()
    {
        // Arrange & Act: context メンバーと blackboard メンバーが混在
        var source = "(tree T (guard (and .IsReady $.IsAlarmTriggered) (.Patrol)))";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var guard = Assert.IsType<IrGuard>(irTrees[0].Body[0]);
        var logic = Assert.IsType<IrLogicOp>(guard.Condition);
        Assert.Equal(LogicOp.And, logic.Op);
        Assert.IsType<IrMemberLoad>(logic.Operands[0]);  // .IsReady → context
        Assert.IsType<IrBlackboardLoad>(logic.Operands[1]); // $.IsAlarmTriggered → blackboard
    }

    [Fact]
    public void ソースからBlackboardAccessの比較式をパースしてIRまで変換できる()
    {
        // Arrange & Act
        var source = "(tree T (check (> $.EnemyCount 3)))";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var condition = Assert.IsType<IrCondition>(irTrees[0].Body[0]);
        var binOp = Assert.IsType<IrBinaryOp>(condition.Expr);
        var lhs = Assert.IsType<IrBlackboardLoad>(binOp.Lhs);
        Assert.Equal("EnemyCount", lhs.Ref.Chain[0]);
        var rhs = Assert.IsType<IrLiteral>(binOp.Rhs);
        Assert.Equal(3, rhs.Value);
    }
}
