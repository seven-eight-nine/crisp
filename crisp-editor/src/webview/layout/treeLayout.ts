/**
 * クライアント側ツリーレイアウト補助
 *
 * LSP Server から受信したレイアウト座標を基に、
 * WebView 側で必要な追加計算（エッジのベジェカーブ座標、
 * ノード検索、全体フィット計算等）を行う。
 *
 * メインのレイアウト計算は LSP Server (TreeLayoutBuilder) が担当する。
 * このモジュールは描画に必要な補助計算のみを提供する。
 */
import type { TreeNode, TreeLayout, TreeBounds } from "../protocol/messages";
import { EDGE, ZOOM_PAN } from "./constants";

/**
 * ベジェカーブのパスデータを生成する
 *
 * 親ノードの下端から子ノードの上端へ、なめらかな曲線で接続するための
 * SVG パスデータ (d 属性) を計算する。
 *
 * @param fromX 親ノードの中心 X 座標
 * @param fromY 親ノードの下端 Y 座標
 * @param toX 子ノードの中心 X 座標
 * @param toY 子ノードの上端 Y 座標
 * @returns SVG パスの d 属性文字列
 */
export function computeEdgePath(
  fromX: number,
  fromY: number,
  toX: number,
  toY: number
): string {
  const dy = toY - fromY;
  const cpOffset = dy * EDGE.BEZIER_OFFSET_RATIO;

  return `M ${fromX},${fromY} C ${fromX},${fromY + cpOffset} ${toX},${toY - cpOffset} ${toX},${toY}`;
}

/**
 * ツリー全体をビューに収めるためのトランスフォームを計算する
 *
 * キャンバスのサイズとツリーの境界ボックスから、
 * 最適なズームレベルと平行移動量を算出する。
 *
 * @param bounds ツリー全体の境界ボックス
 * @param canvasWidth キャンバスの幅
 * @param canvasHeight キャンバスの高さ
 * @returns 計算されたトランスフォーム { x, y, scale }
 */
export function computeFitTransform(
  bounds: TreeBounds,
  canvasWidth: number,
  canvasHeight: number
): { x: number; y: number; scale: number } {
  const padding = ZOOM_PAN.FIT_PADDING;
  const availableWidth = canvasWidth - padding * 2;
  const availableHeight = canvasHeight - padding * 2;

  if (bounds.width === 0 || bounds.height === 0) {
    return { x: canvasWidth / 2, y: padding, scale: ZOOM_PAN.DEFAULT_SCALE };
  }

  const scaleX = availableWidth / bounds.width;
  const scaleY = availableHeight / bounds.height;
  const scale = Math.min(
    scaleX,
    scaleY,
    ZOOM_PAN.MAX_SCALE
  );

  const clampedScale = Math.max(scale, ZOOM_PAN.MIN_SCALE);

  /* ツリーをキャンバスの中央に配置する */
  const x = (canvasWidth - bounds.width * clampedScale) / 2;
  const y = (canvasHeight - bounds.height * clampedScale) / 2;

  return { x, y, scale: clampedScale };
}

/**
 * ノード ID でツリーノードを検索する
 *
 * ツリーを深さ優先で走査し、指定された ID を持つノードを返す。
 *
 * @param root ツリーのルートノード
 * @param nodeId 検索するノード ID
 * @returns 見つかったノード、存在しなければ null
 */
export function findNodeById(
  root: TreeNode,
  nodeId: number
): TreeNode | null {
  if (root.id === nodeId) return root;
  if (root.children) {
    for (const child of root.children) {
      const found = findNodeById(child, nodeId);
      if (found) return found;
    }
  }
  return null;
}

/**
 * キャンバス座標からノードを検索する
 *
 * クリックやドロップ位置のキャンバス座標から、
 * その位置に存在するノードを特定する。
 *
 * @param root ツリーのルートノード
 * @param canvasX キャンバス X 座標
 * @param canvasY キャンバス Y 座標
 * @returns 見つかったノード、存在しなければ null
 */
export function findNodeAtPosition(
  root: TreeNode,
  canvasX: number,
  canvasY: number
): TreeNode | null {
  function walk(node: TreeNode): TreeNode | null {
    const left = node.x - node.width / 2;
    const right = left + node.width;
    const top = node.y;
    const bottom = top + node.height;

    if (canvasX >= left && canvasX <= right && canvasY >= top && canvasY <= bottom) {
      /* 子を先に探索（手前のノードを優先） */
      if (node.children) {
        for (const child of node.children) {
          const found = walk(child);
          if (found) return found;
        }
      }
      return node;
    }

    /* ノードの外でも子の範囲内にある可能性がある */
    if (node.children) {
      for (const child of node.children) {
        const found = walk(child);
        if (found) return found;
      }
    }

    return null;
  }

  return walk(root);
}

/**
 * ノードが別のノードの子孫かどうかを判定する
 *
 * ドラッグ＆ドロップ時に、ノードを自分自身の子孫にドロップすることを防止する。
 *
 * @param root ツリーのルートノード
 * @param nodeId 判定対象のノード ID
 * @param ancestorId 祖先候補のノード ID
 * @returns nodeId が ancestorId の子孫であれば true
 */
export function isDescendantOf(
  root: TreeNode,
  nodeId: number,
  ancestorId: number
): boolean {
  const ancestor = findNodeById(root, ancestorId);
  if (!ancestor) return false;

  function walk(node: TreeNode): boolean {
    if (node.id === nodeId) return true;
    if (node.children) {
      for (const child of node.children) {
        if (walk(child)) return true;
      }
    }
    return false;
  }

  /* ancestor 自体は除外し、その子以下を探索する */
  if (ancestor.children) {
    for (const child of ancestor.children) {
      if (walk(child)) return true;
    }
  }

  return false;
}

/**
 * ツリーの全ノードをフラットなリストとして収集する
 *
 * ミニマップやノード数の表示等で使用する。
 *
 * @param root ツリーのルートノード
 * @returns 全ノードのフラットリスト
 */
export function flattenTree(root: TreeNode): TreeNode[] {
  const nodes: TreeNode[] = [];
  function walk(node: TreeNode): void {
    nodes.push(node);
    if (node.children) {
      for (const child of node.children) {
        walk(child);
      }
    }
  }
  walk(root);
  return nodes;
}
