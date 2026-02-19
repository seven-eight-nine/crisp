/**
 * ツリーレイアウトフック
 *
 * LSP サーバーから受信したレイアウト座標を基に、
 * エッジのパスデータやフィットトランスフォームを計算する。
 *
 * レイアウト計算自体は LSP Server (TreeLayoutBuilder) が担当し、
 * このフックは描画に必要な追加情報を提供する補助的な役割。
 */
import { useMemo } from "react";
import type { TreeLayout, TreeNode, TreeEdge } from "../protocol/messages";
import { computeEdgePath } from "../layout/treeLayout";
import { LAYOUT } from "../layout/constants";
import { findNodeById } from "../layout/treeLayout";

/**
 * エッジの描画情報
 */
export interface EdgeRenderInfo {
  /** エッジの識別キー */
  key: string;
  /** SVG パスの d 属性文字列 */
  pathData: string;
  /** 元のエッジ情報 */
  edge: TreeEdge;
}

/**
 * ツリーレイアウトの描画情報を計算するフック
 *
 * ツリーレイアウトが更新されるたびに、エッジのベジェカーブ座標を
 * メモ化して再計算する。
 *
 * @param tree 現在のツリーレイアウト（null = 未受信）
 * @returns エッジの描画情報リスト
 */
export function useTreeLayout(tree: TreeLayout | null): {
  edgeInfos: EdgeRenderInfo[];
} {
  const edgeInfos = useMemo(() => {
    if (!tree) return [];

    return tree.edges.map((edge): EdgeRenderInfo => {
      const fromNode = findNodeById(tree.root, edge.fromId);
      const toNode = findNodeById(tree.root, edge.toId);

      if (!fromNode || !toNode) {
        return {
          key: `${edge.fromId}-${edge.toId}`,
          pathData: "",
          edge,
        };
      }

      /* 親ノードの下端中央 → 子ノードの上端中央 */
      const fromX = fromNode.x;
      const fromY = fromNode.y + fromNode.height;
      const toX = toNode.x;
      const toY = toNode.y;

      return {
        key: `${edge.fromId}-${edge.toId}`,
        pathData: computeEdgePath(fromX, fromY, toX, toY),
        edge,
      };
    });
  }, [tree]);

  return { edgeInfos };
}
