using Crisp.Syntax.Resolution;

namespace Crisp.Syntax.Tests;

/// <summary>
/// NameConversion（DSL 名 ↔ C# 名変換）のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>各変換メソッド（PascalCase, camelCase, snake_case）を個別に検証する</description></item>
///   <item><description>ケバブケース・PascalCase・小文字のみ等の各入力パターンをカバーする</description></item>
///   <item><description>GenerateCandidates の候補順序が仕様通りであることを検証する</description></item>
///   <item><description>空文字・1文字等のエッジケースをカバーする</description></item>
/// </list>
/// </summary>
public class NameConversionTests
{
    // ═══════════════════════════════════════════════
    //  1. ToPascalCase: ケバブケースの変換
    // ═══════════════════════════════════════════════

    [Fact]
    public void ケバブケースがPascalCaseに変換される()
    {
        var result = NameConversion.ToPascalCase("is-enemy-visible");

        Assert.Equal("IsEnemyVisible", result);
    }

    [Fact]
    public void 小文字のみがPascalCaseに変換される()
    {
        var result = NameConversion.ToPascalCase("health");

        Assert.Equal("Health", result);
    }

    [Fact]
    public void 既にPascalCaseの文字列はそのまま返される()
    {
        var result = NameConversion.ToPascalCase("Health");

        Assert.Equal("Health", result);
    }

    [Fact]
    public void 単一文字の小文字がPascalCaseに変換される()
    {
        var result = NameConversion.ToPascalCase("a");

        Assert.Equal("A", result);
    }

    [Fact]
    public void 空文字列のPascalCase変換は空文字列を返す()
    {
        var result = NameConversion.ToPascalCase("");

        Assert.Equal("", result);
    }

    [Fact]
    public void 複数ハイフンのケバブケースがPascalCaseに変換される()
    {
        var result = NameConversion.ToPascalCase("very-long-action-name");

        Assert.Equal("VeryLongActionName", result);
    }

    // ═══════════════════════════════════════════════
    //  2. ToCamelCase: ケバブケースの変換
    // ═══════════════════════════════════════════════

    [Fact]
    public void ケバブケースがcamelCaseに変換される()
    {
        var result = NameConversion.ToCamelCase("is-enemy-visible");

        Assert.Equal("isEnemyVisible", result);
    }

    [Fact]
    public void PascalCaseの先頭が小文字になる()
    {
        var result = NameConversion.ToCamelCase("Health");

        Assert.Equal("health", result);
    }

    [Fact]
    public void 小文字のみはそのまま返される()
    {
        var result = NameConversion.ToCamelCase("health");

        Assert.Equal("health", result);
    }

    [Fact]
    public void 空文字列のcamelCase変換は空文字列を返す()
    {
        var result = NameConversion.ToCamelCase("");

        Assert.Equal("", result);
    }

    // ═══════════════════════════════════════════════
    //  3. ToSnakeCase: 各種入力の変換
    // ═══════════════════════════════════════════════

    [Fact]
    public void ケバブケースがsnake_caseに変換される()
    {
        var result = NameConversion.ToSnakeCase("is-enemy-visible");

        Assert.Equal("is_enemy_visible", result);
    }

    [Fact]
    public void PascalCaseがsnake_caseに変換される()
    {
        var result = NameConversion.ToSnakeCase("IsEnemyVisible");

        Assert.Equal("is_enemy_visible", result);
    }

    [Fact]
    public void camelCaseがsnake_caseに変換される()
    {
        var result = NameConversion.ToSnakeCase("isEnemyVisible");

        Assert.Equal("is_enemy_visible", result);
    }

    [Fact]
    public void 小文字のみのsnake_case変換はそのまま返される()
    {
        var result = NameConversion.ToSnakeCase("health");

        Assert.Equal("health", result);
    }

    [Fact]
    public void 空文字列のsnake_case変換は空文字列を返す()
    {
        var result = NameConversion.ToSnakeCase("");

        Assert.Equal("", result);
    }

    // ═══════════════════════════════════════════════
    //  4. ToKebabCase: PascalCase → ケバブケース
    // ═══════════════════════════════════════════════

    [Fact]
    public void PascalCaseがケバブケースに変換される()
    {
        var result = NameConversion.ToKebabCase("IsEnemyVisible");

        Assert.Equal("is-enemy-visible", result);
    }

    [Fact]
    public void 単一単語のPascalCaseがケバブケースに変換される()
    {
        var result = NameConversion.ToKebabCase("Health");

        Assert.Equal("health", result);
    }

    [Fact]
    public void 小文字のみのケバブケース変換はそのまま返される()
    {
        var result = NameConversion.ToKebabCase("health");

        Assert.Equal("health", result);
    }

    [Fact]
    public void 空文字列のケバブケース変換は空文字列を返す()
    {
        var result = NameConversion.ToKebabCase("");

        Assert.Equal("", result);
    }

    [Fact]
    public void camelCaseがケバブケースに変換される()
    {
        var result = NameConversion.ToKebabCase("isEnemyVisible");

        Assert.Equal("is-enemy-visible", result);
    }

    [Fact]
    public void 長い名前がケバブケースに変換される()
    {
        var result = NameConversion.ToKebabCase("AttackTargetPosition");

        Assert.Equal("attack-target-position", result);
    }

    // ═══════════════════════════════════════════════
    //  5. GenerateCandidates: 候補生成
    // ═══════════════════════════════════════════════

    [Fact]
    public void ケバブケースから5つの候補が生成される()
    {
        var candidates = NameConversion.GenerateCandidates("is-alive").ToArray();

        Assert.Equal(5, candidates.Length);
        Assert.Equal("is-alive", candidates[0]);        // そのまま
        Assert.Equal("IsAlive", candidates[1]);          // PascalCase
        Assert.Equal("isAlive", candidates[2]);           // camelCase
        Assert.Equal("_isAlive", candidates[3]);          // _camelCase
        Assert.Equal("is_alive", candidates[4]);          // snake_case
    }

    [Fact]
    public void PascalCase名から5つの候補が生成される()
    {
        var candidates = NameConversion.GenerateCandidates("Health").ToArray();

        Assert.Equal(5, candidates.Length);
        Assert.Equal("Health", candidates[0]);            // そのまま
        Assert.Equal("Health", candidates[1]);            // PascalCase（同一）
        Assert.Equal("health", candidates[2]);            // camelCase
        Assert.Equal("_health", candidates[3]);           // _camelCase
        Assert.Equal("health", candidates[4]);            // snake_case
    }

    [Fact]
    public void 複雑なケバブケース名の候補が正しく生成される()
    {
        var candidates = NameConversion.GenerateCandidates("attack-target-position").ToArray();

        Assert.Equal("attack-target-position", candidates[0]);
        Assert.Equal("AttackTargetPosition", candidates[1]);
        Assert.Equal("attackTargetPosition", candidates[2]);
        Assert.Equal("_attackTargetPosition", candidates[3]);
        Assert.Equal("attack_target_position", candidates[4]);
    }
}
