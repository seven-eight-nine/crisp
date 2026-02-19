using System.Collections.Immutable;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.IR;
using Crisp.Syntax.Pipeline;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F2: User-Defined Decorators (defdec) のユニットテスト。
/// t-wada 式 TDD に基づき、最小のケースから段階的に複雑さを増す。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>Parser: defdec 定義と defdec 呼び出しのパース</description></item>
///   <item><description>CstToAstLowering: CstDefdec/CstDefdecCall → AstDefdec/AstDefdecCall</description></item>
///   <item><description>AstDefdecExpander: defdec 呼び出しの展開（body 置換 + パラメータ置換）</description></item>
///   <item><description>CompilationPipeline: defdec を含む完全なコンパイルパイプライン</description></item>
///   <item><description>エラーケース: 未定義 defdec、パラメータ数不一致、再帰呼び出し</description></item>
/// </list>
/// </summary>
public class DefdecTests
{
    // ═══════════════════════════════════════════════
    //  1. Parser: defdec 定義のパース
    // ═══════════════════════════════════════════════

    [Fact]
    public void defdec定義がパースされる()
    {
        // Arrange & Act: <body> プレースホルダを guard で包む defdec
        var (program, diagnostics) = Parser.Parse(
            "(defdec alive-guard () (guard .IsAlive <body>)) (tree Main .Attack)");

        // Assert
        Assert.False(diagnostics.HasErrors);
        Assert.Single(program.Defdecs);
        Assert.Equal("alive-guard", program.Defdecs[0].Name.Text);
        Assert.Empty(program.Defdecs[0].Parameters);
    }

    [Fact]
    public void defdec定義の名前とパラメータが保持される()
    {
        // timeout/cooldown のパラメータは float リテラルとしてパースされるため、
        // defdec のパラメータが式として使われることはない。パラメータ名のみ保持される。
        var (program, _) = Parser.Parse(
            "(defdec safe-timed (duration cd) (timeout 10 <body>)) (tree Main .Attack)");

        Assert.Single(program.Defdecs);
        Assert.Equal("safe-timed", program.Defdecs[0].Name.Text);
        Assert.Equal(2, program.Defdecs[0].Parameters.Length);
        Assert.Equal("duration", program.Defdecs[0].Parameters[0].Text);
        Assert.Equal("cd", program.Defdecs[0].Parameters[1].Text);
    }

    [Fact]
    public void 複数のdefdec定義がパースされる()
    {
        var (program, diagnostics) = Parser.Parse(
            "(defdec a () <body>) (defdec b (x) <body>) (tree Main .Attack)");

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, program.Defdecs.Length);
        Assert.Equal("a", program.Defdecs[0].Name.Text);
        Assert.Equal("b", program.Defdecs[1].Name.Text);
    }

    // ═══════════════════════════════════════════════
    //  2. CstToAstLowering: defdec → AST
    // ═══════════════════════════════════════════════

    [Fact]
    public void CstDefdecがAstDefdecに変換される()
    {
        // Arrange
        var (program, _) = Parser.Parse(
            "(defdec retry (n) (repeat 3 <body>)) (tree Main .Attack)");

        // Act
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);

        // Assert
        Assert.Single(defdecs);
        Assert.Equal("retry", defdecs[0].Name);
        Assert.Single(defdecs[0].Parameters);
        Assert.Equal("n", defdecs[0].Parameters[0]);
    }

    [Fact]
    public void CstDefdecCallがAstDefdecCallに変換される()
    {
        // Arrange: tree 内で defdec を呼び出す
        var (program, _) = Parser.Parse(
            "(defdec my-guard () (guard .IsAlive <body>)) (tree Main (my-guard .Attack))");

        // Act
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);

        // Assert
        var tree = trees[0];
        var call = Assert.IsType<AstDefdecCall>(tree.Body[0]);
        Assert.Equal("my-guard", call.Name);
        Assert.Empty(call.Args);
        Assert.IsType<AstActionCall>(call.Body); // .Attack
    }

    // ═══════════════════════════════════════════════
    //  3. AstDefdecExpander: 基本展開
    // ═══════════════════════════════════════════════

    [Fact]
    public void body置換で単純なdefdecが展開される()
    {
        // Arrange: guard でラップする defdec
        var (program, _) = Parser.Parse(
            "(defdec alive-guard () (guard .IsAlive <body>)) (tree Main (alive-guard .Attack))");
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, defdecs, bag);

        // Assert: (alive-guard .Attack) → (guard .IsAlive .Attack)
        Assert.Empty(bag.Diagnostics);
        var guard = Assert.IsType<AstGuard>(expanded[0].Body[0]);
        var condition = Assert.IsType<AstMemberAccessExpr>(guard.Condition);
        Assert.Equal("IsAlive", condition.Path.Segments[0]);
        Assert.IsType<AstActionCall>(guard.Body);
    }

    [Fact]
    public void 入れ子のdefdec呼び出しが展開される()
    {
        // Arrange: seq(guard(.IsAlive <body>)) の defdec
        var (program, _) = Parser.Parse(
            "(defdec safe-action () (seq (check .IsAlive) <body>)) (tree Main (safe-action .Attack))");
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, defdecs, bag);

        // Assert: (safe-action .Attack) → (seq (check .IsAlive) .Attack)
        Assert.Empty(bag.Diagnostics);
        var seq = Assert.IsType<AstSequence>(expanded[0].Body[0]);
        Assert.Equal(2, seq.Children_.Length);
        Assert.IsType<AstCheck>(seq.Children_[0]);
        Assert.IsType<AstActionCall>(seq.Children_[1]);
    }

    [Fact]
    public void defdec定義がない場合はツリーがそのまま返される()
    {
        // Arrange
        var (program, _) = Parser.Parse("(tree Main .Attack)");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, ImmutableArray<AstDefdec>.Empty, bag);

        // Assert
        Assert.Empty(bag.Diagnostics);
        Assert.Single(expanded);
        Assert.IsType<AstActionCall>(expanded[0].Body[0]);
    }

    // ═══════════════════════════════════════════════
    //  4. AstDefdecExpander: エラーケース
    // ═══════════════════════════════════════════════

    [Fact]
    public void 未定義のdefdec呼び出しでBS0023が報告される()
    {
        // Arrange: 存在する defdec (retry) と、存在しない defdec 呼び出し (nonexistent)
        // AstDefdecExpander は defdec 定義が 1 つ以上ないと展開処理に入らないため、
        // ダミーの defdec 定義を追加する
        var (program, _) = Parser.Parse(
            "(defdec retry () (repeat 3 <body>)) (tree Main (nonexistent .Attack))");
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, defdecs, bag);

        // Assert: BS0023 が報告される
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0023", bag.Diagnostics[0].Id);
        Assert.Contains("nonexistent", bag.Diagnostics[0].Message);
    }

    [Fact]
    public void パラメータ数不一致でBS0024が報告される()
    {
        // Arrange: defdec は 2 パラメータだが 1 つしか渡さない
        var (program, _) = Parser.Parse(
            "(defdec needs-two (a b) (timeout 10 <body>)) (tree Main (needs-two 5.0 .Attack))");
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, defdecs, bag);

        // Assert: パラメータ数の不一致で BS0024 が報告される
        // needs-two は (a b) の 2 パラメータだが、呼び出し側は 1 式引数 (5.0) + body (.Attack)
        var errors = bag.Diagnostics.Where(d => d.Id == "BS0024").ToList();
        Assert.Single(errors);
        Assert.Contains("needs-two", errors[0].Message);
    }

    [Fact]
    public void 再帰的defdec呼び出しでBS0025が報告される()
    {
        // Arrange: defdec が自身を呼び出す（直接再帰）
        var (program, _) = Parser.Parse(
            "(defdec infinite () (infinite .Attack)) (tree Main (infinite .Attack))");
        var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);
        var bag = new DiagnosticBag();

        // Act
        var expanded = AstDefdecExpander.Expand(trees, defdecs, bag);

        // Assert: BS0025 が報告される
        var errors = bag.Diagnostics.Where(d => d.Id == "BS0025").ToList();
        Assert.NotEmpty(errors);
    }

    // ═══════════════════════════════════════════════
    //  5. CompilationPipeline: defdec を含む完全パス
    // ═══════════════════════════════════════════════

    [Fact]
    public void パイプラインでdefdecが展開されてIRになる()
    {
        // Arrange: guard ラッパー defdec
        var source = "(defdec alive-guard () (guard .IsAlive <body>)) (tree Main (alive-guard .Attack))";

        // Act
        var result = CompilationPipeline.Compile(source);

        // Assert: defdec が展開されて IR に変換される
        Assert.False(result.Diagnostics.HasErrors);
        var irTree = result.IrTrees[0];
        var guard = Assert.IsType<IrGuard>(irTree.Body[0]);
        Assert.IsType<IrMemberLoad>(guard.Condition);
        Assert.IsType<IrAction>(guard.Body);
    }

    [Fact]
    public void パイプラインでdefdecがC_Sharpコードに変換される()
    {
        // Arrange
        var source = "(defdec alive-guard () (guard .IsAlive <body>)) (tree Main (alive-guard .Attack))";

        // Act
        var code = CompilationPipeline.CompileToCSharp(source, "Game", "Agent", "BuildMain");

        // Assert: 展開後のコード（guard + action）が生成される
        Assert.Contains("IsAlive", code);
        Assert.Contains("Attack", code);
        Assert.Contains("partial class Agent", code);
    }

    [Fact]
    public void パイプラインでdefdecとrefが共存する()
    {
        // Arrange: defdec と ref の組み合わせ
        var source = """
            (defdec alive-guard () (guard .IsAlive <body>))
            (tree Main (alive-guard (ref Combat)))
            (tree Combat .Attack)
            """;

        // Act
        var result = CompilationPipeline.Compile(source);

        // Assert
        Assert.False(result.Diagnostics.HasErrors);
        var irTree = result.IrTrees[0];
        var guard = Assert.IsType<IrGuard>(irTree.Body[0]);
        var treeRef = Assert.IsType<IrTreeRef>(guard.Body);
        Assert.Equal("Combat", treeRef.TreeName);
    }
}
