/**
 * Vitest テスト設定
 *
 * Extension テストと WebView テストの両方を実行する。
 * WebView テストは jsdom 環境で React コンポーネントをテストする。
 */
import { defineConfig } from "vitest/config";
import path from "path";

export default defineConfig({
  test: {
    globals: true,
    environment: "jsdom",
    include: ["test/**/*.test.{ts,tsx}"],
    setupFiles: [],
  },
  resolve: {
    alias: {
      "@shared": path.resolve(__dirname, "src/shared"),
      "@extension": path.resolve(__dirname, "src/extension"),
      "@webview": path.resolve(__dirname, "src/webview"),
    },
  },
});
