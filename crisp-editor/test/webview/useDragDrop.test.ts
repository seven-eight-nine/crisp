/**
 * ドラッグ＆ドロップのバリデーションテスト
 *
 * ビジュアルエディタ上のノードドラッグ＆ドロップのバリデーションロジックをテストする。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * テスト対象のバリデーション:
 * - ノードを別の親にドロップ → moveNode メッセージ
 * - ノードを同じ親の別位置にドロップ → moveNode (位置変更)
 * - 自分自身の子にドロップ → 拒否
 * - ルートノードのドロップ → 拒否
 *
 * useDragDrop フック自体は React コンポーネントに依存するため、
 * ここではバリデーションロジック（isDescendantOf）をテストする。
 */
import { describe, it, expect } from "vitest";
import { isDescendantOf } from "../../src/webview/layout/treeLayout";
import type { TreeNode } from "../../src/shared/treeModel";

/* === テスト用ヘルパー === */

function createNode(
  id: number,
  children?: TreeNode[]
): TreeNode {
  return {
    id,
    type: "selector",
    label: `node-${id}`,
    x: 0,
    y: 0,
    width: 140,
    height: 36,
    origin: { start: 0, length: 10 },
    diagnostics: [],
    collapsed: false,
    children,
  };
}

/* ============================================================
 * ドラッグ＆ドロップのバリデーション
 * ============================================================ */

describe("ドラッグ＆ドロップのバリデーション", () => {
  /*
   * テストツリー構造:
   *
   *      1 (root/selector)
   *     / \
   *    2   3
   *   /
   *  4
   */
  const node4 = createNode(4);
  const node2 = createNode(2, [node4]);
  const node3 = createNode(3);
  const root = createNode(1, [node2, node3]);

  /* --- 許可されるドロップ操作 --- */

  it("ノード3をノード2の子にドロップできる", () => {
    /* ノード3はノード2の子孫ではないので移動可能 */
    const isDescendant = isDescendantOf(root, 2, 3);
    expect(isDescendant).toBe(false);
  });

  it("ノード4をノード3の子にドロップできる", () => {
    /* ノード4はノード3の子孫ではないので移動可能 */
    const isDescendant = isDescendantOf(root, 3, 4);
    expect(isDescendant).toBe(false);
  });

  it("ノード2をノード3の子にドロップできる", () => {
    /* ノード2はノード3の子孫ではないので移動可能 */
    const isDescendant = isDescendantOf(root, 3, 2);
    expect(isDescendant).toBe(false);
  });

  /* --- 拒否されるドロップ操作 --- */

  it("ノード2を自分自身の子（ノード4）にドロップすると拒否される", () => {
    /* ノード4はノード2の子孫なので、ノード2をノード4にドロップすると循環参照になる */
    const isDescendant = isDescendantOf(root, 4, 2);
    expect(isDescendant).toBe(true);
  });

  it("ルートノードの子孫チェックは全ノードを含む", () => {
    expect(isDescendantOf(root, 2, 1)).toBe(true);
    expect(isDescendantOf(root, 3, 1)).toBe(true);
    expect(isDescendantOf(root, 4, 1)).toBe(true);
  });

  it("自分自身は自分の子孫ではない", () => {
    expect(isDescendantOf(root, 2, 2)).toBe(false);
  });

  /* --- エッジケース --- */

  it("リーフノード間のドロップバリデーション", () => {
    /* ノード3とノード4はどちらもリーフ（子なし）なので相互にドロップ可能 */
    expect(isDescendantOf(root, 3, 4)).toBe(false);
    expect(isDescendantOf(root, 4, 3)).toBe(false);
  });

  it("深いネストでの子孫判定", () => {
    /*
     * 深いツリー:
     *   1 → 2 → 3 → 4 → 5
     */
    const n5 = createNode(5);
    const n4 = createNode(4, [n5]);
    const n3 = createNode(3, [n4]);
    const n2 = createNode(2, [n3]);
    const n1 = createNode(1, [n2]);

    /* ノード5はノード1の子孫 */
    expect(isDescendantOf(n1, 5, 1)).toBe(true);
    /* ノード5はノード3の子孫 */
    expect(isDescendantOf(n1, 5, 3)).toBe(true);
    /* ノード2はノード5の子孫ではない */
    expect(isDescendantOf(n1, 2, 5)).toBe(false);
  });
});
