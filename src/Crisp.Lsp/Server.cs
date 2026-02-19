using Crisp.Lsp.Handlers;
using Crisp.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Crisp.Lsp;

/// <summary>
/// Crisp DSL 用の Language Server Protocol サーバー。
///
/// 標準入出力経由で LSP クライアント（VS Code 等）と通信し、
/// 以下の IDE 機能を提供する:
/// <list type="bullet">
///   <item><description>エラー診断（<c>textDocument/publishDiagnostics</c>）</description></item>
///   <item><description>メンバー補完（<c>textDocument/completion</c>）</description></item>
///   <item><description>型情報ホバー（<c>textDocument/hover</c>）</description></item>
///   <item><description>定義ジャンプ（<c>textDocument/definition</c>）</description></item>
///   <item><description>フォーマット（<c>textDocument/formatting</c>）</description></item>
///   <item><description>セマンティックハイライト（<c>textDocument/semanticTokens</c>）</description></item>
///   <item><description>ツリーレイアウト取得（<c>crisp/treeLayout</c>）— F4: Visual Editor</description></item>
///   <item><description>ツリー編集操作（<c>crisp/addNode</c>, <c>crisp/removeNode</c>,
///     <c>crisp/moveNode</c>, <c>crisp/wrapNode</c>, <c>crisp/unwrapNode</c>）— F4: Visual Editor</description></item>
/// </list>
///
/// 内部的に <see cref="QueryDatabase"/> を保持し、ファイル変更時に増分再計算を行う。
/// </summary>
public static class Server
{
    /// <summary>
    /// LSP サーバーのメインエントリポイント。
    /// 標準入出力を使用してクライアントと通信する。
    /// </summary>
    public static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .WithServices(services =>
                {
                    // QueryDatabase をシングルトンとして登録する。
                    // サーバーのライフタイム中、全ファイルのクエリ結果をキャッシュする。
                    services.AddSingleton<QueryDatabase>();
                })
                .WithServerInfo(new ServerInfo
                {
                    Name = "crisp-lsp",
                    Version = "0.1.0",
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<FormattingHandler>()
                .WithHandler<SemanticTokensHandler>()
                .WithHandler<TreeLayoutHandler>()
                .WithHandler<AddNodeHandler>()
                .WithHandler<RemoveNodeHandler>()
                .WithHandler<MoveNodeHandler>()
                .WithHandler<WrapNodeHandler>()
                .WithHandler<UnwrapNodeHandler>();
        }).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}
