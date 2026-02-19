/**
 * ツリーキャンバスコンポーネント
 *
 * ビジュアルエディタのメイン描画領域。
 * SVG 要素としてツリーを描画し、パン・ズーム操作を提供する。
 *
 * 構成:
 * - SVG キャンバス: ツリーの描画（パン・ズーム対応）
 * - ミニマップ: 全体俯瞰表示（キャンバス右下に重ねて表示）
 *
 * パン・ズーム:
 * - マウスドラッグ（背景）: キャンバスのパン
 * - マウスホイール: ズーム（カーソル位置を中心に拡大縮小）
 */
import React, { useEffect, useRef } from "react";
import { useZoomPan } from "../hooks/useZoomPan";
import { useTreeStore } from "../state/treeStore";
import { computeFitTransform } from "../layout/treeLayout";
import { TreeRenderer } from "./TreeRenderer";
import { Minimap } from "./Minimap";
import type { TreeLayout } from "../protocol/messages";

/**
 * ツリーキャンバスのプロパティ
 */
interface TreeCanvasProps {
  /** 描画対象のツリーレイアウト（null = ツリーなし） */
  tree: TreeLayout | null;
  /** 選択中のノード ID */
  selectedNodeId: number | null;
}

/**
 * ツリーキャンバスの描画コンポーネント
 *
 * ツリーが存在しない場合は空状態メッセージを表示する。
 * ツリーが存在する場合は、パン・ズーム可能な SVG キャンバスに
 * ツリーレンダラを配置する。
 */
export function TreeCanvas({
  tree,
  selectedNodeId,
}: TreeCanvasProps): React.JSX.Element {
  const { transform, handlers, setTransform } = useZoomPan();
  const fitToViewCounter = useTreeStore((s) => s.fitToViewCounter);
  const canvasRef = useRef<HTMLDivElement>(null);

  /*
   * fitToView リクエストを検知し、ツリー全体がビューに収まるように
   * ズーム・パンを自動調整する。
   * fitToViewCounter が変化するたびに実行される。
   */
  useEffect(() => {
    if (fitToViewCounter === 0 || !tree || !canvasRef.current) return;

    const rect = canvasRef.current.getBoundingClientRect();
    const fitTransform = computeFitTransform(tree.bounds, rect.width, rect.height);
    if (fitTransform) {
      setTransform(fitTransform);
    }
  }, [fitToViewCounter, tree, setTransform]);

  if (!tree) {
    return (
      <div className="crisp-empty">
        ツリーを表示するには .crisp ファイルを開いてください
      </div>
    );
  }

  return (
    <div className="crisp-canvas" ref={canvasRef} {...handlers}>
      <svg className="crisp-canvas-svg">
        {/* パン・ズームの座標変換グループ */}
        <g
          transform={`translate(${transform.x},${transform.y}) scale(${transform.scale})`}
        >
          <TreeRenderer tree={tree} selectedNodeId={selectedNodeId} />
        </g>
      </svg>

      {/* ミニマップ — キャンバス右下に重ねて表示 */}
      <Minimap tree={tree} transform={transform} />
    </div>
  );
}
