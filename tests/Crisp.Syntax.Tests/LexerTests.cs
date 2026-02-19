using Crisp.Syntax;

namespace Crisp.Syntax.Tests;

/// <summary>
/// Lexer のユニットテスト。
/// t-wada 式 TDD に基づき、最小のテストケースから段階的に複雑さを増していく。
/// Arrange-Act-Assert パターンで記述する。
/// </summary>
public class LexerTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー: Trivia を除いた実質トークンだけを取得する
    // ──────────────────────────────────────────────

    /// <summary>Trivia を含まない実質トークン（EndOfFile も除く）のリストを返す。</summary>
    private static List<Token> Tokenize(string source)
    {
        var tokens = Lexer.Lex(source);
        return tokens.Where(t => t.Kind != TokenKind.EndOfFile).ToList();
    }

    /// <summary>EndOfFile を含む全トークンのリストを返す。</summary>
    private static List<Token> TokenizeAll(string source)
    {
        return Lexer.Lex(source).ToList();
    }

    // ═══════════════════════════════════════════════
    //  1. 空入力
    // ═══════════════════════════════════════════════

    [Fact]
    public void 空文字列をレキシングするとEndOfFileのみ返す()
    {
        var tokens = TokenizeAll("");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
    }

    // ═══════════════════════════════════════════════
    //  2. 括弧
    // ═══════════════════════════════════════════════

    [Fact]
    public void 開き括弧をレキシングできる()
    {
        var tokens = Tokenize("(");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal("(", tokens[0].Text);
    }

    [Fact]
    public void 閉じ括弧をレキシングできる()
    {
        var tokens = Tokenize(")");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.RightParen, tokens[0].Kind);
    }

    [Fact]
    public void 括弧の組み合わせをレキシングできる()
    {
        var tokens = Tokenize("()");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal(TokenKind.RightParen, tokens[1].Kind);
    }

    // ═══════════════════════════════════════════════
    //  3. 識別子
    // ═══════════════════════════════════════════════

    [Fact]
    public void 単純な識別子をレキシングできる()
    {
        var tokens = Tokenize("tree");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("tree", tokens[0].Text);
    }

    [Fact]
    public void ケバブケースの識別子をレキシングできる()
    {
        var tokens = Tokenize("is-enemy-visible");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("is-enemy-visible", tokens[0].Text);
    }

    [Fact]
    public void アンダースコアで始まる識別子をレキシングできる()
    {
        var tokens = Tokenize("_health");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("_health", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  4. リテラル
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数リテラルをレキシングできる()
    {
        var tokens = Tokenize("42");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal("42", tokens[0].Text);
    }

    [Fact]
    public void 浮動小数点リテラルをレキシングできる()
    {
        var tokens = Tokenize("3.14");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.FloatLiteral, tokens[0].Kind);
        Assert.Equal("3.14", tokens[0].Text);
    }

    [Fact]
    public void 負の整数リテラルをレキシングできる()
    {
        // 負号はリテラルの一部として認識される（空白の後に -digit が来る場合）
        var tokens = Tokenize("-3");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal("-3", tokens[0].Text);
    }

    [Fact]
    public void 負の浮動小数点リテラルをレキシングできる()
    {
        var tokens = Tokenize("-0.5");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.FloatLiteral, tokens[0].Kind);
        Assert.Equal("-0.5", tokens[0].Text);
    }

    [Fact]
    public void 文字列リテラルをレキシングできる()
    {
        var tokens = Tokenize("\"hello\"");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal("\"hello\"", tokens[0].Text);
    }

    [Fact]
    public void エスケープを含む文字列リテラルをレキシングできる()
    {
        var tokens = Tokenize("\"he\\\"llo\"");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal("\"he\\\"llo\"", tokens[0].Text);
    }

    [Fact]
    public void trueリテラルをレキシングできる()
    {
        var tokens = Tokenize("true");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.BoolTrue, tokens[0].Kind);
    }

    [Fact]
    public void falseリテラルをレキシングできる()
    {
        var tokens = Tokenize("false");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.BoolFalse, tokens[0].Kind);
    }

    [Fact]
    public void nullリテラルをレキシングできる()
    {
        var tokens = Tokenize("null");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.NullLiteral, tokens[0].Kind);
    }

    // ═══════════════════════════════════════════════
    //  5. メンバーアクセス
    // ═══════════════════════════════════════════════

    [Fact]
    public void 単一メンバーアクセスをレキシングできる()
    {
        var tokens = Tokenize(".Health");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.MemberAccess, tokens[0].Kind);
        Assert.Equal(".Health", tokens[0].Text);
    }

    [Fact]
    public void チェーンメンバーアクセスをレキシングできる()
    {
        var tokens = Tokenize(".Target.Position.x");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.MemberAccess, tokens[0].Kind);
        Assert.Equal(".Target.Position.x", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  6. enum リテラル
    // ═══════════════════════════════════════════════

    [Fact]
    public void enumリテラルをレキシングできる()
    {
        var tokens = Tokenize("::AIState.Combat");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.EnumLiteral, tokens[0].Kind);
        Assert.Equal("::AIState.Combat", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  7. キーワード引数
    // ═══════════════════════════════════════════════

    [Fact]
    public void キーワード引数をレキシングできる()
    {
        var tokens = Tokenize(":all");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal(":all", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  8. 演算子
    // ═══════════════════════════════════════════════

    [Theory]
    [InlineData("+", TokenKind.Plus)]
    [InlineData("-", TokenKind.Minus)]
    [InlineData("*", TokenKind.Star)]
    [InlineData("/", TokenKind.Slash)]
    [InlineData("%", TokenKind.Percent)]
    [InlineData("<", TokenKind.LessThan)]
    [InlineData(">", TokenKind.GreaterThan)]
    [InlineData("<=", TokenKind.LessEqual)]
    [InlineData(">=", TokenKind.GreaterEqual)]
    [InlineData("=", TokenKind.Equal)]
    [InlineData("!=", TokenKind.NotEqual)]
    public void 演算子をレキシングできる(string text, TokenKind expectedKind)
    {
        var tokens = Tokenize(text);

        Assert.Single(tokens);
        Assert.Equal(expectedKind, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  9. Trivia（空白・改行・コメント）
    // ═══════════════════════════════════════════════

    [Fact]
    public void 空白はトークンのLeadingTriviaとして保持される()
    {
        var tokens = Tokenize("  tree");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Single(tokens[0].LeadingTrivia);
        Assert.Equal(TriviaKind.Whitespace, tokens[0].LeadingTrivia[0].Kind);
        Assert.Equal("  ", tokens[0].LeadingTrivia[0].Text);
    }

    [Fact]
    public void コメントはTriviaとして保持される()
    {
        var tokens = Tokenize(";; これはコメント\ntree");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        // LeadingTrivia には コメント + 改行 が含まれる
        Assert.True(tokens[0].LeadingTrivia.Length >= 2);
        Assert.Equal(TriviaKind.Comment, tokens[0].LeadingTrivia[0].Kind);
        Assert.Equal(TriviaKind.Newline, tokens[0].LeadingTrivia[1].Kind);
    }

    [Fact]
    public void トークン直後の空白はTrailingTriviaになる()
    {
        // "( tree)" — ( の直後のスペースは ( の TrailingTrivia
        var tokens = Tokenize("( tree)");

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Single(tokens[0].TrailingTrivia);
        Assert.Equal(TriviaKind.Whitespace, tokens[0].TrailingTrivia[0].Kind);
    }

    // ═══════════════════════════════════════════════
    //  10. TextSpan の位置追跡
    // ═══════════════════════════════════════════════

    [Fact]
    public void 各トークンのSpanが正しい()
    {
        // "(tree)"
        var tokens = Tokenize("(tree)");

        Assert.Equal(3, tokens.Count);
        Assert.Equal(new TextSpan(0, 1), tokens[0].Span); // (
        Assert.Equal(new TextSpan(1, 4), tokens[1].Span); // tree
        Assert.Equal(new TextSpan(5, 1), tokens[2].Span); // )
    }

    // ═══════════════════════════════════════════════
    //  11. エラートークン
    // ═══════════════════════════════════════════════

    [Fact]
    public void 認識できない文字はErrorトークンになる()
    {
        var tokens = Tokenize("@");

        Assert.Single(tokens);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal("@", tokens[0].Text);
    }

    // ═══════════════════════════════════════════════
    //  12. 複合的なレキシング（Pattern 1: Minimal）
    // ═══════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalの基本構造をレキシングできる()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;

        var tokens = Tokenize(source);

        // 主要なトークンが正しく分類されているか確認
        var kinds = tokens.Select(t => t.Kind).ToList();

        // 先頭: ( tree SimpleCombat
        Assert.Equal(TokenKind.LeftParen, kinds[0]);
        Assert.Equal(TokenKind.Identifier, kinds[1]); // "tree"
        Assert.Equal("tree", tokens[1].Text);
        Assert.Equal(TokenKind.Identifier, kinds[2]); // "SimpleCombat"

        // .Health は MemberAccess
        Assert.Contains(tokens, t => t.Kind == TokenKind.MemberAccess && t.Text == ".Health");

        // 30 は IntLiteral
        Assert.Contains(tokens, t => t.Kind == TokenKind.IntLiteral && t.Text == "30");

        // < は LessThan
        Assert.Contains(tokens, t => t.Kind == TokenKind.LessThan);

        // .Flee, .Patrol は MemberAccess
        Assert.Contains(tokens, t => t.Kind == TokenKind.MemberAccess && t.Text == ".Flee");
        Assert.Contains(tokens, t => t.Kind == TokenKind.MemberAccess && t.Text == ".Patrol");
    }

    // ═══════════════════════════════════════════════
    //  13. 負リテラル vs 単項マイナスの区別
    // ═══════════════════════════════════════════════

    [Fact]
    public void 開き括弧直後のマイナスは演算子として認識される()
    {
        // "(- 3 x)" — ( の直後の - は演算子
        var tokens = Tokenize("(- 3 x)");

        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal(TokenKind.Minus, tokens[1].Kind);
        Assert.Equal(TokenKind.IntLiteral, tokens[2].Kind);
        Assert.Equal("3", tokens[2].Text);
    }

    [Fact]
    public void 空白後の負リテラルはリテラルとして認識される()
    {
        // 引数位置での -3 は負リテラル
        var tokens = Tokenize("(> .Health -1)");

        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal(TokenKind.GreaterThan, tokens[1].Kind);
        Assert.Equal(TokenKind.MemberAccess, tokens[2].Kind);
        Assert.Equal(TokenKind.IntLiteral, tokens[3].Kind);
        Assert.Equal("-1", tokens[3].Text);
        Assert.Equal(TokenKind.RightParen, tokens[4].Kind);
    }

    // ═══════════════════════════════════════════════
    //  14. ラウンドトリップ（ソース復元）
    // ═══════════════════════════════════════════════

    [Theory]
    [InlineData("(tree SimpleCombat (.Patrol))")]
    [InlineData(";; comment\n(tree T (.A))")]
    [InlineData("(check (< .Health 30))")]
    public void トークン列からソーステキストを復元できる(string source)
    {
        var tokens = TokenizeAll(source);
        var restored = string.Concat(
            tokens.SelectMany(t =>
                t.LeadingTrivia.Select(tr => tr.Text)
                    .Append(t.Kind == TokenKind.EndOfFile ? "" : t.Text)
                    .Concat(t.TrailingTrivia.Select(tr => tr.Text))));

        Assert.Equal(source, restored);
    }
}
