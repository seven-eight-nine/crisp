/**
 * ツールバーコンポーネント
 *
 * ビジュアルエディタ上部に表示されるツールバー。
 * ズーム操作、フィット、折りたたみ展開等の操作ボタンを提供する。
 *
 * ボタン:
 * - Zoom In / Zoom Out: ズームレベルの増減
 * - Fit: ツリー全体をビューにフィット
 * - Expand All / Collapse All: 全ノードの展開/折りたたみ
 */
import React from "react";
import { useTreeStore } from "../state/treeStore";
import { useDebugStore } from "../state/debugStore";

/**
 * ツールバーのプロパティ
 */
interface ToolbarProps {
  /** ツリーフィット実行コールバック */
  onFitToView?: () => void;
}

/**
 * ツールバーの描画コンポーネント
 */
export function Toolbar({ onFitToView }: ToolbarProps): React.JSX.Element {
  const tree = useTreeStore((s) => s.tree);
  const isDebugging = useDebugStore((s) => s.isDebugging);

  return (
    <div className="crisp-toolbar">
      {/* ツリー名の表示 */}
      {tree && (
        <span style={{ fontSize: 12, fontWeight: 600, marginRight: 8 }}>
          {tree.treeName}
        </span>
      )}

      <div className="crisp-toolbar-separator" />

      {/* フィットボタン */}
      <button
        className="crisp-toolbar-btn"
        onClick={onFitToView}
        title="ツリー全体をフィット (Ctrl+Shift+F)"
      >
        <FitIcon />
      </button>

      <div className="crisp-toolbar-separator" />

      {/* デバッグ状態インジケータ */}
      {isDebugging && (
        <span
          style={{
            fontSize: 11,
            color: "var(--vscode-debugIcon-startForeground)",
            display: "flex",
            alignItems: "center",
            gap: 4,
          }}
        >
          <DebugIcon /> Debugging
        </span>
      )}
    </div>
  );
}

/** フィットアイコン (SVG) */
function FitIcon(): React.JSX.Element {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
      <path d="M2 2h4v1H3v3H2V2zm8 0h4v4h-1V3h-3V2zM2 10h1v3h3v1H2v-4zm11 3h-3v1h4v-4h-1v3z" />
    </svg>
  );
}

/** デバッグアイコン (SVG) */
function DebugIcon(): React.JSX.Element {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
      <circle cx="8" cy="8" r="6" />
    </svg>
  );
}
