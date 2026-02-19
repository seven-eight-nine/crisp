# Crisp Editor — VSCode 拡張 設計仕様書

> **Version**: 0.2.0-draft
> **Status**: Planning (Review Incorporated)
> **Prerequisites**: Crisp Core (Phase 1〜4), Crisp LSP (Phase 2)

本ドキュメントは、Crisp DSL のための VSCode 拡張の設計仕様を定義する。
テキスト編集とビジュアルツリー編集の双方向同期を実現する統合開発環境を目指す。

---

## 目次

1. [プロダクト概要](#1-プロダクト概要)
2. [ユーザー体験](#2-ユーザー体験)
3. [拡張アーキテクチャ](#3-拡張アーキテクチャ)
4. [パッケージ構成](#4-パッケージ構成)
5. [LSP クライアント](#5-lsp-クライアント)
6. [ビジュアルエディタ (WebView)](#6-ビジュアルエディタ-webview)
7. [双方向同期プロトコル](#7-双方向同期プロトコル)
8. [ツリーレイアウトエンジン](#8-ツリーレイアウトエンジン)
9. [ノードパレット & プロパティパネル](#9-ノードパレット--プロパティパネル)
10. [デバッガ統合 (F7)](#10-デバッガ統合-f7)
11. [テーマ & スタイル](#11-テーマ--スタイル)
12. [設定](#12-設定)
13. [テスト戦略](#13-テスト戦略)
14. [実装ロードマップ](#14-実装ロードマップ)

---

## 1. プロダクト概要

### 1.1 何を作るか

`crisp-editor` は VSCode 拡張であり、以下の3つの機能を提供する:

1. **テキストエディタ支援**: .crisp ファイルのシンタックスハイライト、補完、診断、フォーマット
2. **ビジュアルツリーエディタ**: ビヘイビアツリーのノードグラフ表示・編集
3. **デバッグ統合**: 実行中のツリーの状態可視化、ブレークポイント（F7 実装後）

### 1.2 ユーザーが見るもの

```
┌─ VSCode ──────────────────────────────────────────────────┐
│                                                           │
│  ┌─ Editor Group ───────────────────────────────────────┐ │
│  │                                                       │ │
│  │  ┌─ Tab: EnemyAI.crisp ──┐ ┌─ Tab: Tree View ──┐  │ │
│  │  │                          │ │                    │  │ │
│  │  │  ;; Combat AI            │ │   ┌──────────┐    │  │ │
│  │  │  (tree EnemyCombat       │ │   │ selector │    │  │ │
│  │  │    (select               │ │   └────┬─────┘    │  │ │
│  │  │      (seq                │ │   ┌────┴────┐     │  │ │
│  │  │        (check ...)       │ │   ▼         ▼     │  │ │
│  │  │        (.Flee))          │ │ ┌─────┐ ┌──────┐  │  │ │
│  │  │      (.Patrol)))         │ │ │ seq │ │Patrol│  │  │ │
│  │  │                          │ │ └──┬──┘ └──────┘  │  │ │
│  │  │                          │ │  ┌─┴──┐           │  │ │
│  │  │                          │ │  ▼    ▼           │  │ │
│  │  │                          │ │ ┌───┐┌────┐      │  │ │
│  │  │                          │ │ │chk││Flee│      │  │ │
│  │  │                          │ │ └───┘└────┘      │  │ │
│  │  └──────────────────────────┘ └────────────────────┘  │ │
│  └───────────────────────────────────────────────────────┘ │
│                                                           │
│  ┌─ Panel: Properties ──────────────────────────────────┐ │
│  │  Node: check                                         │ │
│  │  Expression: (< .Health 30)                          │ │
│  │  Type: bool                                          │ │
│  │  Diagnostics: (none)                                 │ │
│  └──────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
```

### 1.3 対象ユーザー

- ゲーム開発者（Unity + VSCode）
- AIデザイナー（テキストよりビジュアルを好む）
- テクニカルアーティスト（テキストとビジュアルを行き来したい）

---

## 2. ユーザー体験

### 2.1 基本ワークフロー

```
1. .crisp ファイルを開く
   → テキストエディタが表示される（LSP自動起動）

2. コマンドパレット: "Crisp: Open Tree View"
   または .crisp を右クリック → "Open Tree View"
   → 横にビジュアルエディタが開く（Side by Side）

3. テキストを編集
   → リアルタイムでビジュアルが更新される

4. ビジュアル上でノードをドラッグ&ドロップ
   → テキストがリアルタイムで更新される

5. ビジュアル上のノードをクリック
   → テキストエディタのカーソルが対応位置にジャンプ
   → プロパティパネルにノード情報を表示

6. テキストエディタでカーソル移動
   → ビジュアル上の対応ノードがハイライト
```

### 2.2 ビジュアルエディタの操作

#### ナビゲーション

| 操作 | 動作 |
|---|---|
| マウスドラッグ（背景） | キャンバスのパン |
| マウスホイール | ズーム |
| ダブルクリック（ノード） | テキストエディタの対応行にジャンプ |
| シングルクリック（ノード） | ノード選択、プロパティパネル表示 |
| `Ctrl+Shift+F` | ツリー全体をフィットするようにズーム |

#### 編集

| 操作 | 動作 |
|---|---|
| ノードパレットからドラッグ | 新しいノードを追加 |
| ノードをドラッグ | 別の親の下に移動 |
| `Delete` / `Backspace` | 選択ノードを削除 |
| 右クリック → "Wrap with..." | デコレータで包む |
| 右クリック → "Unwrap" | デコレータを解除 |
| 右クリック → "Extract subtree" | サブツリーとして切り出し（F11） |
| `Ctrl+Z` / `Ctrl+Y` | テキストエディタのUndo/Redoと同期 |

#### キーボードショートカット

| キー | 動作 |
|---|---|
| `↑` `↓` `←` `→` | ノード間のフォーカス移動 |
| `Enter` | 選択ノードの下に子ノードを追加（タイプ選択UI） |
| `Space` | ノードの展開/折りたたみ |
| `Ctrl+D` | ノードの複製 |
| `Ctrl+Click` | 複数選択 |

### 2.3 ステータス表示

ビジュアルエディタ上の各ノードには状態がリアルタイムに表示される:

| 状態 | 表示 |
|---|---|
| 正常 | ノード種別に応じた色 |
| Diagnostic (Error) | 赤枠 + エラーアイコン |
| Diagnostic (Warning) | 黄枠 + 警告アイコン |
| 選択中 | 青枠 + ハイライト |
| カーソル位置に対応 | 薄い青背景 |
| デバッグ: Success | 緑のパルス |
| デバッグ: Failure | 赤のパルス |
| デバッグ: Running | 黄色のアニメーション |
| デバッグ: ブレークポイント | 赤い丸印 |

---

## 3. 拡張アーキテクチャ

### 3.1 全体構成

```
┌─ VSCode ──────────────────────────────────────────────────────┐
│                                                               │
│  ┌─ Extension Host (Node.js) ──────────────────────────────┐  │
│  │                                                          │  │
│  │  ┌──────────────────┐  ┌──────────────────────────────┐  │  │
│  │  │  LSP Client      │  │  TreeView Provider           │  │  │
│  │  │  (vscode-        │  │  (CustomEditorProvider)      │  │  │
│  │  │   languageclient)│  │                              │  │  │
│  │  └────────┬─────────┘  │  ┌─────────────────────────┐ │  │  │
│  │           │             │  │ Sync Controller         │ │  │  │
│  │           │             │  │ (テキスト↔ビジュアル同期) │ │  │  │
│  │           │             │  └────────────┬────────────┘ │  │  │
│  │           │             └───────────────┼──────────────┘  │  │
│  │           │                             │                 │  │
│  └───────────┼─────────────────────────────┼─────────────────┘  │
│              │                             │                    │
│              │  stdio / pipe               │  postMessage       │
│              ▼                             ▼                    │
│  ┌───────────────────┐          ┌──────────────────────┐       │
│  │  Crisp LSP Server │          │  WebView (React)     │       │
│  │  (C# process)     │◄────────►│  Tree Renderer       │       │
│  │                    │ custom   │  Node Graph          │       │
│  │  ┌──────────────┐ │ notif.   │  Property Panel      │       │
│  │  │  Query DB     │ │          │                      │       │
│  │  │  ┌─────────┐ │ │          │  ┌────────────────┐  │       │
│  │  │  │ Parse   │ │ │          │  │ D3.js          │  │       │
│  │  │  │ Lower   │ │ │          │  │ Tree Layout    │  │       │
│  │  │  │ Resolve │ │ │          │  └────────────────┘  │       │
│  │  │  │ Check   │ │ │          └──────────────────────┘       │
│  │  │  └─────────┘ │ │                                         │
│  │  │  Tree Layout  │ │                                         │
│  │  │  Engine       │ │                                         │
│  │  └──────────────┘ │                                         │
│  └───────────────────┘                                         │
└────────────────────────────────────────────────────────────────┘
```

### 3.2 プロセス構成

| プロセス | 言語 | 役割 |
|---|---|---|
| Extension Host | TypeScript (Node.js) | VSCode API の制御、LSP/WebView の橋渡し |
| Crisp LSP Server | C# (.NET) | パース、型チェック、ツリーレイアウト計算 |
| WebView | TypeScript (React) | ビジュアルツリーの描画・インタラクション |

### 3.3 通信経路

```
(A) テキストエディタ → LSP
    VSCode TextDocument API → LSP Client → LSP Server (stdio)
    標準の didOpen / didChange / completion / hover 等

(B) LSP → ビジュアルエディタ
    LSP Server --(custom notification)--> LSP Client
    LSP Client --(postMessage)--> WebView
    
    通知: crisp/treeLayout (ツリー構造JSON)
          crisp/diagnostics (診断情報)

(C) ビジュアルエディタ → テキストエディタ
    WebView --(postMessage)--> Extension Host
    Extension Host --(workspace.applyEdit)--> TextDocument
    
    コマンド: addNode, removeNode, moveNode, wrapNode, unwrapNode

(D) テキストエディタ → ビジュアルエディタ (カーソル同期)
    VSCode onDidChangeTextEditorSelection
    Extension Host --(postMessage)--> WebView
    
    通知: cursorPosition (カーソル位置のnodeId)
```

---

## 4. パッケージ構成

```
crisp-editor/
├── package.json                    # VSCode拡張マニフェスト
├── tsconfig.json
├── webpack.config.js               # Extension + WebView バンドル
│
├── src/
│   ├── extension/                   # Extension Host (TypeScript)
│   │   ├── extension.ts             #   エントリポイント
│   │   ├── lspClient.ts             #   LSP クライアント設定
│   │   ├── treeViewProvider.ts      #   WebViewパネル管理
│   │   ├── syncController.ts        #   テキスト↔ビジュアル同期
│   │   ├── cursorSync.ts            #   カーソル位置同期
│   │   ├── commands.ts              #   コマンド登録
│   │   └── types.ts                 #   共有型定義
│   │
│   ├── webview/                     # WebView (React + TypeScript)
│   │   ├── index.tsx                #   WebView エントリポイント
│   │   ├── App.tsx                  #   ルートコンポーネント
│   │   ├── components/
│   │   │   ├── TreeCanvas.tsx       #   キャンバス（パン・ズーム）
│   │   │   ├── TreeRenderer.tsx     #   ツリー描画（D3）
│   │   │   ├── NodeComponent.tsx    #   個別ノード描画
│   │   │   ├── EdgeComponent.tsx    #   エッジ（接続線）描画
│   │   │   ├── NodePalette.tsx      #   ノードパレット
│   │   │   ├── PropertyPanel.tsx    #   プロパティパネル
│   │   │   ├── Minimap.tsx          #   ミニマップ
│   │   │   ├── ContextMenu.tsx      #   右クリックメニュー
│   │   │   └── Toolbar.tsx          #   ツールバー
│   │   ├── hooks/
│   │   │   ├── useTreeLayout.ts     #   レイアウト計算フック
│   │   │   ├── useDragDrop.ts       #   ドラッグ&ドロップ
│   │   │   ├── useSelection.ts      #   ノード選択管理
│   │   │   ├── useZoomPan.ts        #   ズーム・パン
│   │   │   └── useVscodeApi.ts      #   VSCode API ブリッジ
│   │   ├── layout/
│   │   │   ├── treeLayout.ts        #   クライアント側レイアウト補助
│   │   │   └── constants.ts         #   レイアウト定数
│   │   ├── state/
│   │   │   ├── treeStore.ts         #   ツリー状態管理 (Zustand)
│   │   │   └── debugStore.ts        #   デバッグ状態管理
│   │   ├── styles/
│   │   │   ├── theme.ts             #   テーマ定義
│   │   │   ├── nodeStyles.ts        #   ノード種別ごとのスタイル
│   │   │   └── global.css           #   グローバルCSS
│   │   └── protocol/
│   │       ├── messages.ts          #   メッセージ型定義
│   │       └── bridge.ts            #   Extension↔WebView通信
│   │
│   └── shared/                      # Extension / WebView 共有
│       ├── treeModel.ts             #   ツリーデータモデル
│       ├── nodeTypes.ts             #   ノード種別定義
│       └── protocol.ts             #   通信プロトコル型
│
├── media/                           # 静的リソース
│   ├── icons/                       #   ノード種別アイコン (SVG)
│   │   ├── selector.svg
│   │   ├── sequence.svg
│   │   ├── parallel.svg
│   │   ├── check.svg
│   │   ├── action.svg
│   │   ├── guard.svg
│   │   ├── if.svg
│   │   ├── invert.svg
│   │   ├── repeat.svg
│   │   ├── timeout.svg
│   │   ├── cooldown.svg
│   │   ├── while.svg
│   │   ├── reactive.svg             # F1
│   │   └── reactive-select.svg      # F1
│   └── logo.png
│
├── syntaxes/
│   └── crisp.tmLanguage.json        # TextMate文法 (シンタックスハイライト)
│
├── language-configuration.json       # 括弧マッチ、コメント設定
│
└── test/
    ├── extension/
    │   ├── lspClient.test.ts
    │   └── syncController.test.ts
    └── webview/
        ├── TreeRenderer.test.tsx
        ├── useDragDrop.test.ts
        └── protocol.test.ts
```

---

## 5. LSP クライアント

### 5.1 起動と接続

```typescript
// lspClient.ts
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from "vscode-languageclient/node";

export function createLspClient(
  context: vscode.ExtensionContext
): LanguageClient {
  // Crisp LSP Server は .NET の実行可能ファイル
  const serverPath = context.asAbsolutePath("server/Crisp.Lsp");

  const serverOptions: ServerOptions = {
    run: { command: "dotnet", args: [serverPath] },
    debug: { command: "dotnet", args: [serverPath, "--debug"] },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "crisp" }],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher("**/*.crisp"),
    },
    // カスタム通知のハンドラ登録はここではなく起動後に行う
  };

  return new LanguageClient("crisp", "Crisp Language Server", serverOptions, clientOptions);
}
```

### 5.2 カスタム通知・リクエスト

```typescript
// LSP Server → Client (通知)
client.onNotification("crisp/treeLayout", (params: TreeLayoutNotification) => {
  syncController.onTreeLayoutUpdate(params);
});

client.onNotification("crisp/diagnostics", (params: TreeDiagnosticsNotification) => {
  syncController.onDiagnosticsUpdate(params);
});

// Client → LSP Server (リクエスト)
// ビジュアル編集コマンドをLSPに送信し、テキスト編集を取得
interface EditResult {
  edits: vscode.TextEdit[];
}

async function requestAddNode(params: AddNodeParams): Promise<EditResult> {
  return client.sendRequest("crisp/addNode", params);
}

async function requestRemoveNode(params: RemoveNodeParams): Promise<EditResult> {
  return client.sendRequest("crisp/removeNode", params);
}

async function requestMoveNode(params: MoveNodeParams): Promise<EditResult> {
  return client.sendRequest("crisp/moveNode", params);
}

async function requestWrapNode(params: WrapNodeParams): Promise<EditResult> {
  return client.sendRequest("crisp/wrapNode", params);
}

async function requestUnwrapNode(params: UnwrapNodeParams): Promise<EditResult> {
  return client.sendRequest("crisp/unwrapNode", params);
}
```

### 5.3 LSP Server 側の拡張

Crisp LSP Server に以下のハンドラを追加:

TreeLayout の再計算は `didChange` のたびに発生するため、
LSP Server 側で **100ms の debounce** を行う。
高速タイピング中はレイアウト再計算を抑制し、入力が止まった時点で1回だけ計算する。

```csharp
// TreeLayout の計算と通知
// didChange 後 100ms debounce で実行
private readonly Debouncer _layoutDebouncer = new(TimeSpan.FromMilliseconds(100));

void OnDocumentChanged(FileId fileId)
{
    _layoutDebouncer.Invoke(fileId, () =>
    {
        ComputeAndNotifyLayout(fileId);
    });
}

void ComputeAndNotifyLayout(FileId fileId)
{
    var ast = _db.Resolve(fileId);
    var diagnostics = _db.AllDiagnostics(fileId);
    var layout = TreeLayoutEngine.Compute(ast);

    NotifyClient("crisp/treeLayout", new TreeLayoutNotification
    {
        Uri = fileId.Path,
        Layout = layout
    });
}

// 編集リクエストの処理
// ビジュアル操作 → CST変更 → テキスト編集を返す
EditResult HandleAddNode(AddNodeParams p)
{
    var cst = _db.Parse(p.FileId);
    var parentNode = cst.FindById(p.ParentNodeId);
    var newCst = CstEditor.InsertChild(cst, parentNode, p.Position, p.NodeTemplate);
    var formatted = Formatter.Format(newCst);
    return ComputeTextEdits(cst, formatted);
}

EditResult HandleMoveNode(MoveNodeParams p)
{
    var cst = _db.Parse(p.FileId);
    var node = cst.FindById(p.NodeId);
    var newParent = cst.FindById(p.NewParentId);
    var newCst = CstEditor.MoveChild(cst, node, newParent, p.NewPosition);
    var formatted = Formatter.Format(newCst);
    return ComputeTextEdits(cst, formatted);
}
```

`CstEditor` は CST に対する構造変更操作を提供するユーティリティ:

```csharp
public static class CstEditor
{
    /// 子ノードを挿入する
    public static CstProgram InsertChild(
        CstProgram root,
        CstNode parent,
        int position,          // 何番目の子として挿入するか
        CstNode template)      // 挿入するノードのテンプレート
    {
        // Immutable な CST を新しい CST として再構築
        // 挿入位置の前後のTrivia を適切に調整
    }

    /// 子ノードを移動する
    public static CstProgram MoveChild(
        CstProgram root,
        CstNode node,
        CstNode newParent,
        int newPosition)
    {
        // 元の位置から削除 → 新しい位置に挿入
        // Triviaの再配置
    }

    /// 子ノードを削除する
    public static CstProgram RemoveChild(CstProgram root, CstNode node)
    {
        // ノードを削除し、前後のTriviaを調整
    }

    /// ノードをデコレータで包む
    public static CstProgram WrapWithDecorator(
        CstProgram root,
        CstNode node,
        string decoratorType,    // "guard", "timeout" 等
        string[]? args)          // デコレータの引数
    {
        // (guard <condition> <node>) のようなCST構造を構築
    }

    /// デコレータを解除する
    public static CstProgram Unwrap(CstProgram root, CstNode decoratorNode)
    {
        // デコレータの子ノードを、デコレータの位置に昇格
    }
}
```

---

## 6. ビジュアルエディタ (WebView)

### 6.1 WebView Provider

```typescript
// treeViewProvider.ts
export class CrispTreeViewProvider implements vscode.WebviewViewProvider {
  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly syncController: SyncController
  ) {}

  resolveWebviewView(webviewView: vscode.WebviewView) {
    webviewView.webview.options = {
      enableScripts: true,
      localResourceRoots: [this.extensionUri],
    };

    webviewView.webview.html = this.getHtml(webviewView.webview);

    // WebView → Extension のメッセージハンドリング
    webviewView.webview.onDidReceiveMessage((msg) => {
      this.syncController.handleWebViewMessage(msg);
    });

    // Extension → WebView の接続を SyncController に登録
    this.syncController.registerWebView(webviewView.webview);
  }
}
```

Side Panel ではなくエディタ横のパネルとして開く場合は
`WebviewPanel` を使用:

```typescript
// コマンド: "crisp.openTreeView"
export function openTreeViewPanel(
  context: vscode.ExtensionContext,
  syncController: SyncController,
  document: vscode.TextDocument
) {
  const panel = vscode.window.createWebviewPanel(
    "crispTreeView",
    `Tree: ${path.basename(document.fileName)}`,
    vscode.ViewColumn.Beside,
    {
      enableScripts: true,
      retainContextWhenHidden: true,     // タブ切り替え時も状態保持
      localResourceRoots: [context.extensionUri],
    }
  );

  syncController.registerWebView(panel.webview, document.uri);

  panel.onDidDispose(() => {
    syncController.unregisterWebView(document.uri);
  });
}
```

### 6.2 React アプリケーション構成

```tsx
// App.tsx
import { useTreeStore } from "./state/treeStore";
import { TreeCanvas } from "./components/TreeCanvas";
import { NodePalette } from "./components/NodePalette";
import { PropertyPanel } from "./components/PropertyPanel";
import { Toolbar } from "./components/Toolbar";

export function App() {
  const tree = useTreeStore((s) => s.tree);
  const selectedNodeId = useTreeStore((s) => s.selectedNodeId);

  return (
    <div className="crisp-editor">
      <Toolbar />
      <div className="crisp-editor-main">
        <NodePalette />
        <TreeCanvas tree={tree} selectedNodeId={selectedNodeId} />
        <PropertyPanel nodeId={selectedNodeId} />
      </div>
    </div>
  );
}
```

### 6.3 ツリーキャンバス

```tsx
// TreeCanvas.tsx
import { useZoomPan } from "../hooks/useZoomPan";
import { TreeRenderer } from "./TreeRenderer";
import { Minimap } from "./Minimap";

export function TreeCanvas({
  tree,
  selectedNodeId,
}: {
  tree: TreeLayout | null;
  selectedNodeId: number | null;
}) {
  const { transform, handlers } = useZoomPan();

  if (!tree) {
    return <div className="crisp-empty">No tree to display</div>;
  }

  return (
    <div className="crisp-canvas" {...handlers}>
      <svg className="crisp-canvas-svg">
        <g transform={`translate(${transform.x},${transform.y}) scale(${transform.scale})`}>
          <TreeRenderer tree={tree} selectedNodeId={selectedNodeId} />
        </g>
      </svg>
      <Minimap tree={tree} transform={transform} />
    </div>
  );
}
```

### 6.4 ノードコンポーネント

```tsx
// NodeComponent.tsx
import { useDragDrop } from "../hooks/useDragDrop";
import { useSelection } from "../hooks/useSelection";
import { NODE_STYLES } from "../styles/nodeStyles";

interface NodeProps {
  node: TreeNode;
  x: number;
  y: number;
  isSelected: boolean;
  isCursorTarget: boolean;
  diagnosticLevel: "none" | "warning" | "error";
  debugStatus?: "success" | "failure" | "running" | "idle";
}

export function NodeComponent({
  node,
  x,
  y,
  isSelected,
  isCursorTarget,
  diagnosticLevel,
  debugStatus,
}: NodeProps) {
  const { dragHandlers, isDragging } = useDragDrop(node.id);
  const { onClick, onDoubleClick } = useSelection(node.id);

  const style = NODE_STYLES[node.type];
  const width = style.width;
  const height = style.height;

  return (
    <g
      transform={`translate(${x - width / 2}, ${y})`}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
      {...dragHandlers}
      className={[
        "crisp-node",
        isSelected && "crisp-node--selected",
        isCursorTarget && "crisp-node--cursor",
        isDragging && "crisp-node--dragging",
        diagnosticLevel !== "none" && `crisp-node--${diagnosticLevel}`,
        debugStatus && `crisp-node--debug-${debugStatus}`,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      {/* 背景 */}
      <rect
        width={width}
        height={height}
        rx={style.borderRadius}
        fill={style.backgroundColor}
        stroke={style.borderColor}
        strokeWidth={isSelected ? 2 : 1}
      />

      {/* アイコン */}
      <image
        href={style.iconUri}
        x={8}
        y={(height - 16) / 2}
        width={16}
        height={16}
      />

      {/* ラベル */}
      <text
        x={32}
        y={height / 2}
        dominantBaseline="central"
        fill={style.textColor}
        fontSize={12}
        fontFamily="var(--vscode-editor-font-family)"
      >
        {node.label}
      </text>

      {/* Diagnosticバッジ */}
      {diagnosticLevel === "error" && (
        <circle cx={width - 8} cy={8} r={6} fill="var(--vscode-errorForeground)" />
      )}
      {diagnosticLevel === "warning" && (
        <circle cx={width - 8} cy={8} r={6} fill="var(--vscode-warningForeground)" />
      )}

      {/* デバッグステータスインジケータ */}
      {debugStatus === "running" && (
        <rect
          x={0}
          y={height - 3}
          width={width}
          height={3}
          fill="var(--vscode-debugIcon-startForeground)"
          className="crisp-node-debug-pulse"
        />
      )}
    </g>
  );
}
```

---

## 7. 双方向同期プロトコル

### 7.1 SyncController

同期の中枢。テキストエディタ、LSPサーバー、WebViewの3者間を仲介する。

```typescript
// syncController.ts
export class SyncController {
  private webviews = new Map<string, vscode.Webview>(); // uri → webview
  private treeLayouts = new Map<string, TreeLayout>();   // uri → layout
  private isApplyingEdit = false;                        // 再帰防止フラグ

  constructor(private lspClient: LanguageClient) {}

  // --- LSP → WebView ---

  onTreeLayoutUpdate(params: TreeLayoutNotification) {
    const layout = params.layout;
    this.treeLayouts.set(params.uri, layout);

    const webview = this.webviews.get(params.uri);
    if (webview) {
      webview.postMessage({
        type: "treeLayout",
        payload: layout,
      });
    }
  }

  onDiagnosticsUpdate(params: TreeDiagnosticsNotification) {
    const webview = this.webviews.get(params.uri);
    if (webview) {
      webview.postMessage({
        type: "diagnostics",
        payload: params.diagnostics,
      });
    }
  }

  // --- WebView → テキストエディタ ---

  async handleWebViewMessage(msg: WebViewMessage) {
    switch (msg.type) {
      case "addNode":
        await this.applyLspEdit("crisp/addNode", msg.payload);
        break;
      case "removeNode":
        await this.applyLspEdit("crisp/removeNode", msg.payload);
        break;
      case "moveNode":
        await this.applyLspEdit("crisp/moveNode", msg.payload);
        break;
      case "wrapNode":
        await this.applyLspEdit("crisp/wrapNode", msg.payload);
        break;
      case "unwrapNode":
        await this.applyLspEdit("crisp/unwrapNode", msg.payload);
        break;
      case "nodeClicked":
        this.jumpToNode(msg.payload.nodeId, msg.payload.uri);
        break;
    }
  }

  private async applyLspEdit(method: string, params: any) {
    this.isApplyingEdit = true;
    try {
      const result: EditResult = await this.lspClient.sendRequest(method, params);
      const uri = vscode.Uri.parse(params.uri);
      const edit = new vscode.WorkspaceEdit();
      for (const e of result.edits) {
        edit.replace(uri, e.range, e.newText);
      }
      await vscode.workspace.applyEdit(edit);
    } finally {
      this.isApplyingEdit = false;
    }
  }

  // --- テキストエディタ → WebView (カーソル同期) ---

  onCursorPositionChanged(uri: string, offset: number) {
    const layout = this.treeLayouts.get(uri);
    if (!layout) return;

    // テキスト上のオフセットから対応するnodeIdを特定
    const nodeId = this.findNodeAtOffset(layout, offset);
    const webview = this.webviews.get(uri);
    if (webview && nodeId !== null) {
      webview.postMessage({
        type: "cursorSync",
        payload: { nodeId },
      });
    }
  }

  private findNodeAtOffset(layout: TreeLayout, offset: number): number | null {
    // ツリーを走査し、origin.start <= offset < origin.start + origin.length
    // を満たす最も深いノードのIdを返す
    function walk(node: TreeNode): number | null {
      if (offset < node.origin.start || offset >= node.origin.start + node.origin.length)
        return null;

      // 子を先に探索（より深いマッチを優先）
      for (const child of node.children ?? []) {
        const found = walk(child);
        if (found !== null) return found;
      }
      return node.id;
    }
    return walk(layout.root);
  }

  // --- テキスト変更フィルタ ---

  onDocumentChanged(uri: string) {
    // ビジュアル編集によるテキスト変更は無視（ループ防止）
    if (this.isApplyingEdit) return;

    // LSPが自動でtreeLayout通知を送るので、ここでは何もしない
    // (LSP の didChange ハンドラが処理)
  }

  // --- ノードクリック → テキストジャンプ ---

  private jumpToNode(nodeId: number, uri: string) {
    const layout = this.treeLayouts.get(uri);
    if (!layout) return;

    const node = this.findNodeById(layout, nodeId);
    if (!node) return;

    const doc = vscode.workspace.textDocuments.find(
      (d) => d.uri.toString() === uri
    );
    if (!doc) return;

    const pos = doc.positionAt(node.origin.start);
    const editor = vscode.window.visibleTextEditors.find(
      (e) => e.document.uri.toString() === uri
    );
    if (editor) {
      editor.selection = new vscode.Selection(pos, pos);
      editor.revealRange(new vscode.Range(pos, pos));
    }
  }
}
```

### 7.2 同期ループの防止

```
テキスト編集 → LSP didChange → treeLayout通知 → WebView更新
  ↑                                                    │
  └──── workspace.applyEdit ←── crisp/addNode ←────────┘

このループを防止するため:
1. isApplyingEdit フラグでビジュアル起因の変更を検知
2. ビジュアル起因の変更でも LSP didChange は発火するが、
   LSP が treeLayout を再計算して WebView に送り返す
3. WebView は新しい treeLayout を受け取り、差分で更新
4. ノード位置が変わらなければ描画は変化しない
```

同期は **LSP Server の treeLayout が Single Source of Truth** であるため、
テキスト起因でもビジュアル起因でも最終的に LSP の出力で一致する。

### 7.3 メッセージ型定義

```typescript
// protocol.ts

// === Extension → WebView ===

interface TreeLayoutMessage {
  type: "treeLayout";
  payload: TreeLayout;
}

interface DiagnosticsMessage {
  type: "diagnostics";
  payload: NodeDiagnostic[];
}

interface CursorSyncMessage {
  type: "cursorSync";
  payload: { nodeId: number };
}

interface DebugStateMessage {
  type: "debugState";
  payload: DebugTreeState;
}

type ExtensionToWebViewMessage =
  | TreeLayoutMessage
  | DiagnosticsMessage
  | CursorSyncMessage
  | DebugStateMessage;

// === WebView → Extension ===

interface AddNodeMessage {
  type: "addNode";
  payload: {
    uri: string;
    parentNodeId: number;
    position: number;
    nodeType: string;
    args?: Record<string, string>;
  };
}

interface RemoveNodeMessage {
  type: "removeNode";
  payload: { uri: string; nodeId: number };
}

interface MoveNodeMessage {
  type: "moveNode";
  payload: {
    uri: string;
    nodeId: number;
    newParentId: number;
    newPosition: number;
  };
}

interface WrapNodeMessage {
  type: "wrapNode";
  payload: {
    uri: string;
    nodeId: number;
    decoratorType: string;
    args?: string[];
  };
}

interface UnwrapNodeMessage {
  type: "unwrapNode";
  payload: { uri: string; nodeId: number };
}

interface NodeClickedMessage {
  type: "nodeClicked";
  payload: { uri: string; nodeId: number };
}

type WebViewToExtensionMessage =
  | AddNodeMessage
  | RemoveNodeMessage
  | MoveNodeMessage
  | WrapNodeMessage
  | UnwrapNodeMessage
  | NodeClickedMessage;
```

---

## 8. ツリーレイアウトエンジン

### 8.1 設計方針

ツリーレイアウトは **LSP Server 側で計算** する。

理由:
- LSPはASTを持っており、ノードの構造情報に直接アクセスできる
- レイアウト結果をキャッシュし、増分更新できる
- WebView側はレイアウト座標を受け取って描画するだけ

### 8.2 レイアウトアルゴリズム

Reingold-Tilford アルゴリズムのバリエーションを使用:

```
1. 各ノードのサイズ計算（ラベル幅に基づく）
2. Post-order traversal で各ノードのXオフセットを計算
   - 子ノード群の合計幅を計算
   - 親ノードを子の中央に配置
3. Pre-order traversal で絶対座標に変換
4. エッジ情報を生成
```

### 8.3 TreeLayout データモデル

```typescript
// treeModel.ts

interface TreeLayout {
  fileUri: string;
  treeName: string;
  root: TreeNode;
  edges: TreeEdge[];
  bounds: { width: number; height: number };
}

interface TreeNode {
  id: number;                      // IrNode.Id (= CstNode.Id)
  type: NodeType;                  // "selector" | "sequence" | "check" | ...
  label: string;                   // 表示ラベル ("< .Health 30", "Flee()" 等)
  x: number;                      // レイアウト計算済みX座標
  y: number;                      // レイアウト計算済みY座標
  width: number;                  // ノード幅
  height: number;                 // ノード高
  origin: TextSpan;               // ソース上の位置
  resolvedType?: string;          // 式の解決型 ("bool", "float" 等)
  diagnostics: NodeDiagnostic[];  // このノードに付与された診断
  children?: TreeNode[];          // 子ノード
  collapsed: boolean;             // 折りたたみ状態
}

interface TreeEdge {
  fromId: number;
  toId: number;
  fromPort: "bottom";              // 常に親の下
  toPort: "top";                   // 常に子の上
}

interface TextSpan {
  start: number;
  length: number;
}

**Origin の更新保証**: `origin` フィールドは常にソーステキスト上の実位置を反映する。
以下の操作の後も LSP Server が Origin を再計算し、カーソル同期の正確性を保証する:
- フォーマッタによる再整形
- CstEditor による構造変更（ノード追加・移動・削除）
- マクロ展開（F10、将来）

interface NodeDiagnostic {
  code: string;                    // "BS0001"
  message: string;
  severity: "error" | "warning" | "info";
}

type NodeType =
  | "selector"
  | "sequence"
  | "parallel"
  | "check"
  | "action"
  | "guard"
  | "if"
  | "invert"
  | "repeat"
  | "timeout"
  | "cooldown"
  | "while"
  | "reactive"         // F1
  | "reactive-select"  // F1
  | "ref"              // F11
  | "defdec-call"      // F2
  | "unknown";
```

### 8.4 レイアウト定数

```typescript
// constants.ts

export const LAYOUT = {
  NODE_MIN_WIDTH: 120,
  NODE_MAX_WIDTH: 250,
  NODE_HEIGHT: 36,
  NODE_PADDING_X: 12,
  NODE_PADDING_Y: 8,
  LEVEL_GAP: 60,          // 親子間の垂直距離
  SIBLING_GAP: 16,        // 兄弟間の水平距離
  SUBTREE_GAP: 24,        // サブツリー間の水平距離
} as const;
```

### 8.5 ラベル生成ルール

| ノード種別 | ラベル例 |
|---|---|
| selector | `selector` |
| sequence | `sequence` |
| parallel | `parallel :any` / `parallel :all` / `parallel :n 2` |
| check | `< .Health 30` (式のテキスト表現) |
| action (引数なし) | `Patrol()` |
| action (引数あり) | `Attack(.Target)` |
| guard | `guard: .IsAlive` |
| if | `if: (and .X .Y)` |
| invert | `invert` |
| repeat | `repeat 3×` |
| timeout | `timeout 5.0s` |
| cooldown | `cooldown 2.5s` |
| while | `while: > .Stamina 0` |
| ref (F11) | `→ CombatBehavior` |

---

## 9. ノードパレット & プロパティパネル

### 9.1 ノードパレット

画面左側に表示される、ドラッグ可能なノード種別一覧。

```tsx
// NodePalette.tsx
const NODE_PALETTE_ITEMS = [
  {
    category: "Composites",
    items: [
      { type: "selector", label: "Selector", icon: "selector.svg" },
      { type: "sequence", label: "Sequence", icon: "sequence.svg" },
      { type: "parallel", label: "Parallel", icon: "parallel.svg" },
    ],
  },
  {
    category: "Decorators",
    items: [
      { type: "guard", label: "Guard", icon: "guard.svg" },
      { type: "if", label: "If / Else", icon: "if.svg" },
      { type: "invert", label: "Invert", icon: "invert.svg" },
      { type: "repeat", label: "Repeat", icon: "repeat.svg" },
      { type: "timeout", label: "Timeout", icon: "timeout.svg" },
      { type: "cooldown", label: "Cooldown", icon: "cooldown.svg" },
      { type: "while", label: "While", icon: "while.svg" },
    ],
  },
  {
    category: "Leaves",
    items: [
      { type: "check", label: "Check", icon: "check.svg" },
      { type: "action", label: "Action", icon: "action.svg" },
    ],
  },
];
```

パレットからキャンバスへのドラッグ時:
1. ドロップ位置に最も近い親ノードを特定
2. 挿入位置（何番目の子か）を計算
3. `addNode` メッセージを送信
4. LSPがテンプレートCST生成 → テキスト編集 → treeLayout更新

### 9.2 ノードテンプレート

新しいノードを追加する際のデフォルトテンプレート:

| ノード種別 | 生成されるDSL |
|---|---|
| selector | `(select\n  )` |
| sequence | `(seq\n  )` |
| parallel | `(parallel :any\n  )` |
| check | `(check )` |
| action | `(.)` |
| guard | `(guard \n  )` |
| if | `(if \n  \n  )` |
| invert | `(invert\n  )` |
| repeat | `(repeat 1\n  )` |
| timeout | `(timeout 1.0\n  )` |
| cooldown | `(cooldown 1.0\n  )` |
| while | `(while \n  )` |

テンプレート挿入後、テキストエディタにフォーカスを移して
ユーザーが条件式やメンバー名を入力できるようにする。

### 9.3 プロパティパネル

選択ノードの詳細情報を表示する、画面右側のパネル。

```tsx
// PropertyPanel.tsx
interface PropertyPanelProps {
  nodeId: number | null;
}

export function PropertyPanel({ nodeId }: PropertyPanelProps) {
  const node = useTreeStore((s) =>
    nodeId ? s.findNode(nodeId) : null
  );

  if (!node) {
    return <div className="crisp-props-empty">Select a node</div>;
  }

  return (
    <div className="crisp-props">
      <div className="crisp-props-header">
        <img src={getNodeIcon(node.type)} />
        <span>{node.type}</span>
      </div>

      <div className="crisp-props-section">
        <label>Label</label>
        <code>{node.label}</code>
      </div>

      {node.resolvedType && (
        <div className="crisp-props-section">
          <label>Type</label>
          <code>{node.resolvedType}</code>
        </div>
      )}

      {node.origin && (
        <div className="crisp-props-section">
          <label>Source</label>
          <code>offset {node.origin.start}..{node.origin.start + node.origin.length}</code>
        </div>
      )}

      {node.diagnostics.length > 0 && (
        <div className="crisp-props-section">
          <label>Diagnostics</label>
          {node.diagnostics.map((d) => (
            <div key={d.code} className={`crisp-diag crisp-diag--${d.severity}`}>
              <span className="crisp-diag-code">{d.code}</span>
              <span>{d.message}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
```

---

## 10. デバッガ統合 (F7)

### 10.1 概要

F7 (Debugger) が実装された後、ビジュアルエディタでリアルタイムの
実行状態を可視化する。

### 10.2 Debug Adapter Protocol (DAP)

VSCode の DAP を利用してデバッガを統合:

```typescript
// extension.ts
vscode.debug.registerDebugAdapterDescriptorFactory("crisp", {
  createDebugAdapterDescriptor(session) {
    // Crisp Debug Adapter をインプロセスで起動
    return new vscode.DebugAdapterInlineImplementation(
      new CrispDebugAdapter()
    );
  },
});
```

### 10.3 launch.json 設定

```json
{
  "type": "crisp",
  "request": "attach",
  "name": "Attach to Crisp BT",
  "host": "localhost",
  "port": 9229,
  "bscriptFile": "${workspaceFolder}/Assets/AI/EnemyAI.crisp"
}
```

### 10.4 デバッグ状態の可視化

ゲームランタイムが WebSocket で Debug Protocol メッセージを送信:

```
Runtime → Debug Adapter → Extension Host → WebView

node_enter(nodeId: 3, type: "check")
  → ノード3を黄色（評価中）に

node_exit(nodeId: 3, status: "success")
  → ノード3を緑（成功）に、0.5秒後にフェードアウト

node_exit(nodeId: 5, status: "failure")
  → ノード5を赤（失敗）に、0.5秒後にフェードアウト

node_exit(nodeId: 7, status: "running")
  → ノード7を黄色パルスアニメーション（Running中）
```

### 10.5 ブレークポイント

ビジュアルエディタ上でノードを右クリック → "Toggle Breakpoint":

```typescript
// WebView → Extension
{ type: "toggleBreakpoint", payload: { nodeId: 5, uri: "..." } }

// Extension → DAP
debug.addBreakpoints([
  new vscode.SourceBreakpoint(
    new vscode.Location(uri, positionFromNodeId(5))
  )
]);
```

ブレークポイントが設定されたノードには赤い丸印が表示される。

### 10.6 DebugTreeState

```typescript
interface DebugTreeState {
  tick: number;
  frameIndex: number;
  nodes: Map<number, NodeDebugInfo>;
}

interface NodeDebugInfo {
  status: "idle" | "evaluating" | "success" | "failure" | "running";
  lastTickResult?: "success" | "failure" | "running";
  exprValues?: Record<string, string>;    // "Health" → "25.5"
  hasBreakpoint: boolean;
}
```

---

## 11. テーマ & スタイル

### 11.1 VSCode テーマ統合

WebView 内で VSCode のテーマカラーを使用する:

```css
/* global.css */
.crisp-editor {
  --bg: var(--vscode-editor-background);
  --fg: var(--vscode-editor-foreground);
  --border: var(--vscode-panel-border);
  --accent: var(--vscode-focusBorder);
  --error: var(--vscode-errorForeground);
  --warning: var(--vscode-editorWarning-foreground);
  --font: var(--vscode-editor-font-family);
  --font-size: var(--vscode-editor-font-size);
}
```

### 11.2 ノード種別ごとのスタイル

```typescript
// nodeStyles.ts

export const NODE_STYLES: Record<NodeType, NodeStyle> = {
  selector: {
    backgroundColor: "var(--vscode-charts-purple)",
    borderColor: "var(--vscode-charts-purple)",
    textColor: "var(--bg)",
    iconUri: "selector.svg",
    borderRadius: 8,
    width: 140,
    height: 36,
  },
  sequence: {
    backgroundColor: "var(--vscode-charts-blue)",
    borderColor: "var(--vscode-charts-blue)",
    textColor: "var(--bg)",
    iconUri: "sequence.svg",
    borderRadius: 8,
    width: 140,
    height: 36,
  },
  parallel: {
    backgroundColor: "var(--vscode-charts-orange)",
    borderColor: "var(--vscode-charts-orange)",
    textColor: "var(--bg)",
    iconUri: "parallel.svg",
    borderRadius: 8,
    width: 160,
    height: 36,
  },
  check: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-green)",
    textColor: "var(--fg)",
    iconUri: "check.svg",
    borderRadius: 4,
    width: 160,
    height: 36,
  },
  action: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-yellow)",
    textColor: "var(--fg)",
    iconUri: "action.svg",
    borderRadius: 4,
    width: 160,
    height: 36,
  },
  guard: {
    backgroundColor: "var(--vscode-editor-background)",
    borderColor: "var(--vscode-charts-red)",
    textColor: "var(--fg)",
    iconUri: "guard.svg",
    borderRadius: 12,
    width: 180,
    height: 36,
  },
  // ... 他のノード種別も同様
};
```

### 11.3 エッジスタイル

```css
.crisp-edge {
  stroke: var(--vscode-editorWidget-border);
  stroke-width: 1.5;
  fill: none;
}

/* ベジェカーブでなめらかな接続線 */
.crisp-edge path {
  d: path("M x1,y1 C x1,y1+gap/2 x2,y2-gap/2 x2,y2");
}
```

### 11.4 アニメーション

```css
/* デバッグ: Running パルス */
@keyframes crisp-pulse {
  0% { opacity: 0.4; }
  50% { opacity: 1.0; }
  100% { opacity: 0.4; }
}

.crisp-node--debug-running .crisp-node-debug-pulse {
  animation: crisp-pulse 1s infinite;
}

/* デバッグ: Success/Failure フラッシュ */
@keyframes crisp-flash {
  0% { opacity: 1; }
  100% { opacity: 0; }
}

.crisp-node--debug-success {
  box-shadow: 0 0 8px var(--vscode-testing-iconPassed);
  animation: crisp-flash 0.5s forwards;
}

.crisp-node--debug-failure {
  box-shadow: 0 0 8px var(--vscode-testing-iconFailed);
  animation: crisp-flash 0.5s forwards;
}

/* ドラッグ中 */
.crisp-node--dragging {
  opacity: 0.6;
  filter: drop-shadow(0 4px 8px rgba(0, 0, 0, 0.3));
}
```

---

## 12. 設定

### 12.1 拡張設定 (settings.json)

```json
{
  "crisp.lsp.path": "",
  "crisp.lsp.args": [],
  "crisp.treeView.defaultOpen": false,
  "crisp.treeView.layout": "side",
  "crisp.treeView.showMinimap": true,
  "crisp.treeView.showPropertyPanel": true,
  "crisp.treeView.animateDebugState": true,
  "crisp.treeView.autoFitOnChange": false,
  "crisp.treeView.nodeSpacing.horizontal": 16,
  "crisp.treeView.nodeSpacing.vertical": 60,
  "crisp.formatter.indentSize": 2,
  "crisp.formatter.maxLineWidth": 80,
  "crisp.debug.port": 9229
}
```

| 設定 | デフォルト | 説明 |
|---|---|---|
| `lsp.path` | 拡張同梱 | LSPサーバーのパス |
| `treeView.defaultOpen` | `false` | .crisp を開いた時に自動でTree Viewを開くか |
| `treeView.layout` | `"side"` | `"side"` (横) or `"below"` (下) |
| `treeView.showMinimap` | `true` | ミニマップの表示 |
| `treeView.showPropertyPanel` | `true` | プロパティパネルの表示 |
| `treeView.animateDebugState` | `true` | デバッグ時のアニメーション |
| `treeView.autoFitOnChange` | `false` | テキスト変更時にツリー全体をフィット |

### 12.2 package.json 抜粋

```json
{
  "name": "crisp-editor",
  "displayName": "Crisp - Behavior Tree DSL",
  "description": "Language support and visual editor for the Crisp behavior tree DSL",
  "version": "0.1.0",
  "engines": { "vscode": "^1.85.0" },
  "categories": ["Programming Languages", "Visualization"],
  "activationEvents": [
    "onLanguage:crisp",
    "onCommand:crisp.openTreeView"
  ],
  "main": "./dist/extension.js",
  "contributes": {
    "languages": [
      {
        "id": "crisp",
        "aliases": ["Crisp", "BScript"],
        "extensions": [".crisp", ".crisp"],
        "configuration": "./language-configuration.json"
      }
    ],
    "grammars": [
      {
        "language": "crisp",
        "scopeName": "source.crisp",
        "path": "./syntaxes/crisp.tmLanguage.json"
      }
    ],
    "commands": [
      {
        "command": "crisp.openTreeView",
        "title": "Open Tree View",
        "category": "Crisp",
        "icon": "$(type-hierarchy)"
      },
      {
        "command": "crisp.fitTree",
        "title": "Fit Tree to View",
        "category": "Crisp"
      }
    ],
    "menus": {
      "editor/title": [
        {
          "command": "crisp.openTreeView",
          "when": "resourceLangId == crisp",
          "group": "navigation"
        }
      ],
      "editor/context": [
        {
          "command": "crisp.openTreeView",
          "when": "resourceLangId == crisp"
        }
      ]
    },
    "configuration": {
      "title": "Crisp",
      "properties": {
        "crisp.lsp.path": { "type": "string", "default": "" },
        "crisp.treeView.defaultOpen": { "type": "boolean", "default": false },
        "crisp.treeView.layout": {
          "type": "string",
          "enum": ["side", "below"],
          "default": "side"
        },
        "crisp.treeView.showMinimap": { "type": "boolean", "default": true },
        "crisp.treeView.showPropertyPanel": { "type": "boolean", "default": true }
      }
    },
    "breakpoints": [
      { "language": "crisp" }
    ],
    "debuggers": [
      {
        "type": "crisp",
        "label": "Crisp Behavior Tree",
        "configurationAttributes": {
          "attach": {
            "properties": {
              "host": { "type": "string", "default": "localhost" },
              "port": { "type": "number", "default": 9229 },
              "bscriptFile": { "type": "string" }
            }
          }
        }
      }
    ]
  }
}
```

---

## 13. テスト戦略

### 13.1 テスト層

| 層 | 対象 | フレームワーク |
|---|---|---|
| Unit | SyncController, レイアウト計算, プロトコル | Vitest |
| Component | React コンポーネント (NodeComponent等) | Vitest + Testing Library |
| Integration | Extension ↔ LSP ↔ WebView の結合 | @vscode/test-electron |
| E2E | 実際のVSCodeでの操作 | @vscode/test-electron |

### 13.2 テストケース（主要なもの）

#### SyncController

```
- テキスト変更 → treeLayout 通知 → WebView postMessage
- WebView addNode → LSP request → workspace.applyEdit
- カーソル位置変更 → nodeId 特定 → WebView cursorSync
- isApplyingEdit フラグによるループ防止
- WebView 未接続時の graceful degradation
```

#### ツリーレイアウト

```
- 単一ノード → 中央配置
- 2子ノード → 左右均等配置
- 深いネスト → 正しい垂直配置
- ノード幅が異なる場合 → 重ならない
- 折りたたみ → 子が非表示
```

#### ドラッグ&ドロップ

```
- ノードを別の親にドロップ → moveNode メッセージ
- ノードを同じ親の別位置にドロップ → moveNode (位置変更)
- 自分自身の子にドロップ → 拒否
- ルートノードのドロップ → 拒否
```

#### カーソル同期

```
- テキスト上でcheck式内にカーソル → ビジュアルでcheckノードがハイライト
- ビジュアルでノードダブルクリック → テキストの対応位置にジャンプ
- 該当ノードなし（コメント内等） → ハイライトなし
```

### 13.3 スナップショットテスト

ツリーレイアウト JSON のスナップショット:

```
Pattern1_Minimal/
  source.crisp
  treeLayout.verified.json     ← レイアウト結果
```

Pattern 1〜4 のサンプルDSL（crisp-sample-patterns-v2.md）を入力として、
レイアウト結果のJSONをスナップショットで固定する。

---

## 14. 実装ロードマップ

### Phase A: テキストエディタ支援（LSP不要の部分）

| ステップ | 内容 | 工数 |
|---|---|---|
| A1 | TextMate文法 (シンタックスハイライト) | 2日 |
| A2 | language-configuration (括弧マッチ、コメント) | 1日 |
| A3 | 拡張の基本構造 (activate, commands) | 1日 |

### Phase B: LSP統合

| ステップ | 内容 | 工数 |
|---|---|---|
| B1 | LSP Client セットアップ (vscode-languageclient) | 1日 |
| B2 | LSP Server のバンドル (拡張に同梱) | 2日 |
| B3 | 標準LSP機能の動作確認 (completion, hover, diagnostics) | 2日 |
| B4 | カスタム通知 (crisp/treeLayout) の実装 | 3日 |

### Phase C: ビジュアルエディタ（閲覧専用）

| ステップ | 内容 | 工数 |
|---|---|---|
| C1 | WebView Provider + React アプリ基盤 | 2日 |
| C2 | TreeRenderer (D3, 読み取り専用) | 5日 |
| C3 | NodeComponent + EdgeComponent | 3日 |
| C4 | ズーム・パン | 2日 |
| C5 | ミニマップ | 2日 |
| C6 | テーマ統合 (VSCodeカラー) | 1日 |
| C7 | カーソル同期 (テキスト→ビジュアル、ビジュアル→テキスト) | 3日 |
| C8 | プロパティパネル | 2日 |
| C9 | Diagnostic表示 (エラーバッジ) | 1日 |

### Phase D: ビジュアル編集

| ステップ | 内容 | 工数 |
|---|---|---|
| D1 | SyncController + 双方向同期基盤 | 3日 |
| D2 | LSP Server 側 CstEditor 実装 | 5日 |
| D3 | ノードパレット | 2日 |
| D4 | ドラッグ&ドロップ (ノード移動) | 5日 |
| D5 | ノード追加・削除 | 3日 |
| D6 | デコレータ Wrap / Unwrap | 2日 |
| D7 | 右クリックメニュー | 2日 |
| D8 | キーボードショートカット | 2日 |
| D9 | Undo/Redo 同期 | 2日 |

### Phase E: デバッガ統合 (F7 実装後)

**初期スコープ**: Tree 可視化に特化する。VSCode 標準の Variables pane / Call Stack pane との統合は将来対応。
初期は「ツリー上のどのノードがどの状態か」をビジュアルで見ることに集中する。

| ステップ | 内容 | 工数 |
|---|---|---|
| E1 | Debug Adapter 実装 | 5日 |
| E2 | デバッグ状態可視化 (Success/Failure/Running) | 3日 |
| E3 | ブレークポイント (設定・表示) | 3日 |
| E4 | ステップ実行 UI | 2日 |

### 合計工数見積もり

| Phase | 工数 |
|---|---|
| A: テキスト支援 | 4日 |
| B: LSP統合 | 8日 |
| C: ビジュアル(閲覧) | 21日 |
| D: ビジュアル(編集) | 26日 |
| E: デバッガ | 13日 |
| **合計** | **約72日 (3.5ヶ月)** |

### MVP (Minimum Viable Product)

**Phase A + B + C** で MVP とする（約33日 / 1.5ヶ月）。

MVP では:
- テキスト編集 + LSP（補完、診断、フォーマット）
- ビジュアルツリーの閲覧（読み取り専用）
- テキスト↔ビジュアルのカーソル同期
- Diagnosticの視覚的表示

が動作する。ビジュアル側からの編集は Phase D で追加。
