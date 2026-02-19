using System.Collections.Immutable;
using Crisp.Semantics;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;

namespace Crisp.Semantics.Tests;

/// <summary>
/// F13: Nullable Analysis (NullableAnalyzer) のユニットテスト。
/// t-wada 式 TDD に基づき、NullState の基本操作から条件式の絞り込みまで段階的にテストする。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>NullState: イミュータブルな null 状態管理</description></item>
///   <item><description>NarrowFromCondition: 条件式からの null 状態絞り込み</description></item>
///   <item><description>IsNullableSymbol / IsDefinitelyNonNull: Roslyn シンボルの null 許容判定</description></item>
/// </list>
/// </summary>
public class NullableAnalyzerTests
{
    /// <summary>テスト用のダミー CstNode を生成する。</summary>
    private static CstNode DummyOrigin() =>
        new CstError(ImmutableArray<Token>.Empty);

    /// <summary>テスト用のメンバーアクセス式を生成する。</summary>
    private static AstMemberAccessExpr MemberExpr(params string[] segments) =>
        new(DummyOrigin(), new MemberPath(segments.ToImmutableArray()));

    /// <summary>テスト用の null リテラル式を生成する。</summary>
    private static AstLiteralExpr NullLiteral() =>
        new(DummyOrigin(), null, LiteralKind.Null);

    /// <summary>テスト用の二項式を生成する。</summary>
    private static AstBinaryExpr Binary(BinaryOp op, AstExpr lhs, AstExpr rhs) =>
        new(DummyOrigin(), op, lhs, rhs);

    /// <summary>テスト用の単項式を生成する。</summary>
    private static AstUnaryExpr Unary(UnaryOp op, AstExpr operand) =>
        new(DummyOrigin(), op, operand);

    /// <summary>テスト用の論理式を生成する。</summary>
    private static AstLogicExpr Logic(LogicOp op, params AstExpr[] operands) =>
        new(DummyOrigin(), op, operands.ToImmutableArray());

    // ═══════════════════════════════════════════════
    //  1. NullState: 基本操作
    // ═══════════════════════════════════════════════

    [Fact]
    public void 初期状態で全メンバーが不明()
    {
        // Arrange & Act
        var state = NullableAnalyzer.NullState.Empty;

        // Assert
        Assert.False(state.IsKnownNonNull("Target"));
        Assert.False(state.IsKnownNull("Target"));
        Assert.Equal(0, state.KnownNonNullCount);
        Assert.Equal(0, state.KnownNullCount);
    }

    [Fact]
    public void WithNonNullで非null状態が記録される()
    {
        // Arrange
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var updated = state.WithNonNull("Target");

        // Assert
        Assert.True(updated.IsKnownNonNull("Target"));
        Assert.False(updated.IsKnownNull("Target"));
        Assert.Equal(1, updated.KnownNonNullCount);
    }

    [Fact]
    public void WithNullでnull状態が記録される()
    {
        // Arrange
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var updated = state.WithNull("Target");

        // Assert
        Assert.True(updated.IsKnownNull("Target"));
        Assert.False(updated.IsKnownNonNull("Target"));
        Assert.Equal(1, updated.KnownNullCount);
    }

    [Fact]
    public void WithNonNullはイミュータブルで元の状態を変更しない()
    {
        // Arrange
        var original = NullableAnalyzer.NullState.Empty;

        // Act
        var updated = original.WithNonNull("Target");

        // Assert: 元の状態は変更されていない
        Assert.False(original.IsKnownNonNull("Target"));
        Assert.True(updated.IsKnownNonNull("Target"));
    }

    [Fact]
    public void WithNonNullはnull状態を解除する()
    {
        // Arrange: まず null として記録
        var state = NullableAnalyzer.NullState.Empty.WithNull("Target");

        // Act: 非 null に変更
        var updated = state.WithNonNull("Target");

        // Assert: null 状態が解除されている
        Assert.True(updated.IsKnownNonNull("Target"));
        Assert.False(updated.IsKnownNull("Target"));
    }

    [Fact]
    public void WithNullは非null状態を解除する()
    {
        // Arrange: まず非 null として記録
        var state = NullableAnalyzer.NullState.Empty.WithNonNull("Target");

        // Act: null に変更
        var updated = state.WithNull("Target");

        // Assert: 非 null 状態が解除されている
        Assert.False(updated.IsKnownNonNull("Target"));
        Assert.True(updated.IsKnownNull("Target"));
    }

    [Fact]
    public void 複数メンバーの状態が独立に管理される()
    {
        // Arrange & Act
        var state = NullableAnalyzer.NullState.Empty
            .WithNonNull("Target")
            .WithNull("Weapon");

        // Assert
        Assert.True(state.IsKnownNonNull("Target"));
        Assert.False(state.IsKnownNull("Target"));
        Assert.True(state.IsKnownNull("Weapon"));
        Assert.False(state.IsKnownNonNull("Weapon"));
        Assert.Equal(1, state.KnownNonNullCount);
        Assert.Equal(1, state.KnownNullCount);
    }

    // ═══════════════════════════════════════════════
    //  2. NarrowFromCondition: != null パターン
    // ═══════════════════════════════════════════════

    [Fact]
    public void NotEqual_null条件でメンバーが非nullに絞り込まれる()
    {
        // Arrange: (!= .Target null)
        var condition = Binary(BinaryOp.Ne, MemberExpr("Target"), NullLiteral());
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert
        Assert.True(narrowed.IsKnownNonNull("Target"));
    }

    [Fact]
    public void Equal_null条件でメンバーがnullに絞り込まれる()
    {
        // Arrange: (== .Target null)
        var condition = Binary(BinaryOp.Eq, MemberExpr("Target"), NullLiteral());
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert
        Assert.True(narrowed.IsKnownNull("Target"));
    }

    // ═══════════════════════════════════════════════
    //  3. NarrowFromCondition: not パターン
    // ═══════════════════════════════════════════════

    [Fact]
    public void Not_Equal_null条件でメンバーが非nullに絞り込まれる()
    {
        // Arrange: (not (== .Target null))
        var inner = Binary(BinaryOp.Eq, MemberExpr("Target"), NullLiteral());
        var condition = Unary(UnaryOp.Not, inner);
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert: not(== null) → 非 null
        Assert.True(narrowed.IsKnownNonNull("Target"));
    }

    [Fact]
    public void Not_NotEqual_null条件でメンバーがnullに絞り込まれる()
    {
        // Arrange: (not (!= .Target null))
        var inner = Binary(BinaryOp.Ne, MemberExpr("Target"), NullLiteral());
        var condition = Unary(UnaryOp.Not, inner);
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert: not(!= null) → null
        Assert.True(narrowed.IsKnownNull("Target"));
    }

    // ═══════════════════════════════════════════════
    //  4. NarrowFromCondition: and パターン
    // ═══════════════════════════════════════════════

    [Fact]
    public void And条件で複数メンバーが絞り込まれる()
    {
        // Arrange: (and (!= .Target null) (!= .Weapon null))
        var cond1 = Binary(BinaryOp.Ne, MemberExpr("Target"), NullLiteral());
        var cond2 = Binary(BinaryOp.Ne, MemberExpr("Weapon"), NullLiteral());
        var condition = Logic(LogicOp.And, cond1, cond2);
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert: 両方とも非 null に絞り込まれる
        Assert.True(narrowed.IsKnownNonNull("Target"));
        Assert.True(narrowed.IsKnownNonNull("Weapon"));
    }

    // ═══════════════════════════════════════════════
    //  5. NarrowFromCondition: null が左辺のパターン
    // ═══════════════════════════════════════════════

    [Fact]
    public void null左辺のNotEqual条件でメンバーが非nullに絞り込まれる()
    {
        // Arrange: (!= null .Target)
        var condition = Binary(BinaryOp.Ne, NullLiteral(), MemberExpr("Target"));
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert
        Assert.True(narrowed.IsKnownNonNull("Target"));
    }

    // ═══════════════════════════════════════════════
    //  6. NarrowFromCondition: 無関係な条件
    // ═══════════════════════════════════════════════

    [Fact]
    public void 非null比較条件では状態が変更されない()
    {
        // Arrange: (> .Health 0) — null 比較ではない
        var intLiteral = new AstLiteralExpr(DummyOrigin(), 0, LiteralKind.Int);
        var condition = Binary(BinaryOp.Gt, MemberExpr("Health"), intLiteral);
        var state = NullableAnalyzer.NullState.Empty;

        // Act
        var narrowed = NullableAnalyzer.NarrowFromCondition(condition, state);

        // Assert: 状態は変更されない
        Assert.Equal(0, narrowed.KnownNonNullCount);
        Assert.Equal(0, narrowed.KnownNullCount);
    }

    // ═══════════════════════════════════════════════
    //  7. IsNullableSymbol / IsDefinitelyNonNull: Roslyn 依存テスト
    // ═══════════════════════════════════════════════

    [Fact]
    public void 値型プロパティは非nullと判定される()
    {
        // Arrange
        var csSource = @"
namespace Test {
    public class Agent {
        public int Health { get; set; }
    }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.Agent")!;
        var healthProp = typeSymbol.GetMembers("Health").OfType<Microsoft.CodeAnalysis.IPropertySymbol>().First();

        // Act & Assert
        Assert.True(NullableAnalyzer.IsDefinitelyNonNull(healthProp));
        Assert.False(NullableAnalyzer.IsNullableSymbol(healthProp));
    }

    [Fact]
    public void Nullable値型プロパティはnull許容と判定される()
    {
        // Arrange
        var csSource = @"
#nullable enable
namespace Test {
    public class Agent {
        public int? OptionalHealth { get; set; }
    }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.Agent")!;
        var prop = typeSymbol.GetMembers("OptionalHealth").OfType<Microsoft.CodeAnalysis.IPropertySymbol>().First();

        // Act & Assert
        Assert.True(NullableAnalyzer.IsNullableSymbol(prop));
        Assert.False(NullableAnalyzer.IsDefinitelyNonNull(prop));
    }

    [Fact]
    public void nullable参照型プロパティはnull許容と判定される()
    {
        // Arrange
        var csSource = @"
#nullable enable
namespace Test {
    public class Agent {
        public string? Name { get; set; }
    }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.Agent")!;
        var prop = typeSymbol.GetMembers("Name").OfType<Microsoft.CodeAnalysis.IPropertySymbol>().First();

        // Act & Assert
        Assert.True(NullableAnalyzer.IsNullableSymbol(prop));
    }

    [Fact]
    public void 非nullable参照型プロパティは非nullと判定される()
    {
        // Arrange
        var csSource = @"
#nullable enable
namespace Test {
    public class Agent {
        public string Name { get; set; } = """";
    }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.Agent")!;
        var prop = typeSymbol.GetMembers("Name").OfType<Microsoft.CodeAnalysis.IPropertySymbol>().First();

        // Act & Assert
        Assert.True(NullableAnalyzer.IsDefinitelyNonNull(prop));
        Assert.False(NullableAnalyzer.IsNullableSymbol(prop));
    }
}
