/**
 * ドラッグ＆ドロップフック
 *
 * ビジュアルエディタ上のノード移動とパレットからのノード追加を管理する。
 *
 * ドラッグ操作:
 * 1. ノードをドラッグ開始 → isDragging = true
 * 2. 別の親ノード上にドロップ → moveNode メッセージ送信
 * 3. LSP Server がテキスト編集を返却 → テキスト更新 → treeLayout 再計算
 *
 * バリデーション:
 * - ルートノードはドラッグ不可
 * - 自分自身の子孫にドロップ不可（循環参照防止）
 */
import { useState, useCallback, useRef } from "react";
import { useTreeStore } from "../state/treeStore";
import { sendMessage } from "./useVscodeApi";
import { isDescendantOf, findNodeAtPosition } from "../layout/treeLayout";

/**
 * ドラッグ＆ドロップフックの戻り値
 */
interface UseDragDropResult {
  /** ドラッグ中かどうか */
  isDragging: boolean;
  /** ドラッグ関連のイベントハンドラ群 */
  dragHandlers: {
    onMouseDown: (e: React.MouseEvent) => void;
    onMouseMove: (e: React.MouseEvent) => void;
    onMouseUp: (e: React.MouseEvent) => void;
  };
}

/** ドラッグ操作の状態 */
interface DragState {
  /** ドラッグ中のノード ID */
  nodeId: number;
  /** ドラッグ開始時のマウス座標 */
  startX: number;
  startY: number;
  /** ドラッグしきい値を超えたか（微小な移動をクリックと区別） */
  hasMoved: boolean;
}

/** ドラッグ開始の移動しきい値（ピクセル） — これ未満はクリックとみなす */
const DRAG_THRESHOLD = 5;

/**
 * ノードのドラッグ＆ドロップを管理するカスタムフック
 *
 * @param nodeId 対象ノードの ID
 * @returns ドラッグ状態とイベントハンドラ
 */
export function useDragDrop(nodeId: number): UseDragDropResult {
  const [isDragging, setIsDragging] = useState(false);
  const dragState = useRef<DragState | null>(null);
  const tree = useTreeStore((s) => s.tree);
  const fileUri = useTreeStore((s) => s.fileUri);

  /**
   * ドラッグ開始
   *
   * マウスボタンが押された位置を記録する。
   * 実際のドラッグ状態は、マウスが DRAG_THRESHOLD 以上移動した時に開始する。
   */
  const onMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      dragState.current = {
        nodeId,
        startX: e.clientX,
        startY: e.clientY,
        hasMoved: false,
      };
    },
    [nodeId]
  );

  /**
   * ドラッグ中のマウス移動
   *
   * しきい値を超えた移動があれば、ドラッグ状態を有効にする。
   */
  const onMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (!dragState.current) return;

      const dx = e.clientX - dragState.current.startX;
      const dy = e.clientY - dragState.current.startY;

      if (!dragState.current.hasMoved) {
        if (Math.abs(dx) + Math.abs(dy) > DRAG_THRESHOLD) {
          dragState.current.hasMoved = true;
          setIsDragging(true);
        }
      }
    },
    []
  );

  /**
   * ドロップ処理
   *
   * ドラッグ中のノードをドロップ位置の親ノードに移動する。
   * ルートノードへのドロップや自分自身の子孫へのドロップは拒否する。
   */
  const onMouseUp = useCallback(
    (e: React.MouseEvent) => {
      if (!dragState.current?.hasMoved || !tree || !fileUri) {
        dragState.current = null;
        setIsDragging(false);
        return;
      }

      /*
       * ドロップ位置のキャンバス座標を計算し、ターゲットノードを特定する。
       * ツリーのルートノードはドロップターゲットとして使用可能だが、
       * ルートノード自体のドラッグは不可。
       */
      const canvasEl = e.currentTarget.closest(".crisp-canvas-svg");
      if (canvasEl) {
        const rect = canvasEl.getBoundingClientRect();
        const canvasX = e.clientX - rect.left;
        const canvasY = e.clientY - rect.top;

        const targetNode = findNodeAtPosition(tree.root, canvasX, canvasY);

        if (targetNode && targetNode.id !== nodeId) {
          /* 自分自身の子孫にドロップすることを防止する */
          if (!isDescendantOf(tree.root, targetNode.id, nodeId)) {
            sendMessage({
              type: "moveNode",
              payload: {
                uri: fileUri,
                nodeId: nodeId,
                newParentId: targetNode.id,
                newPosition: 0,
              },
            });
          }
        }
      }

      dragState.current = null;
      setIsDragging(false);
    },
    [nodeId, tree, fileUri]
  );

  return {
    isDragging,
    dragHandlers: { onMouseDown, onMouseMove, onMouseUp },
  };
}
