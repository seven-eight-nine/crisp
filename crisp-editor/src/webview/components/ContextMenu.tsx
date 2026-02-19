/**
 * コンテキストメニューコンポーネント
 *
 * ノードを右クリックした時に表示されるメニュー。
 * ノード操作（削除、デコレータラップ/アンラップ、ブレークポイント等）を提供する。
 *
 * メニュー項目:
 * - Delete: ノードの削除
 * - Wrap with...: デコレータでノードを包む（サブメニュー）
 * - Unwrap: デコレータを解除して子ノードを昇格
 * - Toggle Breakpoint: ブレークポイントの設定/解除 (F7)
 * - Duplicate: ノードの複製（将来拡張）
 */
import React, { useEffect, useCallback } from "react";
import { useTreeStore } from "../state/treeStore";
import { sendMessage } from "../hooks/useVscodeApi";
import { NODE_TYPE_INFO } from "../../shared/nodeTypes";

/**
 * コンテキストメニューのプロパティ
 */
interface ContextMenuProps {
  /** メニュー表示位置の X 座標 */
  x: number;
  /** メニュー表示位置の Y 座標 */
  y: number;
  /** 対象ノードの ID */
  nodeId: number;
  /** メニューを閉じるコールバック */
  onClose: () => void;
}

/** デコレータとしてラップ可能なノード種別 */
const WRAPPABLE_DECORATORS = [
  { type: "guard", label: "Guard" },
  { type: "invert", label: "Invert" },
  { type: "repeat", label: "Repeat" },
  { type: "timeout", label: "Timeout" },
  { type: "cooldown", label: "Cooldown" },
  { type: "while", label: "While" },
] as const;

/**
 * コンテキストメニューの描画コンポーネント
 *
 * メニュー外のクリックや Escape キーでメニューを閉じる。
 */
export function ContextMenu({
  x,
  y,
  nodeId,
  onClose,
}: ContextMenuProps): React.JSX.Element {
  const fileUri = useTreeStore((s) => s.fileUri);
  const node = useTreeStore((s) => s.findNode(nodeId));

  /* メニュー外のクリックでメニューを閉じる */
  useEffect(() => {
    const handleClickOutside = () => onClose();
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };

    /* 次のイベントループで登録し、メニュー表示時のクリックで閉じないようにする */
    const timer = setTimeout(() => {
      document.addEventListener("click", handleClickOutside);
      document.addEventListener("keydown", handleEscape);
    }, 0);

    return () => {
      clearTimeout(timer);
      document.removeEventListener("click", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [onClose]);

  /** ノード削除 */
  const handleDelete = useCallback(() => {
    if (!fileUri) return;
    sendMessage({
      type: "removeNode",
      payload: { uri: fileUri, nodeId },
    });
    onClose();
  }, [fileUri, nodeId, onClose]);

  /** デコレータでラップ */
  const handleWrap = useCallback(
    (decoratorType: string) => {
      if (!fileUri) return;
      sendMessage({
        type: "wrapNode",
        payload: { uri: fileUri, nodeId, decoratorType },
      });
      onClose();
    },
    [fileUri, nodeId, onClose]
  );

  /** デコレータのアンラップ */
  const handleUnwrap = useCallback(() => {
    if (!fileUri) return;
    sendMessage({
      type: "unwrapNode",
      payload: { uri: fileUri, nodeId },
    });
    onClose();
  }, [fileUri, nodeId, onClose]);

  /** ブレークポイント切替 */
  const handleToggleBreakpoint = useCallback(() => {
    if (!fileUri) return;
    sendMessage({
      type: "toggleBreakpoint",
      payload: { uri: fileUri, nodeId },
    });
    onClose();
  }, [fileUri, nodeId, onClose]);

  /* ノードの種別に応じてアンラップ可能かどうかを判定する */
  const isDecorator = node
    ? NODE_TYPE_INFO[node.type]?.category === "decorator"
    : false;

  return (
    <div
      className="crisp-context-menu"
      style={{ left: x, top: y }}
      onClick={(e) => e.stopPropagation()}
    >
      {/* ノード削除 */}
      <div className="crisp-context-menu-item" onClick={handleDelete}>
        Delete
      </div>

      <div className="crisp-context-menu-separator" />

      {/* Wrap with... サブメニュー */}
      {WRAPPABLE_DECORATORS.map((dec) => (
        <div
          key={dec.type}
          className="crisp-context-menu-item"
          onClick={() => handleWrap(dec.type)}
        >
          Wrap with {dec.label}
        </div>
      ))}

      {/* Unwrap（デコレータノードの場合のみ表示） */}
      {isDecorator && (
        <>
          <div className="crisp-context-menu-separator" />
          <div className="crisp-context-menu-item" onClick={handleUnwrap}>
            Unwrap
          </div>
        </>
      )}

      <div className="crisp-context-menu-separator" />

      {/* ブレークポイント切替 (F7) */}
      <div
        className="crisp-context-menu-item"
        onClick={handleToggleBreakpoint}
      >
        Toggle Breakpoint
      </div>
    </div>
  );
}
