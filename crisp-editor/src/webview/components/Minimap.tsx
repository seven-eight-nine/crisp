/**
 * ミニマップコンポーネント
 *
 * ツリー全体の俯瞰表示をキャンバス右下に表示する。
 * 現在のビューポート範囲を矩形で示し、
 * ユーザーがツリーのどの部分を見ているかを把握できるようにする。
 *
 * ミニマップは読み取り専用（pointer-events: none）で、
 * キャンバスのパン・ズーム操作に追従して自動更新される。
 */
import React, { useMemo } from "react";
import { MINIMAP } from "../layout/constants";
import { flattenTree } from "../layout/treeLayout";
import type { TreeLayout } from "../protocol/messages";
import type { Transform } from "../hooks/useZoomPan";

/**
 * ミニマップのプロパティ
 */
interface MinimapProps {
  /** 描画対象のツリーレイアウト */
  tree: TreeLayout;
  /** 現在のキャンバストランスフォーム（パン・ズーム状態） */
  transform: Transform;
}

/**
 * ミニマップの描画コンポーネント
 *
 * ツリーの全ノードを縮小して描画し、
 * 現在のビューポートを青い矩形で重ねて表示する。
 */
export function Minimap({ tree, transform }: MinimapProps): React.JSX.Element {
  /* ツリーの全ノードをフラットリストとして取得し、ミニマップ用の縮尺を計算する */
  const { nodes, scale, offsetX, offsetY } = useMemo(() => {
    const allNodes = flattenTree(tree.root);
    const bounds = tree.bounds;

    if (bounds.width === 0 || bounds.height === 0) {
      return { nodes: allNodes, scale: 1, offsetX: 0, offsetY: 0 };
    }

    /* ミニマップ内にツリー全体が収まるように縮尺を計算する */
    const padding = 8;
    const availableW = MINIMAP.WIDTH - padding * 2;
    const availableH = MINIMAP.HEIGHT - padding * 2;
    const scaleX = availableW / bounds.width;
    const scaleY = availableH / bounds.height;
    const mapScale = Math.min(scaleX, scaleY);

    return {
      nodes: allNodes,
      scale: mapScale,
      offsetX: padding + (availableW - bounds.width * mapScale) / 2,
      offsetY: padding + (availableH - bounds.height * mapScale) / 2,
    };
  }, [tree]);

  return (
    <div className="crisp-minimap">
      <svg width={MINIMAP.WIDTH} height={MINIMAP.HEIGHT}>
        {/* ツリーノードを小さな矩形として描画する */}
        {nodes.map((node) => (
          <rect
            key={node.id}
            x={offsetX + (node.x - node.width / 2) * scale}
            y={offsetY + node.y * scale}
            width={node.width * scale}
            height={node.height * scale}
            fill="var(--vscode-editor-foreground)"
            opacity={0.3}
            rx={1}
          />
        ))}

        {/* ビューポート表示矩形 — 現在の表示範囲を示す */}
        <rect
          className="crisp-minimap-viewport"
          x={offsetX - transform.x * scale / transform.scale}
          y={offsetY - transform.y * scale / transform.scale}
          width={MINIMAP.WIDTH * scale / transform.scale}
          height={MINIMAP.HEIGHT * scale / transform.scale}
        />
      </svg>
    </div>
  );
}
