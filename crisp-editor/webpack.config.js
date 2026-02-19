/**
 * Crisp Editor Webpack 設定
 *
 * 2つのエントリポイントをバンドルする:
 * 1. extension — VSCode Extension Host (Node.js 環境)
 * 2. webview — WebView 内の React アプリケーション (ブラウザ環境)
 *
 * Extension は Node.js モジュールとして出力し、vscode モジュールは external として扱う。
 * WebView はブラウザ向けにバンドルし、CSS も含める。
 */
//@ts-check
"use strict";

const path = require("path");

/** Extension Host 用の設定 (Node.js 環境) */
const extensionConfig = {
  target: "node",
  mode: "none",
  entry: "./src/extension/extension.ts",
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "extension.js",
    libraryTarget: "commonjs2",
  },
  externals: {
    /* vscode モジュールは VSCode が提供するため、バンドルから除外する */
    vscode: "commonjs vscode",
  },
  resolve: {
    extensions: [".ts", ".js"],
    alias: {
      "@shared": path.resolve(__dirname, "src/shared"),
      "@extension": path.resolve(__dirname, "src/extension"),
    },
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        exclude: /node_modules/,
        use: [{ loader: "ts-loader" }],
      },
    ],
  },
  devtool: "nosources-source-map",
};

/** WebView 用の設定 (ブラウザ環境) */
const webviewConfig = {
  target: "web",
  mode: "none",
  entry: "./src/webview/index.tsx",
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "webview.js",
  },
  resolve: {
    extensions: [".ts", ".tsx", ".js", ".jsx"],
    alias: {
      "@shared": path.resolve(__dirname, "src/shared"),
      "@webview": path.resolve(__dirname, "src/webview"),
    },
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        exclude: /node_modules/,
        use: [{ loader: "ts-loader" }],
      },
      {
        test: /\.css$/,
        use: ["style-loader", "css-loader"],
      },
    ],
  },
  devtool: "nosources-source-map",
};

module.exports = [extensionConfig, webviewConfig];
