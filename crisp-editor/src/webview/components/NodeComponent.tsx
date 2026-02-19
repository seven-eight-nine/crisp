/**
 * ノードコンポーネント
 *
 * ビジュアルエディタ上の個別ノードを描画する SVG コンポーネント。
 * ノードの種別に応じたスタイル（色、角丸、アイコン）を適用し、
 * 選択状態、診断レベル、デバッグ状態を視覚的に反映する。
 *
 * ユーザーインタラクション:
 * - シングルクリック: ノード選択
 * - ダブルクリック: テキストエディタの対応行にジャンプ
 * - ドラッグ: ノードの移動（別の親への再配置）
 */
import React, { useCallback } from "react";
import { useDragDrop } from "../hooks/useDragDrop";
import { useSelection } from "../hooks/useSelection";
import { useTreeStore } from "../state/treeStore";
import { NODE_STYLES } from "../styles/nodeStyles";
import type { TreeNode, NodeType } from "../protocol/messages";

/**
 * ノードコンポーネントのプロパティ
 */
interface NodeProps {
  /** 描画対象のツリーノード */
  node: TreeNode;
  /** レイアウト計算済みの X 座標（ノード中心） */
  x: number;
  /** レイアウト計算済みの Y 座標（ノード上端） */
  y: number;
  /** このノードが選択中かどうか */
  isSelected: boolean;
  /** テキストカーソルがこのノードに対応しているか */
  isCursorTarget: boolean;
  /** 診断レベル（エラー/警告/なし） */
  diagnosticLevel: "none" | "warning" | "error";
  /** デバッグ状態 (F7) */
  debugStatus?: "success" | "failure" | "running" | "idle";
}

/**
 * ノードの描画コンポーネント
 *
 * SVG の <g> 要素として描画される。
 * translate でレイアウト座標に配置し、内部に背景矩形、アイコン、ラベル、
 * 診断バッジ、デバッグインジケータを含む。
 */
export function NodeComponent({
  node,
  x,
  y,
  isSelected,
  isCursorTarget,
  diagnosticLevel,
  debugStatus,
}: NodeProps): React.JSX.Element {
  const { dragHandlers, isDragging } = useDragDrop(node.id);
  const { onClick, onDoubleClick } = useSelection(node.id);
  const openContextMenu = useTreeStore((s) => s.openContextMenu);

  /**
   * ノード右クリック — コンテキストメニューを表示する
   *
   * ブラウザのデフォルトコンテキストメニューを抑制し、
   * ノード ID と座標を Zustand ストアに設定して
   * カスタムコンテキストメニューを表示する。
   */
  const onContextMenu = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      e.stopPropagation();
      openContextMenu(e.clientX, e.clientY, node.id);
    },
    [node.id, openContextMenu]
  );

  const style = NODE_STYLES[node.type] ?? NODE_STYLES.unknown;
  const width = style.width;
  const height = style.height;

  /* アイコンの URI を解決する（WebView のグローバル変数から取得） */
  const iconsBaseUri =
    typeof window !== "undefined"
      ? (window as Record<string, unknown>).__CRISP_ICONS_URI__ ?? ""
      : "";
  const iconHref = `${iconsBaseUri}/${style.iconUri}`;

  /* CSS クラスの構築 — 状態に応じたスタイルを付与する */
  const className = [
    "crisp-node",
    isSelected && "crisp-node--selected",
    isCursorTarget && "crisp-node--cursor",
    isDragging && "crisp-node--dragging",
    diagnosticLevel !== "none" && `crisp-node--${diagnosticLevel}`,
    debugStatus && `crisp-node--debug-${debugStatus}`,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <g
      transform={`translate(${x - width / 2}, ${y})`}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
      onContextMenu={onContextMenu}
      onMouseDown={dragHandlers.onMouseDown}
      onMouseMove={dragHandlers.onMouseMove}
      onMouseUp={dragHandlers.onMouseUp}
      className={className}
    >
      {/* 背景矩形 — ノード種別に応じた色と角丸で描画 */}
      <rect
        width={width}
        height={height}
        rx={style.borderRadius}
        fill={style.backgroundColor}
        stroke={style.borderColor}
        strokeWidth={isSelected ? 2 : 1}
      />

      {/* カーソル対応ハイライト — テキストカーソル位置のノードを薄い青で表示 */}
      {isCursorTarget && (
        <rect
          width={width}
          height={height}
          rx={style.borderRadius}
          fill="var(--vscode-editor-selectionBackground)"
          opacity={0.3}
        />
      )}

      {/* ノードアイコン */}
      <image
        href={iconHref}
        x={8}
        y={(height - 16) / 2}
        width={16}
        height={16}
      />

      {/* ノードラベル — 表示テキストをクリッピングして描画 */}
      <text
        x={32}
        y={height / 2}
        dominantBaseline="central"
        fill={style.textColor}
        fontSize={12}
        fontFamily="var(--vscode-editor-font-family)"
      >
        {truncateLabel(node.label, width - 40)}
      </text>

      {/* 診断バッジ — エラーは赤丸、警告は黄丸で表示 */}
      {diagnosticLevel === "error" && (
        <circle
          cx={width - 8}
          cy={8}
          r={6}
          fill="var(--vscode-errorForeground)"
        />
      )}
      {diagnosticLevel === "warning" && (
        <circle
          cx={width - 8}
          cy={8}
          r={6}
          fill="var(--vscode-editorWarning-foreground)"
        />
      )}

      {/* デバッグ: Running インジケータ — ノード下部に黄色のパルスバー */}
      {debugStatus === "running" && (
        <rect
          x={0}
          y={height - 3}
          width={width}
          height={3}
          fill="var(--vscode-debugIcon-startForeground)"
          className="crisp-node-debug-pulse"
        />
      )}
    </g>
  );
}

/**
 * ラベルテキストを最大幅に収まるよう切り詰める
 *
 * SVG テキスト要素はオーバーフロー制御が限定的なため、
 * 文字数ベースの簡易的な切り詰めを行う。
 * 実際のピクセル幅の計算は省略し、概算で処理する。
 *
 * @param label 元のラベル文字列
 * @param maxWidth 利用可能な最大幅（ピクセル）
 * @returns 切り詰め済みのラベル文字列
 */
function truncateLabel(label: string, maxWidth: number): string {
  /* フォントサイズ 12px でおおよそ 1文字 7px と概算する */
  const maxChars = Math.floor(maxWidth / 7);
  if (label.length <= maxChars) return label;
  return label.slice(0, maxChars - 1) + "\u2026";
}
