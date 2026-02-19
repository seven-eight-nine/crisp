using Crisp.Syntax;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Tests;

/// <summary>
/// Parser のユニットテスト。
/// t-wada 式 TDD に基づき、最小の入力から段階的に複雑さを増す。
/// CST のロスレス性（ソース復元）も合わせて検証する。
/// </summary>
public class ParserTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー
    // ──────────────────────────────────────────────

    private static (CstProgram program, DiagnosticBag diagnostics) Parse(string source)
    {
        return Parser.Parse(source);
    }

    // ═══════════════════════════════════════════════
    //  1. 空入力
    // ═══════════════════════════════════════════════

    [Fact]
    public void 空文字列をパースすると空のプログラムを返す()
    {
        var (program, diagnostics) = Parse("");

        Assert.Empty(program.Trees);
        Assert.False(diagnostics.HasErrors);
    }

    // ═══════════════════════════════════════════════
    //  2. 最小のツリー定義
    // ═══════════════════════════════════════════════

    [Fact]
    public void 最小のツリー定義をパースできる()
    {
        var (program, diagnostics) = Parse("(tree T .Patrol)");

        Assert.False(diagnostics.HasErrors);
        Assert.Single(program.Trees);
        Assert.Equal("T", program.Trees[0].Name.Text);
        Assert.Single(program.Trees[0].Body);
        Assert.IsType<CstMemberAccess>(program.Trees[0].Body[0]);
    }

    // ═══════════════════════════════════════════════
    //  3. select ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void selectノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (select .A .B))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var select = Assert.IsType<CstSelect>(tree.Body[0]);
        Assert.Equal(2, select.Children_.Length);
    }

    // ═══════════════════════════════════════════════
    //  4. seq ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void seqノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (seq .A .B))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var seq = Assert.IsType<CstSequence>(tree.Body[0]);
        Assert.Equal(2, seq.Children_.Length);
    }

    // ═══════════════════════════════════════════════
    //  5. check ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void checkノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check .IsAlive))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var check = Assert.IsType<CstCheck>(tree.Body[0]);
        Assert.IsType<CstMemberAccess>(check.Expr);
    }

    // ═══════════════════════════════════════════════
    //  6. 二項式
    // ═══════════════════════════════════════════════

    [Fact]
    public void checkの中の二項式をパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check (< .Health 30)))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var check = Assert.IsType<CstCheck>(tree.Body[0]);
        var binExpr = Assert.IsType<CstBinaryExpr>(check.Expr);
        Assert.Equal("<", binExpr.Operator.Text);
        Assert.IsType<CstMemberAccess>(binExpr.Lhs);
        Assert.IsType<CstIntLiteral>(binExpr.Rhs);
    }

    // ═══════════════════════════════════════════════
    //  7. 呼び出しノード（アクション）
    // ═══════════════════════════════════════════════

    [Fact]
    public void 引数ありの呼び出しをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (.Attack .Target))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var call = Assert.IsType<CstCall>(tree.Body[0]);
        Assert.Equal("Attack", call.Member.Segments[0]);
        Assert.Single(call.Args);
    }

    [Fact]
    public void 引数なしの括弧付き呼び出しをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (.Patrol))");

        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var call = Assert.IsType<CstCall>(tree.Body[0]);
        Assert.Equal("Patrol", call.Member.Segments[0]);
        Assert.Empty(call.Args);
    }

    // ═══════════════════════════════════════════════
    //  8. デコレータノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void guardノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (guard .IsAlive .Patrol))");

        Assert.False(diagnostics.HasErrors);
        var guard = Assert.IsType<CstGuard>(program.Trees[0].Body[0]);
        Assert.IsType<CstMemberAccess>(guard.Condition);
        Assert.IsType<CstMemberAccess>(guard.Body);
    }

    [Fact]
    public void ifノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (if .IsAlive .A .B))");

        Assert.False(diagnostics.HasErrors);
        var ifNode = Assert.IsType<CstIf>(program.Trees[0].Body[0]);
        Assert.IsType<CstMemberAccess>(ifNode.Condition);
        Assert.IsType<CstMemberAccess>(ifNode.Then);
        Assert.IsType<CstMemberAccess>(ifNode.Else);
    }

    [Fact]
    public void elseなしのifノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (if .IsAlive .A))");

        Assert.False(diagnostics.HasErrors);
        var ifNode = Assert.IsType<CstIf>(program.Trees[0].Body[0]);
        Assert.Null(ifNode.Else);
    }

    [Fact]
    public void invertノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (invert .A))");

        Assert.False(diagnostics.HasErrors);
        Assert.IsType<CstInvert>(program.Trees[0].Body[0]);
    }

    [Fact]
    public void repeatノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (repeat 3 .A))");

        Assert.False(diagnostics.HasErrors);
        var repeat = Assert.IsType<CstRepeat>(program.Trees[0].Body[0]);
        Assert.Equal("3", repeat.Count.Text);
    }

    [Fact]
    public void timeoutノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (timeout 5.0 .A))");

        Assert.False(diagnostics.HasErrors);
        var timeout = Assert.IsType<CstTimeout>(program.Trees[0].Body[0]);
        Assert.Equal("5.0", timeout.Seconds.Text);
    }

    [Fact]
    public void cooldownノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (cooldown 2.5 .A))");

        Assert.False(diagnostics.HasErrors);
        Assert.IsType<CstCooldown>(program.Trees[0].Body[0]);
    }

    [Fact]
    public void whileノードをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (while .IsAlive .A))");

        Assert.False(diagnostics.HasErrors);
        Assert.IsType<CstWhile>(program.Trees[0].Body[0]);
    }

    // ═══════════════════════════════════════════════
    //  9. parallel ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void parallel_anyをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (parallel :any .A .B))");

        Assert.False(diagnostics.HasErrors);
        var parallel = Assert.IsType<CstParallel>(program.Trees[0].Body[0]);
        Assert.Equal(":any", parallel.Policy.Text);
        Assert.Null(parallel.PolicyArg);
    }

    [Fact]
    public void parallel_nをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (parallel :n 2 .A .B .C))");

        Assert.False(diagnostics.HasErrors);
        var parallel = Assert.IsType<CstParallel>(program.Trees[0].Body[0]);
        Assert.Equal(":n", parallel.Policy.Text);
        Assert.NotNull(parallel.PolicyArg);
        Assert.Equal("2", parallel.PolicyArg!.Value.Text);
        Assert.Equal(3, parallel.Children_.Length);
    }

    // ═══════════════════════════════════════════════
    //  10. 式ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void 論理式andをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check (and .A .B)))");

        Assert.False(diagnostics.HasErrors);
        var check = Assert.IsType<CstCheck>(program.Trees[0].Body[0]);
        var logic = Assert.IsType<CstLogicExpr>(check.Expr);
        Assert.Equal("and", logic.Operator.Text);
        Assert.Equal(2, logic.Operands.Length);
    }

    [Fact]
    public void not式をパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check (not .A)))");

        Assert.False(diagnostics.HasErrors);
        var check = Assert.IsType<CstCheck>(program.Trees[0].Body[0]);
        var unary = Assert.IsType<CstUnaryExpr>(check.Expr);
        Assert.Equal("not", unary.Operator.Text);
    }

    [Fact]
    public void enumリテラルをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check (= .State ::AIState.Combat)))");

        Assert.False(diagnostics.HasErrors);
        var check = Assert.IsType<CstCheck>(program.Trees[0].Body[0]);
        var bin = Assert.IsType<CstBinaryExpr>(check.Expr);
        var enumLit = Assert.IsType<CstEnumLiteral>(bin.Rhs);
        Assert.Equal("AIState", enumLit.TypeName);
        Assert.Equal("Combat", enumLit.MemberName);
    }

    [Fact]
    public void 式コンテキストでの呼び出しをパースできる()
    {
        var (program, diagnostics) = Parse("(tree T (check (< (.DistanceTo .Target) 5.0)))");

        Assert.False(diagnostics.HasErrors);
        var check = Assert.IsType<CstCheck>(program.Trees[0].Body[0]);
        var bin = Assert.IsType<CstBinaryExpr>(check.Expr);
        var call = Assert.IsType<CstCall>(bin.Lhs);
        Assert.Equal("DistanceTo", call.Member.Segments[0]);
    }

    // ═══════════════════════════════════════════════
    //  11. ロスレスCST: ソース復元
    // ═══════════════════════════════════════════════

    [Theory]
    [InlineData("(tree T .Patrol)")]
    [InlineData("(tree T (select .A .B))")]
    [InlineData("(tree T (check (< .Health 30)))")]
    [InlineData(";; comment\n(tree T .A)")]
    public void CSTからソーステキストを復元できる(string source)
    {
        var (program, _) = Parse(source);

        Assert.Equal(source, program.ToFullString());
    }

    // ═══════════════════════════════════════════════
    //  12. Pattern 1: Minimal の全構造をパースできる
    // ═══════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalをパースできる()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;

        var (program, diagnostics) = Parse(source);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(program.Trees);

        var tree = program.Trees[0];
        Assert.Equal("SimpleCombat", tree.Name.Text);

        var select = Assert.IsType<CstSelect>(tree.Body[0]);
        Assert.Equal(2, select.Children_.Length);

        var seq = Assert.IsType<CstSequence>(select.Children_[0]);
        Assert.Equal(2, seq.Children_.Length);

        Assert.IsType<CstCheck>(seq.Children_[0]);

        // 括弧付き引数なし呼び出し
        var flee = Assert.IsType<CstCall>(seq.Children_[1]);
        Assert.Equal("Flee", flee.Member.Segments[0]);
        Assert.Empty(flee.Args);

        // 括弧付き引数なし呼び出し
        var patrol = Assert.IsType<CstCall>(select.Children_[1]);
        Assert.Equal("Patrol", patrol.Member.Segments[0]);
        Assert.Empty(patrol.Args);

        // ソース復元
        Assert.Equal(source, program.ToFullString());
    }

    // ═══════════════════════════════════════════════
    //  13. エラー回復
    // ═══════════════════════════════════════════════

    [Fact]
    public void 閉じ括弧不足でもCSTが生成される()
    {
        var (program, diagnostics) = Parse("(tree T (select .A .B)");

        // CSTは生成される（エラーがあっても）
        Assert.NotNull(program);
        // エラーが報告される
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void 予期しないトークンでもパースが続行される()
    {
        var (program, diagnostics) = Parse("(tree T 42)");

        Assert.NotNull(program);
        Assert.True(diagnostics.HasErrors);
    }
}
