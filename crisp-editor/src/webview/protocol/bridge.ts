/**
 * Extension ↔ WebView 通信ブリッジ
 *
 * VSCode WebView API (acquireVsCodeApi) のラッパー。
 * 型安全なメッセージ送受信インターフェースを提供する。
 *
 * WebView 内から Extension Host へメッセージを送信し、
 * Extension Host からのメッセージを購読するための関数群。
 */
import type {
  ExtensionToWebViewMessage,
  WebViewToExtensionMessage,
} from "./messages";

/**
 * VSCode WebView API のインターフェース
 *
 * acquireVsCodeApi() で取得されるオブジェクトの型定義。
 * WebView から Extension Host へのメッセージ送信と、
 * WebView の状態保存・復元に使用する。
 */
interface VsCodeApi {
  postMessage(message: WebViewToExtensionMessage): void;
  getState(): unknown;
  setState(state: unknown): void;
}

/** グローバルの acquireVsCodeApi 関数の型宣言 */
declare function acquireVsCodeApi(): VsCodeApi;

/**
 * VSCode API のシングルトンインスタンス
 *
 * acquireVsCodeApi() は WebView のライフサイクル内で1回しか呼べないため、
 * 初回取得後にキャッシュする。
 */
let vsCodeApiInstance: VsCodeApi | null = null;

/**
 * VSCode API インスタンスを取得する
 *
 * 初回呼び出し時に acquireVsCodeApi() を実行し、結果をキャッシュする。
 * テスト環境では acquireVsCodeApi が存在しない場合があるため、
 * その場合はモック用のダミーオブジェクトを返す。
 *
 * @returns VSCode API インスタンス
 */
export function getVsCodeApi(): VsCodeApi {
  if (!vsCodeApiInstance) {
    if (typeof acquireVsCodeApi === "function") {
      vsCodeApiInstance = acquireVsCodeApi();
    } else {
      /* テスト環境やスタンドアロン実行時のフォールバック */
      vsCodeApiInstance = {
        postMessage: () => {},
        getState: () => null,
        setState: () => {},
      };
    }
  }
  return vsCodeApiInstance;
}

/**
 * Extension Host にメッセージを送信する
 *
 * WebView のユーザー操作（ノードクリック、ドラッグ&ドロップ等）を
 * Extension Host に通知する。
 *
 * @param message 送信するメッセージ
 */
export function postMessageToExtension(
  message: WebViewToExtensionMessage
): void {
  getVsCodeApi().postMessage(message);
}

/** Extension からのメッセージを受信するコールバック型 */
export type MessageHandler = (message: ExtensionToWebViewMessage) => void;

/**
 * Extension Host からのメッセージリスナーを登録する
 *
 * Extension が postMessage で送信する treeLayout、diagnostics、cursorSync 等の
 * メッセージを受信するイベントリスナーを登録する。
 *
 * @param handler メッセージ受信時のコールバック
 * @returns リスナーの解除関数
 */
export function onMessageFromExtension(handler: MessageHandler): () => void {
  const listener = (event: MessageEvent) => {
    handler(event.data as ExtensionToWebViewMessage);
  };
  window.addEventListener("message", listener);
  return () => window.removeEventListener("message", listener);
}
