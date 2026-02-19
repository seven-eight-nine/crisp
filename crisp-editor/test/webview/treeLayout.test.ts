/**
 * ツリーレイアウト補助関数テスト
 *
 * クライアント側ツリーレイアウト補助モジュールのテスト。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * テスト対象:
 * - computeEdgePath: ベジェカーブのパスデータ生成
 * - computeFitTransform: ツリーフィット計算
 * - findNodeById: ID によるノード検索
 * - findNodeAtPosition: 座標によるノード検索
 * - isDescendantOf: 子孫判定
 * - flattenTree: ツリーのフラット化
 */
import { describe, it, expect } from "vitest";
import {
  computeEdgePath,
  computeFitTransform,
  findNodeById,
  findNodeAtPosition,
  isDescendantOf,
  flattenTree,
} from "../../src/webview/layout/treeLayout";
import type { TreeNode } from "../../src/shared/treeModel";

/* === テスト用ヘルパー: ノード生成 === */

/**
 * テスト用のツリーノードを生成するヘルパー
 *
 * 必要最小限のフィールドだけ指定し、残りはデフォルト値を使用する。
 */
function createNode(
  id: number,
  x: number,
  y: number,
  children?: TreeNode[]
): TreeNode {
  return {
    id,
    type: "selector",
    label: `node-${id}`,
    x,
    y,
    width: 140,
    height: 36,
    origin: { start: 0, length: 10 },
    diagnostics: [],
    collapsed: false,
    children,
  };
}

/* ============================================================
 * computeEdgePath — ベジェカーブのパスデータ生成
 * ============================================================ */

describe("computeEdgePath", () => {
  it("垂直に直下のノードへ接続するパスを生成する", () => {
    const path = computeEdgePath(100, 36, 100, 96);

    expect(path).toContain("M 100,36");
    expect(path).toContain("100,96");
    expect(path).toContain("C");
  });

  it("水平方向にずれたノードへ接続するパスを生成する", () => {
    const path = computeEdgePath(100, 36, 200, 96);

    expect(path).toContain("M 100,36");
    expect(path).toContain("200,96");
  });

  it("同じ座標の場合でもパスを生成する", () => {
    const path = computeEdgePath(0, 0, 0, 0);

    expect(path).toBeDefined();
    expect(path).toContain("M 0,0");
  });
});

/* ============================================================
 * computeFitTransform — ツリーフィット計算
 * ============================================================ */

describe("computeFitTransform", () => {
  it("ツリーがキャンバスより小さい場合、拡大してフィットする", () => {
    const result = computeFitTransform(
      { width: 200, height: 100 },
      800,
      600
    );

    expect(result.scale).toBeGreaterThan(0);
    expect(result.x).toBeGreaterThanOrEqual(0);
    expect(result.y).toBeGreaterThanOrEqual(0);
  });

  it("ツリーがキャンバスより大きい場合、縮小してフィットする", () => {
    const result = computeFitTransform(
      { width: 2000, height: 1500 },
      800,
      600
    );

    expect(result.scale).toBeLessThan(1);
  });

  it("サイズ0のツリーでもエラーにならない", () => {
    const result = computeFitTransform({ width: 0, height: 0 }, 800, 600);

    expect(result).toBeDefined();
    expect(result.scale).toBeGreaterThan(0);
  });

  it("正方形のキャンバスと正方形のツリーで等倍に近い値を返す", () => {
    const result = computeFitTransform(
      { width: 400, height: 400 },
      480,
      480
    );

    /* パディング (40px) を考慮して、400/(480-80) = 1.0 */
    expect(result.scale).toBeLessThanOrEqual(1);
  });
});

/* ============================================================
 * findNodeById — ID によるノード検索
 * ============================================================ */

describe("findNodeById", () => {
  it("ルートノードを ID で検索できる", () => {
    const root = createNode(1, 100, 50);

    const result = findNodeById(root, 1);

    expect(result).not.toBeNull();
    expect(result!.id).toBe(1);
  });

  it("子ノードを ID で検索できる", () => {
    const child = createNode(2, 50, 110);
    const root = createNode(1, 100, 50, [child]);

    const result = findNodeById(root, 2);

    expect(result).not.toBeNull();
    expect(result!.id).toBe(2);
  });

  it("深くネストされたノードを検索できる", () => {
    const grandchild = createNode(3, 50, 170);
    const child = createNode(2, 50, 110, [grandchild]);
    const root = createNode(1, 100, 50, [child]);

    const result = findNodeById(root, 3);

    expect(result).not.toBeNull();
    expect(result!.id).toBe(3);
  });

  it("存在しない ID では null を返す", () => {
    const root = createNode(1, 100, 50);

    const result = findNodeById(root, 999);

    expect(result).toBeNull();
  });

  it("兄弟ノードから正しいノードを検索できる", () => {
    const child1 = createNode(2, 50, 110);
    const child2 = createNode(3, 150, 110);
    const root = createNode(1, 100, 50, [child1, child2]);

    expect(findNodeById(root, 2)!.id).toBe(2);
    expect(findNodeById(root, 3)!.id).toBe(3);
  });
});

/* ============================================================
 * findNodeAtPosition — 座標によるノード検索
 * ============================================================ */

describe("findNodeAtPosition", () => {
  it("ノードの中心座標で検索できる", () => {
    const root = createNode(1, 100, 50);

    const result = findNodeAtPosition(root, 100, 68);

    expect(result).not.toBeNull();
    expect(result!.id).toBe(1);
  });

  it("ノードの範囲外では子ノードも探索する", () => {
    const child = createNode(2, 200, 110);
    const root = createNode(1, 100, 50, [child]);

    const result = findNodeAtPosition(root, 200, 128);

    expect(result).not.toBeNull();
    expect(result!.id).toBe(2);
  });

  it("どのノードにも該当しない座標では null を返す", () => {
    const root = createNode(1, 100, 50);

    const result = findNodeAtPosition(root, 500, 500);

    expect(result).toBeNull();
  });
});

/* ============================================================
 * isDescendantOf — 子孫判定
 * ============================================================ */

describe("isDescendantOf", () => {
  it("直接の子は子孫と判定される", () => {
    const child = createNode(2, 50, 110);
    const root = createNode(1, 100, 50, [child]);

    expect(isDescendantOf(root, 2, 1)).toBe(true);
  });

  it("孫ノードは子孫と判定される", () => {
    const grandchild = createNode(3, 50, 170);
    const child = createNode(2, 50, 110, [grandchild]);
    const root = createNode(1, 100, 50, [child]);

    expect(isDescendantOf(root, 3, 1)).toBe(true);
  });

  it("兄弟ノードは子孫ではない", () => {
    const child1 = createNode(2, 50, 110);
    const child2 = createNode(3, 150, 110);
    const root = createNode(1, 100, 50, [child1, child2]);

    expect(isDescendantOf(root, 3, 2)).toBe(false);
  });

  it("親は子の子孫ではない", () => {
    const child = createNode(2, 50, 110);
    const root = createNode(1, 100, 50, [child]);

    expect(isDescendantOf(root, 1, 2)).toBe(false);
  });

  it("自分自身は自分の子孫ではない", () => {
    const root = createNode(1, 100, 50);

    expect(isDescendantOf(root, 1, 1)).toBe(false);
  });

  it("存在しない祖先 ID では false を返す", () => {
    const root = createNode(1, 100, 50);

    expect(isDescendantOf(root, 1, 999)).toBe(false);
  });
});

/* ============================================================
 * flattenTree — ツリーのフラット化
 * ============================================================ */

describe("flattenTree", () => {
  it("単一ノードのツリーはそのノードだけのリストを返す", () => {
    const root = createNode(1, 100, 50);

    const result = flattenTree(root);

    expect(result).toHaveLength(1);
    expect(result[0].id).toBe(1);
  });

  it("親と子のツリーは2要素のリストを返す", () => {
    const child = createNode(2, 50, 110);
    const root = createNode(1, 100, 50, [child]);

    const result = flattenTree(root);

    expect(result).toHaveLength(2);
  });

  it("深いツリーの全ノードを収集する", () => {
    const g1 = createNode(4, 50, 230);
    const g2 = createNode(5, 150, 230);
    const c1 = createNode(2, 50, 110, [g1]);
    const c2 = createNode(3, 150, 110, [g2]);
    const root = createNode(1, 100, 50, [c1, c2]);

    const result = flattenTree(root);

    expect(result).toHaveLength(5);
    const ids = result.map((n) => n.id).sort();
    expect(ids).toEqual([1, 2, 3, 4, 5]);
  });

  it("子なしノードの children が undefined でもエラーにならない", () => {
    const root: TreeNode = {
      id: 1,
      type: "action",
      label: "test",
      x: 0,
      y: 0,
      width: 140,
      height: 36,
      origin: { start: 0, length: 5 },
      diagnostics: [],
      collapsed: false,
    };

    const result = flattenTree(root);

    expect(result).toHaveLength(1);
  });
});
