using System.Collections.Immutable;
using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Formatting;
using MediatR;
using Newtonsoft.Json;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Crisp.Lsp.Handlers;

// ═══════════════════════════════════════════════════════════
//  F4: Visual Editor — Tree Edit ハンドラー群
//
//  ビジュアルエディタからの操作リクエストを処理し、
//  ソーステキストの編集を workspace/applyEdit 経由で反映する。
//
//  各ハンドラーの処理フロー:
//    1. nodeId → CstNode.Id で対象ノードを CST 上に特定
//    2. CST の TextSpan からソース上の位置を決定
//    3. 編集を計算（挿入・削除・置換）
//    4. workspace/applyEdit でテキストエディタに反映
// ═══════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────
//  共通レスポンス
// ─────────────────────────────────────────────────────────

/// <summary>
/// ツリー編集操作の共通レスポンス。
/// 操作の成否と、失敗時のエラーメッセージを返す。
/// </summary>
public sealed class TreeEditResponse
{
    /// <summary>操作が成功したかどうか。</summary>
    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>失敗時のエラーメッセージ。成功時は null。</summary>
    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public string? Error { get; set; }
}

// ─────────────────────────────────────────────────────────
//  crisp/addNode
// ─────────────────────────────────────────────────────────

/// <summary>
/// <c>crisp/addNode</c> リクエストのパラメータ。
/// ビジュアルエディタからノード追加操作を受け取る。
/// </summary>
[Method("crisp/addNode", Direction.ClientToServer)]
public sealed class AddNodeParams : IRequest<TreeEditResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;

    /// <summary>追加先の親ノード Id。</summary>
    [JsonProperty("parentId")]
    public int ParentId { get; set; }

    /// <summary>兄弟内での挿入位置（0-indexed）。</summary>
    [JsonProperty("position")]
    public int Position { get; set; }

    /// <summary>追加するノードの種別（"action", "selector", "sequence" 等）。</summary>
    [JsonProperty("nodeType")]
    public string NodeType { get; set; } = "";
}

// ─────────────────────────────────────────────────────────
//  crisp/removeNode
// ─────────────────────────────────────────────────────────

/// <summary>
/// <c>crisp/removeNode</c> リクエストのパラメータ。
/// ビジュアルエディタからノード削除操作を受け取る。
/// </summary>
[Method("crisp/removeNode", Direction.ClientToServer)]
public sealed class RemoveNodeParams : IRequest<TreeEditResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;

    /// <summary>削除対象のノード Id。</summary>
    [JsonProperty("nodeId")]
    public int NodeId { get; set; }
}

// ─────────────────────────────────────────────────────────
//  crisp/moveNode
// ─────────────────────────────────────────────────────────

/// <summary>
/// <c>crisp/moveNode</c> リクエストのパラメータ。
/// ビジュアルエディタからノード移動操作を受け取る。
/// </summary>
[Method("crisp/moveNode", Direction.ClientToServer)]
public sealed class MoveNodeParams : IRequest<TreeEditResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;

    /// <summary>移動対象のノード Id。</summary>
    [JsonProperty("nodeId")]
    public int NodeId { get; set; }

    /// <summary>移動先の親ノード Id。</summary>
    [JsonProperty("newParentId")]
    public int NewParentId { get; set; }

    /// <summary>移動先の兄弟内での位置（0-indexed）。</summary>
    [JsonProperty("newPosition")]
    public int NewPosition { get; set; }
}

// ─────────────────────────────────────────────────────────
//  crisp/wrapNode
// ─────────────────────────────────────────────────────────

/// <summary>
/// <c>crisp/wrapNode</c> リクエストのパラメータ。
/// ビジュアルエディタからノードをデコレータで包む操作を受け取る。
/// </summary>
[Method("crisp/wrapNode", Direction.ClientToServer)]
public sealed class WrapNodeParams : IRequest<TreeEditResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;

    /// <summary>包む対象のノード Id。</summary>
    [JsonProperty("nodeId")]
    public int NodeId { get; set; }

    /// <summary>デコレータの種別（"invert", "repeat", "guard" 等）。</summary>
    [JsonProperty("decoratorType")]
    public string DecoratorType { get; set; } = "";
}

// ─────────────────────────────────────────────────────────
//  crisp/unwrapNode
// ─────────────────────────────────────────────────────────

/// <summary>
/// <c>crisp/unwrapNode</c> リクエストのパラメータ。
/// ビジュアルエディタからデコレータ解除操作を受け取る。
/// </summary>
[Method("crisp/unwrapNode", Direction.ClientToServer)]
public sealed class UnwrapNodeParams : IRequest<TreeEditResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;

    /// <summary>デコレータ解除対象のノード Id。</summary>
    [JsonProperty("nodeId")]
    public int NodeId { get; set; }
}

// ═══════════════════════════════════════════════════════════
//  ハンドラー実装
// ═══════════════════════════════════════════════════════════

/// <summary>
/// <c>crisp/addNode</c> ハンドラー（F4: Visual Editor）。
///
/// 指定された親ノードの子として新しいノードを追加する。
/// CST 上の親ノードの位置を特定し、適切な位置にS式を挿入した後、
/// フォーマッタで整形して workspace/applyEdit で反映する。
/// </summary>
public class AddNodeHandler : IJsonRpcRequestHandler<AddNodeParams, TreeEditResponse>
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    public AddNodeHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    public async Task<TreeEditResponse> Handle(AddNodeParams request, CancellationToken ct)
    {
        return await TreeEditHelper.ExecuteEdit(_db, _server, request.Uri, source =>
        {
            var (program, _) = Parser.Parse(source);
            var parent = TreeEditHelper.FindNodeById(program, request.ParentId);
            if (parent == null)
                return TreeEditHelper.ErrorResult($"Parent node with Id {request.ParentId} not found");

            var snippet = TreeEditHelper.GenerateNodeSnippet(request.NodeType);
            var insertOffset = TreeEditHelper.ComputeInsertOffset(parent, request.Position);

            var newSource = source.Insert(insertOffset, " " + snippet);
            return TreeEditHelper.SuccessResult(newSource);
        }, ct);
    }
}

/// <summary>
/// <c>crisp/removeNode</c> ハンドラー（F4: Visual Editor）。
///
/// 指定されたノードをソーステキストから削除する。
/// CST 上のノード位置（FullSpan）を使用して、前後の空白も含めて適切に除去する。
/// </summary>
public class RemoveNodeHandler : IJsonRpcRequestHandler<RemoveNodeParams, TreeEditResponse>
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    public RemoveNodeHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    public async Task<TreeEditResponse> Handle(RemoveNodeParams request, CancellationToken ct)
    {
        return await TreeEditHelper.ExecuteEdit(_db, _server, request.Uri, source =>
        {
            var (program, _) = Parser.Parse(source);
            var node = TreeEditHelper.FindNodeById(program, request.NodeId);
            if (node == null)
                return TreeEditHelper.ErrorResult($"Node with Id {request.NodeId} not found");

            var span = node.FullSpan;
            var newSource = source.Remove(span.Start, span.Length);
            return TreeEditHelper.SuccessResult(newSource);
        }, ct);
    }
}

/// <summary>
/// <c>crisp/moveNode</c> ハンドラー（F4: Visual Editor）。
///
/// 指定されたノードを現在位置から削除し、新しい親ノードの指定位置に挿入する。
/// 削除後のオフセット調整を正しく行い、フォーマッタで整形する。
/// </summary>
public class MoveNodeHandler : IJsonRpcRequestHandler<MoveNodeParams, TreeEditResponse>
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    public MoveNodeHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    public async Task<TreeEditResponse> Handle(MoveNodeParams request, CancellationToken ct)
    {
        return await TreeEditHelper.ExecuteEdit(_db, _server, request.Uri, source =>
        {
            var (program, _) = Parser.Parse(source);
            var node = TreeEditHelper.FindNodeById(program, request.NodeId);
            if (node == null)
                return TreeEditHelper.ErrorResult($"Node with Id {request.NodeId} not found");

            var newParent = TreeEditHelper.FindNodeById(program, request.NewParentId);
            if (newParent == null)
                return TreeEditHelper.ErrorResult($"New parent node with Id {request.NewParentId} not found");

            // ノードのテキストを取得してから削除
            var nodeSpan = node.FullSpan;
            var nodeText = source.Substring(nodeSpan.Start, nodeSpan.Length).Trim();

            // 削除
            var afterRemove = source.Remove(nodeSpan.Start, nodeSpan.Length);

            // 削除によるオフセット調整を考慮して挿入位置を再計算
            var (reparsed, _) = Parser.Parse(afterRemove);
            var targetParent = TreeEditHelper.FindNodeById(reparsed, request.NewParentId);
            if (targetParent == null)
                return TreeEditHelper.ErrorResult("New parent not found after removal");

            var insertOffset = TreeEditHelper.ComputeInsertOffset(targetParent, request.NewPosition);
            var newSource = afterRemove.Insert(insertOffset, " " + nodeText);

            return TreeEditHelper.SuccessResult(newSource);
        }, ct);
    }
}

/// <summary>
/// <c>crisp/wrapNode</c> ハンドラー（F4: Visual Editor）。
///
/// 指定されたノードをデコレータで包む。
/// 例: <c>(.Attack)</c> → <c>(invert (.Attack))</c>
/// CST 上のノード位置を使用してソーステキストを編集する。
/// </summary>
public class WrapNodeHandler : IJsonRpcRequestHandler<WrapNodeParams, TreeEditResponse>
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    public WrapNodeHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    public async Task<TreeEditResponse> Handle(WrapNodeParams request, CancellationToken ct)
    {
        return await TreeEditHelper.ExecuteEdit(_db, _server, request.Uri, source =>
        {
            var (program, _) = Parser.Parse(source);
            var node = TreeEditHelper.FindNodeById(program, request.NodeId);
            if (node == null)
                return TreeEditHelper.ErrorResult($"Node with Id {request.NodeId} not found");

            var span = node.Span;
            var nodeText = source.Substring(span.Start, span.Length);

            // デコレータで包む: (decoratorType nodeText)
            var wrapped = $"({request.DecoratorType} {nodeText})";
            var newSource = source.Remove(span.Start, span.Length).Insert(span.Start, wrapped);

            return TreeEditHelper.SuccessResult(newSource);
        }, ct);
    }
}

/// <summary>
/// <c>crisp/unwrapNode</c> ハンドラー（F4: Visual Editor）。
///
/// 指定されたデコレータノードからデコレータを除去し、内部の子ノードを露出させる。
/// 例: <c>(invert (.Attack))</c> → <c>(.Attack)</c>
/// デコレータノードの最後の子要素を子として取り出す。
/// </summary>
public class UnwrapNodeHandler : IJsonRpcRequestHandler<UnwrapNodeParams, TreeEditResponse>
{
    private readonly QueryDatabase _db;
    private readonly ILanguageServerFacade _server;

    public UnwrapNodeHandler(QueryDatabase db, ILanguageServerFacade server)
    {
        _db = db;
        _server = server;
    }

    public async Task<TreeEditResponse> Handle(UnwrapNodeParams request, CancellationToken ct)
    {
        return await TreeEditHelper.ExecuteEdit(_db, _server, request.Uri, source =>
        {
            var (program, _) = Parser.Parse(source);
            var node = TreeEditHelper.FindNodeById(program, request.NodeId);
            if (node == null)
                return TreeEditHelper.ErrorResult($"Node with Id {request.NodeId} not found");

            // デコレータの子ノードを取得する
            var innerChild = TreeEditHelper.GetInnerChild(node);
            if (innerChild == null)
                return TreeEditHelper.ErrorResult($"Node with Id {request.NodeId} has no inner child to unwrap");

            var outerSpan = node.Span;
            var innerSpan = innerChild.Span;
            var innerText = source.Substring(innerSpan.Start, innerSpan.Length);

            // 外側のデコレータを内部の子に置換
            var newSource = source.Remove(outerSpan.Start, outerSpan.Length)
                                  .Insert(outerSpan.Start, innerText);

            return TreeEditHelper.SuccessResult(newSource);
        }, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  ヘルパー
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー編集操作の共通ヘルパーメソッド群（F4: Visual Editor）。
///
/// CST ノード検索、ソーステキスト編集、workspace/applyEdit 送信など
/// 全編集ハンドラーが共有するユーティリティを提供する。
/// </summary>
internal static class TreeEditHelper
{
    /// <summary>
    /// 編集操作を実行する共通パターン。
    /// ソーステキストの取得、編集コールバックの実行、フォーマット適用、
    /// workspace/applyEdit の送信までを一括で処理する。
    /// </summary>
    /// <param name="db">QueryDatabase。</param>
    /// <param name="server">LSP サーバーファサード。</param>
    /// <param name="uri">対象ドキュメントの URI。</param>
    /// <param name="editFn">
    /// ソーステキストを受け取り、編集結果を返すコールバック。
    /// 成功時は <see cref="SuccessResult"/> で新しいソースを、
    /// 失敗時は <see cref="ErrorResult"/> でエラーメッセージを返す。
    /// </param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>編集結果。</returns>
    internal static async Task<TreeEditResponse> ExecuteEdit(
        QueryDatabase db,
        ILanguageServerFacade server,
        DocumentUri uri,
        Func<string, (bool success, string resultOrError)> editFn,
        CancellationToken ct)
    {
        try
        {
            var fileId = TextDocumentSyncHandler.UriToFileId(uri);
            var source = db.GetSourceText(fileId);

            var (success, resultOrError) = editFn(source);
            if (!success)
                return new TreeEditResponse { Success = false, Error = resultOrError };

            var newSource = resultOrError;

            // フォーマッタで整形
            var formatted = Formatter.FormatSource(newSource);

            // workspace/applyEdit でテキストエディタに反映
            var mapper = new PositionMapper(source);
            var endPos = mapper.GetLineColumn(source.Length);

            var edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [uri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                new Position(0, 0),
                                new Position(endPos.Line, endPos.Column)),
                            NewText = formatted,
                        }
                    }
                }
            };

            // LSP workspace/applyEdit リクエストをクライアントに送信する。
            // OmniSharp の IResponseRouter.SendRequest で JSON-RPC リクエストを発行する。
            await server.SendRequest(new ApplyWorkspaceEditParams
            {
                Label = "Crisp Tree Edit",
                Edit = edit,
            }, ct);

            return new TreeEditResponse { Success = true };
        }
        catch (KeyNotFoundException)
        {
            return new TreeEditResponse { Success = false, Error = "File not registered in QueryDatabase" };
        }
        catch (Exception ex)
        {
            return new TreeEditResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>成功結果を生成する。</summary>
    internal static (bool success, string resultOrError) SuccessResult(string newSource)
        => (true, newSource);

    /// <summary>エラー結果を生成する。</summary>
    internal static (bool success, string resultOrError) ErrorResult(string error)
        => (false, error);

    /// <summary>
    /// CST ツリー内のノードを Id で検索する。
    /// 深さ優先で走査し、最初に一致するノードを返す。
    /// </summary>
    /// <param name="root">検索開始ノード。</param>
    /// <param name="nodeId">検索対象の Id。</param>
    /// <returns>一致するノード。見つからない場合は null。</returns>
    internal static CstNode? FindNodeById(CstNode root, int nodeId)
    {
        if (root.Id == nodeId)
            return root;

        foreach (var child in root.Children)
        {
            var found = FindNodeById(child, nodeId);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// ノード種別に応じた最小限のS式スニペットを生成する。
    /// ビジュアルエディタからの addNode 操作で使用する。
    /// </summary>
    /// <param name="nodeType">ノード種別名。</param>
    /// <returns>挿入用のS式テキスト。</returns>
    internal static string GenerateNodeSnippet(string nodeType)
    {
        return nodeType switch
        {
            "action" => "(.TODO)",
            "check" => "(check .TODO)",
            "selector" or "select" => "(select)",
            "sequence" or "seq" => "(seq)",
            "guard" => "(guard .TODO (.TODO))",
            "invert" => "(invert (.TODO))",
            "repeat" => "(repeat 1 (.TODO))",
            "timeout" => "(timeout 10.0 (.TODO))",
            "cooldown" => "(cooldown 1.0 (.TODO))",
            "while" => "(while .TODO (.TODO))",
            "parallel" => "(parallel :all (.TODO))",
            "reactive" => "(reactive .TODO (.TODO))",
            "reactive-select" => "(reactive-select (.TODO) (.TODO))",
            "if" => "(if .TODO (.TODO) (.TODO))",
            _ => $"(.{nodeType})",
        };
    }

    /// <summary>
    /// 親ノード内の指定位置にノードを挿入するためのオフセットを計算する。
    /// 子ノードのリストの position 番目の直前に挿入する。
    /// position が子数以上の場合は末尾（閉じ括弧の直前）に挿入する。
    /// </summary>
    /// <param name="parent">親ノード。</param>
    /// <param name="position">挿入位置（0-indexed）。</param>
    /// <returns>ソーステキスト上の挿入オフセット。</returns>
    internal static int ComputeInsertOffset(CstNode parent, int position)
    {
        var children = parent.Children.ToList();
        if (position < children.Count)
        {
            // 指定位置の子ノードの直前に挿入
            return children[position].FullSpan.Start;
        }

        // 末尾に挿入: 親の Span の End（閉じ括弧）の直前
        var parentSpan = parent.Span;
        return parentSpan.Start + parentSpan.Length - 1;
    }

    /// <summary>
    /// デコレータノードの内部子ノード（最後の子要素）を取得する。
    /// unwrapNode 操作で、デコレータを除去して内部のノードを露出させる際に使用する。
    /// </summary>
    /// <param name="node">デコレータノード。</param>
    /// <returns>内部子ノード。見つからない場合は null。</returns>
    internal static CstNode? GetInnerChild(CstNode node)
    {
        // デコレータノード（invert, repeat, guard 等）の最後の CstNode 子を返す
        // Token ノードはスキップする
        CstNode? lastChild = null;
        foreach (var child in node.Children)
        {
            lastChild = child;
        }
        return lastChild;
    }
}
