/**
 * ツリーレンダラコンポーネント
 *
 * ツリーレイアウト全体を描画する SVG グループコンポーネント。
 * エッジ（接続線）を背景レイヤー、ノードを前景レイヤーとして描画し、
 * ノードが常にエッジの上に表示されるようにする。
 *
 * 折りたたまれたノードの子は描画をスキップする。
 * 選択状態、カーソル対応、診断レベル、デバッグ状態を各ノードに伝搬する。
 */
import React from "react";
import { NodeComponent } from "./NodeComponent";
import { EdgeComponent } from "./EdgeComponent";
import { useTreeLayout } from "../hooks/useTreeLayout";
import { useTreeStore } from "../state/treeStore";
import { useDebugStore } from "../state/debugStore";
import type { TreeLayout, TreeNode } from "../protocol/messages";

/**
 * ツリーレンダラのプロパティ
 */
interface TreeRendererProps {
  /** 描画対象のツリーレイアウト */
  tree: TreeLayout;
  /** 選択中のノード ID */
  selectedNodeId: number | null;
}

/**
 * ツリー全体を描画するコンポーネント
 *
 * 再帰的にノードを走査し、折りたたみ状態を考慮しながら
 * エッジとノードを描画する。
 */
export function TreeRenderer({
  tree,
  selectedNodeId,
}: TreeRendererProps): React.JSX.Element {
  const { edgeInfos } = useTreeLayout(tree);
  const cursorNodeId = useTreeStore((s) => s.cursorNodeId);
  const collapsedNodeIds = useTreeStore((s) => s.collapsedNodeIds);
  const getNodeDebugInfo = useDebugStore((s) => s.getNodeDebugInfo);

  /**
   * ノードの診断レベルを判定する
   *
   * ノードに付与された診断情報のうち最も重大なレベルを返す。
   */
  function getDiagnosticLevel(
    node: TreeNode
  ): "none" | "warning" | "error" {
    if (!node.diagnostics || node.diagnostics.length === 0) return "none";
    if (node.diagnostics.some((d) => d.severity === "error")) return "error";
    if (node.diagnostics.some((d) => d.severity === "warning"))
      return "warning";
    return "none";
  }

  /**
   * ノードのデバッグ状態を取得する
   */
  function getDebugStatus(
    nodeId: number
  ): "success" | "failure" | "running" | "idle" | undefined {
    const info = getNodeDebugInfo(nodeId);
    if (!info) return undefined;
    return info.status === "evaluating" ? "running" : info.status;
  }

  /**
   * ノードを再帰的に収集する（折りたたみを考慮）
   *
   * 折りたたまれたノードの子は収集しない。
   */
  function collectVisibleNodes(node: TreeNode): TreeNode[] {
    const result: TreeNode[] = [node];
    if (!collapsedNodeIds.has(node.id) && node.children) {
      for (const child of node.children) {
        result.push(...collectVisibleNodes(child));
      }
    }
    return result;
  }

  const visibleNodes = collectVisibleNodes(tree.root);

  /* 可視ノードの ID セットを作成し、エッジのフィルタリングに使用する */
  const visibleNodeIds = new Set(visibleNodes.map((n) => n.id));
  const visibleEdges = edgeInfos.filter(
    (e) =>
      visibleNodeIds.has(e.edge.fromId) && visibleNodeIds.has(e.edge.toId)
  );

  return (
    <g>
      {/* エッジレイヤー（背景） — ノードの下に描画されるようにする */}
      <g className="crisp-edges-layer">
        {visibleEdges.map((edgeInfo) => (
          <EdgeComponent key={edgeInfo.key} edgeInfo={edgeInfo} />
        ))}
      </g>

      {/* ノードレイヤー（前景） — エッジの上に描画されるようにする */}
      <g className="crisp-nodes-layer">
        {visibleNodes.map((node) => (
          <NodeComponent
            key={node.id}
            node={node}
            x={node.x}
            y={node.y}
            isSelected={node.id === selectedNodeId}
            isCursorTarget={node.id === cursorNodeId}
            diagnosticLevel={getDiagnosticLevel(node)}
            debugStatus={getDebugStatus(node.id)}
          />
        ))}
      </g>
    </g>
  );
}
