namespace Crisp.Syntax.Cst;

/// <summary>
/// Crisp CST（具象構文木）ノードの抽象基底クラス。
/// CST はロスレスな構文木であり、空白・コメントを含む全ての情報を保持する。
/// これにより、フォーマッタや LSP がエラー状態でも正確にソース情報を参照できる。
/// </summary>
public abstract partial class CstNode
{
    /// <summary>
    /// ソーステキスト上のこのノードの位置範囲（Trivia を含まない）。
    /// </summary>
    public abstract TextSpan Span { get; }

    /// <summary>
    /// Trivia を含むこのノードの完全な位置範囲。
    /// </summary>
    public abstract TextSpan FullSpan { get; }

    /// <summary>
    /// ノードの一意識別子。パーサーが構文木を構築した後に連番で付与される。
    /// CST → AST → IR を通じて安定的に引き継がれ、
    /// Debugger（F7）、Visual Editor（F4）、Hot Reload（F9）で共通に使用する。
    /// デフォルト値 -1 は未割り当てを示す。
    /// </summary>
    public int Id { get; internal set; } = -1;

    /// <summary>
    /// 親ノード。ルートノードの場合は <c>null</c>。
    /// パーサーが構文木を構築した後に設定される。
    /// </summary>
    public CstNode? Parent { get; internal set; }

    /// <summary>
    /// 子ノードの列挙（Trivia を含まない）。
    /// </summary>
    public abstract IEnumerable<CstNode> Children { get; }

    /// <summary>
    /// このノード配下の全トークンを深さ優先で列挙する（Trivia を含む）。
    /// ソーステキストの復元に使用する。
    /// </summary>
    public abstract IEnumerable<Token> DescendantTokens();

    /// <summary>
    /// このノード配下の全トークンからソーステキストを復元する。
    /// ロスレス CST の核心的な不変条件: <c>source == cst.ToFullString()</c>。
    /// </summary>
    public string ToFullString()
    {
        return string.Concat(
            DescendantTokens().SelectMany(t =>
                t.LeadingTrivia.Select(tr => tr.Text)
                    .Append(t.Kind == TokenKind.EndOfFile ? "" : t.Text)
                    .Concat(t.TrailingTrivia.Select(tr => tr.Text))));
    }

    /// <summary>
    /// CSTの構造をインデント付きテキストとしてダンプする（デバッグ・テスト用）。
    /// </summary>
    public string DumpTree(int indent = 0)
    {
        var prefix = new string(' ', indent);
        var result = $"{prefix}{GetType().Name}";

        if (this is CstTokenNode tokenNode)
            result += $" \"{tokenNode.Token.Text}\"";

        result += "\n";

        foreach (var child in Children)
            result += child.DumpTree(indent + 2);

        return result;
    }
}

/// <summary>
/// 単一トークンを保持する CST リーフノードの基底クラス。
/// リテラル、キーワード引数等が該当する。
/// </summary>
public abstract class CstTokenNode : CstNode
{
    /// <summary>保持するトークン。</summary>
    public Token Token { get; }

    protected CstTokenNode(Token token) => Token = token;

    public override TextSpan Span => Token.Span;

    public override TextSpan FullSpan
    {
        get
        {
            var start = Token.LeadingTrivia.Length > 0
                ? Token.LeadingTrivia[0].Span.Start
                : Token.Span.Start;
            var end = Token.TrailingTrivia.Length > 0
                ? Token.TrailingTrivia[^1].Span.End
                : Token.Span.End;
            return new TextSpan(start, end - start);
        }
    }

    public override IEnumerable<CstNode> Children => [];
    public override IEnumerable<Token> DescendantTokens() => [Token];
}
