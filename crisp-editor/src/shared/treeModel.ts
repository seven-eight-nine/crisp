/**
 * ツリーデータモデル
 *
 * LSP サーバーから受信するツリーレイアウトデータの型定義。
 * ビジュアルエディタの描画とテキストエディタとの双方向同期に使用される。
 *
 * Origin フィールドは常にソーステキスト上の実位置を反映する。
 * 以下の操作後も LSP Server が Origin を再計算し、カーソル同期の正確性を保証する:
 * - フォーマッタによる再整形
 * - CstEditor による構造変更（ノード追加・移動・削除）
 * - マクロ展開（F10）
 */
import type { NodeType } from "./nodeTypes";

/**
 * ツリーレイアウト全体
 *
 * LSP サーバーの crisp/treeLayout 通知で送信されるルートオブジェクト。
 * ファイル単位でレイアウト計算結果を保持する。
 */
export interface TreeLayout {
  /** 対象ファイルの URI */
  readonly fileUri: string;
  /** ツリー定義名 (例: "EnemyCombat") */
  readonly treeName: string;
  /** ルートノード（再帰的に子ノードを含む） */
  readonly root: TreeNode;
  /** 全エッジ（親子間の接続線）情報 */
  readonly edges: TreeEdge[];
  /** ツリー全体の境界ボックス */
  readonly bounds: TreeBounds;
}

/**
 * ツリーノード
 *
 * LSP サーバーがレイアウト計算済みの座標を含めて送信する。
 * id は CstNode.Id / IrNode.Id と一致し、テキスト位置との対応に使用される。
 */
export interface TreeNode {
  /** ノードの一意識別子（CstNode.Id = IrNode.Id） */
  readonly id: number;
  /** ノード種別 */
  readonly type: NodeType;
  /** 表示ラベル (例: "< .Health 30", "Flee()", "selector") */
  readonly label: string;
  /** レイアウト計算済み X 座標（ノード中心） */
  readonly x: number;
  /** レイアウト計算済み Y 座標（ノード上端） */
  readonly y: number;
  /** ノードの描画幅（ピクセル） */
  readonly width: number;
  /** ノードの描画高さ（ピクセル） */
  readonly height: number;
  /** ソーステキスト上の位置範囲 */
  readonly origin: TextSpan;
  /** 式の解決型 (型推論結果、例: "bool", "float") */
  readonly resolvedType?: string;
  /** このノードに付与された診断情報 */
  readonly diagnostics: NodeDiagnostic[];
  /** 子ノード（再帰構造） */
  readonly children?: TreeNode[];
  /** 折りたたみ状態 (true = 子ノードを非表示) */
  readonly collapsed: boolean;
}

/**
 * ツリーエッジ（親子間の接続線）
 *
 * ビジュアルエディタでベジェカーブとして描画される。
 */
export interface TreeEdge {
  /** 親ノードの ID */
  readonly fromId: number;
  /** 子ノードの ID */
  readonly toId: number;
  /** 親側の接続ポート位置（常に "bottom"） */
  readonly fromPort: "bottom";
  /** 子側の接続ポート位置（常に "top"） */
  readonly toPort: "top";
}

/**
 * ソーステキスト上の位置範囲
 *
 * テキストエディタとのカーソル同期に使用される。
 * start はファイル先頭からのバイトオフセット。
 */
export interface TextSpan {
  /** ファイル先頭からの開始オフセット */
  readonly start: number;
  /** テキスト範囲の長さ */
  readonly length: number;
}

/**
 * ツリー全体の境界ボックス
 *
 * ズーム・フィット計算やミニマップの表示範囲決定に使用される。
 */
export interface TreeBounds {
  /** ツリー全体の幅（ピクセル） */
  readonly width: number;
  /** ツリー全体の高さ（ピクセル） */
  readonly height: number;
}

/**
 * ノード診断情報
 *
 * コンパイルエラーや警告をビジュアルエディタ上に表示するための情報。
 * LSP の Diagnostic と 1:1 対応する。
 */
export interface NodeDiagnostic {
  /** 診断コード (例: "BS0001") */
  readonly code: string;
  /** 診断メッセージ */
  readonly message: string;
  /** 診断の重大度 */
  readonly severity: "error" | "warning" | "info";
}

/**
 * デバッグツリー状態 (F7)
 *
 * ゲームランタイムからリアルタイムで受信するツリーの実行状態。
 * ビジュアルエディタでノードの色やアニメーションを制御する。
 */
export interface DebugTreeState {
  /** 現在のティック番号 */
  readonly tick: number;
  /** フレームインデックス */
  readonly frameIndex: number;
  /** ノード ID → デバッグ情報のマップ */
  readonly nodes: Record<number, NodeDebugInfo>;
}

/**
 * ノードごとのデバッグ情報
 *
 * 各ノードの評価状態とブレークポイント状態を保持する。
 */
export interface NodeDebugInfo {
  /** ノードの現在の評価状態 */
  readonly status: "idle" | "evaluating" | "success" | "failure" | "running";
  /** 直前のティックでの結果 */
  readonly lastTickResult?: "success" | "failure" | "running";
  /** 式の現在値 (例: { "Health": "25.5" }) */
  readonly exprValues?: Record<string, string>;
  /** ブレークポイントが設定されているか */
  readonly hasBreakpoint: boolean;
}
