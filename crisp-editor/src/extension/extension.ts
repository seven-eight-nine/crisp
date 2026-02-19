/**
 * Crisp Editor — VSCode 拡張エントリポイント
 *
 * Crisp DSL のための統合開発環境を提供する VSCode 拡張。
 * 以下の3つの機能を統合する:
 *
 * 1. テキストエディタ支援: .crisp ファイルのシンタックスハイライト、補完、診断、フォーマット
 * 2. ビジュアルツリーエディタ: ビヘイビアツリーのノードグラフ表示・編集
 * 3. デバッグ統合: 実行中のツリーの状態可視化、ブレークポイント (F7 実装後)
 *
 * アーキテクチャ:
 * - Extension Host (Node.js): VSCode API の制御、LSP/WebView の橋渡し
 * - Crisp LSP Server (C# .NET): パース、型チェック、ツリーレイアウト計算
 * - WebView (React): ビジュアルツリーの描画・インタラクション
 */
import * as vscode from "vscode";
import { createLspClient, registerCustomNotifications } from "./lspClient";
import { SyncController } from "./syncController";
import { registerCursorSync, registerDocumentChangeSync } from "./cursorSync";
import { registerCommands } from "./commands";
import { CrispTreeViewProvider, openTreeViewPanel } from "./treeViewProvider";
import type { LanguageClient } from "vscode-languageclient/node";

/** LSP クライアントのグローバル参照（deactivate 時の停止用） */
let client: LanguageClient | undefined;

/**
 * 拡張の起動処理
 *
 * VSCode が Crisp ファイルを開いた時、または crisp.openTreeView コマンドが
 * 実行された時に呼び出される（activationEvents で定義）。
 *
 * 起動フロー:
 * 1. LSP クライアントを作成・起動
 * 2. SyncController を初期化
 * 3. カスタム LSP 通知のハンドラを登録
 * 4. カーソル同期・ドキュメント変更リスナーを登録
 * 5. コマンドを登録
 * 6. 設定に応じて自動でツリービューを開く
 *
 * @param context 拡張コンテキスト（リソース解放の管理に使用）
 */
export async function activate(
  context: vscode.ExtensionContext
): Promise<void> {
  /* LSP クライアントの作成 */
  client = createLspClient(context);

  /* SyncController の初期化 — テキスト↔ビジュアルの橋渡し */
  const syncController = new SyncController(client);

  /* カーソル同期とドキュメント変更のイベントリスナーを登録する */
  context.subscriptions.push(registerCursorSync(syncController));
  context.subscriptions.push(registerDocumentChangeSync(syncController));

  /* コマンドを登録する */
  const commandDisposables = registerCommands(context, syncController);
  commandDisposables.forEach((d) => context.subscriptions.push(d));

  /*
   * サイドパネル用の WebviewViewProvider を登録する。
   * package.json の contributes.views で定義された "crispTreeView" ビューに対応する。
   */
  const sidePanel = new CrispTreeViewProvider(
    context.extensionUri,
    syncController
  );
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider("crispTreeView", sidePanel)
  );

  /*
   * アクティブエディタの変更を監視し、サイドパネルの追跡 URI を更新する。
   * .crisp ファイルがアクティブになった場合はそのファイルの URI を設定し、
   * それ以外の場合は null を設定する。
   */
  context.subscriptions.push(
    vscode.window.onDidChangeActiveTextEditor((editor) => {
      if (editor && editor.document.languageId === "crisp") {
        syncController.onActiveEditorChanged(editor.document.uri.toString());
      } else {
        syncController.onActiveEditorChanged(null);
      }
    })
  );

  /*
   * Debug Adapter Protocol (DAP) ファクトリの登録 (F7)
   *
   * Crisp デバッガの完全実装は F7 フェーズで行う。
   * ここでは package.json の debuggers 定義と合わせて
   * ファクトリを登録し、将来の実装に備える。
   */
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory("crisp", {
      createDebugAdapterDescriptor(_session) {
        /*
         * F7 完全実装時に CrispDebugAdapter のインライン実装に置き換える:
         * return new vscode.DebugAdapterInlineImplementation(
         *   new CrispDebugAdapter()
         * );
         */
        vscode.window.showInformationMessage(
          "Crisp: デバッガは F7 フェーズで実装予定です。"
        );
        return undefined as unknown as vscode.DebugAdapterDescriptor;
      },
    })
  );

  /*
   * LSP クライアントを起動する。
   * vscode-languageclient v9.x では start() が Promise を返し、
   * 完了後にクライアントが使用可能になる。
   * カスタム通知ハンドラは起動完了後に登録する。
   */
  await client.start();
  registerCustomNotifications(client, syncController);

  /* 拡張の停止時に LSP クライアントを安全に停止する */
  context.subscriptions.push({
    dispose: () => {
      if (client) {
        client.stop();
      }
    },
  });

  /*
   * crisp.treeView.defaultOpen 設定が有効な場合、
   * .crisp ファイルを開いた時に自動でツリービューパネルを開く。
   */
  const config = vscode.workspace.getConfiguration("crisp");
  if (config.get<boolean>("treeView.defaultOpen")) {
    context.subscriptions.push(
      vscode.window.onDidChangeActiveTextEditor((editor) => {
        if (editor && editor.document.languageId === "crisp") {
          openTreeViewPanel(context, syncController, editor.document);
        }
      })
    );
  }
}

/**
 * 拡張の停止処理
 *
 * VSCode が終了する時、または拡張が無効化された時に呼び出される。
 * LSP クライアントを安全に停止する。
 *
 * @returns LSP クライアント停止の Promise
 */
export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}
