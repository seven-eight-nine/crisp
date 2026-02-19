using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// 定義ジャンプハンドラー。
///
/// DSL 内のメンバー参照（<c>.Patrol</c> 等）から、C# 側の定義位置へジャンプする。
///
/// <para>
/// 処理フロー:
/// <list type="number">
///   <item><description>カーソル位置の CST ノードを特定する</description></item>
///   <item><description>対応する AST ノードの ResolvedSymbol を取得する</description></item>
///   <item><description>ResolvedSymbol の Location（C# 側の定義位置）を取得する</description></item>
///   <item><description>LSP Location に変換して返す</description></item>
/// </list>
/// </para>
/// </summary>
public class DefinitionHandler : DefinitionHandlerBase
{
    private readonly QueryDatabase _db;

    private static readonly TextDocumentSelector DocumentSelector =
        TextDocumentSelector.ForPattern("**/*.crisp");

    public DefinitionHandler(QueryDatabase db)
    {
        _db = db;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var fileId = TextDocumentSyncHandler.UriToFileId(request.TextDocument.Uri);

        try
        {
            var source = _db.GetSourceText(fileId);
            var mapper = new PositionMapper(source);
            var offset = mapper.GetOffset(request.Position.Line, request.Position.Character);

            // メンバーアクセストークンでなければスキップ
            var parseResult = _db.Parse(fileId);
            var token = PositionMapper.FindTokenAt(parseResult.Program, offset);
            if (token is null || token.Value.Kind != TokenKind.MemberAccess)
            {
                return Task.FromResult(new LocationOrLocationLinks());
            }

            // セマンティック解析結果からシンボルを取得する
            var result = _db.Resolve(fileId);
            var astTrees = _db.Lower(fileId);
            if (astTrees.Length == 0)
            {
                return Task.FromResult(new LocationOrLocationLinks());
            }

            var astNode = FindAstNodeByOffset(astTrees[0], token.Value.Span.Start);
            if (astNode is null)
            {
                return Task.FromResult(new LocationOrLocationLinks());
            }

            var symbol = result.Model.GetSymbol(astNode);
            if (symbol is null)
            {
                return Task.FromResult(new LocationOrLocationLinks());
            }

            // Roslyn シンボルの定義位置を LSP Location に変換する
            var locations = symbol.Locations
                .Where(loc => loc.IsInSource)
                .Select(loc =>
                {
                    var lineSpan = loc.GetLineSpan();
                    return new LocationOrLocationLink(
                        new OmniSharp.Extensions.LanguageServer.Protocol.Models.Location
                        {
                            Uri = new Uri(lineSpan.Path),
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                new Position(lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character),
                                new Position(lineSpan.EndLinePosition.Line, lineSpan.EndLinePosition.Character)),
                        });
                })
                .ToArray();

            return Task.FromResult(new LocationOrLocationLinks(locations));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(new LocationOrLocationLinks());
        }
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
        };
    }

    /// <summary>
    /// AST ツリーからオフセット位置に対応するノードを探す。
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
