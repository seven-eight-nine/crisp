namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 4: Parallel — 並列実行と Reset セマンティクスのスナップショットテスト。
///
/// カバー範囲:
/// <list type="bullet">
///   <item><description>parallel :any / :all / :n</description></item>
///   <item><description>Running / Success / Failure の各組み合わせ</description></item>
///   <item><description>U4 で決定した Reset セマンティクス</description></item>
///   <item><description>同一メソッドの異なる引数での複数呼び出し</description></item>
/// </list>
/// </summary>
public class Pattern4_ParallelTests
{
    /// <summary>Pattern 4 の DSL ソース。</summary>
    private const string Source = @";; 並列実行パターン
(tree ParallelTest
  (seq
    ;; :any — いずれか成功で全体Success
    (parallel :any
      (.SearchForEnemy)
      (.ListenForSound)
      (.CheckRadar))

    ;; :all — 全て成功で全体Success
    (parallel :all
      (.AimAtTarget .Target)
      (.ChargeWeapon))

    ;; :n — N個成功で全体Success
    (parallel :n 2
      (.NotifyAlly .Ally1)
      (.NotifyAlly .Ally2)
      (.NotifyAlly .Ally3))

    (.ExecuteAttack)))";

    [Fact]
    public Task CST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Cst).UseMethodName("Pattern4_Cst");
    }

    [Fact]
    public Task AST構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Ast).UseMethodName("Pattern4_Ast");
    }

    [Fact]
    public Task 診断がゼロである()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        return Verify(result.Diagnostics).UseMethodName("Pattern4_Diagnostics");
    }

    [Fact]
    public Task IR構造が正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source);
        Assert.NotNull(result.Ir);
        return Verify(result.Ir).UseMethodName("Pattern4_Ir");
    }

    [Fact]
    public Task CSharpコードが正しく生成される()
    {
        var result = SnapshotHelper.CompileAndSnapshot(Source,
            namespaceName: "TestApp",
            className: "ParallelAI",
            methodName: "Build");
        Assert.NotNull(result.CSharp);
        return Verify(result.CSharp).UseMethodName("Pattern4_CSharp");
    }
}
