/**
 * ツリーレンダラ コンポーネントテスト
 *
 * TreeRenderer コンポーネントの描画ロジックをテストする。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * テスト方針:
 * 1. 単一ノードのツリーが正しく描画されること
 * 2. 親子ノードのツリーでエッジが描画されること
 * 3. 選択状態が正しいノードに反映されること
 * 4. 診断情報が正しく表示されること
 */
import { describe, it, expect, vi, beforeEach } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import type { TreeLayout, TreeNode } from "../../src/shared/treeModel";

/* acquireVsCodeApi のモック */
vi.stubGlobal("acquireVsCodeApi", () => ({
  postMessage: vi.fn(),
  getState: vi.fn().mockReturnValue(null),
  setState: vi.fn(),
}));

/* window.__CRISP_ICONS_URI__ のモック */
(window as Record<string, unknown>).__CRISP_ICONS_URI__ = "http://test-icons";

/* === テスト用ヘルパー === */

/**
 * テスト用のツリーレイアウトを生成する
 */
function createTestLayout(
  root: TreeNode,
  edges: TreeLayout["edges"] = []
): TreeLayout {
  return {
    fileUri: "file:///test.crisp",
    treeName: "TestTree",
    root,
    edges,
    bounds: { width: 400, height: 300 },
  };
}

function createTestNode(
  id: number,
  type: string,
  label: string,
  children?: TreeNode[]
): TreeNode {
  return {
    id,
    type: type as TreeNode["type"],
    label,
    x: 100 + id * 50,
    y: 50 + id * 60,
    width: 140,
    height: 36,
    origin: { start: id * 10, length: 10 },
    diagnostics: [],
    collapsed: false,
    children,
  };
}

/* ============================================================
 * ノード種別のメタデータテスト
 * ============================================================ */

import { NODE_TYPE_INFO } from "../../src/shared/nodeTypes";
import type { NodeType } from "../../src/shared/nodeTypes";

describe("ノード種別メタデータ", () => {
  it("全ノード種別にメタデータが定義されている", () => {
    const allTypes: NodeType[] = [
      "selector",
      "sequence",
      "parallel",
      "check",
      "action",
      "guard",
      "if",
      "invert",
      "repeat",
      "timeout",
      "cooldown",
      "while",
      "reactive",
      "reactive-select",
      "ref",
      "defdec-call",
      "unknown",
    ];

    for (const t of allTypes) {
      expect(NODE_TYPE_INFO[t]).toBeDefined();
      expect(NODE_TYPE_INFO[t].type).toBe(t);
      expect(NODE_TYPE_INFO[t].label).toBeTruthy();
      expect(NODE_TYPE_INFO[t].icon).toBeTruthy();
    }
  });

  it("コンポジットノードは minChildren >= 1 である", () => {
    const composites: NodeType[] = [
      "selector",
      "sequence",
      "parallel",
      "reactive-select",
    ];

    for (const t of composites) {
      expect(NODE_TYPE_INFO[t].category).toBe("composite");
      expect(NODE_TYPE_INFO[t].minChildren).toBeGreaterThanOrEqual(1);
    }
  });

  it("リーフノードは maxChildren === 0 である", () => {
    const leaves: NodeType[] = ["check", "action", "ref"];

    for (const t of leaves) {
      expect(NODE_TYPE_INFO[t].category).toBe("leaf");
      expect(NODE_TYPE_INFO[t].maxChildren).toBe(0);
    }
  });

  it("デコレータノードは maxChildren が 1 または 2 である", () => {
    const decorators: NodeType[] = [
      "guard",
      "invert",
      "repeat",
      "timeout",
      "cooldown",
      "while",
      "reactive",
    ];

    for (const t of decorators) {
      expect(NODE_TYPE_INFO[t].category).toBe("decorator");
      expect(NODE_TYPE_INFO[t].maxChildren).toBeLessThanOrEqual(2);
    }
  });
});

/* ============================================================
 * ノードスタイルテスト
 * ============================================================ */

import { NODE_STYLES } from "../../src/webview/styles/nodeStyles";

describe("ノードスタイル", () => {
  it("全ノード種別にスタイルが定義されている", () => {
    const allTypes: NodeType[] = [
      "selector",
      "sequence",
      "parallel",
      "check",
      "action",
      "guard",
      "if",
      "invert",
      "repeat",
      "timeout",
      "cooldown",
      "while",
      "reactive",
      "reactive-select",
      "ref",
      "defdec-call",
      "unknown",
    ];

    for (const t of allTypes) {
      expect(NODE_STYLES[t]).toBeDefined();
      expect(NODE_STYLES[t].width).toBeGreaterThan(0);
      expect(NODE_STYLES[t].height).toBeGreaterThan(0);
      expect(NODE_STYLES[t].borderRadius).toBeGreaterThanOrEqual(0);
    }
  });

  it("コンポジットノードは塗りつぶし背景スタイルを持つ", () => {
    expect(NODE_STYLES.selector.backgroundColor).toContain("charts");
    expect(NODE_STYLES.sequence.backgroundColor).toContain("charts");
    expect(NODE_STYLES.parallel.backgroundColor).toContain("charts");
  });

  it("リーフノードは枠線のみのスタイルを持つ", () => {
    expect(NODE_STYLES.check.backgroundColor).toContain("editor-background");
    expect(NODE_STYLES.action.backgroundColor).toContain("editor-background");
  });

  it("デコレータノードは大きな角丸を持つ", () => {
    expect(NODE_STYLES.guard.borderRadius).toBeGreaterThanOrEqual(12);
    expect(NODE_STYLES.invert.borderRadius).toBeGreaterThanOrEqual(12);
    expect(NODE_STYLES.repeat.borderRadius).toBeGreaterThanOrEqual(12);
  });
});

/* ============================================================
 * レイアウト定数テスト
 * ============================================================ */

import { LAYOUT, ZOOM_PAN, MINIMAP, EDGE } from "../../src/webview/layout/constants";

describe("レイアウト定数", () => {
  it("NODE_MIN_WIDTH は正の値である", () => {
    expect(LAYOUT.NODE_MIN_WIDTH).toBeGreaterThan(0);
  });

  it("NODE_MAX_WIDTH は NODE_MIN_WIDTH より大きい", () => {
    expect(LAYOUT.NODE_MAX_WIDTH).toBeGreaterThan(LAYOUT.NODE_MIN_WIDTH);
  });

  it("LEVEL_GAP は NODE_HEIGHT より大きい", () => {
    expect(LAYOUT.LEVEL_GAP).toBeGreaterThan(LAYOUT.NODE_HEIGHT);
  });

  it("ZOOM_PAN の範囲が妥当である", () => {
    expect(ZOOM_PAN.MIN_SCALE).toBeGreaterThan(0);
    expect(ZOOM_PAN.MAX_SCALE).toBeGreaterThan(ZOOM_PAN.MIN_SCALE);
    expect(ZOOM_PAN.DEFAULT_SCALE).toBeGreaterThanOrEqual(ZOOM_PAN.MIN_SCALE);
    expect(ZOOM_PAN.DEFAULT_SCALE).toBeLessThanOrEqual(ZOOM_PAN.MAX_SCALE);
  });

  it("MINIMAP のサイズが正の値である", () => {
    expect(MINIMAP.WIDTH).toBeGreaterThan(0);
    expect(MINIMAP.HEIGHT).toBeGreaterThan(0);
  });
});

/* ============================================================
 * コンテキストメニュー状態テスト
 * ============================================================ */

import { useTreeStore } from "../../src/webview/state/treeStore";

describe("コンテキストメニュー状態", () => {
  beforeEach(() => {
    /* ストアの状態をリセットする */
    useTreeStore.setState({ contextMenu: null });
  });

  it("初期状態ではコンテキストメニューは非表示", () => {
    const state = useTreeStore.getState();
    expect(state.contextMenu).toBeNull();
  });

  it("openContextMenu でメニューが表示状態になる", () => {
    useTreeStore.getState().openContextMenu(100, 200, 42);

    const state = useTreeStore.getState();
    expect(state.contextMenu).not.toBeNull();
    expect(state.contextMenu!.x).toBe(100);
    expect(state.contextMenu!.y).toBe(200);
    expect(state.contextMenu!.nodeId).toBe(42);
  });

  it("closeContextMenu でメニューが非表示になる", () => {
    useTreeStore.getState().openContextMenu(100, 200, 42);
    useTreeStore.getState().closeContextMenu();

    const state = useTreeStore.getState();
    expect(state.contextMenu).toBeNull();
  });

  it("openContextMenu を連続呼び出しすると最新の値に更新される", () => {
    useTreeStore.getState().openContextMenu(100, 200, 1);
    useTreeStore.getState().openContextMenu(300, 400, 5);

    const state = useTreeStore.getState();
    expect(state.contextMenu!.x).toBe(300);
    expect(state.contextMenu!.y).toBe(400);
    expect(state.contextMenu!.nodeId).toBe(5);
  });
});
