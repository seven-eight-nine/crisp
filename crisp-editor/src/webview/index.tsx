/**
 * WebView エントリポイント
 *
 * VSCode WebView 内で React アプリケーションを起動する。
 * webpack のエントリポイントとして指定され、webview.js にバンドルされる。
 *
 * DOM の #root 要素に React アプリケーションをマウントする。
 * グローバル CSS もここでインポートし、バンドルに含める。
 */
import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./styles/global.css";

/**
 * React アプリケーションのマウント
 *
 * HTML テンプレート (treeViewProvider.ts の getWebViewHtml) で
 * 用意された <div id="root"> に React ツリーをレンダリングする。
 */
const rootElement = document.getElementById("root");
if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}
