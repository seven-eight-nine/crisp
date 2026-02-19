/**
 * テーマ定義
 *
 * VSCode のテーマカラー変数を参照し、ビジュアルエディタの
 * 配色を VSCode のテーマに自動追従させる。
 *
 * CSS カスタムプロパティ (var(--vscode-*)) を使用するため、
 * ライトテーマ・ダークテーマの切り替えに自動対応する。
 */

/**
 * ノードの状態に応じた色定義
 *
 * ビジュアルエディタでノードの選択状態、診断レベル、デバッグ状態を
 * 視覚的に区別するための配色を定義する。
 */
export const STATE_COLORS = {
  /** 選択中のノード枠線色 */
  selected: "var(--vscode-focusBorder)",
  /** カーソル位置対応ノードの背景色 */
  cursorTarget: "var(--vscode-editor-selectionBackground)",
  /** エラー診断のノード枠線色 */
  error: "var(--vscode-errorForeground)",
  /** 警告診断のノード枠線色 */
  warning: "var(--vscode-editorWarning-foreground)",
  /** デバッグ: 成功ステータスの色 */
  debugSuccess: "var(--vscode-testing-iconPassed)",
  /** デバッグ: 失敗ステータスの色 */
  debugFailure: "var(--vscode-testing-iconFailed)",
  /** デバッグ: 実行中ステータスの色 */
  debugRunning: "var(--vscode-debugIcon-startForeground)",
} as const;

/**
 * エッジ（接続線）の色定義
 */
export const EDGE_COLORS = {
  /** 通常のエッジ色 */
  normal: "var(--vscode-editorWidget-border)",
  /** デバッグ中のアクティブパスの色 */
  active: "var(--vscode-debugIcon-startForeground)",
} as const;

/**
 * キャンバスの色定義
 */
export const CANVAS_COLORS = {
  /** キャンバス背景色 */
  background: "var(--vscode-editor-background)",
  /** グリッドの色 */
  grid: "var(--vscode-editorWidget-border)",
} as const;
