using System.Collections.Immutable;
using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.IR;
using MediatR;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Crisp.Lsp.Handlers;

// ═══════════════════════════════════════════════════════════
//  F4: Visual Editor — Tree Layout ハンドラー
// ═══════════════════════════════════════════════════════════

/// <summary>
/// <c>crisp/treeLayout</c> リクエストのパラメータ。
/// クライアントがツリーのビジュアルレイアウト情報を要求する際に送信する。
/// </summary>
[Method("crisp/treeLayout", Direction.ClientToServer)]
public sealed class TreeLayoutParams : IRequest<TreeLayoutResponse>
{
    /// <summary>対象ドキュメントの URI。</summary>
    [JsonProperty("uri")]
    public DocumentUri Uri { get; set; } = null!;
}

/// <summary>
/// <c>crisp/treeLayout</c> リクエストのレスポンス。
/// ツリーレイアウト情報を JSON シリアライズ可能な形式で返す。
/// </summary>
public sealed class TreeLayoutResponse
{
    /// <summary>ツリーレイアウトの配列。1ファイルに複数ツリーがある場合に対応。</summary>
    [JsonProperty("trees")]
    public List<TreeLayoutJson> Trees { get; set; } = new();
}

/// <summary>
/// 個々のツリーの JSON 表現。
/// <see cref="TreeLayout"/> を JSON シリアライズ用に変換したもの。
/// </summary>
public sealed class TreeLayoutJson
{
    /// <summary>ツリー名。</summary>
    [JsonProperty("tree")]
    public string Tree { get; set; } = "";

    /// <summary>ルートノード。</summary>
    [JsonProperty("root")]
    public TreeLayoutNodeJson? Root { get; set; }

    /// <summary>診断情報。</summary>
    [JsonProperty("diagnostics")]
    public List<TreeLayoutDiagnosticJson> Diagnostics { get; set; } = new();
}

/// <summary>
/// ツリーノードの JSON 表現。
/// </summary>
public sealed class TreeLayoutNodeJson
{
    /// <summary>ノード Id（IrNode.Id / CstNode.Id と一致）。</summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>ノード種別（"selector", "sequence", "action" 等）。</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    /// <summary>表示用ラベル。</summary>
    [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
    public string? Label { get; set; }

    /// <summary>ソース上の位置情報。</summary>
    [JsonProperty("origin")]
    public TreeLayoutOriginJson? Origin { get; set; }

    /// <summary>型推論結果。</summary>
    [JsonProperty("resolvedType", NullValueHandling = NullValueHandling.Ignore)]
    public string? ResolvedType { get; set; }

    /// <summary>子ノード。</summary>
    [JsonProperty("children")]
    public List<TreeLayoutNodeJson> Children { get; set; } = new();
}

/// <summary>
/// ソース位置情報の JSON 表現。
/// </summary>
public sealed class TreeLayoutOriginJson
{
    /// <summary>開始オフセット。</summary>
    [JsonProperty("start")]
    public int Start { get; set; }

    /// <summary>長さ。</summary>
    [JsonProperty("length")]
    public int Length { get; set; }
}

/// <summary>
/// 診断情報の JSON 表現。
/// </summary>
public sealed class TreeLayoutDiagnosticJson
{
    /// <summary>対象ノード Id。</summary>
    [JsonProperty("nodeId")]
    public int NodeId { get; set; }

    /// <summary>深刻度。</summary>
    [JsonProperty("severity")]
    public string Severity { get; set; } = "";

    /// <summary>メッセージ。</summary>
    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// <c>crisp/treeLayout</c> リクエストハンドラー（F4: Visual Editor）。
///
/// クライアントからのリクエストに応じて、指定ドキュメントの IR ツリーを
/// <see cref="TreeLayoutBuilder"/> でレイアウト情報に変換して返す。
///
/// <para>
/// 処理フロー:
/// <list type="number">
///   <item><description>URI → FileId に変換</description></item>
///   <item><description>QueryDatabase から IR ツリーを取得（EmitIR クエリ）</description></item>
///   <item><description>TreeLayoutBuilder でレイアウト情報に変換</description></item>
///   <item><description>診断情報を付加してレスポンスを返す</description></item>
/// </list>
/// </para>
/// </summary>
public class TreeLayoutHandler : IJsonRpcRequestHandler<TreeLayoutParams, TreeLayoutResponse>
{
    private readonly QueryDatabase _db;

    public TreeLayoutHandler(QueryDatabase db)
    {
        _db = db;
    }

    public Task<TreeLayoutResponse> Handle(TreeLayoutParams request, CancellationToken cancellationToken)
    {
        var response = new TreeLayoutResponse();
        var fileId = TextDocumentSyncHandler.UriToFileId(request.Uri);

        try
        {
            // IR ツリーを取得する
            var irTrees = _db.EmitIR(fileId);

            // 診断情報を取得する
            var diagnostics = _db.AllDiagnostics(fileId);
            var layoutDiags = diagnostics.Select(d => new TreeLayoutDiagnostic(
                -1,
                d.Severity == Syntax.DiagnosticSeverity.Error ? "error" :
                d.Severity == Syntax.DiagnosticSeverity.Warning ? "warning" : "info",
                d.Message
            )).ToImmutableArray();

            // 各 IR ツリーをレイアウト情報に変換する
            foreach (var irTree in irTrees)
            {
                var layout = TreeLayoutBuilder.Build(irTree, layoutDiags);
                response.Trees.Add(ConvertToJson(layout));
            }
        }
        catch (Exception)
        {
            // ファイル未登録やパースエラー等 — 空のレスポンスを返す
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// <see cref="TreeLayout"/> を JSON シリアライズ用の型に変換する。
    /// </summary>
    internal static TreeLayoutJson ConvertToJson(TreeLayout layout)
    {
        return new TreeLayoutJson
        {
            Tree = layout.TreeName,
            Root = ConvertNodeToJson(layout.Root),
            Diagnostics = layout.Diagnostics.Select(d => new TreeLayoutDiagnosticJson
            {
                NodeId = d.NodeId,
                Severity = d.Severity,
                Message = d.Message,
            }).ToList(),
        };
    }

    /// <summary>
    /// <see cref="TreeLayoutNode"/> を JSON シリアライズ用の型に再帰変換する。
    /// </summary>
    internal static TreeLayoutNodeJson ConvertNodeToJson(TreeLayoutNode node)
    {
        return new TreeLayoutNodeJson
        {
            Id = node.Id,
            Type = node.NodeType,
            Label = node.Label,
            Origin = new TreeLayoutOriginJson
            {
                Start = node.Origin.Start,
                Length = node.Origin.Length,
            },
            ResolvedType = node.ResolvedType,
            Children = node.Children.Select(ConvertNodeToJson).ToList(),
        };
    }
}
