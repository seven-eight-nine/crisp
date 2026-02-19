/**
 * ノード選択管理フック
 *
 * ビジュアルエディタ上のノード選択を管理する。
 * シングルクリックでノード選択、ダブルクリックでテキストエディタへのジャンプ。
 *
 * 操作:
 * - シングルクリック: ノード選択、プロパティパネル表示
 * - ダブルクリック: テキストエディタの対応行にジャンプ
 * - Ctrl+Click: 複数選択（将来拡張）
 */
import { useCallback } from "react";
import { useTreeStore } from "../state/treeStore";
import { sendMessage } from "./useVscodeApi";

/**
 * ノード選択フックの戻り値
 */
interface UseSelectionResult {
  /** シングルクリックハンドラ — ノード選択 */
  onClick: (e: React.MouseEvent) => void;
  /** ダブルクリックハンドラ — テキストジャンプ */
  onDoubleClick: (e: React.MouseEvent) => void;
}

/**
 * ノード選択を管理するカスタムフック
 *
 * @param nodeId 対象ノードの ID
 * @returns クリックイベントハンドラ
 */
export function useSelection(nodeId: number): UseSelectionResult {
  const selectNode = useTreeStore((s) => s.selectNode);
  const fileUri = useTreeStore((s) => s.fileUri);

  /**
   * シングルクリック — ノード選択
   *
   * クリックされたノードを選択状態にし、プロパティパネルに情報を表示する。
   * イベントの伝搬を停止し、キャンバスのパンが誤って開始されるのを防ぐ。
   */
  const onClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      selectNode(nodeId);
    },
    [nodeId, selectNode]
  );

  /**
   * ダブルクリック — テキストエディタへのジャンプ
   *
   * Extension Host に nodeClicked メッセージを送信し、
   * テキストエディタのカーソルをこのノードの対応位置に移動させる。
   */
  const onDoubleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      if (fileUri) {
        sendMessage({
          type: "nodeClicked",
          payload: { uri: fileUri, nodeId },
        });
      }
    },
    [nodeId, fileUri]
  );

  return { onClick, onDoubleClick };
}
