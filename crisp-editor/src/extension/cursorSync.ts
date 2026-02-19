/**
 * カーソル位置同期モジュール
 *
 * テキストエディタのカーソル位置変更を監視し、
 * SyncController を通じて WebView にカーソル同期通知を送信する。
 *
 * 同期の方向:
 * - テキスト → ビジュアル: カーソル位置 → 対応ノードのハイライト
 * - ビジュアル → テキスト: ノードクリック → テキストのカーソルジャンプ
 *   (こちらは SyncController.jumpToNode が担当)
 */
import * as vscode from "vscode";
import type { SyncController } from "./syncController";

/**
 * カーソル同期イベントリスナーを登録する
 *
 * テキストエディタのカーソル位置が変更されるたびに、
 * SyncController.onCursorPositionChanged を呼び出す。
 * Crisp ファイル (.crisp) のみを対象とし、他の言語のファイルは無視する。
 *
 * @param syncController 同期コントローラインスタンス
 * @returns リスナーの Disposable（拡張の deactivate 時に解除する）
 */
export function registerCursorSync(
  syncController: SyncController
): vscode.Disposable {
  return vscode.window.onDidChangeTextEditorSelection((event) => {
    const editor = event.textEditor;

    /* Crisp ファイル以外は無視する */
    if (editor.document.languageId !== "crisp") return;

    /* 主カーソルのオフセットを計算して同期コントローラに通知する */
    const offset = editor.document.offsetAt(event.selections[0].active);
    const uri = editor.document.uri.toString();

    syncController.onCursorPositionChanged(uri, offset);
  });
}

/**
 * ドキュメント変更イベントリスナーを登録する
 *
 * テキストの変更を SyncController に通知する。
 * ビジュアル編集起因の変更は SyncController 内で isApplyingEdit フラグにより無視される。
 *
 * @param syncController 同期コントローラインスタンス
 * @returns リスナーの Disposable
 */
export function registerDocumentChangeSync(
  syncController: SyncController
): vscode.Disposable {
  return vscode.workspace.onDidChangeTextDocument((event) => {
    if (event.document.languageId !== "crisp") return;

    syncController.onDocumentChanged(event.document.uri.toString());
  });
}
