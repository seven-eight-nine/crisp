/**
 * ノード種別定義
 *
 * Crisp DSL のビヘイビアツリーで使用可能な全ノード種別を定義する。
 * ビジュアルエディタでのアイコン選択、スタイル適用、ノードパレットの構成に使用される。
 */

/**
 * ノード種別の列挙型
 *
 * LSP サーバーの TreeLayoutBuilder が出力する NodeType 文字列と完全に一致する。
 * CstNode の種別から IR 経由でこの文字列に変換される。
 */
export type NodeType =
  | "selector"
  | "sequence"
  | "parallel"
  | "check"
  | "action"
  | "guard"
  | "if"
  | "invert"
  | "repeat"
  | "timeout"
  | "cooldown"
  | "while"
  | "reactive"
  | "reactive-select"
  | "ref"
  | "defdec-call"
  | "unknown";

/**
 * ノードのカテゴリ分類
 *
 * コンポジット: 複数の子ノードを持つ制御フローノード
 * デコレータ: 単一の子ノードを修飾するノード
 * リーフ: 子ノードを持たない末端ノード
 */
export type NodeCategory = "composite" | "decorator" | "leaf";

/**
 * ノード種別ごとのメタデータ
 *
 * ノードパレット、ツールチップ、バリデーション等で参照される。
 */
export interface NodeTypeInfo {
  /** ノード種別の識別子 */
  readonly type: NodeType;
  /** パレット等に表示するラベル */
  readonly label: string;
  /** ノードのカテゴリ */
  readonly category: NodeCategory;
  /** アイコンファイル名 (media/icons/ 配下) */
  readonly icon: string;
  /** ツールチップに表示する説明文 */
  readonly description: string;
  /** 子ノードの最小数 (0 = リーフ) */
  readonly minChildren: number;
  /** 子ノードの最大数 (Infinity = 制限なし) */
  readonly maxChildren: number;
}

/**
 * 全ノード種別のメタデータマップ
 *
 * ノードパレットの構成やバリデーションルールの定義に使用する。
 */
export const NODE_TYPE_INFO: Record<NodeType, NodeTypeInfo> = {
  selector: {
    type: "selector",
    label: "Selector",
    category: "composite",
    icon: "selector.svg",
    description: "子ノードを左から順に評価し、最初に成功した子で成功を返す。",
    minChildren: 1,
    maxChildren: Infinity,
  },
  sequence: {
    type: "sequence",
    label: "Sequence",
    category: "composite",
    icon: "sequence.svg",
    description: "子ノードを左から順に評価し、全ての子が成功したら成功を返す。",
    minChildren: 1,
    maxChildren: Infinity,
  },
  parallel: {
    type: "parallel",
    label: "Parallel",
    category: "composite",
    icon: "parallel.svg",
    description: "全ての子ノードを同時に評価する。ポリシー(:any/:all/:n)で成功条件を指定。",
    minChildren: 1,
    maxChildren: Infinity,
  },
  check: {
    type: "check",
    label: "Check",
    category: "leaf",
    icon: "check.svg",
    description: "条件式を評価し、真なら成功、偽なら失敗を返す。",
    minChildren: 0,
    maxChildren: 0,
  },
  action: {
    type: "action",
    label: "Action",
    category: "leaf",
    icon: "action.svg",
    description: "コンテキスト型のメソッドを呼び出すアクションノード。",
    minChildren: 0,
    maxChildren: 0,
  },
  guard: {
    type: "guard",
    label: "Guard",
    category: "decorator",
    icon: "guard.svg",
    description: "条件が真の間だけ子ノードを評価する。偽になったら子をリセット。",
    minChildren: 1,
    maxChildren: 1,
  },
  if: {
    type: "if",
    label: "If / Else",
    category: "decorator",
    icon: "if.svg",
    description: "条件式に応じて then ブランチまたは else ブランチを評価する。",
    minChildren: 1,
    maxChildren: 2,
  },
  invert: {
    type: "invert",
    label: "Invert",
    category: "decorator",
    icon: "invert.svg",
    description: "子ノードの結果を反転する（成功⇔失敗）。",
    minChildren: 1,
    maxChildren: 1,
  },
  repeat: {
    type: "repeat",
    label: "Repeat",
    category: "decorator",
    icon: "repeat.svg",
    description: "子ノードを指定回数繰り返し評価する。",
    minChildren: 1,
    maxChildren: 1,
  },
  timeout: {
    type: "timeout",
    label: "Timeout",
    category: "decorator",
    icon: "timeout.svg",
    description: "指定時間（秒）以内に子ノードが完了しなければ失敗を返す。",
    minChildren: 1,
    maxChildren: 1,
  },
  cooldown: {
    type: "cooldown",
    label: "Cooldown",
    category: "decorator",
    icon: "cooldown.svg",
    description: "子ノード実行後、指定時間（秒）のクールダウン期間を設ける。",
    minChildren: 1,
    maxChildren: 1,
  },
  while: {
    type: "while",
    label: "While",
    category: "decorator",
    icon: "while.svg",
    description: "条件式が真の間、子ノードを繰り返し評価する。",
    minChildren: 1,
    maxChildren: 1,
  },
  reactive: {
    type: "reactive",
    label: "Reactive",
    category: "decorator",
    icon: "reactive.svg",
    description: "条件式をティックごとに再評価し、偽になったら子をアボートする (F1)。",
    minChildren: 1,
    maxChildren: 1,
  },
  "reactive-select": {
    type: "reactive-select",
    label: "Reactive Select",
    category: "composite",
    icon: "reactive-select.svg",
    description: "条件付き子ノードをティックごとに再評価し、優先度に基づいて切り替える (F1)。",
    minChildren: 1,
    maxChildren: Infinity,
  },
  ref: {
    type: "ref",
    label: "Tree Ref",
    category: "leaf",
    icon: "selector.svg",
    description: "別のツリー定義を参照する (F11: Multi-Tree)。",
    minChildren: 0,
    maxChildren: 0,
  },
  "defdec-call": {
    type: "defdec-call",
    label: "Custom Decorator",
    category: "decorator",
    icon: "guard.svg",
    description: "ユーザー定義デコレータの呼び出し (F2)。",
    minChildren: 1,
    maxChildren: 1,
  },
  unknown: {
    type: "unknown",
    label: "Unknown",
    category: "leaf",
    icon: "action.svg",
    description: "認識できないノード種別。パース中のエラー等で発生する可能性がある。",
    minChildren: 0,
    maxChildren: 0,
  },
};
