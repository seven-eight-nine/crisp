/**
 * ルートコンポーネント
 *
 * Crisp ビジュアルエディタの最上位 React コンポーネント。
 * ツールバー、ノードパレット、ツリーキャンバス、プロパティパネルを
 * 統合レイアウトで配置する。
 *
 * レイアウト:
 * ┌─ Toolbar ──────────────────────────────────┐
 * ├─ Palette ─┬─ Canvas ──────┬─ Properties ──┤
 * │           │               │               │
 * │  selector │  ┌────────┐   │  Node: check  │
 * │  sequence │  │ select │   │  Type: bool   │
 * │  parallel │  └───┬────┘   │               │
 * │  ...      │      │        │               │
 * └───────────┴──────┴────────┴───────────────┘
 *
 * コンテキストメニュー:
 * ノードを右クリックすると、Zustand ストアの contextMenu 状態が更新され、
 * ContextMenu コンポーネントが表示される。メニュー外のクリックや
 * Escape キーで閉じる。
 *
 * Extension Host との通信は useVscodeMessages フックで初期化する。
 */
import React from "react";
import { useTreeStore } from "./state/treeStore";
import { useVscodeMessages } from "./hooks/useVscodeApi";
import { TreeCanvas } from "./components/TreeCanvas";
import { NodePalette } from "./components/NodePalette";
import { PropertyPanel } from "./components/PropertyPanel";
import { Toolbar } from "./components/Toolbar";
import { ContextMenu } from "./components/ContextMenu";

/**
 * アプリケーションルートコンポーネント
 *
 * Zustand ストアからツリーレイアウトと選択状態を購読し、
 * 各子コンポーネントに渡す。
 * コンテキストメニューの状態もストアから購読し、
 * 表示・非表示を制御する。
 */
export function App(): React.JSX.Element {
  /* Extension Host との通信を初期化する */
  useVscodeMessages();

  const tree = useTreeStore((s) => s.tree);
  const selectedNodeId = useTreeStore((s) => s.selectedNodeId);
  const contextMenu = useTreeStore((s) => s.contextMenu);
  const closeContextMenu = useTreeStore((s) => s.closeContextMenu);

  return (
    <div className="crisp-editor">
      {/* ツールバー — ズーム操作、フィット、デバッグ状態表示 */}
      <Toolbar onFitToView={() => useTreeStore.getState().requestFitToView()} />

      {/* メインレイアウト — パレット | キャンバス | プロパティパネル */}
      <div className="crisp-editor-main">
        {/* ノードパレット — ドラッグ可能なノード種別一覧 */}
        <NodePalette />

        {/* ツリーキャンバス — メイン描画領域（パン・ズーム対応） */}
        <TreeCanvas tree={tree} selectedNodeId={selectedNodeId} />

        {/* プロパティパネル — 選択ノードの詳細情報 */}
        <PropertyPanel nodeId={selectedNodeId} />
      </div>

      {/* コンテキストメニュー — ノード右クリック時にストアの状態に応じて表示 */}
      {contextMenu && (
        <ContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          nodeId={contextMenu.nodeId}
          onClose={closeContextMenu}
        />
      )}
    </div>
  );
}
