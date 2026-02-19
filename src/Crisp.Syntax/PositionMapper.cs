using Crisp.Syntax.Cst;

namespace Crisp.Syntax;

/// <summary>
/// ソーステキストにおける行/列座標とオフセットの相互変換、
/// および CST ノードの位置検索を提供するユーティリティ。
///
/// LSP プロトコルは行/列（0始まり）で位置を表すが、
/// Crisp 内部では文字オフセット（<see cref="TextSpan"/>）を使用する。
/// このクラスがその変換を担う。
///
/// <para>
/// 使い方:
/// <code>
/// var mapper = new PositionMapper(source);
/// int offset = mapper.GetOffset(line: 2, column: 5);
/// var (line, col) = mapper.GetLineColumn(offset);
/// </code>
/// </para>
/// </summary>
public sealed class PositionMapper
{
    /// <summary>元のソーステキスト。</summary>
    private readonly string _source;

    /// <summary>
    /// 各行の先頭オフセットのテーブル。
    /// _lineStarts[0] は常に 0（最初の行の先頭）。
    /// _lineStarts[n] は n 行目の先頭オフセット。
    /// </summary>
    private readonly int[] _lineStarts;

    /// <summary>
    /// ソーステキストから PositionMapper を構築する。
    /// 行先頭オフセットテーブルを事前計算する。
    /// </summary>
    /// <param name="source">対象のソーステキスト。</param>
    public PositionMapper(string source)
    {
        _source = source;
        _lineStarts = ComputeLineStarts(source);
    }

    /// <summary>ソーステキスト内の行数。</summary>
    public int LineCount => _lineStarts.Length;

    // ═══════════════════════════════════════════════════════════
    //  座標変換
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 行/列座標（0始まり）から文字オフセットを取得する。
    /// </summary>
    /// <param name="line">行番号（0始まり）。</param>
    /// <param name="column">列番号（0始まり）。</param>
    /// <returns>文字オフセット。</returns>
    /// <exception cref="ArgumentOutOfRangeException">行番号が範囲外の場合。</exception>
    public int GetOffset(int line, int column)
    {
        if (line < 0 || line >= _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line),
                $"行番号 {line} は範囲外です（0〜{_lineStarts.Length - 1}）。");
        }

        var offset = _lineStarts[line] + column;
        return Math.Min(offset, _source.Length);
    }

    /// <summary>
    /// 文字オフセットから行/列座標（0始まり）を取得する。
    /// </summary>
    /// <param name="offset">文字オフセット。</param>
    /// <returns>行番号と列番号のタプル（ともに0始まり）。</returns>
    public (int Line, int Column) GetLineColumn(int offset)
    {
        offset = Math.Max(0, Math.Min(offset, _source.Length));

        // 二分探索で行を特定する
        var line = FindLine(offset);
        var column = offset - _lineStarts[line];
        return (line, column);
    }

    /// <summary>
    /// TextSpan を行/列の範囲（開始と終了）に変換する。
    /// </summary>
    /// <param name="span">変換対象の TextSpan。</param>
    /// <returns>開始位置と終了位置の行/列座標。</returns>
    public ((int Line, int Column) Start, (int Line, int Column) End) GetRange(TextSpan span)
    {
        var start = GetLineColumn(span.Start);
        var end = GetLineColumn(span.End);
        return (start, end);
    }

    // ═══════════════════════════════════════════════════════════
    //  CST ノード検索
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 指定したオフセット位置にある最も深い CST ノードを検索する。
    /// LSP のホバーや補完などで、カーソル位置のノードを特定するために使用する。
    /// </summary>
    /// <param name="root">検索の起点となる CST ルートノード。</param>
    /// <param name="offset">検索対象の文字オフセット。</param>
    /// <returns>
    /// 指定位置を含む最も深い CST ノード。
    /// 位置がどのノードにも含まれない場合はルートノード自身を返す。
    /// </returns>
    public static CstNode FindDeepestNode(CstNode root, int offset)
    {
        var current = root;

        while (true)
        {
            var found = false;
            foreach (var child in current.Children)
            {
                if (child.Span.Start <= offset && offset < child.Span.End)
                {
                    current = child;
                    found = true;
                    break;
                }
            }

            if (!found)
                break;
        }

        return current;
    }

    /// <summary>
    /// 指定したオフセット位置にあるトークンを検索する。
    /// セマンティックトークンやホバーで使用する。
    /// </summary>
    /// <param name="root">検索の起点となる CST ルートノード。</param>
    /// <param name="offset">検索対象の文字オフセット。</param>
    /// <returns>
    /// 指定位置を含むトークン。見つからない場合は <c>null</c>。
    /// </returns>
    public static Token? FindTokenAt(CstNode root, int offset)
    {
        foreach (var token in root.DescendantTokens())
        {
            if (token.Span.Start <= offset && offset < token.Span.End)
            {
                return token;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  内部実装
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストから各行の先頭オフセットを計算する。
    /// 改行文字は \r\n, \r, \n のいずれにも対応する。
    /// </summary>
    private static int[] ComputeLineStarts(string source)
    {
        var starts = new List<int> { 0 };

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                // \r\n の場合は1つの改行として扱う
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i++;
                }
                starts.Add(i + 1);
            }
            else if (source[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    /// <summary>
    /// 二分探索でオフセットが含まれる行を特定する。
    /// </summary>
    private int FindLine(int offset)
    {
        var lo = 0;
        var hi = _lineStarts.Length - 1;

        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (_lineStarts[mid] <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }
}
