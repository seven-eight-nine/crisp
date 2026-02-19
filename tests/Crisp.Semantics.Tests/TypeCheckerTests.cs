using Crisp.Syntax;

namespace Crisp.Semantics.Tests;

/// <summary>
/// <see cref="TypeChecker"/> のテスト。
///
/// t-wada 式 TDD: 条件式の bool 検査から段階的にアクション戻り値検査、
/// カスケード抑制へ。
/// <list type="number">
///   <item><description>check ノードの条件が bool</description></item>
///   <item><description>check ノードの条件が非 bool</description></item>
///   <item><description>guard ノードの条件検査</description></item>
///   <item><description>アクションメソッドが BtStatus を返す</description></item>
///   <item><description>アクションメソッドが非 BtStatus を返す</description></item>
///   <item><description>カスケード抑制</description></item>
/// </list>
/// </summary>
public class TypeCheckerTests
{
    private const string ContextSource = @"
using Crisp.Runtime;

namespace Test;

public class Entity
{
    public float X { get; set; }
}

public class AI
{
    public float Health { get; set; }
    public bool IsAlive { get; set; }
    public string Name { get; set; } = """";
    public Entity Target { get; set; } = new();

    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Attack(Entity target) => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Running;
    public float GetDistance() => 0f;
        public BtNode BuildSubtree() => new ActionNode(() => BtStatus.Success);
}";

    // ═══════════════════════════════════════════════════════════
    //  1. check ノードの条件検査
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void checkでbool式が受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .IsAlive))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void checkで比較式が受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check (< .Health 30.0)))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void checkでfloat式はBS0007が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .Health))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0007").ToList();
        Assert.Single(errors);
        Assert.Contains("check", errors[0].Message);
        Assert.Contains("float", errors[0].Message);
    }

    [Fact]
    public void checkでstring式はBS0007が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .Name))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0007");
    }

    [Fact]
    public void checkでint式はBS0007が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check 42))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0007");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. guard ノードの条件検査
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void guardでbool条件が受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (guard .IsAlive (.Patrol)))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void guardで非bool条件はBS0007が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (guard .Health (.Patrol)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0007").ToList();
        Assert.Single(errors);
        Assert.Contains("guard", errors[0].Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. アクションの戻り値検査
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BtStatusを返すメソッドが受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (.Patrol))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void 非BtStatusを返すメソッドでBS0008が報告される()
    {
        // GetDistance は float を返す
        var result = TestHelper.Analyze(
            "(tree T (.GetDistance))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0008").ToList();
        Assert.Single(errors);
        Assert.Contains("GetDistance", errors[0].Message);
    }

    [Fact]
    public void BtNodeを返すメソッドがアクション位置で受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (.BuildSubtree))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. カスケード抑制
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ErrorType式のcheckでBS0007が抑制される()
    {
        // .NonExistent → 名前解決失敗 → ErrorType
        // check(ErrorType) → BS0007 は報告しない
        var result = TestHelper.Analyze(
            "(tree T (check .NonExistent))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        // BS0001 のみが報告される（BS0007 は抑制される）
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0001");
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Id == "BS0007");
    }

    [Fact]
    public void 算術エラーを含む式のcheckでBS0007が抑制される()
    {
        // (+ .Name 1) → BS0004、結果は Error
        // (> Error 0) → Error 伝搬
        // check(Error) → BS0007 は報告しない
        var result = TestHelper.Analyze(
            "(tree T (check (> (+ .Name 1) 0)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0004");
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Id == "BS0007");
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 複合ツリーの検査
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 複合ツリーで全ての制約が満たされる()
    {
        var result = TestHelper.Analyze(@"
            (tree T
                (guard .IsAlive
                    (select
                        (seq
                            (check (< .Health 30.0))
                            (.Flee))
                        (.Patrol))))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void if文でbool条件が受理される()
    {
        var result = TestHelper.Analyze(
            "(tree T (if .IsAlive (.Patrol) (.Flee)))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void while文で非bool条件はBS0007が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (while .Health (.Patrol)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0007").ToList();
        Assert.Single(errors);
        Assert.Contains("while", errors[0].Message);
    }
}
