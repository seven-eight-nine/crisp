using System.Collections.Immutable;
using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.IR;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Diagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using DiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// テキストドキュメントの同期ハンドラー。
///
/// ファイルのオープン・変更・クローズイベントを処理し、
/// <see cref="QueryDatabase"/> の入力を更新する。
/// 変更のたびに以下をクライアントに送信する:
/// <list type="bullet">
///   <item><description>診断情報（<c>textDocument/publishDiagnostics</c>）</description></item>
///   <item><description>ツリーレイアウト更新（<c>crisp/treeLayoutChanged</c>）— F4: Visual Editor</description></item>
/// </list>
/// </summary>
public class TextDocumentSyncHandler :
    TextDocumentSyncHandlerBase
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    /// <summary>ハンドラーの対象とするドキュメントのフィルタ。</summary>
    private static readonly TextDocumentSelector DocumentSelector = TextDocumentSelector.ForPattern("**/*.crisp");

    public TextDocumentSyncHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    /// <summary>
    /// ドキュメントの属性（言語ID、スキーム）を返す。
    /// OmniSharp のドキュメントルーティングに使用される。
    /// </summary>
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "crisp");
    }

    /// <summary>
    /// ファイルオープン時の処理。
    /// ソーステキストを QueryDatabase に登録し、初回の診断を実行する。
    /// </summary>
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var fileId = UriToFileId(uri);
        var text = request.TextDocument.Text;

        _db.SetSourceText(fileId, text);
        PublishDiagnostics(uri, fileId);
        PublishTreeLayout(uri, fileId);

        return Unit.Task;
    }

    /// <summary>
    /// ファイル変更時の処理。
    /// ソーステキストを更新し、診断を再計算する。
    /// Full sync モードなので、変更内容はドキュメント全体。
    /// </summary>
    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var fileId = UriToFileId(uri);
        var text = request.ContentChanges.First().Text;

        _db.SetSourceText(fileId, text);
        PublishDiagnostics(uri, fileId);
        PublishTreeLayout(uri, fileId);

        return Unit.Task;
    }

    /// <summary>
    /// ファイルクローズ時の処理。
    /// QueryDatabase からファイルデータを削除し、診断をクリアする。
    /// </summary>
    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri;
        var fileId = UriToFileId(uri);

        _db.RemoveFile(fileId);

        // 診断をクリアする（空の配列を送信）
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(),
        });

        return Unit.Task;
    }

    /// <summary>
    /// ファイル保存時の処理。特に追加の処理は行わない。
    /// </summary>
    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false },
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  内部: 診断送信
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 指定ファイルの診断情報を再計算し、クライアントに送信する。
    /// Parse（構文）と TypeCheck（意味）の両方の診断を含む。
    /// </summary>
    private void PublishDiagnostics(DocumentUri uri, FileId fileId)
    {
        var parseResult = _db.Parse(fileId);
        var crispDiags = parseResult.Diagnostics.Diagnostics;

        // セマンティック診断はコンテキスト型が設定されている場合のみ
        var semanticDiags = System.Collections.Immutable.ImmutableArray<Syntax.Diagnostic>.Empty;
        try
        {
            semanticDiags = _db.TypeCheck(fileId);
        }
        catch (KeyNotFoundException)
        {
            // ContextType が未設定の場合はスキップ
        }

        var source = _db.GetSourceText(fileId);
        var mapper = new PositionMapper(source);

        var lspDiags = new List<Diagnostic>();

        foreach (var diag in crispDiags)
        {
            lspDiags.Add(ConvertDiagnostic(diag, mapper));
        }

        foreach (var diag in semanticDiags)
        {
            lspDiags.Add(ConvertDiagnostic(diag, mapper));
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(lspDiags),
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  内部: ツリーレイアウト通知（F4: Visual Editor）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// テキスト変更後にツリーレイアウト情報を再計算し、
    /// <c>crisp/treeLayoutChanged</c> 通知でクライアント（WebView）に送信する。
    ///
    /// この通知により、テキストエディタでの変更がビジュアルエディタに即座に反映される。
    /// パースエラーや IR 生成失敗時は空のレスポンスを送信する。
    /// </summary>
    /// <param name="uri">対象ドキュメントの URI。</param>
    /// <param name="fileId">対象ファイルの FileId。</param>
    private void PublishTreeLayout(DocumentUri uri, FileId fileId)
    {
        var response = new TreeLayoutResponse();

        try
        {
            var irTrees = _db.EmitIR(fileId);
            var diagnostics = _db.AllDiagnostics(fileId);
            var layoutDiags = diagnostics.Select(d => new TreeLayoutDiagnostic(
                -1,
                d.Severity == Syntax.DiagnosticSeverity.Error ? "error" :
                d.Severity == Syntax.DiagnosticSeverity.Warning ? "warning" : "info",
                d.Message
            )).ToImmutableArray();

            foreach (var irTree in irTrees)
            {
                var layout = TreeLayoutBuilder.Build(irTree, layoutDiags);
                response.Trees.Add(TreeLayoutHandler.ConvertToJson(layout));
            }
        }
        catch (Exception)
        {
            // パースエラーや IR 生成失敗時は空のレスポンスを送信する
        }

        _server.SendNotification("crisp/treeLayoutChanged", new
        {
            uri = uri.ToString(),
            trees = response.Trees,
        });
    }

    /// <summary>
    /// Crisp 診断を LSP 診断に変換する。
    /// TextSpan → LSP Range への変換に PositionMapper を使用する。
    /// </summary>
    internal static Diagnostic ConvertDiagnostic(Syntax.Diagnostic crispDiag, PositionMapper mapper)
    {
        var range = mapper.GetRange(crispDiag.Span);
        return new Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(range.Start.Line, range.Start.Column),
                new Position(range.End.Line, range.End.Column)),
            Severity = crispDiag.Severity switch
            {
                Syntax.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                Syntax.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Information,
            },
            Code = crispDiag.Id,
            Source = "crisp",
            Message = crispDiag.Message,
        };
    }

    /// <summary>
    /// ドキュメント URI を FileId に変換する。
    /// </summary>
    internal static FileId UriToFileId(DocumentUri uri)
    {
        return FileId.ForFile(uri.GetFileSystemPath());
    }
}
