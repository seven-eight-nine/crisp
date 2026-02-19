namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 3: Expressions — 式と型推論のスナップショットテスト。
///
/// カバー範囲:
/// <list type="bullet">
///   <item><description>算術式（+, -, *, /）</description></item>
///   <item><description>比較式（&lt;, &gt;, &lt;=, &gt;=, =, !=）</description></item>
///   <item><description>メソッド呼び出し式（式コンテキスト内の CstCall → AstCallExpr）</description></item>
///   <item><description>メンバーチェーン（.Target.Health, .Target.Position）</description></item>
///   <item><description>enum リテラル（::AIState.Combat）</description></item>
///   <item><description>null リテラル</description></item>
///   <item><description>string リテラル</description></item>
///   <item><description>負リテラル（-1）</description></item>
///   <item><description>二項マイナス（(- .MaxHealth .Health)）</description></item>
/// </list>
/// </summary>
public class Pattern3_ExpressionsTests
{
    /// <summary>Pattern 3 の DSL ソース。</summary>
    private const string Source = @";; 式と型推論のテスト
(tree ExpressionTest
  (select
    ;; 算術 + 比較 + メソッド呼び出し
    (seq
      (check (< (.DistanceTo .Target) (* .AttackRange 1.5)))
      (check (!= .Target null))
      (check (= .CurrentState ::AIState.Combat))
      (.Attack .Target))

    ;; メンバーチェーン
    (seq
      (check (> .Target.Health 0))
      (check (<= (- .MaxHealth .Health) 50.0))
      (.MoveTo .Target.Position))

    ;; 文字列比較 + 負リテラル + boolリテラル
    (seq
      (check (= .Name ""Guardian""))
      (check (> .Health -1))
      (check (= .IsAlive true))
      (.Idle))))";

    [Fact]
    public Task CST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Cst).UseMethodName("Pattern3_Cst");
    }

    [Fact]
    public Task AST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Ast).UseMethodName("Pattern3_Ast");
    }

    [Fact]
    public Task 診断がゼロである()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Diagnostics).UseMethodName("Pattern3_Diagnostics");
    }

    [Fact]
    public Task IR構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.NotNull(result.Ir);
        return Verify(result.Ir).UseMethodName("Pattern3_Ir");
    }

    [Fact]
    public Task CSharpコードが正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source,
            namespaceName: "TestApp",
            className: "ExprAI",
            methodName: "Build");
        Assert.NotNull(result.CSharp);
        return Verify(result.CSharp).UseMethodName("Pattern3_CSharp");
    }
}
