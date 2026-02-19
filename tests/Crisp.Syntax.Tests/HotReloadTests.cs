using Crisp.Runtime;
using Crisp.Syntax;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F9: Hot Reload (CrispRuntime) のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>CrispRuntime.Compile: ソースから IR ツリーへのコンパイルを検証する</description></item>
///   <item><description>CrispRuntime.CompileAndBuild: ソースから Evaluator 構築までの一貫性を検証する</description></item>
///   <item><description>CrispCompilationException: エラー診断の正確性を検証する</description></item>
/// </list>
/// </summary>
public class HotReloadTests
{
    // ──────────────────────────────────────────────
    //  テスト用コンテキストクラス
    // ──────────────────────────────────────────────

    /// <summary>最小限のテスト用エージェント。</summary>
    private class SimpleAgent
    {
        public int Health { get; set; } = 100;
        public bool IsAlive { get; set; } = true;
        public BtStatus Patrol() => BtStatus.Success;
        public BtStatus Flee() => BtStatus.Success;
    }

    // ═══════════════════════════════════════════════════
    //  CrispRuntime.Compile — 正常系
    // ═══════════════════════════════════════════════════

    [Fact]
    public void Compile_有効なソースでIrTree配列を返す()
    {
        // Arrange
        var source = "(tree T (.Patrol))";

        // Act
        var trees = CrispRuntime.Compile(source);

        // Assert
        Assert.Single(trees);
        Assert.Equal("T", trees[0].Name);
    }

    [Fact]
    public void Compile_複数ツリーのソースで全ツリーを返す()
    {
        // Arrange
        var source = """
            (tree Alpha (.Patrol))
            (tree Beta (.Flee))
            """;

        // Act
        var trees = CrispRuntime.Compile(source);

        // Assert
        Assert.Equal(2, trees.Length);
        Assert.Equal("Alpha", trees[0].Name);
        Assert.Equal("Beta", trees[1].Name);
    }

    // ═══════════════════════════════════════════════════
    //  CrispRuntime.Compile — エラー系
    // ═══════════════════════════════════════════════════

    [Fact]
    public void Compile_構文エラーでCrispCompilationExceptionをスローする()
    {
        // Arrange — 閉じ括弧が足りない不正なソース
        var source = "(tree T (.Patrol)";

        // Act & Assert
        Assert.Throws<CrispCompilationException>(() => CrispRuntime.Compile(source));
    }

    [Fact]
    public void Compile_エラー診断が例外に含まれる()
    {
        // Arrange
        var source = "(tree T (.Patrol)";

        // Act
        var ex = Assert.Throws<CrispCompilationException>(() => CrispRuntime.Compile(source));

        // Assert
        Assert.NotEmpty(ex.Diagnostics);
        Assert.All(ex.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    // ═══════════════════════════════════════════════════
    //  CrispRuntime.CompileAndBuild — 正常系
    // ═══════════════════════════════════════════════════

    [Fact]
    public void CompileAndBuild_有効なソースでEvaluatorを返す()
    {
        // Arrange
        var source = "(tree T (.Patrol))";
        var agent = new SimpleAgent();

        // Act
        var (tree, evaluator) = CrispRuntime.CompileAndBuild(source, agent);

        // Assert
        Assert.NotNull(tree);
        Assert.NotNull(evaluator);
        Assert.Equal("T", tree.Name);
    }

    [Fact]
    public void CompileAndBuild_返されたEvaluatorでTickが実行可能()
    {
        // Arrange
        var source = "(tree T (.Patrol))";
        var agent = new SimpleAgent();

        // Act
        var (tree, evaluator) = CrispRuntime.CompileAndBuild(source, agent);
        var result = evaluator.Tick(tree, new TickContext(0.016f));

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    // ═══════════════════════════════════════════════════
    //  CrispRuntime.CompileAndBuild — エラー系
    // ═══════════════════════════════════════════════════

    [Fact]
    public void CompileAndBuild_空ソースでInvalidOperationExceptionをスローする()
    {
        // Arrange
        var source = "";
        var agent = new SimpleAgent();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            CrispRuntime.CompileAndBuild(source, agent));
    }

    [Fact]
    public void CompileAndBuild_構文エラーでCrispCompilationExceptionをスローする()
    {
        // Arrange
        var source = "(tree T (.Patrol)";
        var agent = new SimpleAgent();

        // Act & Assert
        Assert.Throws<CrispCompilationException>(() =>
            CrispRuntime.CompileAndBuild(source, agent));
    }

    // ═══════════════════════════════════════════════════
    //  CrispCompilationException — エラーメッセージ検証
    // ═══════════════════════════════════════════════════

    [Fact]
    public void CrispCompilationException_メッセージにエラー件数が含まれる()
    {
        // Arrange
        var source = "(tree T (.Patrol)";

        // Act
        var ex = Assert.Throws<CrispCompilationException>(() => CrispRuntime.Compile(source));

        // Assert
        Assert.Contains("コンパイルエラー", ex.Message);
        Assert.Contains("件", ex.Message);
    }

    [Fact]
    public void CrispCompilationException_メッセージに診断IDが含まれる()
    {
        // Arrange
        var source = "(tree T (.Patrol)";

        // Act
        var ex = Assert.Throws<CrispCompilationException>(() => CrispRuntime.Compile(source));

        // Assert — 各診断の ID がメッセージに含まれる
        foreach (var diag in ex.Diagnostics)
        {
            Assert.Contains(diag.Id, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════
    //  CrispRuntime + defdec/macro/ref 統合テスト
    // ═══════════════════════════════════════════════════

    [Fact]
    public void Compile_defdec付きソースで展開されたIrTreeを返す()
    {
        // Arrange: guard ラッパー defdec
        var source = "(defdec alive-guard () (guard .IsAlive <body>)) (tree T (alive-guard .Patrol))";

        // Act
        var trees = CrispRuntime.Compile(source);

        // Assert: defdec が展開されて IR に変換される
        Assert.Single(trees);
        var guard = Assert.IsType<IrGuard>(trees[0].Body[0]);
        Assert.IsType<IrAction>(guard.Body);
    }

    [Fact]
    public void Compile_defmacro付きソースで展開されたIrTreeを返す()
    {
        // Arrange: repeat でラップするマクロ
        var source = "(defmacro retry () (repeat 3 <body>)) (tree T (retry .Patrol))";

        // Act
        var trees = CrispRuntime.Compile(source);

        // Assert: マクロが展開されて repeat ノードが IR に反映される
        Assert.Single(trees);
        var repeat = Assert.IsType<IrRepeat>(trees[0].Body[0]);
        Assert.Equal(3, repeat.Count);
        Assert.IsType<IrAction>(repeat.Child);
    }

    [Fact]
    public void Compile_ref付きソースで正しくIrTreeを返す()
    {
        // Arrange: ref を使った複数ツリー
        var source = """
            (tree Main (ref Combat))
            (tree Combat .Patrol)
            """;

        // Act
        var trees = CrispRuntime.Compile(source);

        // Assert: 両ツリーが IR に変換される
        Assert.Equal(2, trees.Length);
        Assert.Equal("Main", trees[0].Name);
        Assert.Equal("Combat", trees[1].Name);
    }

    [Fact]
    public void CompileAndBuild_defdec付きソースでTickが実行可能()
    {
        // Arrange: defdec + ref の組み合わせ
        var source = "(defdec alive-guard () (guard .IsAlive <body>)) (tree T (alive-guard .Patrol))";
        var agent = new SimpleAgent();

        // Act
        var (tree, evaluator) = CrispRuntime.CompileAndBuild(source, agent);
        var result = evaluator.Tick(tree, new TickContext(0.016f));

        // Assert: guard 条件 (.IsAlive = true) → body (.Patrol) → Success
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void CompileAndBuild_HotReloadシナリオでツリーが差し替え可能()
    {
        // Arrange: 初回コンパイル
        var source1 = "(tree T (.Patrol))";
        var agent = new SimpleAgent();
        var (tree1, eval1) = CrispRuntime.CompileAndBuild(source1, agent);
        var result1 = eval1.Tick(tree1, new TickContext(0.016f));
        Assert.Equal(BtStatus.Success, result1);

        // Act: ソース変更後に再コンパイル（Hot Reload）
        var source2 = "(tree T (.Flee))";
        var (tree2, eval2) = CrispRuntime.CompileAndBuild(source2, agent);
        var result2 = eval2.Tick(tree2, new TickContext(0.016f));

        // Assert: 新しいツリーで実行される
        Assert.Equal(BtStatus.Success, result2);
        Assert.Equal("T", tree2.Name);
    }
}
