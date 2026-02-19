/**
 * Extension Host 内部で使用する型定義
 *
 * VSCode API の型と共有型を橋渡しする補助型。
 * Extension 固有のインターフェースや設定型を集約する。
 */

/**
 * Crisp 拡張の設定項目
 *
 * settings.json の "crisp.*" セクションに対応する。
 * vscode.workspace.getConfiguration("crisp") で取得可能。
 */
export interface CrispConfiguration {
  /** LSP サーバーのパス（空文字 = 拡張同梱） */
  readonly "lsp.path": string;
  /** LSP サーバーに渡す追加引数 */
  readonly "lsp.args": string[];
  /** .crisp を開いた時に自動でツリービューを開くか */
  readonly "treeView.defaultOpen": boolean;
  /** ツリービューの配置位置 */
  readonly "treeView.layout": "side" | "below";
  /** ミニマップの表示 */
  readonly "treeView.showMinimap": boolean;
  /** プロパティパネルの表示 */
  readonly "treeView.showPropertyPanel": boolean;
  /** デバッグ時のアニメーション表示 */
  readonly "treeView.animateDebugState": boolean;
  /** テキスト変更時のオートフィット */
  readonly "treeView.autoFitOnChange": boolean;
  /** ノード間の水平スペース（ピクセル） */
  readonly "treeView.nodeSpacing.horizontal": number;
  /** ノード間の垂直スペース（ピクセル） */
  readonly "treeView.nodeSpacing.vertical": number;
  /** フォーマッタのインデントサイズ */
  readonly "formatter.indentSize": number;
  /** フォーマッタの最大行幅 */
  readonly "formatter.maxLineWidth": number;
  /** デバッグアダプタのポート番号 */
  readonly "debug.port": number;
}

/**
 * Crisp 設定の型安全な取得ヘルパー
 *
 * @param key 設定キー ("lsp.path" 等)
 * @returns 設定値
 */
export function getCrispConfig<K extends keyof CrispConfiguration>(
  key: K
): CrispConfiguration[K] {
  /* eslint-disable-next-line @typescript-eslint/no-require-imports */
  const config = require("vscode").workspace.getConfiguration("crisp");
  return config.get(key) as CrispConfiguration[K];
}
