/**
 * 通信プロトコルテスト
 *
 * Extension ↔ WebView 間の postMessage 通信ブリッジのテスト。
 * t-wada 式 TDD: 最小のテストから段階的に複雑さを増す。
 *
 * テスト方針:
 * 1. メッセージ送信関数が正しい型のメッセージを送信できること
 * 2. メッセージ受信リスナーが正しくイベントを処理すること
 * 3. リスナーの解除が正常に機能すること
 */
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

/* acquireVsCodeApi のモック — WebView 環境のシミュレーション */
const mockPostMessage = vi.fn();
const mockGetState = vi.fn().mockReturnValue(null);
const mockSetState = vi.fn();

/* グローバル関数のモック設定 */
vi.stubGlobal("acquireVsCodeApi", () => ({
  postMessage: mockPostMessage,
  getState: mockGetState,
  setState: mockSetState,
}));

import {
  getVsCodeApi,
  postMessageToExtension,
  onMessageFromExtension,
} from "../../src/webview/protocol/bridge";
import type {
  ExtensionToWebViewMessage,
  WebViewToExtensionMessage,
} from "../../src/shared/protocol";

describe("通信ブリッジ", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  /* === 最小テスト: VSCode API の取得 === */

  it("getVsCodeApi は acquireVsCodeApi を呼んでインスタンスを返す", () => {
    const api = getVsCodeApi();
    expect(api).toBeDefined();
    expect(api.postMessage).toBeDefined();
  });

  /* === メッセージ送信テスト === */

  it("postMessageToExtension は nodeClicked メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "nodeClicked",
      payload: { uri: "file:///test.crisp", nodeId: 42 },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
    expect(mockPostMessage).toHaveBeenCalledTimes(1);
  });

  it("postMessageToExtension は addNode メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "addNode",
      payload: {
        uri: "file:///test.crisp",
        parentNodeId: 1,
        position: 0,
        nodeType: "sequence",
      },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
  });

  it("postMessageToExtension は removeNode メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "removeNode",
      payload: { uri: "file:///test.crisp", nodeId: 5 },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
  });

  it("postMessageToExtension は moveNode メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "moveNode",
      payload: {
        uri: "file:///test.crisp",
        nodeId: 3,
        newParentId: 7,
        newPosition: 1,
      },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
  });

  it("postMessageToExtension は wrapNode メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "wrapNode",
      payload: {
        uri: "file:///test.crisp",
        nodeId: 2,
        decoratorType: "guard",
      },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
  });

  it("postMessageToExtension は unwrapNode メッセージを送信できる", () => {
    const msg: WebViewToExtensionMessage = {
      type: "unwrapNode",
      payload: { uri: "file:///test.crisp", nodeId: 4 },
    };

    postMessageToExtension(msg);

    expect(mockPostMessage).toHaveBeenCalledWith(msg);
  });

  /* === メッセージ受信テスト === */

  it("onMessageFromExtension は treeLayout メッセージを受信できる", () => {
    const handler = vi.fn();
    const unsubscribe = onMessageFromExtension(handler);

    const msg: ExtensionToWebViewMessage = {
      type: "treeLayout",
      payload: {
        fileUri: "file:///test.crisp",
        treeName: "TestTree",
        root: {
          id: 1,
          type: "selector",
          label: "selector",
          x: 100,
          y: 50,
          width: 140,
          height: 36,
          origin: { start: 0, length: 50 },
          diagnostics: [],
          collapsed: false,
        },
        edges: [],
        bounds: { width: 300, height: 200 },
      },
    };

    window.dispatchEvent(new MessageEvent("message", { data: msg }));

    expect(handler).toHaveBeenCalledWith(msg);
    expect(handler).toHaveBeenCalledTimes(1);

    unsubscribe();
  });

  it("onMessageFromExtension は cursorSync メッセージを受信できる", () => {
    const handler = vi.fn();
    const unsubscribe = onMessageFromExtension(handler);

    const msg: ExtensionToWebViewMessage = {
      type: "cursorSync",
      payload: { nodeId: 5 },
    };

    window.dispatchEvent(new MessageEvent("message", { data: msg }));

    expect(handler).toHaveBeenCalledWith(msg);

    unsubscribe();
  });

  /* === リスナー解除テスト === */

  it("unsubscribe を呼ぶとメッセージを受信しなくなる", () => {
    const handler = vi.fn();
    const unsubscribe = onMessageFromExtension(handler);

    /* リスナー解除前 — メッセージを受信する */
    window.dispatchEvent(
      new MessageEvent("message", {
        data: { type: "cursorSync", payload: { nodeId: 1 } },
      })
    );
    expect(handler).toHaveBeenCalledTimes(1);

    /* リスナーを解除する */
    unsubscribe();

    /* リスナー解除後 — メッセージを受信しない */
    window.dispatchEvent(
      new MessageEvent("message", {
        data: { type: "cursorSync", payload: { nodeId: 2 } },
      })
    );
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it("複数のリスナーを登録し、個別に解除できる", () => {
    const handler1 = vi.fn();
    const handler2 = vi.fn();
    const unsub1 = onMessageFromExtension(handler1);
    const unsub2 = onMessageFromExtension(handler2);

    const msg: ExtensionToWebViewMessage = {
      type: "cursorSync",
      payload: { nodeId: 10 },
    };

    /* 両方のリスナーがメッセージを受信する */
    window.dispatchEvent(new MessageEvent("message", { data: msg }));
    expect(handler1).toHaveBeenCalledTimes(1);
    expect(handler2).toHaveBeenCalledTimes(1);

    /* handler1 のみ解除する */
    unsub1();

    window.dispatchEvent(new MessageEvent("message", { data: msg }));
    expect(handler1).toHaveBeenCalledTimes(1);
    expect(handler2).toHaveBeenCalledTimes(2);

    unsub2();
  });
});
