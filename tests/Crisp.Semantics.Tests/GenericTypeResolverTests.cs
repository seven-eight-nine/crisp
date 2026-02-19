using Crisp.Semantics;
using Crisp.Syntax;

namespace Crisp.Semantics.Tests;

/// <summary>
/// F12: Generics (GenericTypeResolver) のユニットテスト。
/// t-wada 式 TDD に基づき、型名パーシングから制約検証まで段階的にテストする。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>ParseGenericTypeName: ジェネリック型名の分解</description></item>
///   <item><description>IsGenericType: ジェネリック型判定</description></item>
///   <item><description>ValidateTypeArgumentCount: 型引数の数の検証（BS0042）</description></item>
///   <item><description>ValidateTypeConstraints: 型引数の制約検証（BS0041）</description></item>
///   <item><description>IsOpenGenericType: 開いたジェネリック型の検出（BS0043）</description></item>
/// </list>
/// </summary>
public class GenericTypeResolverTests
{
    // ═══════════════════════════════════════════════
    //  1. ParseGenericTypeName: 基本テスト
    // ═══════════════════════════════════════════════

    [Fact]
    public void 非ジェネリック型名が正しくパースされる()
    {
        // Arrange & Act
        var (baseName, typeArgs) = GenericTypeResolver.ParseGenericTypeName("SimpleAI");

        // Assert
        Assert.Equal("SimpleAI", baseName);
        Assert.Empty(typeArgs);
    }

    [Fact]
    public void 単一型引数のジェネリック型名がパースされる()
    {
        var (baseName, typeArgs) = GenericTypeResolver.ParseGenericTypeName("StateMachineAI<EnemyState>");

        Assert.Equal("StateMachineAI", baseName);
        Assert.Single(typeArgs);
        Assert.Equal("EnemyState", typeArgs[0]);
    }

    [Fact]
    public void 複数型引数のジェネリック型名がパースされる()
    {
        var (baseName, typeArgs) = GenericTypeResolver.ParseGenericTypeName("Dictionary<string, int>");

        Assert.Equal("Dictionary", baseName);
        Assert.Equal(2, typeArgs.Length);
        Assert.Equal("string", typeArgs[0]);
        Assert.Equal("int", typeArgs[1]);
    }

    [Fact]
    public void ネストされたジェネリック型引数が正しくパースされる()
    {
        // Arrange: ネストされたジェネリック型
        var (baseName, typeArgs) = GenericTypeResolver.ParseGenericTypeName("Outer<Inner<T>>");

        // Assert
        Assert.Equal("Outer", baseName);
        Assert.Single(typeArgs);
        Assert.Equal("Inner<T>", typeArgs[0]);
    }

    [Fact]
    public void 空の型名が処理される()
    {
        var (baseName, typeArgs) = GenericTypeResolver.ParseGenericTypeName("");

        Assert.Equal("", baseName);
        Assert.Empty(typeArgs);
    }

    // ═══════════════════════════════════════════════
    //  2. IsGenericType: 判定テスト
    // ═══════════════════════════════════════════════

    [Fact]
    public void ジェネリック型がtrueを返す()
    {
        Assert.True(GenericTypeResolver.IsGenericType("List<int>"));
    }

    [Fact]
    public void 非ジェネリック型がfalseを返す()
    {
        Assert.False(GenericTypeResolver.IsGenericType("SimpleAI"));
    }

    [Fact]
    public void ネストされたジェネリック型がtrueを返す()
    {
        Assert.True(GenericTypeResolver.IsGenericType("Dict<string, List<int>>"));
    }

    // ═══════════════════════════════════════════════
    //  3. ValidateTypeArgumentCount: Roslyn 依存テスト
    // ═══════════════════════════════════════════════

    [Fact]
    public void 型引数の数が一致する場合はエラーなし()
    {
        // Arrange: 1 型パラメータのジェネリック型
        var csSource = @"
namespace Test {
    public class AI<T> { }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.AI`1")!;
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeArgumentCount(
            typeSymbol, 1, bag, default);

        // Assert
        Assert.True(isValid);
        Assert.Empty(bag.Diagnostics);
    }

    [Fact]
    public void 型引数の数が不一致の場合BS0042が報告される()
    {
        // Arrange: 1 型パラメータのジェネリック型に 2 個指定
        var csSource = @"
namespace Test {
    public class AI<T> { }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.AI`1")!;
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeArgumentCount(
            typeSymbol, 2, bag, default);

        // Assert
        Assert.False(isValid);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0042", bag.Diagnostics[0].Id);
    }

    // ═══════════════════════════════════════════════
    //  4. IsOpenGenericType: 開いたジェネリック型
    // ═══════════════════════════════════════════════

    [Fact]
    public void 型引数が0個指定された場合BS0042が報告される()
    {
        // Arrange: 2 型パラメータのジェネリック型に 0 個指定
        var csSource = @"
namespace Test {
    public class Pair<T, U> { }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.Pair`2")!;
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeArgumentCount(
            typeSymbol, 0, bag, default);

        // Assert
        Assert.False(isValid);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0042", bag.Diagnostics[0].Id);
        Assert.Contains("2", bag.Diagnostics[0].Message); // 期待数
    }

    // ═══════════════════════════════════════════════
    //  3b. ValidateTypeConstraints: 制約検証
    // ═══════════════════════════════════════════════

    [Fact]
    public void 値型制約を満たす場合エラーなし()
    {
        // Arrange: where T : struct 制約のジェネリック型を int で構築
        var csSource = @"
namespace Test {
    public class Container<T> where T : struct { }
}";
        var compilation = TestHelper.GetCompilation(csSource);
        var openType = compilation.GetTypeByMetadataName("Test.Container`1")!;
        var intType = compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_Int32);
        var constructedType = openType.Construct(intType);
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeConstraints(constructedType, bag, default);

        // Assert
        Assert.True(isValid);
        Assert.Empty(bag.Diagnostics);
    }

    [Fact]
    public void 値型制約違反でBS0041が報告される()
    {
        // Arrange: where T : struct 制約に string（参照型）を渡す
        var csSource = @"
namespace Test {
    public class Container<T> where T : struct { }
}";
        var compilation = TestHelper.GetCompilation(csSource);
        var openType = compilation.GetTypeByMetadataName("Test.Container`1")!;
        var stringType = compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_String);
        var constructedType = openType.Construct(stringType);
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeConstraints(constructedType, bag, default);

        // Assert
        Assert.False(isValid);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0041", bag.Diagnostics[0].Id);
    }

    [Fact]
    public void 参照型制約を満たす場合エラーなし()
    {
        // Arrange: where T : class 制約に string（参照型）を渡す
        var csSource = @"
namespace Test {
    public class RefContainer<T> where T : class { }
}";
        var compilation = TestHelper.GetCompilation(csSource);
        var openType = compilation.GetTypeByMetadataName("Test.RefContainer`1")!;
        var stringType = compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_String);
        var constructedType = openType.Construct(stringType);
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeConstraints(constructedType, bag, default);

        // Assert
        Assert.True(isValid);
        Assert.Empty(bag.Diagnostics);
    }

    [Fact]
    public void 参照型制約違反でBS0041が報告される()
    {
        // Arrange: where T : class 制約に int（値型）を渡す
        var csSource = @"
namespace Test {
    public class RefContainer<T> where T : class { }
}";
        var compilation = TestHelper.GetCompilation(csSource);
        var openType = compilation.GetTypeByMetadataName("Test.RefContainer`1")!;
        var intType = compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_Int32);
        var constructedType = openType.Construct(intType);
        var bag = new DiagnosticBag();

        // Act
        var isValid = GenericTypeResolver.ValidateTypeConstraints(constructedType, bag, default);

        // Assert
        Assert.False(isValid);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0041", bag.Diagnostics[0].Id);
    }

    // ═══════════════════════════════════════════════
    //  4. IsOpenGenericType: 開いたジェネリック型
    // ═══════════════════════════════════════════════

    [Fact]
    public void 開いたジェネリック型でBS0043が報告される()
    {
        // Arrange: 開いたジェネリック型（型引数未指定）
        var csSource = @"
namespace Test {
    public class AI<T> { }
}";
        var typeSymbol = TestHelper.GetTypeSymbol(csSource, "Test.AI`1")!;
        var bag = new DiagnosticBag();

        // Act
        var isOpen = GenericTypeResolver.IsOpenGenericType(typeSymbol, bag, default);

        // Assert: 開いたジェネリック型は BS0043
        Assert.True(isOpen);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0043", bag.Diagnostics[0].Id);
    }
}
