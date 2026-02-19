/**
 * VSCode API ブリッジフック
 *
 * Extension Host からのメッセージ受信と、
 * Extension Host へのメッセージ送信を React コンポーネントから
 * 利用するためのカスタムフック。
 *
 * useEffect で window の message イベントをリスンし、
 * メッセージの種類に応じて Zustand ストアを更新する。
 */
import { useEffect } from "react";
import { onMessageFromExtension, postMessageToExtension } from "../protocol/bridge";
import { useTreeStore } from "../state/treeStore";
import { useDebugStore } from "../state/debugStore";
import type { WebViewToExtensionMessage } from "../protocol/messages";

/**
 * Extension Host との通信を初期化するフック
 *
 * コンポーネントのマウント時に以下のメッセージリスナーを登録する:
 * - treeLayout: ツリーレイアウトの更新
 * - diagnostics: 診断情報の更新（将来拡張）
 * - cursorSync: テキストカーソル位置のノード同期
 * - debugState: デバッグ状態の更新 (F7)
 *
 * アンマウント時にリスナーを自動解除する。
 */
export function useVscodeMessages(): void {
  const setTree = useTreeStore((s) => s.setTree);
  const setCursorNode = useTreeStore((s) => s.setCursorNode);
  const requestFitToView = useTreeStore((s) => s.requestFitToView);
  const updateDebugState = useDebugStore((s) => s.updateDebugState);

  useEffect(() => {
    const unsubscribe = onMessageFromExtension((msg) => {
      switch (msg.type) {
        case "treeLayout":
          setTree(msg.payload);
          break;
        case "cursorSync":
          setCursorNode(msg.payload.nodeId);
          break;
        case "debugState":
          updateDebugState(msg.payload);
          break;
        case "fitToView":
          requestFitToView();
          break;
        case "diagnostics":
          /* 診断情報は treeLayout に含まれるため、追加処理は不要 */
          break;
      }
    });

    return unsubscribe;
  }, [setTree, setCursorNode, requestFitToView, updateDebugState]);
}

/**
 * Extension Host にメッセージを送信するヘルパー
 *
 * WebView のコンポーネントから直接呼び出して使用する。
 * ブリッジモジュールの postMessageToExtension を re-export する。
 *
 * @param message 送信するメッセージ
 */
export function sendMessage(message: WebViewToExtensionMessage): void {
  postMessageToExtension(message);
}
