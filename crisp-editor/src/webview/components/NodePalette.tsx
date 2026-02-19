/**
 * ノードパレットコンポーネント
 *
 * 画面左側に表示される、ドラッグ可能なノード種別一覧。
 * カテゴリ（Composites / Decorators / Leaves）ごとにグループ化して表示する。
 *
 * パレットからキャンバスへのドラッグ時のフロー:
 * 1. パレットアイテムをドラッグ開始
 * 2. キャンバス上のドロップ位置に最も近い親ノードを特定
 * 3. addNode メッセージを Extension Host に送信
 * 4. LSP Server がテンプレート CST 生成 → テキスト編集 → treeLayout 更新
 */
import React, { useCallback } from "react";
import { useTreeStore } from "../state/treeStore";
import { sendMessage } from "../hooks/useVscodeApi";

/**
 * パレットアイテムの定義
 */
interface PaletteItem {
  /** ノード種別 */
  readonly type: string;
  /** 表示ラベル */
  readonly label: string;
  /** アイコンファイル名 */
  readonly icon: string;
}

/**
 * パレットカテゴリの定義
 */
interface PaletteCategory {
  /** カテゴリ名 */
  readonly category: string;
  /** カテゴリに含まれるアイテム */
  readonly items: readonly PaletteItem[];
}

/**
 * ノードパレットのアイテム定義
 *
 * Crisp DSL の全ノード種別をカテゴリ別に整理する。
 * 各アイテムはドラッグ可能で、キャンバスへのドロップでノード追加が行われる。
 */
const NODE_PALETTE_ITEMS: readonly PaletteCategory[] = [
  {
    category: "Composites",
    items: [
      { type: "selector", label: "Selector", icon: "selector.svg" },
      { type: "sequence", label: "Sequence", icon: "sequence.svg" },
      { type: "parallel", label: "Parallel", icon: "parallel.svg" },
    ],
  },
  {
    category: "Decorators",
    items: [
      { type: "guard", label: "Guard", icon: "guard.svg" },
      { type: "if", label: "If / Else", icon: "if.svg" },
      { type: "invert", label: "Invert", icon: "invert.svg" },
      { type: "repeat", label: "Repeat", icon: "repeat.svg" },
      { type: "timeout", label: "Timeout", icon: "timeout.svg" },
      { type: "cooldown", label: "Cooldown", icon: "cooldown.svg" },
      { type: "while", label: "While", icon: "while.svg" },
    ],
  },
  {
    category: "Leaves",
    items: [
      { type: "check", label: "Check", icon: "check.svg" },
      { type: "action", label: "Action", icon: "action.svg" },
    ],
  },
];

/**
 * ノードパレットの描画コンポーネント
 *
 * カテゴリごとにアイテムをグループ化して表示する。
 * 各アイテムはドラッグ可能で、dragstart イベントにノード種別情報を格納する。
 */
export function NodePalette(): React.JSX.Element {
  const fileUri = useTreeStore((s) => s.fileUri);
  const tree = useTreeStore((s) => s.tree);

  /* アイコンの URI を解決する */
  const iconsBaseUri =
    typeof window !== "undefined"
      ? (window as Record<string, unknown>).__CRISP_ICONS_URI__ ?? ""
      : "";

  /**
   * パレットアイテムのドラッグ開始ハンドラ
   *
   * ドラッグデータにノード種別を格納する。
   * キャンバス上のドロップハンドラでこの情報を使って addNode メッセージを生成する。
   */
  const handleDragStart = useCallback(
    (e: React.DragEvent, nodeType: string) => {
      e.dataTransfer.setData("application/crisp-node-type", nodeType);
      e.dataTransfer.effectAllowed = "copy";
    },
    []
  );

  /**
   * パレットアイテムのクリックハンドラ
   *
   * ドラッグが難しい環境（タッチデバイス等）向けに、
   * クリックでルートノードの末尾に追加する簡易操作。
   */
  const handleClick = useCallback(
    (nodeType: string) => {
      if (!fileUri || !tree) return;
      sendMessage({
        type: "addNode",
        payload: {
          uri: fileUri,
          parentNodeId: tree.root.id,
          position: tree.root.children?.length ?? 0,
          nodeType,
        },
      });
    },
    [fileUri, tree]
  );

  return (
    <div className="crisp-palette">
      {NODE_PALETTE_ITEMS.map((category) => (
        <div key={category.category} className="crisp-palette-category">
          <div className="crisp-palette-category-title">
            {category.category}
          </div>
          {category.items.map((item) => (
            <div
              key={item.type}
              className="crisp-palette-item"
              draggable
              onDragStart={(e) => handleDragStart(e, item.type)}
              onClick={() => handleClick(item.type)}
              title={item.label}
            >
              <img
                src={`${iconsBaseUri}/${item.icon}`}
                alt={item.label}
                className="crisp-palette-icon"
              />
              <span>{item.label}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
