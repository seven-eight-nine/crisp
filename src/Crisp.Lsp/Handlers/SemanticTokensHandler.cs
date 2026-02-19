using Crisp.Query;
using Crisp.Syntax;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// セマンティックトークンハンドラー。
///
/// <c>textDocument/semanticTokens/full</c> リクエストに応答し、
/// ソーステキストのトークンにセマンティック分類（キーワード、プロパティ、
/// 数値リテラル等）を付与する。
///
/// <para>
/// トークン種別とセマンティックトークンタイプの対応:
/// <list type="table">
///   <item><term>予約語（select, seq 等）</term><description>keyword</description></item>
///   <item><term>メンバーアクセス（.Foo）</term><description>property</description></item>
///   <item><term>IntLiteral / FloatLiteral</term><description>number</description></item>
///   <item><term>BoolLiteral</term><description>keyword</description></item>
///   <item><term>StringLiteral</term><description>string</description></item>
///   <item><term>演算子</term><description>operator</description></item>
///   <item><term>コメント</term><description>comment</description></item>
///   <item><term>キーワード引数（:all）</term><description>parameter</description></item>
/// </list>
/// </para>
/// </summary>
public class SemanticTokensHandler : SemanticTokensFullHandlerBase
{
    private readonly QueryDatabase _db;

    private static readonly TextDocumentSelector DocumentSelector =
        TextDocumentSelector.ForPattern("**/*.crisp");

    /// <summary>サポートするセマンティックトークンタイプのリスト。</summary>
    internal static readonly string[] TokenTypes =
    [
        "keyword",      // 0
        "property",     // 1
        "number",       // 2
        "string",       // 3
        "operator",     // 4
        "comment",      // 5
        "parameter",    // 6
        "method",       // 7
        "variable",     // 8
    ];

    public SemanticTokensHandler(QueryDatabase db)
    {
        _db = db;
    }

    public override Task<SemanticTokens?> Handle(SemanticTokensParams request, CancellationToken ct)
    {
        var fileId = TextDocumentSyncHandler.UriToFileId(request.TextDocument.Uri);

        try
        {
            var source = _db.GetSourceText(fileId);
            var tokens = _db.Lex(fileId);
            var mapper = new PositionMapper(source);

            var data = new List<int>();
            var prevLine = 0;
            var prevCol = 0;

            foreach (var token in tokens)
            {
                if (token.Kind == TokenKind.EndOfFile)
                    break;

                // コメント Trivia をセマンティックトークンとして出力する
                foreach (var trivia in token.LeadingTrivia)
                {
                    if (trivia.Kind == TriviaKind.Comment)
                    {
                        EncodeToken(data, mapper, trivia.Span, 5, ref prevLine, ref prevCol);
                    }
                }

                var tokenType = ClassifyToken(token);
                if (tokenType >= 0)
                {
                    EncodeToken(data, mapper, token.Span, tokenType, ref prevLine, ref prevCol);
                }
            }

            return Task.FromResult<SemanticTokens?>(new SemanticTokens
            {
                Data = new System.Collections.Immutable.ImmutableArray<int>().AddRange(data),
            });
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult<SemanticTokens?>(null);
        }
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>(),
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false,
        };
    }

    /// <summary>
    /// トークン種別からセマンティックトークンタイプのインデックスを返す。
    /// 分類対象外の場合は -1 を返す。
    /// </summary>
    internal static int ClassifyToken(Token token)
    {
        return token.Kind switch
        {
            // 予約語（ノード位置の識別子で予約語名を持つもの）
            TokenKind.Identifier when IsKeyword(token.Text) => 0,  // keyword

            // メンバーアクセス
            TokenKind.MemberAccess => 1,  // property

            // 数値リテラル
            TokenKind.IntLiteral => 2,    // number
            TokenKind.FloatLiteral => 2,  // number

            // ブールリテラル
            TokenKind.BoolTrue => 0,      // keyword
            TokenKind.BoolFalse => 0,     // keyword

            // null リテラル
            TokenKind.NullLiteral => 0,   // keyword

            // 文字列リテラル
            TokenKind.StringLiteral => 3, // string

            // 演算子
            TokenKind.Plus => 4,          // operator
            TokenKind.Minus => 4,         // operator
            TokenKind.Star => 4,          // operator
            TokenKind.Slash => 4,         // operator
            TokenKind.Percent => 4,       // operator
            TokenKind.LessThan => 4,      // operator
            TokenKind.GreaterThan => 4,   // operator
            TokenKind.LessEqual => 4,     // operator
            TokenKind.GreaterEqual => 4,  // operator
            TokenKind.Equal => 4,         // operator
            TokenKind.NotEqual => 4,      // operator

            // キーワード引数（:all, :any, :n）
            TokenKind.Keyword => 6,       // parameter

            // enum リテラル
            TokenKind.EnumLiteral => 8,   // variable

            _ => -1,
        };
    }

    /// <summary>
    /// 識別子が Crisp の予約語かどうかを判定する。
    /// </summary>
    private static bool IsKeyword(string text)
    {
        return text is "tree" or "select" or "seq" or "parallel"
            or "check" or "guard" or "if" or "invert"
            or "repeat" or "timeout" or "cooldown" or "while"
            or "else";
    }

    /// <summary>
    /// LSP セマンティックトークンの差分エンコーディングでデータを追加する。
    /// </summary>
    /// <param name="data">エンコード先のリスト。</param>
    /// <param name="mapper">位置変換用のマッパー。</param>
    /// <param name="span">トークンの TextSpan。</param>
    /// <param name="tokenType">セマンティックトークンタイプのインデックス。</param>
    /// <param name="prevLine">前回のトークンの行番号（更新される）。</param>
    /// <param name="prevCol">前回のトークンの列番号（更新される）。</param>
    private static void EncodeToken(
        List<int> data,
        PositionMapper mapper,
        TextSpan span,
        int tokenType,
        ref int prevLine,
        ref int prevCol)
    {
        var (line, col) = mapper.GetLineColumn(span.Start);
        var deltaLine = line - prevLine;
        var deltaCol = deltaLine == 0 ? col - prevCol : col;

        data.Add(deltaLine);
        data.Add(deltaCol);
        data.Add(span.Length);
        data.Add(tokenType);
        data.Add(0); // modifiers (なし)

        prevLine = line;
        prevCol = col;
    }
}
