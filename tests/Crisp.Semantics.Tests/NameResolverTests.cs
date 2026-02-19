using Crisp.Syntax;

namespace Crisp.Semantics.Tests;

/// <summary>
/// <see cref="NameResolver"/> のテスト。
///
/// t-wada 式 TDD: 最小テストから段階的に複雑さを増す。
/// <list type="number">
///   <item><description>単一プロパティの解決</description></item>
///   <item><description>存在しないメンバーの検出</description></item>
///   <item><description>メソッドの解決</description></item>
///   <item><description>ケバブケース名の解決</description></item>
///   <item><description>チェーンメンバーの解決</description></item>
///   <item><description>メソッド引数の数の検証</description></item>
/// </list>
/// </summary>
public class NameResolverTests
{
    // ──────────── 共通のコンテキスト型定義 ────────────

    private const string ContextSource = @"
using Crisp.Runtime;

namespace Test;

public class Entity
{
    public float X { get; set; }
    public float Y { get; set; }
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
    public float GetDistance(Entity target) => 0f;
}";

    // ═══════════════════════════════════════════════════════════
    //  1. 基本: 単一プロパティの解決
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 単一プロパティが正しく解決される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .IsAlive))",
            ContextSource, "Test.AI");

        // .IsAlive は bool プロパティ → 解決成功、check(bool) → OK
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void float型プロパティが正しく解決される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check (> .Health 0.0)))",
            ContextSource, "Test.AI");

        // .Health は float、0.0 は float → (> float float) → bool → check(bool) → OK
        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 存在しないメンバー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 存在しないメンバーでBS0001が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .NonExistent))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0001").ToList();
        Assert.Single(errors);
        Assert.Contains("NonExistent", errors[0].Message);
    }

    [Fact]
    public void 存在しないメソッドでBS0001が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (.DoSomething))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0001");
    }

    // ═══════════════════════════════════════════════════════════
    //  3. メソッドの解決
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 引数なしメソッドが正しく解決される()
    {
        var result = TestHelper.Analyze(
            "(tree T (.Patrol))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void 引数ありメソッドが正しく解決される()
    {
        var result = TestHelper.Analyze(
            "(tree T (.Attack .Target))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 引数の数の不一致
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void メソッド引数の数が不一致でBS0005が報告される()
    {
        // Attack は 1 引数が必要だが、0 引数で呼び出している
        var result = TestHelper.Analyze(
            "(tree T (.Attack))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0005").ToList();
        Assert.Single(errors);
        Assert.Contains("Attack", errors[0].Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. チェーンメンバーの解決
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void チェーンメンバーが正しく解決される()
    {
        // .Target.X → Target は Entity、X は float
        var result = TestHelper.Analyze(
            "(tree T (check (> .Target.X 0.0)))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void チェーンの途中で存在しないメンバーがあるとBS0001が報告される()
    {
        // .Target.Z → Target は Entity だが Z は存在しない
        var result = TestHelper.Analyze(
            "(tree T (check (> .Target.Z 0.0)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0001");
    }

    // ═══════════════════════════════════════════════════════════
    //  6. ケバブケース名の解決
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ケバブケースのメンバーがPascalCaseで解決される()
    {
        // .is-alive → IsAlive (bool)
        var result = TestHelper.Analyze(
            "(tree T (check .is-alive))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ケバブケースのメソッドがPascalCaseで解決される()
    {
        // .patrol → Patrol (BtStatus)
        // NameConversion は先頭を大文字化する PascalCase 候補を生成
        var result = TestHelper.Analyze(
            "(tree T (.patrol))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. 複数のメンバー参照を含むツリー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 複数のメンバー参照が全て正しく解決される()
    {
        var result = TestHelper.Analyze(@"
            (tree T
                (select
                    (seq
                        (check (< .Health 30.0))
                        (.Flee))
                    (.Patrol)))",
            ContextSource, "Test.AI");

        Assert.False(result.HasErrors);
    }
}
