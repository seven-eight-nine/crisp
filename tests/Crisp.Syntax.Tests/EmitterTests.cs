using System.Collections.Immutable;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// CSharpEmitter のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// - IR ノードを直接構築し、エミッターの出力文字列を検証する
/// - まずリーフノード（アクション・条件）の単純な出力を確認
/// - 次に式（リテラル・メンバーロード・演算子）の出力を確認
/// - 最後に複合ツリーや完全なファイル生成を確認
/// </summary>
public class EmitterTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー: IR ノード構築用ファクトリメソッド
    // ──────────────────────────────────────────────

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("TestClass", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty);

    /// <summary>引数ありアクションの IR ノードを構築する。</summary>
    private static IrAction ActionWithArgs(string name, params IrExpr[] args)
        => new(new MethodRef("TestClass", name, ImmutableArray<string>.Empty),
               args.ToImmutableArray());

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = TypeRef.Bool };

    /// <summary>整数リテラル式を構築する。</summary>
    private static IrLiteral IntLit(int value)
        => new(value, LiteralKind.Int) { TypeRef = TypeRef.Int };

    /// <summary>浮動小数点リテラル式を構築する。</summary>
    private static IrLiteral FloatLit(float value)
        => new(value, LiteralKind.Float) { TypeRef = TypeRef.Float };

    /// <summary>真偽値リテラル式を構築する。</summary>
    private static IrLiteral BoolLit(bool value)
        => new(value, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

    /// <summary>文字列リテラル式を構築する。</summary>
    private static IrLiteral StringLit(string value)
        => new(value, LiteralKind.String) { TypeRef = TypeRef.String };

    /// <summary>null リテラル式を構築する。</summary>
    private static IrLiteral NullLit()
        => new(null, LiteralKind.Null) { TypeRef = new TypeRef("object") };

    /// <summary>enum リテラル式を構築する。</summary>
    private static IrLiteral EnumLit(string typeName, string memberName)
        => new(new EnumRef(typeName, memberName), LiteralKind.Enum) { TypeRef = new TypeRef(typeName) };

    // =================================================================
    //  1. 最小のアクションノード
    // =================================================================

    [Fact]
    public void 引数なしアクションがActionNode構築式に変換される()
    {
        // Arrange: 引数なしの Patrol メソッド呼び出し
        var node = Action("Patrol");

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: this.Patrol() をラムダでラップした ActionNode 構築式（debugLabel 付き）
        Assert.Equal("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
    }

    [Fact]
    public void 引数ありアクションがActionNode構築式に変換される()
    {
        // Arrange: 引数ありの Attack(this.Target) メソッド呼び出し
        var node = ActionWithArgs("Attack", Member("Target"));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: 引数付きの ActionNode 構築式（debugLabel 付き）
        Assert.Equal("new ActionNode(() => this.Attack(this.Target), \"Attack(.Target)\")", result);
    }

    // =================================================================
    //  2. 条件ノード
    // =================================================================

    [Fact]
    public void メンバーロードの条件がConditionNode構築式に変換される()
    {
        // Arrange: 単純なメンバーアクセス条件（this.IsAlive）
        var node = new IrCondition(Member("IsAlive"));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: bool ラムダでラップされた ConditionNode（debugLabel 付き）
        Assert.Equal("new ConditionNode(() => this.IsAlive, \".IsAlive\")", result);
    }

    [Fact]
    public void メンバーチェーンの条件が正しく変換される()
    {
        // Arrange: ドットチェーンのメンバーアクセス条件（this.Target.IsAlive）
        var node = new IrCondition(Member("Target", "IsAlive"));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: チェーンされたメンバーアクセスが正しく連結される
        Assert.Equal("new ConditionNode(() => this.Target.IsAlive, \".Target.IsAlive\")", result);
    }

    // =================================================================
    //  3. セレクター・シーケンスノード
    // =================================================================

    [Fact]
    public void セレクターの子ノードが改行区切りで出力される()
    {
        // Arrange: 2つの子を持つセレクター
        var node = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("Flee"),
            Action("Patrol")));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: 複数子は改行・インデント付きで出力される
        Assert.Contains("new SelectorNode(", result);
        Assert.Contains("new ActionNode(() => this.Flee(), \"Flee()\")", result);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
    }

    [Fact]
    public void シーケンスの子ノードが正しく出力される()
    {
        // Arrange: 2つの子を持つシーケンス
        var node = new IrSequence(ImmutableArray.Create<IrNode>(
            new IrCondition(Member("IsAlive")),
            Action("Patrol")));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: SequenceNode として出力される
        Assert.Contains("new SequenceNode(", result);
        Assert.Contains("new ConditionNode(() => this.IsAlive, \".IsAlive\")", result);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
    }

    // =================================================================
    //  4. リテラルの出力
    // =================================================================

    [Fact]
    public void 整数リテラルが数値としてそのまま出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(IntLit(42));

        Assert.Equal("42", result);
    }

    [Fact]
    public void 負の整数リテラルがマイナス付きで出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(IntLit(-1));

        Assert.Equal("-1", result);
    }

    [Fact]
    public void 浮動小数点リテラルがfサフィックス付きで出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(FloatLit(2.5f));

        Assert.Equal("2.5f", result);
    }

    [Fact]
    public void 整数値の浮動小数点リテラルもfサフィックスが付く()
    {
        // 3.0f のような整数値の float でも f サフィックスが付く
        var result = CSharpEmitter.EmitExpressionString(FloatLit(3.0f));

        Assert.Equal("3f", result);
    }

    [Fact]
    public void trueリテラルがtrueとして出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(BoolLit(true));

        Assert.Equal("true", result);
    }

    [Fact]
    public void falseリテラルがfalseとして出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(BoolLit(false));

        Assert.Equal("false", result);
    }

    [Fact]
    public void 文字列リテラルがダブルクォートで囲まれて出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(StringLit("hello"));

        Assert.Equal("\"hello\"", result);
    }

    [Fact]
    public void 文字列リテラル内のエスケープ文字が正しく処理される()
    {
        // ダブルクォートやバックスラッシュがエスケープされる
        var result = CSharpEmitter.EmitExpressionString(StringLit("he\"llo\\world"));

        Assert.Equal("\"he\\\"llo\\\\world\"", result);
    }

    [Fact]
    public void nullリテラルがnullとして出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(NullLit());

        Assert.Equal("null", result);
    }

    [Fact]
    public void enumリテラルが型名ドットメンバー名で出力される()
    {
        var result = CSharpEmitter.EmitExpressionString(EnumLit("AIState", "Combat"));

        Assert.Equal("AIState.Combat", result);
    }

    // =================================================================
    //  5. 二項演算式
    // =================================================================

    [Fact]
    public void 二項演算式が括弧付きの演算子式に変換される()
    {
        // Arrange: this.Health < 30
        var expr = new IrBinaryOp(
            BinaryOp.Lt,
            Member("Health"),
            IntLit(30)) { TypeRef = TypeRef.Bool };

        // Act
        var result = CSharpEmitter.EmitExpressionString(expr);

        // Assert: 括弧で囲まれた二項演算式
        Assert.Equal("(this.Health < 30)", result);
    }

    [Theory]
    [InlineData(BinaryOp.Add, "+")]
    [InlineData(BinaryOp.Sub, "-")]
    [InlineData(BinaryOp.Mul, "*")]
    [InlineData(BinaryOp.Div, "/")]
    [InlineData(BinaryOp.Mod, "%")]
    [InlineData(BinaryOp.Lt, "<")]
    [InlineData(BinaryOp.Gt, ">")]
    [InlineData(BinaryOp.Le, "<=")]
    [InlineData(BinaryOp.Ge, ">=")]
    [InlineData(BinaryOp.Eq, "==")]
    [InlineData(BinaryOp.Ne, "!=")]
    public void 全ての二項演算子が正しい記号に変換される(BinaryOp op, string expectedSymbol)
    {
        // Arrange: 1 {op} 2 の形式
        var expr = new IrBinaryOp(op, IntLit(1), IntLit(2)) { TypeRef = TypeRef.Int };

        // Act
        var result = CSharpEmitter.EmitExpressionString(expr);

        // Assert: 期待される演算子記号が含まれる
        Assert.Equal($"(1 {expectedSymbol} 2)", result);
    }

    // =================================================================
    //  6. 単項演算式
    // =================================================================

    [Fact]
    public void Not演算がエクスクラメーション記号で出力される()
    {
        var expr = new IrUnaryOp(UnaryOp.Not, Member("IsAlive")) { TypeRef = TypeRef.Bool };

        var result = CSharpEmitter.EmitExpressionString(expr);

        Assert.Equal("(!this.IsAlive)", result);
    }

    [Fact]
    public void Negate演算がマイナス記号で出力される()
    {
        var expr = new IrUnaryOp(UnaryOp.Negate, Member("Speed")) { TypeRef = TypeRef.Float };

        var result = CSharpEmitter.EmitExpressionString(expr);

        Assert.Equal("(-this.Speed)", result);
    }

    // =================================================================
    //  7. 論理演算式
    // =================================================================

    [Fact]
    public void And論理演算がダブルアンパサンドで連結される()
    {
        var expr = new IrLogicOp(
            LogicOp.And,
            ImmutableArray.Create<IrExpr>(Member("A"), Member("B"), Member("C")))
            { TypeRef = TypeRef.Bool };

        var result = CSharpEmitter.EmitExpressionString(expr);

        Assert.Equal("(this.A && this.B && this.C)", result);
    }

    [Fact]
    public void Or論理演算がダブルパイプで連結される()
    {
        var expr = new IrLogicOp(
            LogicOp.Or,
            ImmutableArray.Create<IrExpr>(Member("X"), Member("Y")))
            { TypeRef = TypeRef.Bool };

        var result = CSharpEmitter.EmitExpressionString(expr);

        Assert.Equal("(this.X || this.Y)", result);
    }

    // =================================================================
    //  8. メソッド呼び出し式
    // =================================================================

    [Fact]
    public void 式位置のメソッド呼び出しがthisプレフィックス付きで出力される()
    {
        var expr = new IrCall(
            new MethodRef("TestClass", "DistanceTo", ImmutableArray.Create("Entity")),
            ImmutableArray.Create<IrExpr>(Member("Target")))
            { TypeRef = TypeRef.Float };

        var result = CSharpEmitter.EmitExpressionString(expr);

        Assert.Equal("this.DistanceTo(this.Target)", result);
    }

    // =================================================================
    //  9. 型変換式（IrConvert）
    // =================================================================

    [Fact]
    public void IrConvertがキャスト式に変換される()
    {
        // Arrange: (float)30 — 整数を float にキャスト
        var expr = new IrConvert(IntLit(30), TypeRef.Float) { TypeRef = TypeRef.Float };

        // Act
        var result = CSharpEmitter.EmitExpressionString(expr);

        // Assert: C# のキャスト構文
        Assert.Equal("((float)30)", result);
    }

    [Fact]
    public void ネストしたIrConvertが正しくキャストされる()
    {
        // Arrange: (int)(float)this.Value — 二重キャスト
        var inner = new IrConvert(Member("Value"), TypeRef.Float) { TypeRef = TypeRef.Float };
        var outer = new IrConvert(inner, TypeRef.Int) { TypeRef = TypeRef.Int };

        // Act
        var result = CSharpEmitter.EmitExpressionString(outer);

        // Assert: ネストしたキャスト
        Assert.Equal("((int)((float)this.Value))", result);
    }

    // =================================================================
    //  10. デコレータノード
    // =================================================================

    [Fact]
    public void InvertノードがInvertNode構築式に変換される()
    {
        var node = new IrInvert(Action("Patrol"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new InvertNode(new ActionNode(() => this.Patrol(), \"Patrol()\"))", result);
    }

    [Fact]
    public void RepeatノードがRepeatNode構築式に変換される()
    {
        var node = new IrRepeat(3, Action("Attack"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new RepeatNode(3, new ActionNode(() => this.Attack(), \"Attack()\"))", result);
    }

    [Fact]
    public void TimeoutノードがTimeoutNode構築式に変換される()
    {
        var node = new IrTimeout(2.5f, Action("Search"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new TimeoutNode(2.5f, new ActionNode(() => this.Search(), \"Search()\"))", result);
    }

    [Fact]
    public void CooldownノードがCooldownNode構築式に変換される()
    {
        var node = new IrCooldown(5.0f, Action("HealSelf"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new CooldownNode(5f, new ActionNode(() => this.HealSelf(), \"HealSelf()\"))", result);
    }

    [Fact]
    public void GuardノードがGuardNode構築式に変換される()
    {
        var node = new IrGuard(Member("IsAlive"), Action("Patrol"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new GuardNode(() => this.IsAlive, new ActionNode(() => this.Patrol(), \"Patrol()\"))", result);
    }

    [Fact]
    public void WhileノードがWhileNode構築式に変換される()
    {
        var node = new IrWhile(Member("HasEnemy"), Action("Attack"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new WhileNode(() => this.HasEnemy, new ActionNode(() => this.Attack(), \"Attack()\"))", result);
    }

    // =================================================================
    //  11. if ノード
    // =================================================================

    [Fact]
    public void elseなしIfノードが2引数IfNode構築式に変換される()
    {
        var node = new IrIf(Member("IsAlive"), Action("Patrol"), null);

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("new IfNode(() => this.IsAlive, new ActionNode(() => this.Patrol(), \"Patrol()\"))", result);
    }

    [Fact]
    public void elseありIfノードが3引数IfNode構築式に変換される()
    {
        var node = new IrIf(Member("IsAlive"), Action("Patrol"), Action("Respawn"));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal(
            "new IfNode(() => this.IsAlive, new ActionNode(() => this.Patrol(), \"Patrol()\"), new ActionNode(() => this.Respawn(), \"Respawn()\"))",
            result);
    }

    // =================================================================
    //  12. パラレルノード
    // =================================================================

    [Fact]
    public void AnyポリシーのパラレルがParallelNode構築式に変換される()
    {
        var node = new IrParallel(
            new ParallelPolicy.Any(),
            ImmutableArray.Create<IrNode>(Action("A"), Action("B")));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Contains("new ParallelNode(new Crisp.Runtime.ParallelPolicy.Any()", result);
        Assert.Contains("new ActionNode(() => this.A(), \"A()\")", result);
        Assert.Contains("new ActionNode(() => this.B(), \"B()\")", result);
    }

    [Fact]
    public void AllポリシーのパラレルがParallelNode構築式に変換される()
    {
        var node = new IrParallel(
            new ParallelPolicy.All(),
            ImmutableArray.Create<IrNode>(Action("A"), Action("B")));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Contains("new ParallelNode(new Crisp.Runtime.ParallelPolicy.All()", result);
    }

    [Fact]
    public void Nポリシーのパラレルがカウント付きParallelNode構築式に変換される()
    {
        var node = new IrParallel(
            new ParallelPolicy.N(2),
            ImmutableArray.Create<IrNode>(Action("A"), Action("B"), Action("C")));

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Contains("new ParallelNode(new Crisp.Runtime.ParallelPolicy.N(2)", result);
    }

    // =================================================================
    //  13. 完全なファイル生成
    // =================================================================

    [Fact]
    public void 完全なCSharpソースファイルが正しい構造で生成される()
    {
        // Arrange: 単純なセレクターツリー
        var tree = new IrTree("SimpleCombat", ImmutableArray.Create<IrNode>(
            new IrSelector(ImmutableArray.Create<IrNode>(
                Action("Flee"),
                Action("Patrol")))));

        // Act
        var result = CSharpEmitter.Emit(tree, "Game.AI", "EnemyAI", "BuildCombatTree");

        // Assert: ヘッダー
        Assert.Contains("// <auto-generated by Crisp Source Generator/>", result);
        Assert.Contains("#nullable enable", result);

        // Assert: using 宣言
        Assert.Contains("using Crisp.Runtime.Nodes;", result);

        // Assert: namespace 宣言
        Assert.Contains("namespace Game.AI;", result);

        // Assert: partial class 宣言
        Assert.Contains("partial class EnemyAI", result);

        // Assert: メソッドシグネチャ
        Assert.Contains("public partial Crisp.Runtime.BtNode BuildCombatTree()", result);

        // Assert: return 文内にノード構築式が含まれる
        Assert.Contains("return new SelectorNode(", result);
        Assert.Contains("new ActionNode(() => this.Flee(), \"Flee()\")", result);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
    }

    [Fact]
    public void 単一BodyノードのツリーがSequenceでラップされない()
    {
        // Arrange: Body が単一ノードのツリー
        var tree = new IrTree("Simple", ImmutableArray.Create<IrNode>(
            Action("Patrol")));

        // Act
        var result = CSharpEmitter.Emit(tree, "Game", "Agent", "Build");

        // Assert: SequenceNode でラップされず、直接 ActionNode が返される
        Assert.Contains("return new ActionNode(() => this.Patrol(), \"Patrol()\");", result);
        Assert.DoesNotContain("SequenceNode", result);
    }

    // =================================================================
    //  14. ネストした複合ツリー
    // =================================================================

    [Fact]
    public void ネストした複合ツリーが正しくインデントされて出力される()
    {
        // Arrange: セレクター > シーケンス（チェック + アクション）+ アクション
        // (select
        //   (seq (check (< .Health 30)) (.Flee))
        //   (.Patrol))
        var tree = new IrTree("Combat", ImmutableArray.Create<IrNode>(
            new IrSelector(ImmutableArray.Create<IrNode>(
                new IrSequence(ImmutableArray.Create<IrNode>(
                    new IrCondition(
                        new IrBinaryOp(
                            BinaryOp.Lt,
                            new IrMemberLoad(
                                new MemberRef(ImmutableArray.Create("Health")))
                                { TypeRef = TypeRef.Int },
                            IntLit(30))
                            { TypeRef = TypeRef.Bool }),
                    Action("Flee"))),
                Action("Patrol")))));

        // Act
        var result = CSharpEmitter.Emit(tree, "Game.AI", "EnemyAI", "BuildTree");

        // Assert: 全体構造が含まれる
        Assert.Contains("new SelectorNode(", result);
        Assert.Contains("new SequenceNode(", result);
        Assert.Contains("new ConditionNode(() => (this.Health < 30), \".Health < 30\")", result);
        Assert.Contains("new ActionNode(() => this.Flee(), \"Flee()\")", result);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
    }

    [Fact]
    public void 深くネストしたデコレータツリーが正しく出力される()
    {
        // Arrange: guard > invert > repeat > timeout > action
        var node = new IrGuard(
            Member("IsAlive"),
            new IrInvert(
                new IrRepeat(3,
                    new IrTimeout(5.0f,
                        Action("Search")))));

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: 各デコレータが正しくネストされている
        Assert.Contains("new GuardNode(() => this.IsAlive", result);
        Assert.Contains("new InvertNode(", result);
        Assert.Contains("new RepeatNode(3, ", result);
        Assert.Contains("new TimeoutNode(5f, ", result);
        Assert.Contains("new ActionNode(() => this.Search(), \"Search()\")", result);
    }

    // =================================================================
    //  15. 式の複合パターン
    // =================================================================

    [Fact]
    public void 二項演算にIrConvertが含まれる複合式が正しく出力される()
    {
        // Arrange: this.Health < (float)30
        var expr = new IrBinaryOp(
            BinaryOp.Lt,
            new IrMemberLoad(
                new MemberRef(ImmutableArray.Create("Health")))
                { TypeRef = TypeRef.Float },
            new IrConvert(IntLit(30), TypeRef.Float) { TypeRef = TypeRef.Float })
            { TypeRef = TypeRef.Bool };

        // Act
        var result = CSharpEmitter.EmitExpressionString(expr);

        // Assert: キャストが演算式内に正しく埋め込まれている
        Assert.Equal("(this.Health < ((float)30))", result);
    }

    [Fact]
    public void 論理演算と比較演算の組み合わせが正しく出力される()
    {
        // Arrange: this.A && (this.X > 10) && this.B
        var expr = new IrLogicOp(
            LogicOp.And,
            ImmutableArray.Create<IrExpr>(
                Member("A"),
                new IrBinaryOp(BinaryOp.Gt, Member("X"), IntLit(10)) { TypeRef = TypeRef.Bool },
                Member("B")))
            { TypeRef = TypeRef.Bool };

        // Act
        var result = CSharpEmitter.EmitExpressionString(expr);

        // Assert: 論理演算の各項が正しくフォーマットされている
        Assert.Equal("(this.A && (this.X > 10) && this.B)", result);
    }

    // =================================================================
    //  サブツリー埋め込み（BtNode 戻りメソッド）
    // =================================================================

    [Fact]
    public void サブツリーアクションがActionNodeラッパーなしで直接呼び出しに変換される()
    {
        // Arrange: IsSubtree=true の引数なしアクション
        var node = new IrAction(
            new MethodRef("TestClass", "BuildSubtree", ImmutableArray<string>.Empty),
            ImmutableArray<IrExpr>.Empty,
            IsSubtree: true);

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: ActionNode でラップされず、直接メソッド呼び出し
        Assert.Equal("this.BuildSubtree()", result);
    }

    [Fact]
    public void 引数ありサブツリーアクションが直接呼び出しに変換される()
    {
        // Arrange: IsSubtree=true の引数ありアクション
        var node = new IrAction(
            new MethodRef("TestClass", "BuildCombat", ImmutableArray<string>.Empty),
            ImmutableArray.Create<IrExpr>(Member("Target")),
            IsSubtree: true);

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert: 引数付き直接呼び出し
        Assert.Equal("this.BuildCombat(this.Target)", result);
    }
}
