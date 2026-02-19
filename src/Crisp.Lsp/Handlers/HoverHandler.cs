using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// 型情報ホバーハンドラー。
///
/// カーソル位置のトークンに対応するシンボルや型の情報を表示する。
///
/// <para>
/// ホバー情報の例:
/// <list type="bullet">
///   <item><description>メンバーアクセス: <c>プロパティ float EnemyAI.Health</c></description></item>
///   <item><description>メソッド呼び出し: <c>メソッド BtStatus EnemyAI.Attack(Entity target)</c></description></item>
///   <item><description>リテラル: <c>型: int, 値: 42</c></description></item>
///   <item><description>予約語: <c>キーワード: select</c></description></item>
/// </list>
/// </para>
/// </summary>
public class HoverHandler : HoverHandlerBase
{
    private readonly QueryDatabase _db;

    private static readonly TextDocumentSelector DocumentSelector =
        TextDocumentSelector.ForPattern("**/*.crisp");

    public HoverHandler(QueryDatabase db)
    {
        _db = db;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var fileId = TextDocumentSyncHandler.UriToFileId(request.TextDocument.Uri);

        try
        {
            var source = _db.GetSourceText(fileId);
            var mapper = new PositionMapper(source);
            var offset = mapper.GetOffset(request.Position.Line, request.Position.Character);

            var parseResult = _db.Parse(fileId);
            var token = PositionMapper.FindTokenAt(parseResult.Program, offset);

            if (token is null)
            {
                return Task.FromResult<Hover?>(null);
            }

            var hoverText = GetHoverText(token.Value, fileId);
            if (hoverText is null)
            {
                return Task.FromResult<Hover?>(null);
            }

            var range = mapper.GetRange(token.Value.Span);
            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(
                    new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = hoverText,
                    }),
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(range.Start.Line, range.Start.Column),
                    new Position(range.End.Line, range.End.Column)),
            });
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult<Hover?>(null);
        }
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
        };
    }

    /// <summary>
    /// トークンに対応するホバーテキストを生成する。
    /// セマンティック情報が利用可能な場合は型やシンボル情報を含める。
    /// </summary>
    private string? GetHoverText(Token token, FileId fileId)
    {
        return token.Kind switch
        {
            TokenKind.MemberAccess => GetMemberHoverText(token, fileId),
            TokenKind.Identifier => GetKeywordHoverText(token),
            TokenKind.IntLiteral => $"`int` リテラル: `{token.Text}`",
            TokenKind.FloatLiteral => $"`float` リテラル: `{token.Text}`",
            TokenKind.BoolTrue or TokenKind.BoolFalse => $"`bool` リテラル: `{token.Text}`",
            TokenKind.StringLiteral => $"`string` リテラル: `{token.Text}`",
            _ => null,
        };
    }

    /// <summary>
    /// メンバーアクセストークンのホバーテキストを生成する。
    /// Resolve 結果からシンボル情報を取得し、型・種別を表示する。
    /// </summary>
    private string? GetMemberHoverText(Token token, FileId fileId)
    {
        try
        {
            var result = _db.Resolve(fileId);
            var astTrees = _db.Lower(fileId);

            if (astTrees.Length == 0)
                return $"メンバー: `{token.Text}`";

            // AST ノードを走査してトークンのオフセットに対応するノードを探す
            var node = FindAstNodeByOffset(astTrees[0], token.Span.Start);
            if (node is not null)
            {
                var symbol = result.Model.GetSymbol(node);
                if (symbol is not null)
                {
                    return FormatSymbolHover(symbol);
                }

                if (node is AstExpr expr)
                {
                    var type = result.Model.GetType(expr);
                    if (type is not null)
                    {
                        return $"型: `{type.Name}`";
                    }
                }
            }
        }
        catch (KeyNotFoundException)
        {
            // ContextType が未設定
        }

        return $"メンバー: `{token.Text}`";
    }

    /// <summary>
    /// キーワードトークンのホバーテキストを生成する。
    /// </summary>
    private static string? GetKeywordHoverText(Token token)
    {
        return token.Text switch
        {
            "tree" => "**tree** — ビヘイビアツリーのルート定義",
            "select" => "**select** — 子ノードを順に試行し、最初の Success を返すセレクター",
            "seq" => "**seq** — 子ノードを順に実行し、全て Success なら Success を返すシーケンス",
            "parallel" => "**parallel** — 子ノードを並列実行するパラレルノード",
            "check" => "**check** — 式を評価し bool 結果で Success/Failure を返す条件チェック",
            "guard" => "**guard** — 条件が true の場合のみ子ノードを実行するガード",
            "if" => "**if** — 条件分岐。then/else ブランチを持つ",
            "invert" => "**invert** — 子ノードの結果を反転するデコレーター",
            "repeat" => "**repeat** — 子ノードを指定回数繰り返すデコレーター",
            "timeout" => "**timeout** — 指定秒数後に Failure を返すデコレーター",
            "cooldown" => "**cooldown** — 前回実行から指定秒数経過するまで Failure を返すデコレーター",
            "while" => "**while** — 条件が true の間、子ノードを繰り返すループ",
            _ => null,
        };
    }

    /// <summary>
    /// シンボルの種別に応じたホバーテキストを生成する。
    /// </summary>
    private static string FormatSymbolHover(ISymbol symbol)
    {
        return symbol switch
        {
            IPropertySymbol prop =>
                $"プロパティ `{prop.Type}` **{prop.ContainingType.Name}.{prop.Name}**",
            IFieldSymbol field =>
                $"フィールド `{field.Type}` **{field.ContainingType.Name}.{field.Name}**",
            IMethodSymbol method =>
                $"メソッド `{method.ReturnType}` **{method.ContainingType.Name}.{method.Name}**" +
                $"({string.Join(", ", method.Parameters.Select(p => $"`{p.Type}` {p.Name}"))})",
            _ => $"シンボル: `{symbol.Name}`",
        };
    }

    /// <summary>
    /// AST ツリーからオフセット位置に対応するノードを探す。
    /// CstOrigin の Span を使用して位置を照合する。
    /// </summary>
    private static AstNode? FindAstNodeByOffset(AstTree tree, int offset)
    {
        AstNode? best = null;

        void Visit(AstNode node)
        {
            if (node.CstOrigin is not null)
            {
                var span = node.CstOrigin.Span;
                if (span.Start <= offset && offset < span.End)
                {
                    best = node;
                }
            }

            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }

        Visit(tree);
        return best;
    }
}
