using System.Collections.Immutable;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// IR 最適化パスのユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>各最適化パスを個別にテストし、正しく変換されることを検証する</description></item>
///   <item><description>最適化対象外のノードが変更されないことも検証する</description></item>
///   <item><description>IrPassManager による複合最適化を検証する</description></item>
/// </list>
/// </summary>
public class IrOptimizerTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>int リテラルを生成する。</summary>
    private static IrLiteral IntLit(int value) =>
        new(value, LiteralKind.Int) { TypeRef = TypeRef.Int };

    /// <summary>float リテラルを生成する。</summary>
    private static IrLiteral FloatLit(float value) =>
        new(value, LiteralKind.Float) { TypeRef = TypeRef.Float };

    /// <summary>bool リテラルを生成する。</summary>
    private static IrLiteral BoolLit(bool value) =>
        new(value, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

    /// <summary>アクションノードを生成する。</summary>
    private static IrAction MakeAction(string name) =>
        new(new MethodRef("this", name, ImmutableArray<string>.Empty),
            ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>メンバーロード式を生成する。</summary>
    private static IrMemberLoad MakeMember(string name) =>
        new(new MemberRef(ImmutableArray.Create(name))) { TypeRef = TypeRef.Int };

    // ═══════════════════════════════════════════════
    //  1. 定数畳み込み (ConstantFoldingPass)
    // ═══════════════════════════════════════════════

    [Fact]
    public void 定数畳み込み_整数加算のリテラルが畳み込まれる()
    {
        // Arrange: (+ 10 20)
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(10), IntLit(20))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(add);

        // Assert: literal 30
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(30, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
        Assert.Equal(TypeRef.Int, lit.TypeRef);
    }

    [Fact]
    public void 定数畳み込み_整数乗算のリテラルが畳み込まれる()
    {
        // Arrange: (* 3 4)
        var mul = new IrBinaryOp(BinaryOp.Mul, IntLit(3), IntLit(4))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(mul);

        // Assert: literal 12
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(12, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
    }

    [Fact]
    public void 定数畳み込み_整数比較のリテラルが畳み込まれる()
    {
        // Arrange: (< 5 10)
        var lt = new IrBinaryOp(BinaryOp.Lt, IntLit(5), IntLit(10))
            { TypeRef = TypeRef.Bool };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(lt);

        // Assert: literal true
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(true, lit.Value);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
        Assert.Equal(TypeRef.Bool, lit.TypeRef);
    }

    [Fact]
    public void 定数畳み込み_notの単項演算が畳み込まれる()
    {
        // Arrange: (not true)
        var notOp = new IrUnaryOp(UnaryOp.Not, BoolLit(true))
            { TypeRef = TypeRef.Bool };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(notOp);

        // Assert: literal false
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(false, lit.Value);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
    }

    [Fact]
    public void 定数畳み込み_negateの単項演算が畳み込まれる()
    {
        // Arrange: (- 5)
        var neg = new IrUnaryOp(UnaryOp.Negate, IntLit(5))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(neg);

        // Assert: literal -5
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(-5, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
    }

    [Fact]
    public void 定数畳み込み_float加算のリテラルが畳み込まれる()
    {
        // Arrange: (+ 1.5 2.5)
        var add = new IrBinaryOp(BinaryOp.Add, FloatLit(1.5f), FloatLit(2.5f))
            { TypeRef = TypeRef.Float };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(add);

        // Assert: literal 4.0
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(4.0f, lit.Value);
        Assert.Equal(LiteralKind.Float, lit.Kind);
        Assert.Equal(TypeRef.Float, lit.TypeRef);
    }

    [Fact]
    public void 定数畳み込み_ゼロ除算は畳み込まれない()
    {
        // Arrange: (/ 10 0)
        var div = new IrBinaryOp(BinaryOp.Div, IntLit(10), IntLit(0))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(div);

        // Assert: IrBinaryOp のまま変化しない
        var binOp = Assert.IsType<IrBinaryOp>(result);
        Assert.Equal(BinaryOp.Div, binOp.Op);
    }

    [Fact]
    public void 定数畳み込み_非リテラルオペランドは畳み込まれない()
    {
        // Arrange: (+ .Health 10) — 左辺がメンバーアクセス
        var member = MakeMember("Health");
        var add = new IrBinaryOp(BinaryOp.Add, member, IntLit(10))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(add);

        // Assert: IrBinaryOp のまま変化しない
        var binOp = Assert.IsType<IrBinaryOp>(result);
        Assert.Equal(BinaryOp.Add, binOp.Op);
        Assert.IsType<IrMemberLoad>(binOp.Lhs);
        Assert.IsType<IrLiteral>(binOp.Rhs);
    }

    [Fact]
    public void 定数畳み込み_ネストされた式が再帰的に畳み込まれる()
    {
        // Arrange: (+ (* 2 3) (* 4 5)) → (+ 6 20) → 26
        var lhs = new IrBinaryOp(BinaryOp.Mul, IntLit(2), IntLit(3))
            { TypeRef = TypeRef.Int };
        var rhs = new IrBinaryOp(BinaryOp.Mul, IntLit(4), IntLit(5))
            { TypeRef = TypeRef.Int };
        var add = new IrBinaryOp(BinaryOp.Add, lhs, rhs)
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(add);

        // Assert: literal 26
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(26, lit.Value);
    }

    [Fact]
    public void 定数畳み込み_Conditionノード内の式が畳み込まれる()
    {
        // Arrange: (check (+ 1 2)) — Condition 内の二項演算
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(1), IntLit(2))
            { TypeRef = TypeRef.Int };
        var condition = new IrCondition(add);
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(condition);

        // Assert: IrCondition の中の式がリテラル 3 に畳み込まれる
        var cond = Assert.IsType<IrCondition>(result);
        var lit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void 定数畳み込み_floatのゼロ除算は畳み込まれない()
    {
        // Arrange: (/ 1.0 0.0)
        var div = new IrBinaryOp(BinaryOp.Div, FloatLit(1.0f), FloatLit(0.0f))
            { TypeRef = TypeRef.Float };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(div);

        // Assert: IrBinaryOp のまま変化しない
        Assert.IsType<IrBinaryOp>(result);
    }

    [Fact]
    public void 定数畳み込み_整数減算のリテラルが畳み込まれる()
    {
        // Arrange: (- 20 7)
        var sub = new IrBinaryOp(BinaryOp.Sub, IntLit(20), IntLit(7))
            { TypeRef = TypeRef.Int };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(sub);

        // Assert: literal 13
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(13, lit.Value);
    }

    [Fact]
    public void 定数畳み込み_float否定が畳み込まれる()
    {
        // Arrange: (- 3.14)
        var neg = new IrUnaryOp(UnaryOp.Negate, FloatLit(3.14f))
            { TypeRef = TypeRef.Float };
        var pass = new ConstantFoldingPass();

        // Act
        var result = pass.Run(neg);

        // Assert: literal -3.14
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(-3.14f, lit.Value);
        Assert.Equal(LiteralKind.Float, lit.Kind);
    }

    // ═══════════════════════════════════════════════
    //  2. 到達不能ノード除去 (DeadNodeEliminationPass)
    // ═══════════════════════════════════════════════

    [Fact]
    public void 到達不能除去_Sequence先頭のfalseチェック以降が除去される()
    {
        // Arrange: (seq (check false) (action "Attack") (action "Patrol"))
        var checkFalse = new IrCondition(BoolLit(false));
        var attack = MakeAction("Attack");
        var patrol = MakeAction("Patrol");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(checkFalse, attack, patrol));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(seq);

        // Assert: check false のみ残る（単一子なので IrCondition に畳み込まれる）
        var cond = Assert.IsType<IrCondition>(result);
        var lit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(false, lit.Value);
    }

    [Fact]
    public void 到達不能除去_Selector先頭のtrueチェック以降が除去される()
    {
        // Arrange: (select (check true) (action "Attack") (action "Patrol"))
        var checkTrue = new IrCondition(BoolLit(true));
        var attack = MakeAction("Attack");
        var patrol = MakeAction("Patrol");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(checkTrue, attack, patrol));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(sel);

        // Assert: check true のみ残る（単一子なので IrCondition に畳み込まれる）
        var cond = Assert.IsType<IrCondition>(result);
        var lit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(true, lit.Value);
    }

    [Fact]
    public void 到達不能除去_通常のSequenceは変更されない()
    {
        // Arrange: (seq (action "Patrol") (action "Attack")) — 到達不能ノードなし
        var patrol = MakeAction("Patrol");
        var attack = MakeAction("Attack");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(patrol, attack));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(seq);

        // Assert: IrSequence のまま子が2つとも残る
        var seqResult = Assert.IsType<IrSequence>(result);
        Assert.Equal(2, seqResult.Children.Length);
    }

    [Fact]
    public void 到達不能除去_通常のSelectorは変更されない()
    {
        // Arrange: (select (action "Patrol") (action "Attack")) — 到達不能ノードなし
        var patrol = MakeAction("Patrol");
        var attack = MakeAction("Attack");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(patrol, attack));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(sel);

        // Assert: IrSelector のまま子が2つとも残る
        var selResult = Assert.IsType<IrSelector>(result);
        Assert.Equal(2, selResult.Children.Length);
    }

    [Fact]
    public void 到達不能除去_Sequence中間のfalseチェック以降が除去される()
    {
        // Arrange: (seq (action "Patrol") (check false) (action "Attack"))
        var patrol = MakeAction("Patrol");
        var checkFalse = new IrCondition(BoolLit(false));
        var attack = MakeAction("Attack");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(patrol, checkFalse, attack));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(seq);

        // Assert: Patrol と check false のみ残る
        var seqResult = Assert.IsType<IrSequence>(result);
        Assert.Equal(2, seqResult.Children.Length);
        Assert.IsType<IrAction>(seqResult.Children[0]);
        Assert.IsType<IrCondition>(seqResult.Children[1]);
    }

    [Fact]
    public void 到達不能除去_ネストされたSequence内でも除去が適用される()
    {
        // Arrange: (select (seq (check false) (action "Attack")) (action "Patrol"))
        var checkFalse = new IrCondition(BoolLit(false));
        var attack = MakeAction("Attack");
        var innerSeq = new IrSequence(ImmutableArray.Create<IrNode>(checkFalse, attack));
        var patrol = MakeAction("Patrol");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(innerSeq, patrol));
        var pass = new DeadNodeEliminationPass();

        // Act
        var result = pass.Run(sel);

        // Assert: 内側の seq が check false のみに畳み込まれる
        var selResult = Assert.IsType<IrSelector>(result);
        Assert.Equal(2, selResult.Children.Length);
        // 内側: 単一子なので IrCondition に畳み込まれる
        var innerCond = Assert.IsType<IrCondition>(selResult.Children[0]);
        Assert.Equal(false, ((IrLiteral)innerCond.Expr).Value);
    }

    // ═══════════════════════════════════════════════
    //  3. 単一子ノード畳み込み (SingleChildCollapsePass)
    // ═══════════════════════════════════════════════

    [Fact]
    public void 単一子畳み込み_Selectorの単一子が畳み込まれる()
    {
        // Arrange: (select (action "Patrol"))
        var patrol = MakeAction("Patrol");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(patrol));
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(sel);

        // Assert: IrAction "Patrol" に畳み込まれる
        var action = Assert.IsType<IrAction>(result);
        Assert.Equal("Patrol", action.Method.Name);
    }

    [Fact]
    public void 単一子畳み込み_Sequenceの単一子が畳み込まれる()
    {
        // Arrange: (seq (action "Patrol"))
        var patrol = MakeAction("Patrol");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(patrol));
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(seq);

        // Assert: IrAction "Patrol" に畳み込まれる
        var action = Assert.IsType<IrAction>(result);
        Assert.Equal("Patrol", action.Method.Name);
    }

    [Fact]
    public void 単一子畳み込み_二重Invertが除去される()
    {
        // Arrange: (invert (invert (action "Patrol")))
        var patrol = MakeAction("Patrol");
        var inner = new IrInvert(patrol);
        var outer = new IrInvert(inner);
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(outer);

        // Assert: IrAction "Patrol" に畳み込まれる
        var action = Assert.IsType<IrAction>(result);
        Assert.Equal("Patrol", action.Method.Name);
    }

    [Fact]
    public void 単一子畳み込み_複数子のSelectorは変更されない()
    {
        // Arrange: (select (action "Patrol") (action "Attack"))
        var patrol = MakeAction("Patrol");
        var attack = MakeAction("Attack");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(patrol, attack));
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(sel);

        // Assert: IrSelector のまま2つの子を持つ
        var selResult = Assert.IsType<IrSelector>(result);
        Assert.Equal(2, selResult.Children.Length);
    }

    [Fact]
    public void 単一子畳み込み_複数子のSequenceは変更されない()
    {
        // Arrange: (seq (action "Patrol") (action "Attack"))
        var patrol = MakeAction("Patrol");
        var attack = MakeAction("Attack");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(patrol, attack));
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(seq);

        // Assert: IrSequence のまま2つの子を持つ
        var seqResult = Assert.IsType<IrSequence>(result);
        Assert.Equal(2, seqResult.Children.Length);
    }

    [Fact]
    public void 単一子畳み込み_単一Invertは変更されない()
    {
        // Arrange: (invert (action "Patrol"))
        var patrol = MakeAction("Patrol");
        var inv = new IrInvert(patrol);
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(inv);

        // Assert: IrInvert のまま
        var invResult = Assert.IsType<IrInvert>(result);
        Assert.IsType<IrAction>(invResult.Child);
    }

    [Fact]
    public void 単一子畳み込み_ネストされた単一子Selectorが再帰的に畳み込まれる()
    {
        // Arrange: (select (seq (action "Patrol")))
        var patrol = MakeAction("Patrol");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(patrol));
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(seq));
        var pass = new SingleChildCollapsePass();

        // Act
        var result = pass.Run(sel);

        // Assert: IrAction "Patrol" に畳み込まれる
        var action = Assert.IsType<IrAction>(result);
        Assert.Equal("Patrol", action.Method.Name);
    }

    // ═══════════════════════════════════════════════
    //  4. 型変換統合 (ConvertFusionPass)
    // ═══════════════════════════════════════════════

    [Fact]
    public void 型変換統合_二重変換が統合される()
    {
        // Arrange: (convert (convert expr float) float)
        var member = MakeMember("Value");
        var inner = new IrConvert(member, TypeRef.Float) { TypeRef = TypeRef.Float };
        var outer = new IrConvert(inner, TypeRef.Float) { TypeRef = TypeRef.Float };
        var pass = new ConvertFusionPass();

        // Act
        var result = pass.Run(outer);

        // Assert: (convert expr float) — 二重変換が1つに統合される
        var conv = Assert.IsType<IrConvert>(result);
        Assert.IsType<IrMemberLoad>(conv.Operand);
        Assert.Equal(TypeRef.Float, conv.TargetType);
    }

    [Fact]
    public void 型変換統合_intリテラルのfloat変換がインライン化される()
    {
        // Arrange: (convert (literal 42 int) float)
        var lit42 = IntLit(42);
        var conv = new IrConvert(lit42, TypeRef.Float) { TypeRef = TypeRef.Float };
        var pass = new ConvertFusionPass();

        // Act
        var result = pass.Run(conv);

        // Assert: (literal 42.0 float) — リテラルが直接 float に変換される
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(42.0f, lit.Value);
        Assert.Equal(LiteralKind.Float, lit.Kind);
        Assert.Equal(TypeRef.Float, lit.TypeRef);
    }

    [Fact]
    public void 型変換統合_通常の変換は変更されない()
    {
        // Arrange: (convert .Health float) — メンバーアクセスの変換
        var member = MakeMember("Health");
        var conv = new IrConvert(member, TypeRef.Float) { TypeRef = TypeRef.Float };
        var pass = new ConvertFusionPass();

        // Act
        var result = pass.Run(conv);

        // Assert: IrConvert のまま
        var convResult = Assert.IsType<IrConvert>(result);
        Assert.IsType<IrMemberLoad>(convResult.Operand);
        Assert.Equal(TypeRef.Float, convResult.TargetType);
    }

    [Fact]
    public void 型変換統合_Conditionノード内の変換が統合される()
    {
        // Arrange: (check (convert (convert .Value float) float))
        var member = MakeMember("Value");
        var inner = new IrConvert(member, TypeRef.Float) { TypeRef = TypeRef.Float };
        var outer = new IrConvert(inner, TypeRef.Float) { TypeRef = TypeRef.Float };
        var cond = new IrCondition(outer);
        var pass = new ConvertFusionPass();

        // Act
        var result = pass.Run(cond);

        // Assert: Condition 内の二重変換が統合される
        var condResult = Assert.IsType<IrCondition>(result);
        var conv = Assert.IsType<IrConvert>(condResult.Expr);
        Assert.IsType<IrMemberLoad>(conv.Operand);
    }

    [Fact]
    public void 型変換統合_三重変換が二重に削減される()
    {
        // Arrange: (convert (convert (convert .Value float) float) float)
        // ConvertFusion は内側から走査するので、最内 convert(member, float) はそのまま、
        // 中間は内側と統合、外側は中間統合結果と統合 → 最終的に1つの convert
        var member = MakeMember("Value");
        var c1 = new IrConvert(member, TypeRef.Float) { TypeRef = TypeRef.Float };
        var c2 = new IrConvert(c1, TypeRef.Float) { TypeRef = TypeRef.Float };
        var c3 = new IrConvert(c2, TypeRef.Float) { TypeRef = TypeRef.Float };
        var pass = new ConvertFusionPass();

        // Act
        var result = pass.Run(c3);

        // Assert: 最終的に (convert .Value float) に統合される
        var conv = Assert.IsType<IrConvert>(result);
        Assert.IsType<IrMemberLoad>(conv.Operand);
        Assert.Equal(TypeRef.Float, conv.TargetType);
    }

    // ═══════════════════════════════════════════════
    //  5. IrPassManager (複合最適化)
    // ═══════════════════════════════════════════════

    [Fact]
    public void パスマネージャ_定数畳み込みが適用される()
    {
        // Arrange: (+ 1 2)
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(1), IntLit(2))
            { TypeRef = TypeRef.Int };
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(add);

        // Assert: literal 3
        var lit = Assert.IsType<IrLiteral>(result);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void パスマネージャ_Sequence内の定数畳み込みが適用される()
    {
        // Arrange: (seq (check (+ 1 2)) (action "Do"))
        // → 定数畳み込み後: (seq (check 3) (action "Do"))
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(1), IntLit(2))
            { TypeRef = TypeRef.Int };
        var check = new IrCondition(add);
        var doAction = MakeAction("Do");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(check, doAction));
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(seq);

        // Assert: Sequence は維持され、check の式がリテラル 3 に畳み込まれる
        var seqResult = Assert.IsType<IrSequence>(result);
        Assert.Equal(2, seqResult.Children.Length);
        var cond = Assert.IsType<IrCondition>(seqResult.Children[0]);
        var lit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void パスマネージャ_定数畳み込みと到達不能除去が連携する()
    {
        // Arrange: (seq (check (< 10 5)) (action "Attack"))
        // → 定数畳み込み: (seq (check false) (action "Attack"))
        // → 到達不能除去: (check false)
        var lt = new IrBinaryOp(BinaryOp.Lt, IntLit(10), IntLit(5))
            { TypeRef = TypeRef.Bool };
        var check = new IrCondition(lt);
        var attack = MakeAction("Attack");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(check, attack));
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(seq);

        // Assert: check false のみ残る
        var cond = Assert.IsType<IrCondition>(result);
        var boolLit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(false, boolLit.Value);
    }

    [Fact]
    public void パスマネージャ_単一子Selectorが畳み込まれる()
    {
        // Arrange: (select (action "Patrol"))
        var patrol = MakeAction("Patrol");
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(patrol));
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(sel);

        // Assert: IrAction "Patrol" に畳み込まれる
        var action = Assert.IsType<IrAction>(result);
        Assert.Equal("Patrol", action.Method.Name);
    }

    [Fact]
    public void パスマネージャ_全パスが連携する複合ケース()
    {
        // Arrange: (select (seq (check (not false)) (action "Attack")))
        // Pass 1 (定数畳み込み): (select (seq (check true) (action "Attack")))
        // Pass 2 (到達不能除去): selector 内の seq は通常動作（true チェックは Selector ではない）
        // Pass 3 (単一子畳み込み): (select ...) の子は1つだけなので外側 selector 畳み込み
        var notFalse = new IrUnaryOp(UnaryOp.Not, BoolLit(false))
            { TypeRef = TypeRef.Bool };
        var check = new IrCondition(notFalse);
        var attack = MakeAction("Attack");
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(check, attack));
        var sel = new IrSelector(ImmutableArray.Create<IrNode>(seq));
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(sel);

        // Assert: 外側 selector が畳み込まれ、seq の中の check が true リテラルに
        var seqResult = Assert.IsType<IrSequence>(result);
        var cond = Assert.IsType<IrCondition>(seqResult.Children[0]);
        var boolLitTrue = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(true, boolLitTrue.Value);
    }

    [Fact]
    public void パスマネージャ_OptimizeTreeがツリー全体を最適化する()
    {
        // Arrange: IrTree に (+ 10 20) を含む Condition を配置
        var add = new IrBinaryOp(BinaryOp.Add, IntLit(10), IntLit(20))
            { TypeRef = TypeRef.Int };
        var check = new IrCondition(add);
        var tree = new IrTree("TestTree", ImmutableArray.Create<IrNode>(check))
            { Id = 1 };
        var manager = new IrPassManager();

        // Act
        var result = manager.OptimizeTree(tree);

        // Assert: ツリー名と Id が維持され、中身が最適化される
        Assert.Equal("TestTree", result.Name);
        Assert.Equal(1, result.Id);
        var cond = Assert.IsType<IrCondition>(result.Body[0]);
        var lit = Assert.IsType<IrLiteral>(cond.Expr);
        Assert.Equal(30, lit.Value);
    }

    [Fact]
    public void パスマネージャ_型変換統合とリテラルインライン化が連携する()
    {
        // Arrange: (convert (literal 100 int) float) を含む Condition
        var lit100 = IntLit(100);
        var conv = new IrConvert(lit100, TypeRef.Float) { TypeRef = TypeRef.Float };
        var cond = new IrCondition(conv);
        var manager = new IrPassManager();

        // Act
        var result = manager.Optimize(cond);

        // Assert: convert がインライン化され、float リテラル 100.0 になる
        var condResult = Assert.IsType<IrCondition>(result);
        var floatLit = Assert.IsType<IrLiteral>(condResult.Expr);
        Assert.Equal(100.0f, floatLit.Value);
        Assert.Equal(LiteralKind.Float, floatLit.Kind);
    }
}
