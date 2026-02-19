/**
 * LSP クライアント設定
 *
 * Crisp LSP Server (C# .NET プロセス) との接続を管理する。
 * 標準 LSP 機能（補完、ホバー、診断、フォーマット、セマンティックトークン）に加え、
 * カスタム通知（crisp/treeLayout, crisp/diagnostics）と
 * カスタムリクエスト（crisp/addNode 等のツリー編集操作）を提供する。
 *
 * LSP Server は stdio 経由で通信し、QueryDatabase による増分計算で
 * 高速なレスポンスを実現する。
 */
import * as vscode from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from "vscode-languageclient/node";
import type { SyncController } from "./syncController";
import type {
  TreeLayoutNotification,
  TreeDiagnosticsNotification,
} from "../shared/protocol";

/**
 * LSP クライアントを生成する
 *
 * Crisp LSP Server のパスを解決し、LanguageClient インスタンスを構成する。
 * サーバーは dotnet コマンドで起動され、stdio 経由で通信する。
 *
 * @param context 拡張コンテキスト（サーバーパスの解決に使用）
 * @returns 設定済みの LanguageClient インスタンス
 */
export function createLspClient(
  context: vscode.ExtensionContext
): LanguageClient {
  const config = vscode.workspace.getConfiguration("crisp");
  const customPath = config.get<string>("lsp.path");
  const customArgs = config.get<string[]>("lsp.args") ?? [];

  /*
   * サーバーパスの解決優先順位:
   * 1. ユーザー設定 (crisp.lsp.path) — カスタムビルドのサーバーを指定可能
   * 2. 拡張同梱 (server/Crisp.Lsp) — デフォルト
   */
  const serverPath = customPath || context.asAbsolutePath("server/Crisp.Lsp");

  const serverOptions: ServerOptions = {
    run: {
      command: "dotnet",
      args: [serverPath, ...customArgs],
    },
    debug: {
      command: "dotnet",
      args: [serverPath, "--debug", ...customArgs],
    },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "crisp" }],
    synchronize: {
      /* .crisp ファイルの変更を監視して LSP に通知する */
      fileEvents: vscode.workspace.createFileSystemWatcher("**/*.crisp"),
    },
  };

  return new LanguageClient(
    "crisp",
    "Crisp Language Server",
    serverOptions,
    clientOptions
  );
}

/**
 * LSP クライアントにカスタム通知ハンドラを登録する
 *
 * Crisp LSP Server からのカスタム通知を SyncController に転送する。
 * didChange 後に LSP Server が debounce (100ms) してから
 * treeLayout 通知を送信するため、高速タイピング中のレイアウト再計算を抑制できる。
 *
 * @param client 起動済みの LanguageClient
 * @param syncController 同期コントローラ
 */
export function registerCustomNotifications(
  client: LanguageClient,
  syncController: SyncController
): void {
  /*
   * crisp/treeLayout 通知
   * ツリーレイアウトの再計算結果を受信し、WebView に転送する。
   * didChange → 100ms debounce → TreeLayout計算 → この通知
   */
  client.onNotification(
    "crisp/treeLayout",
    (params: TreeLayoutNotification) => {
      syncController.onTreeLayoutUpdate(params);
    }
  );

  /*
   * crisp/diagnostics 通知
   * ノードごとの診断情報を受信し、WebView に転送する。
   * エラーバッジや警告表示の更新に使用される。
   */
  client.onNotification(
    "crisp/diagnostics",
    (params: TreeDiagnosticsNotification) => {
      syncController.onDiagnosticsUpdate(params);
    }
  );
}

/*
 * ツリー編集リクエスト (crisp/addNode, crisp/removeNode 等) は
 * SyncController.applyLspEdit() が LanguageClient.sendRequest() を
 * 直接呼び出すため、個別のラッパー関数は設けない。
 */
