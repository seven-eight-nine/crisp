namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 1: Minimal — 最小構造のスナップショットテスト。
///
/// カバー範囲:
/// <list type="bullet">
///   <item><description>tree 定義</description></item>
///   <item><description>select / seq</description></item>
///   <item><description>check</description></item>
///   <item><description>引数なしアクション（括弧あり / なし）</description></item>
///   <item><description>プロパティアクセス</description></item>
///   <item><description>比較演算</description></item>
///   <item><description>int → float 暗黙変換（IrConvert）</description></item>
///   <item><description>コメント</description></item>
/// </list>
///
/// 固定される仕様:
/// <list type="bullet">
///   <item><description><c>.Patrol</c>（括弧なし MemberAccess）は AstActionCall(args=[]) に lowering される</description></item>
///   <item><description><c>(.Flee)</c>（括弧あり引数なし CstCall）も AstActionCall(args=[]) に lowering される</description></item>
///   <item><description><c>(< .Health 30)</c> は bool 型</description></item>
///   <item><description>30 (int) は .Health (float) との比較で IrConvert(int→float) が挿入される</description></item>
///   <item><description>コメントは CST の LeadingTrivia として保持される</description></item>
/// </list>
/// </summary>
public class Pattern1_MinimalTests
{
    /// <summary>Pattern 1 の DSL ソース。</summary>
    private const string Source = @";; 最小限の戦闘AI
(tree SimpleCombat
  (select
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol)))";

    [Fact]
    public Task CST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Cst).UseMethodName("Pattern1_Cst");
    }

    [Fact]
    public Task AST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Ast).UseMethodName("Pattern1_Ast");
    }

    [Fact]
    public Task 診断がゼロである()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Diagnostics).UseMethodName("Pattern1_Diagnostics");
    }

    [Fact]
    public Task IR構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.NotNull(result.Ir);
        return Verify(result.Ir).UseMethodName("Pattern1_Ir");
    }

    [Fact]
    public Task CSharpコードが正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source,
            namespaceName: "TestApp",
            className: "SimpleAI",
            methodName: "Build");
        Assert.NotNull(result.CSharp);
        return Verify(result.CSharp).UseMethodName("Pattern1_CSharp");
    }
}
