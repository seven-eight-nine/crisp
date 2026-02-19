/**
 * 通信プロトコル型定義
 *
 * Extension Host ↔ WebView 間の postMessage 通信で使用するメッセージ型。
 * LSP Server ↔ Extension Host 間のカスタム通知・リクエストの型も定義する。
 *
 * 通信経路:
 * (A) テキストエディタ → LSP: 標準 LSP (didOpen/didChange/completion 等)
 * (B) LSP → ビジュアルエディタ: crisp/treeLayout, crisp/diagnostics 通知
 * (C) ビジュアルエディタ → テキストエディタ: addNode/removeNode 等のコマンド
 * (D) テキストエディタ → ビジュアルエディタ: カーソル位置同期
 */
import type { TreeLayout, NodeDiagnostic, DebugTreeState } from "./treeModel";

/* ============================================================
 * Extension → WebView メッセージ
 * Extension Host から WebView (React) に送信されるメッセージ群。
 * LSP サーバーからの通知を中継するものが中心。
 * ============================================================ */

/** ツリーレイアウト更新通知 — LSP の crisp/treeLayout 通知を中継 */
export interface TreeLayoutMessage {
  readonly type: "treeLayout";
  readonly payload: TreeLayout;
}

/** 診断情報更新通知 — ノードごとのエラー・警告をビジュアル上に反映 */
export interface DiagnosticsMessage {
  readonly type: "diagnostics";
  readonly payload: NodeDiagnostic[];
}

/** カーソル同期通知 — テキストエディタのカーソル位置に対応するノードを通知 */
export interface CursorSyncMessage {
  readonly type: "cursorSync";
  readonly payload: { readonly nodeId: number };
}

/** デバッグ状態通知 — ゲームランタイムからの実行状態をビジュアルに反映 (F7) */
export interface DebugStateMessage {
  readonly type: "debugState";
  readonly payload: DebugTreeState;
}

/** ツリーフィットコマンド — ツリー全体をビューに収まるようにズームを自動調整する */
export interface FitToViewMessage {
  readonly type: "fitToView";
}

/** Extension → WebView の全メッセージ型のユニオン */
export type ExtensionToWebViewMessage =
  | TreeLayoutMessage
  | DiagnosticsMessage
  | CursorSyncMessage
  | DebugStateMessage
  | FitToViewMessage;

/* ============================================================
 * WebView → Extension メッセージ
 * WebView (React) から Extension Host に送信されるメッセージ群。
 * ビジュアル操作を LSP リクエストに変換してテキスト編集に反映する。
 * ============================================================ */

/** ノード追加コマンド — パレットからのドラッグ＆ドロップで発生 */
export interface AddNodeMessage {
  readonly type: "addNode";
  readonly payload: {
    /** 対象ファイルの URI */
    readonly uri: string;
    /** 挿入先の親ノード ID */
    readonly parentNodeId: number;
    /** 親の子リスト内での挿入位置（0-indexed） */
    readonly position: number;
    /** 挿入するノードの種別 */
    readonly nodeType: string;
    /** ノードの引数（オプション） */
    readonly args?: Record<string, string>;
  };
}

/** ノード削除コマンド — Delete キーまたは右クリックメニューから発生 */
export interface RemoveNodeMessage {
  readonly type: "removeNode";
  readonly payload: {
    readonly uri: string;
    /** 削除対象のノード ID */
    readonly nodeId: number;
  };
}

/** ノード移動コマンド — ドラッグ＆ドロップで別の親にノードを移動 */
export interface MoveNodeMessage {
  readonly type: "moveNode";
  readonly payload: {
    readonly uri: string;
    /** 移動するノードの ID */
    readonly nodeId: number;
    /** 新しい親ノードの ID */
    readonly newParentId: number;
    /** 新しい親の子リスト内での挿入位置（0-indexed） */
    readonly newPosition: number;
  };
}

/** ノードラップコマンド — 右クリック "Wrap with..." で既存ノードをデコレータで包む */
export interface WrapNodeMessage {
  readonly type: "wrapNode";
  readonly payload: {
    readonly uri: string;
    /** ラップ対象のノード ID */
    readonly nodeId: number;
    /** デコレータの種別（"guard", "timeout" 等） */
    readonly decoratorType: string;
    /** デコレータの引数（オプション） */
    readonly args?: string[];
  };
}

/** ノードアンラップコマンド — 右クリック "Unwrap" でデコレータを解除 */
export interface UnwrapNodeMessage {
  readonly type: "unwrapNode";
  readonly payload: {
    readonly uri: string;
    /** アンラップ対象のデコレータノード ID */
    readonly nodeId: number;
  };
}

/** ノードクリックイベント — ビジュアル上のノードクリックでテキストエディタにジャンプ */
export interface NodeClickedMessage {
  readonly type: "nodeClicked";
  readonly payload: {
    readonly uri: string;
    /** クリックされたノードの ID */
    readonly nodeId: number;
  };
}

/** ブレークポイント切替コマンド — 右クリック "Toggle Breakpoint" で発生 (F7) */
export interface ToggleBreakpointMessage {
  readonly type: "toggleBreakpoint";
  readonly payload: {
    readonly uri: string;
    /** ブレークポイント対象のノード ID */
    readonly nodeId: number;
  };
}

/** WebView → Extension の全メッセージ型のユニオン */
export type WebViewToExtensionMessage =
  | AddNodeMessage
  | RemoveNodeMessage
  | MoveNodeMessage
  | WrapNodeMessage
  | UnwrapNodeMessage
  | NodeClickedMessage
  | ToggleBreakpointMessage;

/* ============================================================
 * LSP カスタム通知・リクエスト型
 * Crisp LSP Server との拡張プロトコル。
 * 標準 LSP に加えて、ツリーレイアウトとツリー編集の機能を提供する。
 * ============================================================ */

/** LSP crisp/treeLayout 通知のパラメータ */
export interface TreeLayoutNotification {
  /** 対象ファイルの URI */
  readonly uri: string;
  /** レイアウト計算結果 */
  readonly layout: TreeLayout;
}

/** LSP crisp/diagnostics 通知のパラメータ */
export interface TreeDiagnosticsNotification {
  /** 対象ファイルの URI */
  readonly uri: string;
  /** ノードごとの診断情報リスト */
  readonly diagnostics: NodeDiagnostic[];
}

/** LSP ツリー編集リクエストの結果 — テキスト編集操作のリスト */
export interface EditResult {
  readonly edits: TextEditItem[];
}

/** テキスト編集操作の1要素 */
export interface TextEditItem {
  /** 編集対象の範囲 */
  readonly range: {
    readonly start: { readonly line: number; readonly character: number };
    readonly end: { readonly line: number; readonly character: number };
  };
  /** 置換後のテキスト */
  readonly newText: string;
}

/** crisp/addNode リクエストのパラメータ */
export interface AddNodeParams {
  readonly uri: string;
  readonly parentNodeId: number;
  readonly position: number;
  readonly nodeType: string;
  readonly args?: Record<string, string>;
}

/** crisp/removeNode リクエストのパラメータ */
export interface RemoveNodeParams {
  readonly uri: string;
  readonly nodeId: number;
}

/** crisp/moveNode リクエストのパラメータ */
export interface MoveNodeParams {
  readonly uri: string;
  readonly nodeId: number;
  readonly newParentId: number;
  readonly newPosition: number;
}

/** crisp/wrapNode リクエストのパラメータ */
export interface WrapNodeParams {
  readonly uri: string;
  readonly nodeId: number;
  readonly decoratorType: string;
  readonly args?: string[];
}

/** crisp/unwrapNode リクエストのパラメータ */
export interface UnwrapNodeParams {
  readonly uri: string;
  readonly nodeId: number;
}
