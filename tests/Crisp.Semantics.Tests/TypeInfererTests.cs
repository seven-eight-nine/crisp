using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Pipeline;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics.Tests;

/// <summary>
/// <see cref="TypeInferer"/> のテスト。
///
/// t-wada 式 TDD: リテラルの型推論から段階的に二項演算、ErrorType 伝搬へ。
/// <list type="number">
///   <item><description>リテラルの型推論</description></item>
///   <item><description>メンバーアクセスの型推論</description></item>
///   <item><description>算術演算の型推論</description></item>
///   <item><description>比較演算の型推論</description></item>
///   <item><description>無効な算術演算のエラー報告</description></item>
///   <item><description>ErrorType 伝搬（カスケード抑制）</description></item>
/// </list>
/// </summary>
public class TypeInfererTests
{
    private const string ContextSource = @"
using Crisp.Runtime;

namespace Test;

public class AI
{
    public float Health { get; set; }
    public int Level { get; set; }
    public bool IsAlive { get; set; }
    public string Name { get; set; } = """";

    public BtStatus Patrol() => BtStatus.Success;
    public float GetDistance() => 0f;
}";

    /// <summary>
    /// 指定した DSL 式のチェックノード内の式に推論された型を取得する。
    /// テスト簡略化のため、(tree T (check EXPR)) の形式を想定する。
    /// </summary>
    private static CrispType InferExprType(string dslExpr)
    {
        var dsl = $"(tree T (check {dslExpr}))";
        var contextType = TestHelper.GetTypeSymbol(ContextSource, "Test.AI")!;
        var astTrees = TestHelper.ParseToAst(dsl);

        var model = new SemanticModel();
        var diagnostics = new DiagnosticBag();

        var resolver = new NameResolver(contextType, model, diagnostics);
        resolver.ResolveTree(astTrees[0]);

        var inferer = new TypeInferer(model, diagnostics);
        inferer.InferTree(astTrees[0]);

        // check ノードの式を取得
        var checkNode = (AstCheck)astTrees[0].Body[0];
        return model.GetType(checkNode.Expr);
    }

    // ═══════════════════════════════════════════════════════════
    //  1. リテラルの型推論
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void intリテラルの型がIntに推論される()
    {
        Assert.Equal(CrispType.Int, InferExprType("42"));
    }

    [Fact]
    public void floatリテラルの型がFloatに推論される()
    {
        Assert.Equal(CrispType.Float, InferExprType("3.14"));
    }

    [Fact]
    public void boolリテラルの型がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("true"));
    }

    [Fact]
    public void stringリテラルの型がStringに推論される()
    {
        Assert.Equal(CrispType.String, InferExprType("\"hello\""));
    }

    // ═══════════════════════════════════════════════════════════
    //  2. メンバーアクセスの型推論
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void floatプロパティの型がFloatに推論される()
    {
        Assert.Equal(CrispType.Float, InferExprType(".Health"));
    }

    [Fact]
    public void intプロパティの型がIntに推論される()
    {
        Assert.Equal(CrispType.Int, InferExprType(".Level"));
    }

    [Fact]
    public void boolプロパティの型がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType(".IsAlive"));
    }

    [Fact]
    public void stringプロパティの型がStringに推論される()
    {
        Assert.Equal(CrispType.String, InferExprType(".Name"));
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 算術演算の型推論
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void int同士の加算がIntに推論される()
    {
        Assert.Equal(CrispType.Int, InferExprType("(+ 1 2)"));
    }

    [Fact]
    public void float同士の加算がFloatに推論される()
    {
        Assert.Equal(CrispType.Float, InferExprType("(+ 1.0 2.0)"));
    }

    [Fact]
    public void intとfloatの混合がFloatに昇格される()
    {
        Assert.Equal(CrispType.Float, InferExprType("(+ 1 2.0)"));
    }

    [Fact]
    public void floatとintの混合がFloatに昇格される()
    {
        Assert.Equal(CrispType.Float, InferExprType("(+ .Health 1)"));
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 比較演算の型推論
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 比較演算の結果がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("(< .Health 30.0)"));
    }

    [Fact]
    public void int同士の等価比較がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("(= .Level 5)"));
    }

    [Fact]
    public void intとfloatの比較がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("(> .Level 1.5)"));
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 論理演算の型推論
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void and演算の結果がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("(and .IsAlive (> .Health 0.0))"));
    }

    [Fact]
    public void or演算の結果がBoolに推論される()
    {
        Assert.Equal(CrispType.Bool, InferExprType("(or .IsAlive (> .Level 0))"));
    }

    // ═══════════════════════════════════════════════════════════
    //  6. 無効な算術演算のエラー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void string型に算術演算子を適用するとBS0004が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check (+ .Name 1)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0004").ToList();
        Assert.Single(errors);
        Assert.Contains("+", errors[0].Message);
        Assert.Contains("string", errors[0].Message);
    }

    [Fact]
    public void bool型に算術演算子を適用するとBS0004が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check (+ .IsAlive 1)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Id == "BS0004");
    }

    // ═══════════════════════════════════════════════════════════
    //  7. 比較不可能な型
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void floatとstringの比較でBS0003が報告される()
    {
        var result = TestHelper.Analyze(
            "(tree T (check (< .Health \"hello\")))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        var errors = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0003").ToList();
        Assert.Single(errors);
    }

    // ═══════════════════════════════════════════════════════════
    //  8. ErrorType 伝搬（カスケード抑制）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ErrorTypeが算術演算の親に伝搬する()
    {
        // (+ .Name 1) → BS0004、結果は Error
        // (> Error 0) → 左辺が Error なので比較エラーは報告しない
        Assert.Equal(CrispType.Error, InferExprType("(> (+ .Name 1) 0)"));
    }

    [Fact]
    public void ErrorType伝搬時に追加のBS0003が報告されない()
    {
        // (+ .Name 1) → BS0004 のみ
        // (> (+ .Name 1) 0) → 左辺 Error → BS0003 は報告されない
        var result = TestHelper.Analyze(
            "(tree T (check (> (+ .Name 1) 0)))",
            ContextSource, "Test.AI");

        Assert.True(result.HasErrors);
        // BS0004 のみが報告され、BS0003 は抑制される
        var bs0004 = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0004").ToList();
        var bs0003 = result.Diagnostics.Diagnostics.Where(d => d.Id == "BS0003").ToList();
        Assert.Single(bs0004);
        Assert.Empty(bs0003);
    }

    [Fact]
    public void 未解決メンバーの型がErrorに推論される()
    {
        Assert.Equal(CrispType.Error, InferExprType(".NonExistent"));
    }
}
