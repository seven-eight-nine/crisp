/**
 * ツリービュー WebView プロバイダ
 *
 * ビジュアルツリーエディタを WebView パネルとして管理する。
 * 以下の2つの使用形態をサポートする:
 *
 * 1. WebviewViewProvider — サイドパネルに常駐するビュー
 * 2. WebviewPanel — エディタ横に開く独立パネル（"Crisp: Open Tree View" コマンド）
 *
 * WebView 内では React アプリケーションが動作し、
 * ツリーの描画・インタラクション・プロパティ表示を担当する。
 * Extension Host との通信は postMessage API を使用する。
 */
import * as vscode from "vscode";
import * as path from "path";
import type { SyncController } from "./syncController";
import type { WebViewToExtensionMessage } from "../shared/protocol";

/**
 * サイドパネル用の WebView プロバイダ
 *
 * VSCode のサイドバーに常駐するツリービューを提供する。
 * contributes.views で登録し、自動的にアクティブ化される。
 */
export class CrispTreeViewProvider implements vscode.WebviewViewProvider {
  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly syncController: SyncController
  ) {}

  /**
   * WebView の初期化処理
   *
   * VSCode がサイドパネルを表示する際に呼び出される。
   * WebView のオプション設定、HTML の生成、メッセージハンドラの登録を行い、
   * SyncController にサイドパネル WebView を登録する。
   */
  resolveWebviewView(webviewView: vscode.WebviewView): void {
    webviewView.webview.options = {
      enableScripts: true,
      localResourceRoots: [this.extensionUri],
    };

    webviewView.webview.html = getWebViewHtml(
      webviewView.webview,
      this.extensionUri
    );

    /* WebView → Extension のメッセージハンドリング */
    webviewView.webview.onDidReceiveMessage(
      (msg: WebViewToExtensionMessage) => {
        this.syncController.handleWebViewMessage(msg);
      }
    );

    /* Extension → WebView の接続を SyncController に登録する */
    this.syncController.registerSidePanelWebView(webviewView.webview);

    /* サイドパネルが非表示になった時に登録を解除する */
    webviewView.onDidDispose(() => {
      this.syncController.unregisterSidePanelWebView();
    });
  }
}

/**
 * エディタ横にツリービューパネルを開く
 *
 * コマンドパレットの "Crisp: Open Tree View" または
 * エディタの右クリックメニューから呼び出される。
 * エディタの横（ViewColumn.Beside）に WebView パネルを作成する。
 *
 * @param context 拡張コンテキスト
 * @param syncController 同期コントローラ
 * @param document 対象の .crisp テキストドキュメント
 * @returns 作成された WebView パネル
 */
export function openTreeViewPanel(
  context: vscode.ExtensionContext,
  syncController: SyncController,
  document: vscode.TextDocument
): vscode.WebviewPanel {
  const panel = vscode.window.createWebviewPanel(
    "crispTreeView",
    `Tree: ${path.basename(document.fileName)}`,
    vscode.ViewColumn.Beside,
    {
      enableScripts: true,
      /* タブ切り替え時も状態を保持し、再レンダリングを防止する */
      retainContextWhenHidden: true,
      localResourceRoots: [context.extensionUri],
    }
  );

  panel.webview.html = getWebViewHtml(panel.webview, context.extensionUri);

  /* WebView → Extension のメッセージハンドリング */
  panel.webview.onDidReceiveMessage((msg: WebViewToExtensionMessage) => {
    syncController.handleWebViewMessage(msg);
  });

  /* SyncController に WebView を登録し、レイアウトデータの受信を開始する */
  syncController.registerWebView(panel.webview, document.uri);

  /* パネルが閉じられた時に登録を解除する */
  panel.onDidDispose(() => {
    syncController.unregisterWebView(document.uri);
  });

  return panel;
}

/**
 * WebView 用の HTML を生成する
 *
 * React アプリケーション (webview.js) をロードする HTML を構築する。
 * VSCode のセキュリティポリシーに従い、nonce を使用して
 * スクリプトの実行を制限する。
 *
 * @param webview WebView インスタンス
 * @param extensionUri 拡張のルート URI
 * @returns HTML 文字列
 */
function getWebViewHtml(
  webview: vscode.Webview,
  extensionUri: vscode.Uri
): string {
  /* バンドル済み WebView スクリプトの URI を取得する */
  const scriptUri = webview.asWebviewUri(
    vscode.Uri.joinPath(extensionUri, "dist", "webview.js")
  );

  /* アイコンフォルダの URI を取得する（ノードアイコンの表示に使用） */
  const iconsUri = webview.asWebviewUri(
    vscode.Uri.joinPath(extensionUri, "media", "icons")
  );

  /* セキュリティ用の nonce を生成する */
  const nonce = getNonce();

  return `<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy"
    content="default-src 'none';
             img-src ${webview.cspSource} data:;
             style-src ${webview.cspSource} 'unsafe-inline';
             script-src 'nonce-${nonce}';">
  <title>Crisp Tree View</title>
</head>
<body>
  <div id="root"></div>
  <script nonce="${nonce}">
    /* WebView 内でアイコンパスを解決するためのグローバル変数 */
    window.__CRISP_ICONS_URI__ = "${iconsUri}";
  </script>
  <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
}

/**
 * 暗号学的に安全な nonce 文字列を生成する
 *
 * Content Security Policy で使用し、
 * 正規のスクリプトのみ実行を許可するためのトークン。
 *
 * @returns 32文字のランダム英数字文字列
 */
function getNonce(): string {
  let text = "";
  const possible =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  for (let i = 0; i < 32; i++) {
    text += possible.charAt(Math.floor(Math.random() * possible.length));
  }
  return text;
}
