/**
 * レイアウト定数
 *
 * ツリーノードの描画サイズ、間隔、キャンバスのパン・ズーム設定等、
 * ビジュアルエディタのレイアウトに関する定数を集約する。
 *
 * これらの値は LSP Server 側のレイアウト計算結果（座標）を
 * WebView 側で描画する際の参照値として使用される。
 */

/**
 * ノードとツリーのレイアウト定数
 *
 * LSP Server 側の TreeLayoutBuilder と同じ値を使用し、
 * 描画時の座標計算の整合性を保つ。
 */
export const LAYOUT = {
  /** ノードの最小幅（ピクセル） */
  NODE_MIN_WIDTH: 120,
  /** ノードの最大幅（ピクセル） — ラベルが長い場合の上限 */
  NODE_MAX_WIDTH: 250,
  /** ノードの高さ（ピクセル） */
  NODE_HEIGHT: 36,
  /** ノード内のテキスト左右パディング（ピクセル） */
  NODE_PADDING_X: 12,
  /** ノード内のテキスト上下パディング（ピクセル） */
  NODE_PADDING_Y: 8,
  /** 親子間の垂直距離（ピクセル） */
  LEVEL_GAP: 60,
  /** 兄弟ノード間の水平距離（ピクセル） */
  SIBLING_GAP: 16,
  /** サブツリー間の水平距離（ピクセル） */
  SUBTREE_GAP: 24,
  /** ノードアイコンのサイズ（ピクセル） */
  ICON_SIZE: 16,
  /** アイコンとテキストの間隔（ピクセル） */
  ICON_TEXT_GAP: 8,
  /** アイコンの左マージン（ピクセル） */
  ICON_LEFT_MARGIN: 8,
} as const;

/**
 * ズーム・パンの制御定数
 *
 * キャンバスのパン（平行移動）とズーム（拡大縮小）の
 * 挙動を制御するパラメータ。
 */
export const ZOOM_PAN = {
  /** ズームの最小倍率 */
  MIN_SCALE: 0.1,
  /** ズームの最大倍率 */
  MAX_SCALE: 3.0,
  /** ズームの初期倍率 */
  DEFAULT_SCALE: 1.0,
  /** マウスホイールによるズームの感度係数 */
  WHEEL_ZOOM_FACTOR: 0.001,
  /** ツリーフィット時の余白（ピクセル） */
  FIT_PADDING: 40,
} as const;

/**
 * ミニマップの定数
 */
export const MINIMAP = {
  /** ミニマップの幅（ピクセル） */
  WIDTH: 160,
  /** ミニマップの高さ（ピクセル） */
  HEIGHT: 120,
  /** ミニマップの右下からのマージン（ピクセル） */
  MARGIN: 12,
  /** ミニマップの背景透明度 */
  BG_OPACITY: 0.8,
} as const;

/**
 * エッジ（接続線）の定数
 */
export const EDGE = {
  /** エッジの線幅（ピクセル） */
  STROKE_WIDTH: 1.5,
  /** ベジェカーブの制御点オフセット（親子間距離に対する比率） */
  BEZIER_OFFSET_RATIO: 0.5,
} as const;

/**
 * デバッグアニメーションの定数
 */
export const DEBUG_ANIMATION = {
  /** Success/Failure フラッシュの持続時間（ミリ秒） */
  FLASH_DURATION_MS: 500,
  /** Running パルスアニメーションの周期（ミリ秒） */
  PULSE_PERIOD_MS: 1000,
} as const;
