/**
 * ツリー状態管理ストア (Zustand)
 *
 * ビジュアルエディタの中心的な状態管理。
 * LSP からのツリーレイアウト更新、ノード選択、カーソル同期、
 * 折りたたみ状態等を一元管理する。
 *
 * Zustand を採用する理由:
 * - React の Context API より軽量で高速
 * - セレクタベースの購読で不要な再レンダリングを防止
 * - ミドルウェア不要でシンプルなコード
 */
import { create } from "zustand";
import type {
  TreeLayout,
  TreeNode,
} from "../protocol/messages";
import { findNodeById } from "../layout/treeLayout";

/**
 * コンテキストメニューの表示状態
 */
export interface ContextMenuState {
  /** メニュー表示位置の X 座標（ビューポート座標） */
  x: number;
  /** メニュー表示位置の Y 座標（ビューポート座標） */
  y: number;
  /** 対象ノードの ID */
  nodeId: number;
}

/**
 * ツリーストアの状態型定義
 */
interface TreeState {
  /** 現在のツリーレイアウト（null = 未受信） */
  tree: TreeLayout | null;
  /** 選択中のノード ID（null = 未選択） */
  selectedNodeId: number | null;
  /** テキストカーソルに対応するノード ID（null = 対応なし） */
  cursorNodeId: number | null;
  /** 折りたたまれているノード ID のセット */
  collapsedNodeIds: Set<number>;
  /** 対象ファイルの URI */
  fileUri: string | null;
  /** fitToView リクエストのカウンタ（変化を検知してフィットを実行する） */
  fitToViewCounter: number;
  /** コンテキストメニューの表示状態（null = 非表示） */
  contextMenu: ContextMenuState | null;

  /** ツリーレイアウトを更新する（LSP の treeLayout 通知から呼び出し） */
  setTree: (layout: TreeLayout) => void;
  /** ノードを選択する */
  selectNode: (nodeId: number | null) => void;
  /** テキストカーソル対応ノードを更新する */
  setCursorNode: (nodeId: number | null) => void;
  /** ノードの折りたたみ状態を切り替える */
  toggleCollapse: (nodeId: number) => void;
  /** ID でノードを検索する */
  findNode: (nodeId: number) => TreeNode | null;
  /** fitToView をリクエストする（Extension の fitTree コマンドから呼び出し） */
  requestFitToView: () => void;
  /** コンテキストメニューを表示する（ノード右クリック時） */
  openContextMenu: (x: number, y: number, nodeId: number) => void;
  /** コンテキストメニューを閉じる */
  closeContextMenu: () => void;
}

/**
 * ツリー状態管理ストアの生成
 *
 * create() で Zustand ストアを構築する。
 * コンポーネントは useTreeStore(selector) で必要な状態のみ購読し、
 * 不要な再レンダリングを防止する。
 */
export const useTreeStore = create<TreeState>((set, get) => ({
  tree: null,
  selectedNodeId: null,
  cursorNodeId: null,
  collapsedNodeIds: new Set(),
  fileUri: null,
  fitToViewCounter: 0,
  contextMenu: null,

  setTree: (layout) =>
    set({
      tree: layout,
      fileUri: layout.fileUri,
    }),

  selectNode: (nodeId) =>
    set({ selectedNodeId: nodeId }),

  setCursorNode: (nodeId) =>
    set({ cursorNodeId: nodeId }),

  toggleCollapse: (nodeId) =>
    set((state) => {
      const newSet = new Set(state.collapsedNodeIds);
      if (newSet.has(nodeId)) {
        newSet.delete(nodeId);
      } else {
        newSet.add(nodeId);
      }
      return { collapsedNodeIds: newSet };
    }),

  findNode: (nodeId) => {
    const { tree } = get();
    if (!tree) return null;
    return findNodeById(tree.root, nodeId);
  },

  requestFitToView: () =>
    set((state) => ({ fitToViewCounter: state.fitToViewCounter + 1 })),

  openContextMenu: (x, y, nodeId) =>
    set({ contextMenu: { x, y, nodeId } }),

  closeContextMenu: () =>
    set({ contextMenu: null }),
}));
