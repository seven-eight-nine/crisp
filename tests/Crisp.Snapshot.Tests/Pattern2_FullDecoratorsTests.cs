namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 2: Full Decorators — 全デコレータのスナップショットテスト。
///
/// カバー範囲:
/// <list type="bullet">
///   <item><description>guard / if+else</description></item>
///   <item><description>invert / repeat</description></item>
///   <item><description>timeout / cooldown</description></item>
///   <item><description>while</description></item>
///   <item><description>論理式（and / or / not）</description></item>
///   <item><description>引数ありアクション</description></item>
///   <item><description>bool プロパティの直接参照（check なしで guard 条件に使用）</description></item>
/// </list>
/// </summary>
public class Pattern2_FullDecoratorsTests
{
    /// <summary>Pattern 2 の DSL ソース。</summary>
    private const string Source = @";; デコレータを網羅するパトロールAI
(tree DecoratorShowcase
  (select
    ;; guard: 条件付き実行
    (guard .IsAlive
      (seq
        ;; if/else
        (if (and .IsEnemyVisible (not .IsFleeing))
          (timeout 5.0
            (.Attack .Target))
          (.Patrol))

        ;; repeat
        (repeat 3
          (.TauntEnemy))

        ;; cooldown
        (cooldown 2.5
          (.UseAbility))

        ;; while
        (while (> .Stamina 0)
          (.Sprint))

        ;; invert
        (invert
          (check .IsStunned))))))";

    [Fact]
    public Task CST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Cst).UseMethodName("Pattern2_Cst");
    }

    [Fact]
    public Task AST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Ast).UseMethodName("Pattern2_Ast");
    }

    [Fact]
    public Task 診断がゼロである()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Diagnostics).UseMethodName("Pattern2_Diagnostics");
    }

    [Fact]
    public Task IR構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.NotNull(result.Ir);
        return Verify(result.Ir).UseMethodName("Pattern2_Ir");
    }

    [Fact]
    public Task CSharpコードが正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source,
            namespaceName: "TestApp",
            className: "DecoratorAI",
            methodName: "Build");
        Assert.NotNull(result.CSharp);
        return Verify(result.CSharp).UseMethodName("Pattern2_CSharp");
    }
}
