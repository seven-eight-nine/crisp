using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.Formatting;
using Crisp.Syntax.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// ドキュメントフォーマットハンドラー。
///
/// <c>textDocument/formatting</c> リクエストに応答し、
/// ソーステキスト全体を Crisp フォーマッターで整形する。
///
/// <para>
/// LSP の FormattingOptions（tabSize, insertSpaces 等）を
/// Crisp の <see cref="FormattingOptions"/> に変換して使用する。
/// </para>
/// </summary>
public class FormattingHandler : DocumentFormattingHandlerBase
{
    private readonly QueryDatabase _db;

    private static readonly TextDocumentSelector DocumentSelector =
        TextDocumentSelector.ForPattern("**/*.crisp");

    public FormattingHandler(QueryDatabase db)
    {
        _db = db;
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken ct)
    {
        var fileId = TextDocumentSyncHandler.UriToFileId(request.TextDocument.Uri);

        try
        {
            var source = _db.GetSourceText(fileId);

            // LSP のフォーマットオプションを Crisp のオプションに変換する
            var options = new Crisp.Syntax.Formatting.FormattingOptions(
                IndentSize: (int)request.Options.TabSize);

            var formatted = CompilationPipeline.Format(source, options);

            // ソース全体を置換する TextEdit を生成する
            var mapper = new PositionMapper(source);
            var lastLine = mapper.LineCount - 1;
            var lastLineEnd = source.Length;
            var endPos = mapper.GetLineColumn(lastLineEnd);

            var edits = new List<TextEdit>
            {
                new TextEdit
                {
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        new Position(0, 0),
                        new Position(endPos.Line, endPos.Column)),
                    NewText = formatted,
                },
            };

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult<TextEditContainer?>(null);
        }
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
        };
    }
}
