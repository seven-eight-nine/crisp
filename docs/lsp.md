# エディタ統合 (LSP)

Crisp は [Language Server Protocol (LSP)](https://microsoft.github.io/language-server-protocol/) に準拠したサーバーを提供し、エディタ上での開発支援機能を利用できます。

## 目次

- [概要](#概要)
- [サポート機能](#サポート機能)
- [セットアップ](#セットアップ)
- [標準 LSP 機能](#標準-lsp-機能)
- [ビジュアルエディタ](#ビジュアルエディタ)
- [ツリー編集コマンド](#ツリー編集コマンド)
- [増分計算](#増分計算)

## 概要

Crisp LSP サーバー（`Crisp.Lsp`）は、`.crisp` ファイルの編集時にリアルタイムのフィードバックを提供します。Source Generator と同じコンパイルパイプラインと増分計算エンジン（`QueryDatabase`）を共有しているため、エディタ上の診断とビルド時の診断は一致します。

## サポート機能

### 標準 LSP メソッド

| 機能 | LSP メソッド | 説明 |
|---|---|---|
| 診断 | `textDocument/publishDiagnostics` | 構文・セマンティックエラーのリアルタイム表示 |
| 補完 | `textDocument/completion` | メンバー名・キーワードの補完候補 |
| ホバー | `textDocument/hover` | カーソル位置の型情報・説明を表示 |
| 定義ジャンプ | `textDocument/definition` | メンバーの C# 定義位置へジャンプ |
| フォーマット | `textDocument/formatting` | ソースコードの自動整形 |
| セマンティックトークン | `textDocument/semanticTokens` | 構文ハイライト情報の提供 |

### カスタムメソッド（ビジュアルエディタ向け）

| メソッド | 方向 | 説明 |
|---|---|---|
| `crisp/treeLayout` | Client → Server | ツリーレイアウト JSON の取得 |
| `crisp/treeLayoutChanged` | Server → Client | テキスト変更時のツリーレイアウト更新通知 |
| `crisp/addNode` | Client → Server | ノード追加 |
| `crisp/removeNode` | Client → Server | ノード削除 |
| `crisp/moveNode` | Client → Server | ノード移動 |
| `crisp/wrapNode` | Client → Server | ノードをデコレータで包む |
| `crisp/unwrapNode` | Client → Server | デコレータを解除 |

## セットアップ

### LSP サーバーのビルド

```bash
dotnet build src/Crisp.Lsp
```

ビルド後、`src/Crisp.Lsp/bin/Debug/net8.0/Crisp.Lsp` が LSP サーバーの実行ファイルです。

### エディタの設定

LSP サーバーは標準入出力 (stdio) で通信します。

#### VSCode の場合

VSCode 拡張 `crisp-editor` をインストールすると、LSP クライアントが自動的にセットアップされます。拡張に同梱された LSP サーバーが使用されますが、`settings.json` で別のパスを指定することもできます:

```json
{
  "crisp.lsp.path": "/path/to/Crisp.Lsp"
}
```

VSCode 拡張の詳細は [ビジュアルエディタ](visual-editor.md) を参照してください。

#### Neovim (nvim-lspconfig) の場合

```lua
local lspconfig = require('lspconfig')
local configs = require('lspconfig.configs')

configs.crisp = {
  default_config = {
    cmd = { '/path/to/Crisp.Lsp' },
    filetypes = { 'crisp' },
    root_dir = lspconfig.util.root_pattern('.git', '*.sln'),
  },
}

lspconfig.crisp.setup({})
```

#### その他のエディタ

LSP をサポートする任意のエディタで利用できます。サーバーコマンドとして `Crisp.Lsp` の実行パスを指定し、ファイルタイプを `crisp` に関連付けてください。

## 標準 LSP 機能

### 診断 (Diagnostics)

ファイルの変更時に自動的にコンパイルが実行され、構文エラー・名前解決エラー・型エラーがリアルタイムで表示されます。全 47 種類の[診断メッセージ](diagnostics.md)がサポートされています。

### 補完 (Completion)

`.` を入力すると、コンテキスト型のメンバー一覧が補完候補として表示されます。

**補完候補に含まれるもの:**
- プロパティ（全アクセス修飾子）
- フィールド（全アクセス修飾子）
- メソッド（全アクセス修飾子）
- ケバブケース候補（PascalCase メンバーに対応するケバブケース名）

**補完候補の分類:**
- `BtStatus` または `BtNode` を返すメソッド → Method アイコン（アクション向け）
- その他のメソッド → Function アイコン（式向け）
- プロパティ → Property アイコン
- フィールド → Field アイコン

コンテキスト型は `partial class` として生成されるため、`private` や `protected` メンバーも補完候補に含まれます。

### ホバー (Hover)

カーソルを合わせると、以下の情報が Markdown 形式で表示されます:

- **メンバー名の上:** 型情報（プロパティの型、メソッドのシグネチャ）
- **キーワードの上:** ノードの説明（`select`, `seq`, `parallel`, `reactive` 等）
- **リテラルの上:** 型と値の情報

### 定義ジャンプ (Go to Definition)

メンバーアクセス（`.Health`）やアクション呼び出し（`.Attack`）にカーソルを合わせて「定義へ移動」を実行すると、コンテキスト型の C# ソースコード上の定義位置にジャンプします。

### フォーマット (Formatting)

ソースコードを統一されたスタイルに自動整形します。

**フォーマット規則:**
- インデントはスペース（デフォルト 2 つ、エディタの tabSize 設定に従う）
- 短い式は 1 行に収める
- 長い式は複数行に展開
- ツリー定義間に空行を挿入
- コメントは元の位置を保持
- Lisp スタイルの閉じ括弧（最後の子と同じ行に `)`）

**フォーマット前:**

```lisp
(tree T (select(seq (check(< .Health 30))(.Flee))(.Patrol)))
```

**フォーマット後:**

```lisp
(tree T
  (select
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol)))
```

### セマンティックトークン (Semantic Tokens)

エディタのシンタックスハイライトを強化するために、トークンの意味情報を提供します。

**トークン種別:**

| 種別 | 対象 |
|---|---|
| keyword | `tree`, `select`, `seq`, `check`, `guard`, `if`, `reactive`, `true`, `false`, `null` 等 |
| property | メンバーアクセス（`.Health`, `.Target`） |
| number | 数値リテラル（整数、浮動小数点） |
| string | 文字列リテラル |
| operator | 演算子（`<`, `>`, `=`, `+` 等） |
| comment | コメント |
| parameter | キーワード引数（`:all`, `:any`, `:n`） |
| variable | 列挙型リテラル（`::AIState.Combat`） |

## ビジュアルエディタ

LSP サーバーは、WebView ベースのビジュアルツリーエディタをサポートするためのカスタムメソッドを提供します。テキスト編集とビジュアル編集の双方向同期を実現します。

ビジュアルエディタの操作方法やアーキテクチャの詳細は [ビジュアルエディタ](visual-editor.md) を参照してください。

### アーキテクチャ

```
テキスト → ビジュアル:
  1. テキスト変更 → LSP didChange
  2. QueryDatabase 更新 → CST/AST/IR 再計算
  3. IR → TreeLayout JSON 変換
  4. crisp/treeLayoutChanged 通知 → WebView に送信
  5. WebView がツリーを再描画

ビジュアル → テキスト:
  1. WebView でノード操作（追加/削除/移動/ラップ/アンラップ）
  2. crisp/addNode 等のリクエスト → LSP サーバー
  3. LSP が CST 上の位置を特定し、テキスト編集を計算
  4. workspace/applyEdit → テキストエディタに反映
  5. テキスト変更 → ビジュアルも自動更新
```

### ツリーレイアウト JSON

`crisp/treeLayout` リクエストまたは `crisp/treeLayoutChanged` 通知で、以下の形式の JSON が提供されます:

```json
{
  "trees": [
    {
      "tree": "EnemyCombat",
      "root": {
        "id": 1,
        "type": "selector",
        "label": "",
        "origin": { "start": 24, "length": 156 },
        "resolvedType": "BtStatus",
        "children": [
          {
            "id": 2,
            "type": "sequence",
            "children": [
              {
                "id": 3,
                "type": "check",
                "label": "Health < 30",
                "resolvedType": "bool"
              },
              {
                "id": 4,
                "type": "action",
                "label": "Flee()",
                "resolvedType": "BtStatus"
              }
            ]
          }
        ]
      },
      "diagnostics": []
    }
  ]
}
```

ノードの `id` フィールドは `IrNode.Id`（= `CstNode.Id`）と一致します。これにより:
- ビジュアルノードクリック → `origin` でテキスト上の位置にジャンプ
- テキスト上のカーソル位置 → 対応するビジュアルノードをハイライト

## ツリー編集コマンド

ビジュアルエディタから以下の編集操作を実行できます。全てのコマンドはテキスト編集（`workspace/applyEdit`）として実行され、フォーマッタを通して整形されます。

### crisp/addNode

指定した親ノードの指定位置に新しいノードを追加します。

```json
{
  "uri": "file:///path/to/file.crisp",
  "parentId": 1,
  "position": 0,
  "nodeType": "action"
}
```

`nodeType` に応じて以下のスニペットが生成されます:

| nodeType | 生成されるスニペット |
|---|---|
| `action` | `(.TODO)` |
| `check` | `(check .TODO)` |
| `selector` / `select` | `(select)` |
| `sequence` / `seq` | `(seq)` |
| `invert` | `(invert (.TODO))` |
| `guard` | `(guard .TODO (.TODO))` |
| その他 | `(.NodeType)` |

### crisp/removeNode

指定 ID のノードをソースから削除します。

```json
{
  "uri": "file:///path/to/file.crisp",
  "nodeId": 5
}
```

### crisp/moveNode

ノードを別の親の指定位置に移動します。

```json
{
  "uri": "file:///path/to/file.crisp",
  "nodeId": 3,
  "newParentId": 1,
  "newPosition": 2
}
```

### crisp/wrapNode

ノードをデコレータで包みます。

```json
{
  "uri": "file:///path/to/file.crisp",
  "nodeId": 2,
  "decoratorType": "invert"
}
```

例: `(.Attack)` → `(invert (.Attack))`

### crisp/unwrapNode

デコレータを解除し、内部の子ノードを露出させます。

```json
{
  "uri": "file:///path/to/file.crisp",
  "nodeId": 4
}
```

例: `(invert (.Attack))` → `(.Attack)`

## 増分計算

LSP サーバーは `QueryDatabase` による増分計算を活用しています。ファイルの一部を変更した場合、変更に影響を受ける計算のみが再実行されます。

```
ファイル変更
    │
    ▼
QueryDatabase
    │
    ├─ SourceText 変更検知
    │
    ├─ Lex (再実行)
    ├─ Parse (再実行)
    ├─ Lower (再実行)
    ├─ Resolve (再実行: ContextType が同じなら差分のみ)
    │
    ├─ Diagnostics 更新 → エディタに通知
    └─ TreeLayout 更新 → ビジュアルエディタに通知
```

ContextType や RoslynCompilation が変化しなければ、構文変更のみを再計算します。大規模なファイルでも高速なフィードバックが得られます。
