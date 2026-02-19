using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Tests;

/// <summary>
/// CST → AST Lowering のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// 重要な検証ポイント:
/// - CstCall がノード位置では AstActionCall に、式位置では AstCallExpr に変換される
/// - 括弧なし CstMemberAccess がノード位置では引数なし AstActionCall に変換される
/// - リテラルの値が正しくパースされる
/// - CstError / CstMissing はスキップされる
/// </summary>
public class LoweringTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー
    // ──────────────────────────────────────────────

    private static AstTree LowerFirst(string source)
    {
        var (program, _) = Parser.Parse(source);
        var trees = CstToAstLowering.Lower(program);
        return trees[0];
    }

    // ═══════════════════════════════════════════════
    //  1. 最小のツリー
    // ═══════════════════════════════════════════════

    [Fact]
    public void 括弧なしMemberAccessはAstActionCallに変換される()
    {
        var tree = LowerFirst("(tree T .Patrol)");

        Assert.Equal("T", tree.Name);
        var action = Assert.IsType<AstActionCall>(tree.Body[0]);
        Assert.Equal("Patrol", action.Member.Segments[0]);
        Assert.Empty(action.Args);
    }

    [Fact]
    public void 括弧付き引数なしCstCallはAstActionCallに変換される()
    {
        var tree = LowerFirst("(tree T (.Patrol))");

        var action = Assert.IsType<AstActionCall>(tree.Body[0]);
        Assert.Equal("Patrol", action.Member.Segments[0]);
        Assert.Empty(action.Args);
    }

    // ═══════════════════════════════════════════════
    //  2. 複合ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void selectノードがAstSelectorに変換される()
    {
        var tree = LowerFirst("(tree T (select .A .B))");

        var selector = Assert.IsType<AstSelector>(tree.Body[0]);
        Assert.Equal(2, selector.Children_.Length);
    }

    [Fact]
    public void seqノードがAstSequenceに変換される()
    {
        var tree = LowerFirst("(tree T (seq .A .B))");

        Assert.IsType<AstSequence>(tree.Body[0]);
    }

    [Fact]
    public void parallelノードのポリシーが正しく変換される()
    {
        var tree = LowerFirst("(tree T (parallel :any .A .B))");
        var parallel = Assert.IsType<AstParallel>(tree.Body[0]);
        Assert.IsType<ParallelPolicy.Any>(parallel.Policy);

        tree = LowerFirst("(tree T (parallel :all .A .B))");
        parallel = Assert.IsType<AstParallel>(tree.Body[0]);
        Assert.IsType<ParallelPolicy.All>(parallel.Policy);

        tree = LowerFirst("(tree T (parallel :n 2 .A .B .C))");
        parallel = Assert.IsType<AstParallel>(tree.Body[0]);
        var nPolicy = Assert.IsType<ParallelPolicy.N>(parallel.Policy);
        Assert.Equal(2, nPolicy.Count);
    }

    // ═══════════════════════════════════════════════
    //  3. デコレータノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void checkノードの式がAstExprに変換される()
    {
        var tree = LowerFirst("(tree T (check (< .Health 30)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        Assert.Equal(BinaryOp.Lt, bin.Op);
    }

    [Fact]
    public void guardノードが正しく変換される()
    {
        var tree = LowerFirst("(tree T (guard .IsAlive (.Patrol)))");

        var guard = Assert.IsType<AstGuard>(tree.Body[0]);
        Assert.IsType<AstMemberAccessExpr>(guard.Condition);
        Assert.IsType<AstActionCall>(guard.Body);
    }

    [Fact]
    public void repeatのカウントが正しくパースされる()
    {
        var tree = LowerFirst("(tree T (repeat 5 .A))");

        var repeat = Assert.IsType<AstRepeat>(tree.Body[0]);
        Assert.Equal(5, repeat.Count);
    }

    [Fact]
    public void timeoutの秒数が正しくパースされる()
    {
        var tree = LowerFirst("(tree T (timeout 2.5 .A))");

        var timeout = Assert.IsType<AstTimeout>(tree.Body[0]);
        Assert.Equal(2.5f, timeout.Seconds);
    }

    // ═══════════════════════════════════════════════
    //  4. 式の lowering
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数リテラルが正しくパースされる()
    {
        var tree = LowerFirst("(tree T (check (< .X 42)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var lit = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal(LiteralKind.Int, lit.LiteralKind);
        Assert.Equal(42, lit.Value);
    }

    [Fact]
    public void 負のリテラルが正しくパースされる()
    {
        var tree = LowerFirst("(tree T (check (> .X -1)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var lit = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal(LiteralKind.Int, lit.LiteralKind);
        Assert.Equal(-1, lit.Value);
    }

    [Fact]
    public void 文字列リテラルのエスケープが処理される()
    {
        var tree = LowerFirst("(tree T (check (= .Name \"he\\\"llo\")))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var lit = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal("he\"llo", lit.Value);
    }

    [Fact]
    public void enumリテラルがEnumRefに変換される()
    {
        var tree = LowerFirst("(tree T (check (= .State ::AIState.Combat)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var lit = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal(LiteralKind.Enum, lit.LiteralKind);
        var enumRef = Assert.IsType<EnumRef>(lit.Value);
        Assert.Equal("AIState", enumRef.TypeName);
        Assert.Equal("Combat", enumRef.MemberName);
    }

    [Fact]
    public void boolリテラルが正しく変換される()
    {
        var tree = LowerFirst("(tree T (check (= .IsAlive true)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var lit = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal(true, lit.Value);
    }

    // ═══════════════════════════════════════════════
    //  5. ノード位置 vs 式位置の判定
    // ═══════════════════════════════════════════════

    [Fact]
    public void 式位置のCstCallはAstCallExprに変換される()
    {
        var tree = LowerFirst("(tree T (check (< (.DistanceTo .Target) 5.0)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var call = Assert.IsType<AstCallExpr>(bin.Lhs);
        Assert.Equal("DistanceTo", call.Member.Segments[0]);
        Assert.Single(call.Args);
    }

    [Fact]
    public void ノード位置の引数ありCstCallはAstActionCallに変換される()
    {
        var tree = LowerFirst("(tree T (.Attack .Target))");

        var action = Assert.IsType<AstActionCall>(tree.Body[0]);
        Assert.Equal("Attack", action.Member.Segments[0]);
        Assert.Single(action.Args);
        Assert.IsType<AstMemberAccessExpr>(action.Args[0]);
    }

    // ═══════════════════════════════════════════════
    //  6. 論理式
    // ═══════════════════════════════════════════════

    [Fact]
    public void and式がAstLogicExprに変換される()
    {
        var tree = LowerFirst("(tree T (check (and .A .B)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var logic = Assert.IsType<AstLogicExpr>(check.Expr);
        Assert.Equal(LogicOp.And, logic.Op);
        Assert.Equal(2, logic.Operands.Length);
    }

    [Fact]
    public void not式がAstUnaryExprに変換される()
    {
        var tree = LowerFirst("(tree T (check (not .A)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var unary = Assert.IsType<AstUnaryExpr>(check.Expr);
        Assert.Equal(UnaryOp.Not, unary.Op);
    }

    // ═══════════════════════════════════════════════
    //  7. メンバーチェーン
    // ═══════════════════════════════════════════════

    [Fact]
    public void メンバーチェーンがMemberPathに変換される()
    {
        var tree = LowerFirst("(tree T (check (> .Target.Health 0)))");

        var check = Assert.IsType<AstCheck>(tree.Body[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        var member = Assert.IsType<AstMemberAccessExpr>(bin.Lhs);
        Assert.Equal(2, member.Path.Segments.Length);
        Assert.Equal("Target", member.Path.Segments[0]);
        Assert.Equal("Health", member.Path.Segments[1]);
    }

    // ═══════════════════════════════════════════════
    //  8. Pattern 1: Minimal 全構造の lowering
    // ═══════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalの全構造をloweringできる()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;

        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);

        var trees = CstToAstLowering.Lower(program);
        Assert.Single(trees);

        var tree = trees[0];
        Assert.Equal("SimpleCombat", tree.Name);

        var selector = Assert.IsType<AstSelector>(tree.Body[0]);
        Assert.Equal(2, selector.Children_.Length);

        var seq = Assert.IsType<AstSequence>(selector.Children_[0]);
        var check = Assert.IsType<AstCheck>(seq.Children_[0]);
        var bin = Assert.IsType<AstBinaryExpr>(check.Expr);
        Assert.Equal(BinaryOp.Lt, bin.Op);

        var healthAccess = Assert.IsType<AstMemberAccessExpr>(bin.Lhs);
        Assert.Equal("Health", healthAccess.Path.Segments[0]);

        var literal30 = Assert.IsType<AstLiteralExpr>(bin.Rhs);
        Assert.Equal(30, literal30.Value);

        // .Flee は括弧付き引数なしアクション
        var flee = Assert.IsType<AstActionCall>(seq.Children_[1]);
        Assert.Equal("Flee", flee.Member.Segments[0]);
        Assert.Empty(flee.Args);

        // .Patrol は括弧付き引数なしアクション
        var patrol = Assert.IsType<AstActionCall>(selector.Children_[1]);
        Assert.Equal("Patrol", patrol.Member.Segments[0]);
        Assert.Empty(patrol.Args);
    }
}
