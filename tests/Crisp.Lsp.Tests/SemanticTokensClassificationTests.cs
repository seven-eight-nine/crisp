using Crisp.Lsp.Handlers;
using Crisp.Syntax;

namespace Crisp.Lsp.Tests;

/// <summary>
/// <see cref="SemanticTokensHandler.ClassifyToken"/> の t-wada 式 TDD テスト。
///
/// トークン種別ごとのセマンティック分類を検証する:
/// 1. 予約語キーワード → keyword (0)
/// 2. メンバーアクセス → property (1)
/// 3. 数値リテラル → number (2)
/// 4. 文字列リテラル → string (3)
/// 5. 演算子 → operator (4)
/// 6. キーワード引数 → parameter (6)
/// 7. 分類対象外 → -1
/// </summary>
public class SemanticTokensClassificationTests
{
    // ═══════════════════════════════════════════════════════════
    //  1. 予約語キーワード
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tree")]
    [InlineData("select")]
    [InlineData("seq")]
    [InlineData("parallel")]
    [InlineData("check")]
    [InlineData("guard")]
    [InlineData("if")]
    [InlineData("invert")]
    [InlineData("repeat")]
    [InlineData("timeout")]
    [InlineData("cooldown")]
    [InlineData("while")]
    public void 予約語はkeywordに分類される(string keyword)
    {
        var token = MakeToken(TokenKind.Identifier, keyword);

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(0, result); // keyword
    }

    [Fact]
    public void 予約語でない識別子は分類対象外である()
    {
        var token = MakeToken(TokenKind.Identifier, "Patrol");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(-1, result);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. メンバーアクセス
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void メンバーアクセスはpropertyに分類される()
    {
        var token = MakeToken(TokenKind.MemberAccess, ".Health");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(1, result); // property
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 数値リテラル
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 整数リテラルはnumberに分類される()
    {
        var token = MakeToken(TokenKind.IntLiteral, "42");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(2, result); // number
    }

    [Fact]
    public void 浮動小数点リテラルはnumberに分類される()
    {
        var token = MakeToken(TokenKind.FloatLiteral, "3.14");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(2, result); // number
    }

    // ═══════════════════════════════════════════════════════════
    //  4. ブール・null リテラル
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void trueリテラルはkeywordに分類される()
    {
        var token = MakeToken(TokenKind.BoolTrue, "true");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(0, result); // keyword
    }

    [Fact]
    public void falseリテラルはkeywordに分類される()
    {
        var token = MakeToken(TokenKind.BoolFalse, "false");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(0, result); // keyword
    }

    [Fact]
    public void nullリテラルはkeywordに分類される()
    {
        var token = MakeToken(TokenKind.NullLiteral, "null");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(0, result); // keyword
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 文字列リテラル
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 文字列リテラルはstringに分類される()
    {
        var token = MakeToken(TokenKind.StringLiteral, "\"hello\"");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(3, result); // string
    }

    // ═══════════════════════════════════════════════════════════
    //  6. 演算子
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(TokenKind.Plus)]
    [InlineData(TokenKind.Minus)]
    [InlineData(TokenKind.Star)]
    [InlineData(TokenKind.Slash)]
    [InlineData(TokenKind.Percent)]
    [InlineData(TokenKind.LessThan)]
    [InlineData(TokenKind.GreaterThan)]
    [InlineData(TokenKind.Equal)]
    [InlineData(TokenKind.NotEqual)]
    public void 演算子はoperatorに分類される(TokenKind kind)
    {
        var token = MakeToken(kind, "+");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(4, result); // operator
    }

    // ═══════════════════════════════════════════════════════════
    //  7. キーワード引数
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void キーワード引数はparameterに分類される()
    {
        var token = MakeToken(TokenKind.Keyword, ":all");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(6, result); // parameter
    }

    // ═══════════════════════════════════════════════════════════
    //  8. 分類対象外
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 括弧は分類対象外である()
    {
        var token = MakeToken(TokenKind.LeftParen, "(");

        var result = SemanticTokensHandler.ClassifyToken(token);

        Assert.Equal(-1, result);
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    private static Token MakeToken(TokenKind kind, string text)
    {
        return new Token(kind, text, new TextSpan(0, text.Length),
            System.Collections.Immutable.ImmutableArray<Trivia>.Empty,
            System.Collections.Immutable.ImmutableArray<Trivia>.Empty);
    }
}
