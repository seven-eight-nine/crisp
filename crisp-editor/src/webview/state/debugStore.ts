/**
 * デバッグ状態管理ストア (Zustand)
 *
 * F7 デバッガ統合で使用する実行状態の管理。
 * ゲームランタイムからのデバッグ状態更新を保持し、
 * ノードコンポーネントの色やアニメーションに反映する。
 *
 * デバッグセッション中は、ランタイムが WebSocket 経由で
 * node_enter / node_exit イベントを送信し、
 * それが DebugTreeState としてこのストアに格納される。
 */
import { create } from "zustand";
import type { DebugTreeState, NodeDebugInfo } from "../protocol/messages";

/**
 * デバッグストアの状態型定義
 */
interface DebugState {
  /** デバッグセッション中かどうか */
  isDebugging: boolean;
  /** 現在のデバッグツリー状態 */
  debugState: DebugTreeState | null;

  /** デバッグセッション開始 */
  startDebugging: () => void;
  /** デバッグセッション終了 */
  stopDebugging: () => void;
  /** デバッグ状態を更新する（ランタイムからの通知で呼び出し） */
  updateDebugState: (state: DebugTreeState) => void;
  /** ノードのデバッグ情報を取得する */
  getNodeDebugInfo: (nodeId: number) => NodeDebugInfo | null;
}

/**
 * デバッグ状態管理ストアの生成
 */
export const useDebugStore = create<DebugState>((set, get) => ({
  isDebugging: false,
  debugState: null,

  startDebugging: () =>
    set({ isDebugging: true }),

  stopDebugging: () =>
    set({ isDebugging: false, debugState: null }),

  updateDebugState: (state) =>
    set({ debugState: state }),

  getNodeDebugInfo: (nodeId) => {
    const { debugState } = get();
    if (!debugState) return null;
    return debugState.nodes[nodeId] ?? null;
  },
}));
