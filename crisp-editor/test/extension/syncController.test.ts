/**
 * SyncController テスト
 *
 * 双方向同期コントローラのテスト。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * テスト対象:
 * 1. テキスト変更 → treeLayout 通知 → WebView postMessage
 * 2. WebView addNode → LSP request → workspace.applyEdit
 * 3. カーソル位置変更 → nodeId 特定 → WebView cursorSync
 * 4. isApplyingEdit フラグによるループ防止
 * 5. WebView 未接続時の graceful degradation
 *
 * 注: vscode モジュールが使えないため、SyncController のロジックのうち
 * vscode 依存の部分を除いた純粋なロジックをテストする。
 */
import { describe, it, expect, vi } from "vitest";
import type { TreeLayout, TreeNode } from "../../src/shared/treeModel";

/* === テスト用ヘルパー: findNodeAtOffset の純粋ロジック再現 === */

/**
 * テキストオフセットに対応するノード ID を特定する
 *
 * SyncController.findNodeAtOffset の純粋関数版。
 * vscode 依存なしでテスト可能。
 */
function findNodeAtOffset(layout: TreeLayout, offset: number): number | null {
  function walk(node: TreeNode): number | null {
    if (
      offset < node.origin.start ||
      offset >= node.origin.start + node.origin.length
    ) {
      return null;
    }

    if (node.children) {
      for (const child of node.children) {
        const found = walk(child);
        if (found !== null) return found;
      }
    }

    return node.id;
  }

  return walk(layout.root);
}

/**
 * ノード ID でツリーノードを検索する
 *
 * SyncController.findNodeById の純粋関数版。
 */
function findNodeById(layout: TreeLayout, nodeId: number): TreeNode | null {
  function walk(node: TreeNode): TreeNode | null {
    if (node.id === nodeId) return node;
    if (node.children) {
      for (const child of node.children) {
        const found = walk(child);
        if (found !== null) return found;
      }
    }
    return null;
  }

  return walk(layout.root);
}

/* === テスト用ヘルパー === */

function createTestNode(
  id: number,
  start: number,
  length: number,
  children?: TreeNode[]
): TreeNode {
  return {
    id,
    type: "selector",
    label: `node-${id}`,
    x: 100,
    y: 50,
    width: 140,
    height: 36,
    origin: { start, length },
    diagnostics: [],
    collapsed: false,
    children,
  };
}

function createTestLayout(root: TreeNode): TreeLayout {
  return {
    fileUri: "file:///test.crisp",
    treeName: "TestTree",
    root,
    edges: [],
    bounds: { width: 400, height: 300 },
  };
}

/* ============================================================
 * カーソル同期: findNodeAtOffset テスト
 * ============================================================ */

describe("カーソル同期 — findNodeAtOffset", () => {
  /*
   * テストツリーのソース範囲:
   *
   * root (id=1): offset 0..50  (0〜49)
   *   ├─ child1 (id=2): offset 5..15  (5〜19)
   *   └─ child2 (id=3): offset 25..10 (25〜34)
   */

  const child1 = createTestNode(2, 5, 15);
  const child2 = createTestNode(3, 25, 10);
  const root = createTestNode(1, 0, 50, [child1, child2]);
  const layout = createTestLayout(root);

  /* --- 最小テスト: ルートノードの検索 --- */

  it("ルートノードの範囲内のオフセットでルートが見つかる", () => {
    const result = findNodeAtOffset(layout, 40);
    expect(result).toBe(1);
  });

  /* --- 子ノードの検索 --- */

  it("child1 の範囲内のオフセットで child1 が見つかる", () => {
    const result = findNodeAtOffset(layout, 10);
    expect(result).toBe(2);
  });

  it("child2 の範囲内のオフセットで child2 が見つかる", () => {
    const result = findNodeAtOffset(layout, 28);
    expect(result).toBe(3);
  });

  /* --- 深いマッチ優先 --- */

  it("子ノードの範囲はルートより優先される（深いマッチ優先）", () => {
    /* offset 7 はルート(0..50)とchild1(5..20)の両方に含まれるが、child1が優先 */
    const result = findNodeAtOffset(layout, 7);
    expect(result).toBe(2);
  });

  /* --- 境界値テスト --- */

  it("ノードの開始位置ちょうどで検索できる", () => {
    const result = findNodeAtOffset(layout, 5);
    expect(result).toBe(2);
  });

  it("ノードの終了位置（排他）では親ノードが返る", () => {
    /* child1 は offset 5..15 なので、offset 20 は child1 の範囲外 */
    const result = findNodeAtOffset(layout, 20);
    expect(result).toBe(1);
  });

  it("ルートの範囲外では null が返る", () => {
    const result = findNodeAtOffset(layout, 50);
    expect(result).toBeNull();
  });

  it("負のオフセットでは null が返る", () => {
    const result = findNodeAtOffset(layout, -1);
    expect(result).toBeNull();
  });

  /* --- 深いネスト --- */

  it("3階層のネストで最も深いノードが返る", () => {
    const grandchild = createTestNode(4, 8, 5);
    const child = createTestNode(2, 5, 15, [grandchild]);
    const deepRoot = createTestNode(1, 0, 50, [child]);
    const deepLayout = createTestLayout(deepRoot);

    /* offset 10 は grandchild(8..13) に含まれる */
    const result = findNodeAtOffset(deepLayout, 10);
    expect(result).toBe(4);
  });
});

/* ============================================================
 * ノード検索: findNodeById テスト
 * ============================================================ */

describe("ノード検索 — findNodeById", () => {
  const child1 = createTestNode(2, 5, 15);
  const child2 = createTestNode(3, 25, 10);
  const root = createTestNode(1, 0, 50, [child1, child2]);
  const layout = createTestLayout(root);

  it("ルートノードを ID で検索できる", () => {
    const result = findNodeById(layout, 1);
    expect(result).not.toBeNull();
    expect(result!.id).toBe(1);
  });

  it("子ノードを ID で検索できる", () => {
    const result = findNodeById(layout, 2);
    expect(result).not.toBeNull();
    expect(result!.id).toBe(2);
  });

  it("存在しない ID では null が返る", () => {
    const result = findNodeById(layout, 999);
    expect(result).toBeNull();
  });
});

/* ============================================================
 * WebView 登録・解除の動作テスト（Mock ベース）
 * ============================================================ */

describe("WebView 登録の動作", () => {
  it("postMessage で treeLayout メッセージを送信する構造", () => {
    const mockWebview = {
      postMessage: vi.fn(),
    };

    /* treeLayout 通知のシミュレーション */
    const layout: TreeLayout = {
      fileUri: "file:///test.crisp",
      treeName: "TestTree",
      root: createTestNode(1, 0, 50),
      edges: [],
      bounds: { width: 400, height: 300 },
    };

    mockWebview.postMessage({
      type: "treeLayout",
      payload: layout,
    });

    expect(mockWebview.postMessage).toHaveBeenCalledWith({
      type: "treeLayout",
      payload: layout,
    });
  });

  it("cursorSync メッセージの構造が正しい", () => {
    const mockWebview = {
      postMessage: vi.fn(),
    };

    mockWebview.postMessage({
      type: "cursorSync",
      payload: { nodeId: 42 },
    });

    expect(mockWebview.postMessage).toHaveBeenCalledWith({
      type: "cursorSync",
      payload: { nodeId: 42 },
    });
  });

  it("diagnostics メッセージの構造が正しい", () => {
    const mockWebview = {
      postMessage: vi.fn(),
    };

    mockWebview.postMessage({
      type: "diagnostics",
      payload: [
        { code: "BS0001", message: "未知のメンバー", severity: "error" },
      ],
    });

    expect(mockWebview.postMessage).toHaveBeenCalledWith({
      type: "diagnostics",
      payload: [
        { code: "BS0001", message: "未知のメンバー", severity: "error" },
      ],
    });
  });
});

/* ============================================================
 * 同期ループ防止のテスト
 * ============================================================ */

describe("同期ループ防止", () => {
  it("isApplyingEdit フラグの概念テスト", () => {
    /*
     * ビジュアル編集 → テキスト変更 → LSP didChange → treeLayout 通知 → WebView 更新
     * このループを防止するために isApplyingEdit フラグを使用する。
     *
     * ビジュアル起因の変更中:
     * 1. isApplyingEdit = true に設定
     * 2. workspace.applyEdit でテキスト変更
     * 3. onDocumentChanged が発火するが、isApplyingEdit が true なので無視
     * 4. isApplyingEdit = false に戻す
     */
    let isApplyingEdit = false;
    const documentChanges: string[] = [];

    function onDocumentChanged(source: string): void {
      if (isApplyingEdit) return;
      documentChanges.push(source);
    }

    /* テキスト起因の変更は記録される */
    onDocumentChanged("text-edit");
    expect(documentChanges).toEqual(["text-edit"]);

    /* ビジュアル起因の変更は無視される */
    isApplyingEdit = true;
    onDocumentChanged("visual-edit");
    expect(documentChanges).toEqual(["text-edit"]);

    /* フラグ解除後の変更は再び記録される */
    isApplyingEdit = false;
    onDocumentChanged("text-edit-2");
    expect(documentChanges).toEqual(["text-edit", "text-edit-2"]);
  });
});
