/**
 * コマンド登録モジュール
 *
 * Crisp 拡張が提供する全コマンドを登録する。
 * コマンドはコマンドパレット、右クリックメニュー、キーバインドから呼び出される。
 *
 * 登録されるコマンド:
 * - crisp.openTreeView: ビジュアルツリービューを開く
 * - crisp.fitTree: ツリー全体をビューにフィットする
 */
import * as vscode from "vscode";
import { openTreeViewPanel } from "./treeViewProvider";
import type { SyncController } from "./syncController";

/**
 * 全コマンドを登録する
 *
 * Extension の activate 時に呼び出される。
 * 各コマンドを VSCode に登録し、Disposable を返す。
 *
 * @param context 拡張コンテキスト
 * @param syncController 同期コントローラ
 * @returns 登録された Disposable の配列
 */
export function registerCommands(
  context: vscode.ExtensionContext,
  syncController: SyncController
): vscode.Disposable[] {
  const disposables: vscode.Disposable[] = [];

  /**
   * crisp.openTreeView コマンド
   *
   * 現在アクティブな .crisp ファイルのビジュアルツリービューを開く。
   * コマンドパレット、エディタタイトルバーのアイコン、右クリックメニューから呼び出し可能。
   * .crisp 以外のファイルがアクティブな場合はエラーメッセージを表示する。
   */
  disposables.push(
    vscode.commands.registerCommand("crisp.openTreeView", () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) {
        vscode.window.showErrorMessage(
          "Crisp: ツリービューを開くには .crisp ファイルをアクティブにしてください。"
        );
        return;
      }

      if (editor.document.languageId !== "crisp") {
        vscode.window.showErrorMessage(
          "Crisp: ツリービューは .crisp ファイルでのみ使用できます。"
        );
        return;
      }

      openTreeViewPanel(context, syncController, editor.document);
    })
  );

  /**
   * crisp.fitTree コマンド
   *
   * ツリー全体がビューに収まるようにズームレベルを自動調整する。
   * SyncController 経由で対応する WebView に fitToView メッセージを送信する。
   * キーバインド: Ctrl+Shift+F
   */
  disposables.push(
    vscode.commands.registerCommand("crisp.fitTree", () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor || editor.document.languageId !== "crisp") return;

      syncController.sendFitToView(editor.document.uri.toString());
    })
  );

  return disposables;
}
