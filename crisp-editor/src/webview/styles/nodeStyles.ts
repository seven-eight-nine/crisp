/**
 * ノード種別ごとのスタイル定義
 *
 * ビジュアルエディタで各ノード種別を視覚的に区別するための
 * 色、サイズ、アイコン等のスタイル設定。
 *
 * コンポジットノード（selector, sequence, parallel）は塗りつぶし背景、
 * リーフノード（check, action）は枠線のみ、
 * デコレータノード（guard, if, invert 等）は丸みのある枠線で表現する。
 */
import type { NodeType } from "../../shared/nodeTypes";

/**
 * ノードの描画スタイル
 */
export interface NodeStyle {
  /** ノードの背景色 */
  readonly backgroundColor: string;
  /** ノードの枠線色 */
  readonly borderColor: string;
  /** テキストの色 */
  readonly textColor: string;
  /** アイコンのファイル名 (media/icons/ 配下) */
  readonly iconUri: string;
  /** 角丸の半径（ピクセル） */
  readonly borderRadius: number;
  /** ノードのデフォルト幅（ピクセル） */
  readonly width: number;
  /** ノードの高さ（ピクセル） */
  readonly height: number;
}

/**
 * 全ノード種別のスタイルマップ
 *
 * VSCode のテーマカラー変数を使用することで、
 * ライトテーマとダークテーマの両方に自動対応する。
 */
export const NODE_STYLES: Record<NodeType, NodeStyle> = {
  /* --- コンポジットノード: 塗りつぶし背景で目立たせる --- */
  selector: {
    backgroundColor: "var(--vscode-charts-purple)",
    borderColor: "var(--vscode-charts-purple)",
    textColor: "var(--bg)",
    iconUri: "selector.svg",
    borderRadius: 8,
    width: 140,
    height: 36,
  },
  sequence: {
    backgroundColor: "var(--vscode-charts-blue)",
    borderColor: "var(--vscode-charts-blue)",
    textColor: "var(--bg)",
    iconUri: "sequence.svg",
    borderRadius: 8,
    width: 140,
    height: 36,
  },
  parallel: {
    backgroundColor: "var(--vscode-charts-orange)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--bg)",
    iconUri: "parallel.svg",
    borderRadius: 8,
    width: 160,
    height: 36,
  },
  "reactive-select": {
    backgroundColor: "var(--vscode-charts-orange)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--bg)",
    iconUri: "reactive-select.svg",
    borderRadius: 8,
    width: 180,
    height: 36,
  },

  /* --- リーフノード: 枠線のみで軽い印象にする --- */
  check: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-green)",
    textColor: "var(--fg)",
    iconUri: "check.svg",
    borderRadius: 4,
    width: 160,
    height: 36,
  },
  action: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-yellow)",
    textColor: "var(--fg)",
    iconUri: "action.svg",
    borderRadius: 4,
    width: 160,
    height: 36,
  },
  ref: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-blue)",
    textColor: "var(--fg)",
    iconUri: "selector.svg",
    borderRadius: 4,
    width: 180,
    height: 36,
  },

  /* --- デコレータノード: 丸みのある枠線で修飾的な印象にする --- */
  guard: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-red)",
    textColor: "var(--fg)",
    iconUri: "guard.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },
  if: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-purple)",
    textColor: "var(--fg)",
    iconUri: "if.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },
  invert: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-red)",
    textColor: "var(--fg)",
    iconUri: "invert.svg",
    borderRadius: 12,
    width: 140,
    height: 36,
  },
  repeat: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-blue)",
    textColor: "var(--fg)",
    iconUri: "repeat.svg",
    borderRadius: 12,
    width: 160,
    height: 36,
  },
  timeout: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--fg)",
    iconUri: "timeout.svg",
    borderRadius: 12,
    width: 160,
    height: 36,
  },
  cooldown: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--fg)",
    iconUri: "cooldown.svg",
    borderRadius: 12,
    width: 160,
    height: 36,
  },
  while: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-green)",
    textColor: "var(--fg)",
    iconUri: "while.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },
  reactive: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--fg)",
    iconUri: "reactive.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },
  "defdec-call": {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-purple)",
    textColor: "var(--fg)",
    iconUri: "guard.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },

  /* --- 不明ノード: グレーで控えめに表示 --- */
  unknown: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-editorWidget-border)",
    textColor: "var(--fg)",
    iconUri: "action.svg",
    borderRadius: 4,
    width: 140,
    height: 36,
  },
};
