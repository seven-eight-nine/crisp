/**
 * LSP クライアントテスト
 *
 * LSP カスタム通知・リクエストの型定義と構造のテスト。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * 注: 実際の LSP クライアントの起動はインテグレーションテストで行う。
 * ここでは型定義の正確性とメッセージ構造の整合性をテストする。
 */
import { describe, it, expect } from "vitest";
import type {
  TreeLayoutNotification,
  TreeDiagnosticsNotification,
  EditResult,
  AddNodeParams,
  RemoveNodeParams,
  MoveNodeParams,
  WrapNodeParams,
  UnwrapNodeParams,
} from "../../src/shared/protocol";

/* ============================================================
 * LSP カスタム通知の構造テスト
 * ============================================================ */

describe("LSP カスタム通知 — TreeLayoutNotification", () => {
  it("最小限のツリーレイアウト通知を構築できる", () => {
    const notification: TreeLayoutNotification = {
      uri: "file:///test.crisp",
      layout: {
        fileUri: "file:///test.crisp",
        treeName: "SimpleCombat",
        root: {
          id: 1,
          type: "selector",
          label: "selector",
          x: 200,
          y: 50,
          width: 140,
          height: 36,
          origin: { start: 0, length: 80 },
          diagnostics: [],
          collapsed: false,
        },
        edges: [],
        bounds: { width: 400, height: 300 },
      },
    };

    expect(notification.uri).toBe("file:///test.crisp");
    expect(notification.layout.treeName).toBe("SimpleCombat");
    expect(notification.layout.root.type).toBe("selector");
  });

  it("診断情報付きのツリーレイアウト通知を構築できる", () => {
    const notification: TreeLayoutNotification = {
      uri: "file:///test.crisp",
      layout: {
        fileUri: "file:///test.crisp",
        treeName: "TestTree",
        root: {
          id: 1,
          type: "check",
          label: "< .Health 30",
          x: 200,
          y: 50,
          width: 160,
          height: 36,
          origin: { start: 10, length: 20 },
          diagnostics: [
            {
              code: "BS0001",
              message: "メンバー 'Health' が見つかりません",
              severity: "error",
            },
          ],
          collapsed: false,
        },
        edges: [],
        bounds: { width: 300, height: 100 },
      },
    };

    expect(notification.layout.root.diagnostics).toHaveLength(1);
    expect(notification.layout.root.diagnostics[0].severity).toBe("error");
  });
});

describe("LSP カスタム通知 — TreeDiagnosticsNotification", () => {
  it("空の診断情報通知を構築できる", () => {
    const notification: TreeDiagnosticsNotification = {
      uri: "file:///test.crisp",
      diagnostics: [],
    };

    expect(notification.diagnostics).toHaveLength(0);
  });

  it("複数の診断情報を含む通知を構築できる", () => {
    const notification: TreeDiagnosticsNotification = {
      uri: "file:///test.crisp",
      diagnostics: [
        { code: "BS0001", message: "未知のメンバー", severity: "error" },
        { code: "BS0002", message: "型の不一致", severity: "warning" },
        { code: "BS0003", message: "未使用のインポート", severity: "info" },
      ],
    };

    expect(notification.diagnostics).toHaveLength(3);
    expect(notification.diagnostics[0].severity).toBe("error");
    expect(notification.diagnostics[1].severity).toBe("warning");
    expect(notification.diagnostics[2].severity).toBe("info");
  });
});

/* ============================================================
 * LSP カスタムリクエストの構造テスト
 * ============================================================ */

describe("LSP カスタムリクエスト — パラメータ構造", () => {
  it("AddNodeParams の構造が正しい", () => {
    const params: AddNodeParams = {
      uri: "file:///test.crisp",
      parentNodeId: 1,
      position: 0,
      nodeType: "sequence",
    };

    expect(params.parentNodeId).toBe(1);
    expect(params.position).toBe(0);
    expect(params.nodeType).toBe("sequence");
  });

  it("AddNodeParams に引数を含めることができる", () => {
    const params: AddNodeParams = {
      uri: "file:///test.crisp",
      parentNodeId: 1,
      position: 2,
      nodeType: "action",
      args: { method: ".Attack", target: ".Target" },
    };

    expect(params.args).toBeDefined();
    expect(params.args!["method"]).toBe(".Attack");
  });

  it("RemoveNodeParams の構造が正しい", () => {
    const params: RemoveNodeParams = {
      uri: "file:///test.crisp",
      nodeId: 5,
    };

    expect(params.nodeId).toBe(5);
  });

  it("MoveNodeParams の構造が正しい", () => {
    const params: MoveNodeParams = {
      uri: "file:///test.crisp",
      nodeId: 3,
      newParentId: 7,
      newPosition: 1,
    };

    expect(params.nodeId).toBe(3);
    expect(params.newParentId).toBe(7);
    expect(params.newPosition).toBe(1);
  });

  it("WrapNodeParams の構造が正しい", () => {
    const params: WrapNodeParams = {
      uri: "file:///test.crisp",
      nodeId: 2,
      decoratorType: "guard",
    };

    expect(params.decoratorType).toBe("guard");
  });

  it("WrapNodeParams に引数を含めることができる", () => {
    const params: WrapNodeParams = {
      uri: "file:///test.crisp",
      nodeId: 2,
      decoratorType: "timeout",
      args: ["5.0"],
    };

    expect(params.args).toEqual(["5.0"]);
  });

  it("UnwrapNodeParams の構造が正しい", () => {
    const params: UnwrapNodeParams = {
      uri: "file:///test.crisp",
      nodeId: 4,
    };

    expect(params.nodeId).toBe(4);
  });
});

/* ============================================================
 * EditResult の構造テスト
 * ============================================================ */

describe("LSP カスタムリクエスト — EditResult 構造", () => {
  it("空の編集結果を構築できる", () => {
    const result: EditResult = { edits: [] };

    expect(result.edits).toHaveLength(0);
  });

  it("単一のテキスト編集を含む結果を構築できる", () => {
    const result: EditResult = {
      edits: [
        {
          range: {
            start: { line: 2, character: 4 },
            end: { line: 2, character: 4 },
          },
          newText: "(seq\n      )",
        },
      ],
    };

    expect(result.edits).toHaveLength(1);
    expect(result.edits[0].newText).toContain("seq");
  });

  it("複数のテキスト編集を含む結果を構築できる", () => {
    const result: EditResult = {
      edits: [
        {
          range: {
            start: { line: 1, character: 0 },
            end: { line: 3, character: 10 },
          },
          newText: "(select\n    (seq\n      (.Patrol)))",
        },
        {
          range: {
            start: { line: 5, character: 0 },
            end: { line: 5, character: 0 },
          },
          newText: "\n",
        },
      ],
    };

    expect(result.edits).toHaveLength(2);
  });
});
