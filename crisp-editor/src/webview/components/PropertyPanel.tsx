/**
 * プロパティパネルコンポーネント
 *
 * 選択中のノードの詳細情報を画面右側に表示する。
 * ノードの種別、ラベル、型情報、ソース位置、診断情報を確認できる。
 *
 * 表示項目:
 * - ノードアイコンと種別名
 * - 表示ラベル（式のテキスト表現）
 * - 解決型（型推論結果）
 * - ソーステキスト上の位置
 * - 診断情報（エラー・警告）
 */
import React from "react";
import { useTreeStore } from "../state/treeStore";
import type { NodeType } from "../protocol/messages";

/**
 * プロパティパネルのプロパティ
 */
interface PropertyPanelProps {
  /** 表示対象のノード ID（null = 未選択） */
  nodeId: number | null;
}

/**
 * プロパティパネルの描画コンポーネント
 *
 * ノードが選択されていない場合は空状態メッセージを表示する。
 * 選択されている場合は、ノードの詳細情報をセクションごとに表示する。
 */
export function PropertyPanel({
  nodeId,
}: PropertyPanelProps): React.JSX.Element {
  const node = useTreeStore((s) => (nodeId ? s.findNode(nodeId) : null));

  /* アイコンの URI を解決する */
  const iconsBaseUri =
    typeof window !== "undefined"
      ? (window as Record<string, unknown>).__CRISP_ICONS_URI__ ?? ""
      : "";

  if (!node) {
    return (
      <div className="crisp-props-empty">ノードを選択してください</div>
    );
  }

  return (
    <div className="crisp-props">
      {/* ヘッダー: アイコンとノード種別名 */}
      <div className="crisp-props-header">
        <img
          src={`${iconsBaseUri}/${getNodeIcon(node.type)}`}
          alt={node.type}
        />
        <span>{node.type}</span>
      </div>

      {/* ラベル: 表示テキスト（式のテキスト表現） */}
      <div className="crisp-props-section">
        <label>Label</label>
        <code>{node.label}</code>
      </div>

      {/* 解決型: 型推論結果（存在する場合のみ表示） */}
      {node.resolvedType && (
        <div className="crisp-props-section">
          <label>Type</label>
          <code>{node.resolvedType}</code>
        </div>
      )}

      {/* ソース位置: テキスト上の開始位置と長さ */}
      {node.origin && (
        <div className="crisp-props-section">
          <label>Source</label>
          <code>
            offset {node.origin.start}..
            {node.origin.start + node.origin.length}
          </code>
        </div>
      )}

      {/* ノード ID: デバッグ情報 */}
      <div className="crisp-props-section">
        <label>Node ID</label>
        <code>{node.id}</code>
      </div>

      {/* 診断情報: エラー・警告の一覧（存在する場合のみ表示） */}
      {node.diagnostics.length > 0 && (
        <div className="crisp-props-section">
          <label>Diagnostics</label>
          {node.diagnostics.map((d, i) => (
            <div
              key={`${d.code}-${i}`}
              className={`crisp-diag crisp-diag--${d.severity}`}
            >
              <span className="crisp-diag-code">{d.code}</span>
              <span>{d.message}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * ノード種別に対応するアイコンファイル名を取得する
 *
 * @param nodeType ノード種別
 * @returns アイコンファイル名
 */
function getNodeIcon(nodeType: NodeType): string {
  const iconMap: Record<string, string> = {
    selector: "selector.svg",
    sequence: "sequence.svg",
    parallel: "parallel.svg",
    check: "check.svg",
    action: "action.svg",
    guard: "guard.svg",
    if: "if.svg",
    invert: "invert.svg",
    repeat: "repeat.svg",
    timeout: "timeout.svg",
    cooldown: "cooldown.svg",
    while: "while.svg",
    reactive: "reactive.svg",
    "reactive-select": "reactive-select.svg",
    ref: "selector.svg",
    "defdec-call": "guard.svg",
    unknown: "action.svg",
  };
  return iconMap[nodeType] ?? "action.svg";
}
