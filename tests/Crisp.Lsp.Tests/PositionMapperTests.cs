using Crisp.Syntax;
using Crisp.Syntax.Cst;

namespace Crisp.Lsp.Tests;

/// <summary>
/// <see cref="PositionMapper"/> の t-wada 式 TDD テスト。
///
/// 最小テストから段階的に複雑さを増す:
/// 1. 単一行での基本変換
/// 2. 複数行での変換
/// 3. 改行コードの違い（LF, CRLF, CR）
/// 4. 範囲変換（TextSpan → Range）
/// 5. CST ノード検索
/// </summary>
public class PositionMapperTests
{
    // ═══════════════════════════════════════════════════════════
    //  1. 単一行での基本変換
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 空文字列では行数は1である()
    {
        var mapper = new PositionMapper("");

        Assert.Equal(1, mapper.LineCount);
    }

    [Fact]
    public void 単一行のオフセット0は行0列0である()
    {
        var mapper = new PositionMapper("hello");

        var (line, col) = mapper.GetLineColumn(0);

        Assert.Equal(0, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void 単一行のオフセット3は行0列3である()
    {
        var mapper = new PositionMapper("hello");

        var (line, col) = mapper.GetLineColumn(3);

        Assert.Equal(0, line);
        Assert.Equal(3, col);
    }

    [Fact]
    public void 行0列0のオフセットは0である()
    {
        var mapper = new PositionMapper("hello");

        var offset = mapper.GetOffset(0, 0);

        Assert.Equal(0, offset);
    }

    [Fact]
    public void 行0列3のオフセットは3である()
    {
        var mapper = new PositionMapper("hello");

        var offset = mapper.GetOffset(0, 3);

        Assert.Equal(3, offset);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 複数行での変換
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void LF改行では2行になる()
    {
        var mapper = new PositionMapper("ab\ncd");

        Assert.Equal(2, mapper.LineCount);
    }

    [Fact]
    public void 二行目の先頭は行1列0である()
    {
        var mapper = new PositionMapper("ab\ncd");

        var (line, col) = mapper.GetLineColumn(3); // 'c' のオフセット

        Assert.Equal(1, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void 二行目の二文字目は行1列1である()
    {
        var mapper = new PositionMapper("ab\ncd");

        var (line, col) = mapper.GetLineColumn(4); // 'd' のオフセット

        Assert.Equal(1, line);
        Assert.Equal(1, col);
    }

    [Fact]
    public void 行1列0のオフセットは3である()
    {
        var mapper = new PositionMapper("ab\ncd");

        var offset = mapper.GetOffset(1, 0);

        Assert.Equal(3, offset);
    }

    [Fact]
    public void 三行のテキストでは行数は三である()
    {
        var mapper = new PositionMapper("a\nb\nc");

        Assert.Equal(3, mapper.LineCount);
    }

    [Fact]
    public void 三行目の先頭のオフセットが正しく変換される()
    {
        var mapper = new PositionMapper("a\nb\nc");

        var offset = mapper.GetOffset(2, 0);
        var (line, col) = mapper.GetLineColumn(offset);

        Assert.Equal(2, line);
        Assert.Equal(0, col);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 改行コードの違い（CRLF, CR）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CRLF改行では2行になる()
    {
        var mapper = new PositionMapper("ab\r\ncd");

        Assert.Equal(2, mapper.LineCount);
    }

    [Fact]
    public void CRLF改行の2行目先頭は正しいオフセットを返す()
    {
        var mapper = new PositionMapper("ab\r\ncd");

        var offset = mapper.GetOffset(1, 0);

        Assert.Equal(4, offset); // "ab\r\n" は4文字
    }

    [Fact]
    public void CR単独改行でも2行になる()
    {
        var mapper = new PositionMapper("ab\rcd");

        Assert.Equal(2, mapper.LineCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 範囲変換（TextSpan → Range）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TextSpanを行列の範囲に変換できる()
    {
        var mapper = new PositionMapper("ab\ncd\nef");
        var span = new TextSpan(3, 2); // "cd" のスパン

        var range = mapper.GetRange(span);

        Assert.Equal((1, 0), range.Start);
        Assert.Equal((1, 2), range.End);
    }

    [Fact]
    public void 行をまたぐTextSpanが正しく変換される()
    {
        var mapper = new PositionMapper("ab\ncd");
        var span = new TextSpan(1, 3); // "b\nc"

        var range = mapper.GetRange(span);

        Assert.Equal((0, 1), range.Start);
        Assert.Equal((1, 1), range.End);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 境界値
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ソース末尾のオフセットは最終行の末尾列を返す()
    {
        var mapper = new PositionMapper("hello");

        var (line, col) = mapper.GetLineColumn(5); // "hello" の長さ

        Assert.Equal(0, line);
        Assert.Equal(5, col);
    }

    [Fact]
    public void 範囲外のオフセットはクランプされる()
    {
        var mapper = new PositionMapper("hello");

        var (line, col) = mapper.GetLineColumn(100);

        Assert.Equal(0, line);
        Assert.Equal(5, col); // ソース長にクランプ
    }

    [Fact]
    public void 負のオフセットは0にクランプされる()
    {
        var mapper = new PositionMapper("hello");

        var (line, col) = mapper.GetLineColumn(-5);

        Assert.Equal(0, line);
        Assert.Equal(0, col);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. CST ノード検索
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindTokenAtはオフセット位置のトークンを返す()
    {
        var (program, _) = Parser.Parse("(tree T (.Do))");

        // '(' のオフセット0
        var token = PositionMapper.FindTokenAt(program, 0);

        Assert.NotNull(token);
        Assert.Equal(TokenKind.LeftParen, token.Value.Kind);
    }

    [Fact]
    public void FindTokenAtは識別子トークンを返す()
    {
        var (program, _) = Parser.Parse("(tree T (.Do))");

        // 'tree' のオフセット1
        var token = PositionMapper.FindTokenAt(program, 1);

        Assert.NotNull(token);
        Assert.Equal(TokenKind.Identifier, token.Value.Kind);
        Assert.Equal("tree", token.Value.Text);
    }

    [Fact]
    public void FindTokenAtはメンバーアクセストークンを返す()
    {
        var (program, _) = Parser.Parse("(tree T (.Do))");

        // '.Do' のオフセット9（'(' の後）
        var token = PositionMapper.FindTokenAt(program, 9);

        Assert.NotNull(token);
        Assert.Equal(TokenKind.MemberAccess, token.Value.Kind);
    }

    [Fact]
    public void FindDeepestNodeは最も深いノードを返す()
    {
        var (program, _) = Parser.Parse("(tree T (.Do))");

        // '.Do' のオフセット位置で最も深いノードを探す
        var node = PositionMapper.FindDeepestNode(program, 9);

        Assert.NotNull(node);
    }
}
