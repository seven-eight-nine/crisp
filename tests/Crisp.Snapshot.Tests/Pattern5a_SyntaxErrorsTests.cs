namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 5a: 構文エラー — パーサーのエラー回復スナップショットテスト。
///
/// 固定される仕様:
/// <list type="bullet">
///   <item><description>CST は常に生成される（エラーがあっても）</description></item>
///   <item><description>CstMissing ノード（閉じ括弧不足）と CstError ノード（予期しないトークン）が CST に含まれる</description></item>
///   <item><description>エラー後もパースが続行される</description></item>
///   <item><description>IR / C# は生成されない（エラー時）</description></item>
/// </list>
/// </summary>
public class Pattern5a_SyntaxErrorsTests
{
    /// <summary>Pattern 5a の DSL ソース（閉じ括弧不足 + 予期しないトークン）。</summary>
    private const string Source = @";; 閉じ括弧不足
(tree SyntaxError1
  (select
    (.Patrol)
    (seq
      (check (< .Health 30))

;; 予期しないトークン
(tree SyntaxError2
  (select
    42
    (.Patrol)))";

    [Fact]
    public Task CST構造がエラーノードを含んで生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Cst).UseMethodName("Pattern5a_Cst");
    }

    [Fact]
    public Task AST構造が部分的に生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Ast).UseMethodName("Pattern5a_Ast");
    }

    [Fact]
    public Task 構文エラーの診断が報告される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Diagnostics).UseMethodName("Pattern5a_Diagnostics");
    }

    [Fact]
    public void IRが生成されない()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.Null(result.Ir);
    }

    [Fact]
    public void CSharpが生成されない()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.Null(result.CSharp);
    }
}
