using Crisp.Syntax;

namespace Crisp.Semantics.Tests;

/// <summary>
/// <see cref="SemanticAnalyzer"/> の統合テスト。
///
/// Pattern 5b（型エラー診断）に基づく統合テストケースを含む。
/// 名前解決 → 型推論 → 型検査 の全フェーズを通して、
/// 正しい診断が正しい数だけ報告されることを検証する。
///
/// t-wada 式 TDD:
/// <list type="number">
///   <item><description>全て正常なツリーで診断ゼロ</description></item>
///   <item><description>単一エラーの検出</description></item>
///   <item><description>複数エラーの検出</description></item>
///   <item><description>Pattern 5b: 全エラーケース + カスケード抑制</description></item>
/// </list>
/// </summary>
public class SemanticAnalyzerTests
{
    // ═══════════════════════════════════════════════════════════
    //  Pattern 5b 用コンテキスト型
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Pattern 5b テスト用のコンテキスト型定義。
    /// 型エラーのテストケースを網羅するために、
    /// 様々な型のメンバーとメソッドを含む。
    /// </summary>
    private const string Pattern5bContext = @"
using Crisp.Runtime;

namespace Test;

public class Entity
{
    public float X { get; set; }
}

public class ErrorAI
{
    public float Health { get; set; }
    public bool IsAlive { get; set; }
    public bool IsEnemyVisible { get; set; }
    public string Name { get; set; } = """";
    public Entity Target { get; set; } = new();

    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Running;
    public BtStatus Attack(Entity target) => BtStatus.Success;
    public float DistanceTo(Entity target) => 0f;
}";

    // ═══════════════════════════════════════════════════════════
    //  1. 全て正常なツリー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 正常なツリーで診断が報告されない()
    {
        var result = TestHelper.Analyze(@"
            (tree Combat
                (select
                    (seq
                        (check (< .Health 30.0))
                        (.Flee))
                    (.Patrol)))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void 複雑な正常ツリーで診断が報告されない()
    {
        var result = TestHelper.Analyze(@"
            (tree Combat
                (guard .IsAlive
                    (select
                        (seq
                            (check (and .IsEnemyVisible (< .Health 30.0)))
                            (.Flee))
                        (seq
                            (check .IsEnemyVisible)
                            (.Attack .Target))
                        (.Patrol))))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. Pattern 5b: 個別エラーケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BS0001_存在しないメンバーが検出される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check .NonExistent))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0001");
    }

    [Fact]
    public void BS0007_checkでbool以外の型が検出される()
    {
        // .Health は float → check は bool を要求
        var result = TestHelper.Analyze(
            "(tree T (check .Health))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0007");
    }

    [Fact]
    public void BS0003_比較不可能な型が検出される()
    {
        // float と string は比較不可能
        var result = TestHelper.Analyze(
            "(tree T (check (< .Health \"hello\")))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0003");
    }

    [Fact]
    public void BS0005_メソッド引数の数の不一致が検出される()
    {
        // Attack は Entity を 1 つ要求するが、引数なしで呼び出し
        var result = TestHelper.Analyze(
            "(tree T (.Attack))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0005");
    }

    [Fact]
    public void BS0008_非BtStatus戻り値メソッドが検出される()
    {
        // DistanceTo は float を返す → アクションは BtStatus を要求
        var result = TestHelper.Analyze(
            "(tree T (.DistanceTo .Target))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0008");
    }

    [Fact]
    public void BS0004_無効な算術演算が検出される()
    {
        // string に + 演算子は適用不可
        var result = TestHelper.Analyze(
            "(tree T (check (> (+ .Name 1) 0)))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0004");
    }

    // ═══════════════════════════════════════════════════════════
    //  3. Pattern 5b: カスケード抑制
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void カスケード抑制で二次エラーが報告されない()
    {
        // (+ .Name 1) → BS0004 → ErrorType
        // (> ErrorType 0) → ErrorType 伝搬、BS0003 抑制
        // (check ErrorType) → BS0007 抑制
        // 報告されるのは BS0004 のみ
        var result = TestHelper.Analyze(
            "(tree T (check (> (+ .Name 1) 0)))",
            Pattern5bContext, "Test.ErrorAI");

        var errors = result.Diagnostics.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Single(errors);
        Assert.Equal("BS0004", errors[0].Id);
    }

    [Fact]
    public void 名前解決エラーのカスケード抑制()
    {
        // .NonExistent → BS0001 → ErrorType
        // check(ErrorType) → BS0007 抑制
        // 報告されるのは BS0001 のみ
        var result = TestHelper.Analyze(
            "(tree T (check .NonExistent))",
            Pattern5bContext, "Test.ErrorAI");

        var errors = result.Diagnostics.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Single(errors);
        Assert.Equal("BS0001", errors[0].Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. Pattern 5b: 複数エラーの検出
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 複数の独立したエラーが全て報告される()
    {
        // 3 つの独立したエラー:
        // 1. BS0001: .NonExistent → メンバー不明
        // 2. BS0007: check(.Health) → float は bool でない
        // 3. BS0008: .DistanceTo → float 戻り値
        var result = TestHelper.Analyze(@"
            (tree T
                (select
                    (check .NonExistent)
                    (check .Health)
                    (.DistanceTo .Target)
                    (.Patrol)))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);

        var errors = result.Diagnostics.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // BS0001 + BS0007 + BS0008 = 3 エラー（BS0001 のカスケードで check の BS0007 は抑制）
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, d => d.Id == "BS0001");
        Assert.Contains(errors, d => d.Id == "BS0007");
        Assert.Contains(errors, d => d.Id == "BS0008");
    }

    [Fact]
    public void Pattern5b_全エラーケースを含むツリーの診断()
    {
        // Pattern 5b の全エラーケースを1つのツリーに集約
        var result = TestHelper.Analyze(@"
            (tree TypeError
                (select
                    (check .NonExistent)
                    (check .Health)
                    (check (< .Health ""hello""))
                    (.Attack)
                    (.DistanceTo .Target)
                    (check (> (+ .Name 1) 0))
                    (.Patrol)))",
            Pattern5bContext, "Test.ErrorAI");

        Assert.True(result.HasErrors);

        var errors = result.Diagnostics.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // 期待される診断:
        // 1. BS0001: .NonExistent が見つからない
        // 2. BS0007: .Health は float → check は bool を要求
        // 3. BS0003: float と string は比較不可能
        // 4. BS0005: Attack は 1 引数を要求
        // 5. BS0008: DistanceTo は float を返す
        // 6. BS0004: string に + 演算子は適用不可
        // カスケード抑制: (> (+ .Name 1) 0) の BS0003、check(...) の BS0007 は抑制
        Assert.Equal(6, errors.Count);
        Assert.Contains(errors, d => d.Id == "BS0001");
        Assert.Contains(errors, d => d.Id == "BS0007");
        Assert.Contains(errors, d => d.Id == "BS0003");
        Assert.Contains(errors, d => d.Id == "BS0005");
        Assert.Contains(errors, d => d.Id == "BS0008");
        Assert.Contains(errors, d => d.Id == "BS0004");
    }
}
