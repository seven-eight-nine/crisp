# ビジュアルエディタ

Crisp ビジュアルエディタは VSCode 拡張 `crisp-editor` として提供される、ビヘイビアツリーのノードグラフ表示・編集ツールです。テキストエディタとビジュアルツリーエディタを並べて使用し、双方の編集がリアルタイムに同期されます。

## 目次

- [概要](#概要)
- [インストール](#インストール)
- [基本操作](#基本操作)
- [画面構成](#画面構成)
- [ナビゲーション](#ナビゲーション)
- [ノード選択とカーソル同期](#ノード選択とカーソル同期)
- [ノードの追加](#ノードの追加)
- [ノードの移動](#ノードの移動)
- [ノードの削除](#ノードの削除)
- [デコレータのラップとアンラップ](#デコレータのラップとアンラップ)
- [コンテキストメニュー](#コンテキストメニュー)
- [ノードの見た目](#ノードの見た目)
- [ノード種別一覧](#ノード種別一覧)
- [診断情報の表示](#診断情報の表示)
- [ミニマップ](#ミニマップ)
- [プロパティパネル](#プロパティパネル)
- [デバッグ統合](#デバッグ統合)
- [キーボードショートカット](#キーボードショートカット)
- [設定](#設定)
- [アーキテクチャ](#アーキテクチャ)
- [通信プロトコル](#通信プロトコル)
- [テーマ対応](#テーマ対応)
- [開発とテスト](#開発とテスト)
- [トラブルシューティング](#トラブルシューティング)

---

## 概要

`crisp-editor` は 3 つの機能を統合した VSCode 拡張です:

1. **テキストエディタ支援** — `.crisp` ファイルのシンタックスハイライト、補完、診断、フォーマット（LSP サーバー経由）
2. **ビジュアルツリーエディタ** — ビヘイビアツリーをノードグラフとして可視化し、ドラッグ＆ドロップで編集
3. **デバッグ統合** — 実行中のツリーの状態をリアルタイムに可視化（ブレークポイント対応）

テキストエディタで DSL を記述すると、LSP サーバーがツリーのレイアウトを計算し、ビジュアルエディタにリアルタイムで反映されます。ビジュアル側でノードを操作すると、LSP サーバーが対応するテキスト編集を計算し、テキストエディタに反映されます。

---

## インストール

### 前提条件

- VSCode 1.85.0 以降
- .NET 8 SDK（LSP サーバーのビルドに必要）
- Node.js 20 以降（拡張のビルドに必要）

### 拡張のビルドとインストール

```bash
# リポジトリのルートから

# 1. LSP サーバーをビルドする
dotnet build src/Crisp.Lsp

# 2. VSCode 拡張をビルドする
cd crisp-editor
npm install
npm run build

# 3. 拡張をパッケージングする（オプション）
npm run package
# → crisp-editor-0.1.0.vsix が生成される
```

パッケージングされた `.vsix` ファイルは、VSCode の「Extensions: Install from VSIX...」コマンドでインストールできます。

### 開発モードでの起動

VSCode でリポジトリルートを開き、F5 キーで Extension Development Host を起動します。

---

## 基本操作

### ワークフロー

1. `.crisp` ファイルを VSCode で開く — テキストエディタが表示され、LSP サーバーが自動起動する
2. コマンドパレットから **「Crisp: Open Tree View」** を実行、またはエディタのタイトルバーのアイコンをクリック — ビジュアルエディタが横に開く
3. テキストを編集 — リアルタイムでビジュアルが更新される
4. ビジュアル上でノードをドラッグ＆ドロップ — テキストがリアルタイムで更新される
5. ビジュアル上のノードをクリック — テキストエディタのカーソルが対応位置にジャンプし、プロパティパネルにノード情報が表示される
6. テキストエディタでカーソルを移動 — ビジュアル上の対応ノードがハイライトされる

### ビジュアルエディタの開き方

ビジュアルエディタは 2 つの方法で開くことができます:

| 方法 | 説明 |
|---|---|
| コマンドパレット | `Ctrl+Shift+P` → 「Crisp: Open Tree View」を選択 |
| エディタタイトル | `.crisp` ファイルを開いているとき、エディタ右上に表示されるツリーアイコンをクリック |
| 右クリックメニュー | エディタ上で右クリック → 「Open Tree View」を選択 |

ビジュアルエディタはエディタの横（Side by Side）に開き、テキストエディタと同時に表示されます。

サイドパネルにも常駐する Tree View が表示され、アクティブな `.crisp` ファイルのツリーを自動追従します。

---

## 画面構成

ビジュアルエディタは以下の 4 つの領域で構成されます:

```
┌─ Toolbar ─────────────────────────────────────────┐
├─ Palette ──┬─ Canvas ──────────┬─ Properties ─────┤
│            │                    │                   │
│ Composites │    ┌──────────┐    │  Node: check      │
│  selector  │    │ selector │    │  Type: bool        │
│  sequence  │    └────┬─────┘    │  Source: 42..58    │
│  parallel  │    ┌────┴────┐     │  Diagnostics: —    │
│            │    ▼         ▼     │                   │
│ Decorators │  ┌────┐  ┌──────┐  │                   │
│  guard     │  │ seq│  │Patrol│  │                   │
│  invert    │  └──┬─┘  └──────┘  │                   │
│  repeat    │   ┌─┴──┐           │                   │
│  ...       │   ▼    ▼           │                   │
│            │ ┌────┐┌─────┐     │                   │
│ Leaves     │ │chk ││Flee │     │                   │
│  check     │ └────┘└─────┘     │                   │
│  action    │          ┌────────┐│                   │
│            │          │Minimap ││                   │
└────────────┴──────────┴────────┴┴───────────────────┘
```

| 領域 | 説明 |
|---|---|
| **Toolbar** | ツリー名の表示、フィットボタン、デバッグ状態インジケータ |
| **Node Palette** | ドラッグ可能なノード種別一覧。3 カテゴリに分類される |
| **Canvas** | ツリーのメイン描画領域。パン・ズームに対応した SVG キャンバス |
| **Property Panel** | 選択中のノードの詳細情報（型、ラベル、ソース位置、診断情報） |
| **Minimap** | キャンバス右下に表示されるツリーの全体概要。現在の表示範囲を示す |

---

## ナビゲーション

### パン（スクロール）

キャンバスの背景をマウスでドラッグすると、表示範囲を移動（パン）できます。ドラッグ中はカーソルが掴み手アイコンに変わります。

### ズーム

マウスホイールの回転でキャンバスをズームイン・ズームアウトできます。ズームはマウスカーソルの位置を中心に行われます。

| 設定 | 値 |
|---|---|
| 最小倍率 | 0.1x |
| 最大倍率 | 3.0x |
| 初期倍率 | 1.0x |

### フィット

ツリー全体がキャンバスに収まるようにズームとパンを自動調整します。

| 操作 | 方法 |
|---|---|
| キーボード | `Ctrl+Shift+F`（macOS: `Cmd+Shift+F`） |
| ツールバー | フィットボタン（四隅矢印アイコン）をクリック |
| コマンドパレット | 「Crisp: Fit Tree to View」を選択 |

---

## ノード選択とカーソル同期

### テキスト → ビジュアル（カーソル同期）

テキストエディタでカーソルを移動すると、カーソル位置に対応するノードがビジュアルエディタ上で薄い青色でハイライトされます。LSP サーバーが各ノードのソーステキスト上の位置（`origin`）を保持しており、カーソルのオフセットから対応するノードを特定します。

ネストされたノードの場合、カーソル位置を含む最も深いノードが選択されます。コメント行にカーソルがある場合は、どのノードもハイライトされません。

### ビジュアル → テキスト（ノードクリック）

| 操作 | 動作 |
|---|---|
| シングルクリック | ノードを選択し、プロパティパネルに情報を表示 |
| ダブルクリック | テキストエディタのカーソルを対応する行にジャンプ |

---

## ノードの追加

画面左側のノードパレットから、追加したいノード種別をキャンバス上にドラッグ＆ドロップします。

1. パレットからノードをドラッグ開始する
2. キャンバス上の追加したい位置にドロップする
3. ドロップ位置に最も近い親ノードと挿入位置が自動計算される
4. LSP サーバーがノードテンプレートの DSL コードを生成し、テキストエディタに挿入する
5. テキスト変更により treeLayout が再計算され、ビジュアルが更新される

### ノードテンプレート

各ノード種別には、追加時に生成されるデフォルトの DSL テンプレートがあります:

| ノード種別 | 生成される DSL |
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

テンプレート挿入後はテキストエディタにフォーカスが移り、条件式やメンバー名を入力できます。

---

## ノードの移動

ビジュアルエディタ上でノードをドラッグして、別の親ノードの下に移動できます。

1. 移動したいノードをマウスでドラッグ開始する（5 ピクセル以上の移動で開始を判定）
2. ドラッグ中はノードが半透明になり、ドロップシャドウが付く
3. 新しい親ノードの位置にドロップする
4. LSP サーバーが CST の構造変更とフォーマットを行い、テキスト編集に変換する

以下の操作は拒否されます:

| 操作 | 理由 |
|---|---|
| 自分自身の子孫にドロップ | 循環参照の防止 |
| ルートノードのドラッグ | ルートは移動不可 |

---

## ノードの削除

選択中のノードを削除するには、右クリックメニューの「Delete」を選択します。LSP サーバーが対応するテキスト範囲を計算し、テキストエディタから削除します。

---

## デコレータのラップとアンラップ

### ラップ（Wrap with...）

既存のノードをデコレータで包むことができます。ノードを右クリックして「Wrap with Guard」「Wrap with Repeat」等を選択すると、LSP サーバーがデコレータの CST を生成し、対象ノードをその子として配置します。

ラップ可能なデコレータ: **Guard**, **Invert**, **Repeat**, **Timeout**, **Cooldown**, **While**

### アンラップ（Unwrap）

デコレータノードを右クリックして「Unwrap」を選択すると、デコレータが解除され、子ノードがデコレータの元の位置に昇格します。この操作はデコレータノード（guard, invert, repeat 等）を右クリックした場合にのみ表示されます。

---

## コンテキストメニュー

ノードを右クリックすると、以下の操作が可能なコンテキストメニューが表示されます:

| メニュー項目 | 説明 |
|---|---|
| **Delete** | 選択ノードを削除する |
| **Wrap with Guard** | ノードを `(guard ...)` で包む |
| **Wrap with Invert** | ノードを `(invert ...)` で包む |
| **Wrap with Repeat** | ノードを `(repeat N ...)` で包む |
| **Wrap with Timeout** | ノードを `(timeout N ...)` で包む |
| **Wrap with Cooldown** | ノードを `(cooldown N ...)` で包む |
| **Wrap with While** | ノードを `(while ...)` で包む |
| **Unwrap** | デコレータを解除して子ノードを昇格（デコレータノードのみ表示） |
| **Toggle Breakpoint** | ノードにブレークポイントを設定/解除する |

メニューは以下の方法で閉じることができます:

- メニュー外をクリック
- `Escape` キーを押す

---

## ノードの見た目

各ノードは SVG の矩形として描画され、ノード種別に応じた色、角丸、アイコンが適用されます。

### カテゴリ別のスタイル

| カテゴリ | 背景 | 角丸 | 特徴 |
|---|---|---|---|
| **コンポジット** | 塗りつぶし | 8px | 背景色で目立たせる |
| **デコレータ** | 枠線のみ | 12px | 丸みのある枠線で修飾的な印象 |
| **リーフ** | 枠線のみ | 4px | 軽い印象でアクション/条件を表す |

### 状態の表示

| 状態 | 見た目 |
|---|---|
| 通常 | ノード種別に応じた色 |
| 選択中 | 青い枠線（`stroke-width: 2`） |
| カーソル対応 | 薄い青色のオーバーレイ |
| ドラッグ中 | 半透明（60%）+ ドロップシャドウ |
| 診断エラー | 赤い枠線 + 赤い丸バッジ（右上） |
| 診断警告 | 黄色い枠線 + 黄色い丸バッジ（右上） |
| デバッグ: Running | 下端に黄色のパルスバー（1 秒周期のアニメーション） |
| デバッグ: Success | 緑のフラッシュ（0.5 秒でフェードアウト） |
| デバッグ: Failure | 赤のフラッシュ（0.5 秒でフェードアウト） |

### エッジ（接続線）

親子ノード間はベジェカーブ（3 次曲線）で接続されます。親ノードの下端中央から子ノードの上端中央へ、滑らかな曲線で描画されます。線幅は 1.5 ピクセルで、VSCode のテーマに追従する色を使用します。

---

## ノード種別一覧

ビジュアルエディタで表示・操作できるノード種別の一覧です。

### コンポジットノード

| 種別 | 色 | 幅 | 説明 | 子の数 |
|---|---|---|---|---|
| `selector` | 紫 | 140px | 最初に成功/Running した子を採用 | 1 以上 |
| `sequence` | 青 | 140px | 全ての子が成功する必要がある | 1 以上 |
| `parallel` | オレンジ | 160px | 全ての子を毎ティック評価 | 1 以上 |
| `reactive-select` | オレンジ | 180px | 毎ティック先頭から再評価するセレクタ | 1 以上 |

### デコレータノード

| 種別 | 色 | 幅 | 説明 | 子の数 |
|---|---|---|---|---|
| `guard` | 赤 | 180px | 条件が真なら子を実行 | 1 |
| `if` | 紫 | 180px | 条件分岐（then / else） | 2 |
| `invert` | 赤 | 140px | Success と Failure を反転 | 1 |
| `repeat` | 青 | 160px | 子を指定回数繰り返す | 1 |
| `timeout` | オレンジ | 160px | 制限時間内に完了しなければ Failure | 1 |
| `cooldown` | オレンジ | 160px | 成功後の再実行間隔を制御 | 1 |
| `while` | 緑 | 180px | 条件が真の間、子を繰り返す | 1 |
| `reactive` | オレンジ | 180px | 毎ティック条件を再評価する | 1 |
| `defdec-call` | 紫 | 180px | ユーザー定義デコレータの呼び出し | 1 |

### リーフノード

| 種別 | 色 | 幅 | 説明 | 子の数 |
|---|---|---|---|---|
| `check` | 緑 | 160px | 式を評価し、真なら Success | 0 |
| `action` | 黄 | 160px | メソッドを呼び出し、結果を返す | 0 |
| `ref` | 青 | 180px | 別のツリーを参照 | 0 |

### ラベル生成ルール

各ノードのラベルは以下のルールで生成されます:

| ノード種別 | ラベル例 | 説明 |
|---|---|---|
| selector | `selector` | 種別名 |
| sequence | `sequence` | 種別名 |
| parallel | `parallel :any` | ポリシー付き |
| check | `< .Health 30` | 式のテキスト表現 |
| action（引数なし） | `Patrol()` | メソッド名 |
| action（引数あり） | `Attack(.Target)` | メソッド名と引数 |
| guard | `guard: .IsAlive` | 条件式 |
| if | `if: (and .X .Y)` | 条件式 |
| repeat | `repeat 3x` | 回数 |
| timeout | `timeout 5.0s` | 秒数 |
| cooldown | `cooldown 2.5s` | 秒数 |
| ref | `-> CombatBehavior` | 参照先ツリー名 |

---

## 診断情報の表示

LSP サーバーから送信される診断情報（エラー・警告）は、ビジュアルエディタ上で以下のように表示されます:

- **エラー**: ノードの枠線が赤色に変わり、右上に赤い丸バッジが表示される
- **警告**: ノードの枠線が黄色に変わり、右上に黄色い丸バッジが表示される

プロパティパネルでノードを選択すると、詳細な診断メッセージ（診断コード、メッセージ本文、重要度）が表示されます。

---

## ミニマップ

キャンバス右下に表示される小さなプレビューで、ツリー全体の俯瞰と現在の表示範囲を確認できます。

| 設定 | 値 |
|---|---|
| サイズ | 160 x 120 ピクセル |
| 位置 | キャンバス右下、マージン 12px |
| 透明度 | 80% |

ミニマップの表示/非表示は設定 `crisp.treeView.showMinimap` で切り替えできます。

---

## プロパティパネル

画面右側に表示されるパネルで、選択中のノードの詳細情報を確認できます。

### 表示項目

| セクション | 内容 | 説明 |
|---|---|---|
| **ヘッダー** | アイコン + ノード種別名 | ノードのタイプを視覚的に確認 |
| **Label** | 表示ラベル | 式のテキスト表現やメソッド名 |
| **Type** | 解決型 | 型推論結果（`bool`, `float` 等）。存在する場合のみ表示 |
| **Source** | ソース位置 | テキスト上の `offset 開始..終了` |
| **Node ID** | ノード ID | CST ノードの一意識別子（デバッグ用） |
| **Diagnostics** | 診断情報 | エラー・警告のリスト。診断コードとメッセージを表示 |

ノードが選択されていない場合は「ノードを選択してください」のメッセージが表示されます。

プロパティパネルの表示/非表示は設定 `crisp.treeView.showPropertyPanel` で切り替えできます。

---

## デバッグ統合

Crisp デバッガ（`IDebugSink`）が有効な状態でゲームを実行すると、ビジュアルエディタ上でリアルタイムの実行状態を確認できます。

### デバッグ状態の可視化

ゲームランタイムからの状態更新に応じて、各ノードの見た目が変化します:

| イベント | 表示 | 持続時間 |
|---|---|---|
| ノード評価開始 | 黄色のパルスバー（ノード下端） | 評価中ずっと |
| Success で終了 | 緑色のフラッシュ | 0.5 秒でフェードアウト |
| Failure で終了 | 赤色のフラッシュ | 0.5 秒でフェードアウト |
| Running 継続 | 黄色のパルスアニメーション | Running 中ずっと |

### ブレークポイント

ノードを右クリック → 「Toggle Breakpoint」で、特定のノードにブレークポイントを設定できます。ブレークポイントが設定されたノードには赤い丸印が表示されます。ブレークポイントは VSCode 標準のブレークポイント管理と連動します。

### launch.json の設定

```json
{
  "type": "crisp",
  "request": "attach",
  "name": "Attach to Crisp BT",
  "host": "localhost",
  "port": 9229,
  "crispFile": "${workspaceFolder}/Assets/AI/EnemyAI.crisp"
}
```

デバッグアニメーションの有効/無効は設定 `crisp.treeView.animateDebugState` で切り替えできます。

---

## キーボードショートカット

### グローバルショートカット

| キー | 動作 | 条件 |
|---|---|---|
| `Ctrl+Shift+F` | ツリー全体をビューにフィット | `.crisp` ファイル編集中 |

### コンテキストメニュー内

| キー | 動作 |
|---|---|
| `Escape` | コンテキストメニューを閉じる |

### テキストエディタとの連携

ビジュアルエディタ上の操作（ノード追加・移動・削除）は、テキストエディタの Undo/Redo（`Ctrl+Z` / `Ctrl+Y`）と同期されます。ビジュアル操作によるテキスト変更も、テキストエディタの Undo 履歴に記録されます。

---

## 設定

VSCode の設定（`settings.json`）で、ビジュアルエディタの動作をカスタマイズできます。

### LSP サーバー

| 設定キー | 型 | デフォルト | 説明 |
|---|---|---|---|
| `crisp.lsp.path` | `string` | `""` | LSP サーバーの実行ファイルパス。空の場合は拡張に同梱されたサーバーを使用する |
| `crisp.lsp.args` | `string[]` | `[]` | LSP サーバーに渡す追加引数 |

### ツリービュー

| 設定キー | 型 | デフォルト | 説明 |
|---|---|---|---|
| `crisp.treeView.defaultOpen` | `boolean` | `false` | `.crisp` ファイルを開いた時に自動でツリービューを開くかどうか |
| `crisp.treeView.layout` | `"side" \| "below"` | `"side"` | ツリービューの配置位置。`side` = エディタ横、`below` = エディタ下 |
| `crisp.treeView.showMinimap` | `boolean` | `true` | ミニマップを表示するかどうか |
| `crisp.treeView.showPropertyPanel` | `boolean` | `true` | プロパティパネルを表示するかどうか |
| `crisp.treeView.animateDebugState` | `boolean` | `true` | デバッグ時にノード状態のアニメーションを表示するかどうか |
| `crisp.treeView.autoFitOnChange` | `boolean` | `false` | テキスト変更時にツリー全体がビューに収まるよう自動ズームするかどうか |
| `crisp.treeView.nodeSpacing.horizontal` | `number` | `16` | ノード間の水平方向のスペース（ピクセル） |
| `crisp.treeView.nodeSpacing.vertical` | `number` | `60` | ノード間の垂直方向のスペース（ピクセル） |

### フォーマッタ

| 設定キー | 型 | デフォルト | 説明 |
|---|---|---|---|
| `crisp.formatter.indentSize` | `number` | `2` | フォーマッタのインデントサイズ（スペース数） |
| `crisp.formatter.maxLineWidth` | `number` | `80` | フォーマッタの最大行幅（文字数） |

### デバッガ

| 設定キー | 型 | デフォルト | 説明 |
|---|---|---|---|
| `crisp.debug.port` | `number` | `9229` | Crisp デバッグアダプタのポート番号 |

---

## アーキテクチャ

### 全体構成

ビジュアルエディタは 3 つのプロセス/レイヤーで構成されます:

```
┌─ Extension Host (Node.js) ──────────────────────────────────┐
│                                                              │
│  ┌──────────────────┐   ┌──────────────────────────────────┐ │
│  │  LSP Client      │   │  TreeView Provider               │ │
│  │  (vscode-        │   │  (WebviewViewProvider)            │ │
│  │   languageclient)│   │                                  │ │
│  └────────┬─────────┘   │  ┌────────────────────────────┐  │ │
│           │              │  │  SyncController            │  │ │
│           │              │  │  (テキスト ↔ ビジュアル同期)  │  │ │
│           │              │  └─────────────┬──────────────┘  │ │
│           │              └────────────────┼────────────────-┘ │
└───────────┼───────────────────────────────┼──────────────────┘
            │ stdio                         │ postMessage
            ▼                               ▼
┌──────────────────────┐        ┌──────────────────────────┐
│  Crisp LSP Server    │        │  WebView (React)         │
│  (C# .NET process)   │◄──────►│  TreeCanvas + SVG        │
│                      │ custom │  NodeComponent           │
│  ┌────────────────┐  │ notif. │  NodePalette             │
│  │  QueryDatabase │  │        │  PropertyPanel           │
│  │  TreeLayout    │  │        │  Minimap                 │
│  └────────────────┘  │        │  Zustand (状態管理)       │
└──────────────────────┘        └──────────────────────────┘
```

| プロセス | 言語 | 役割 |
|---|---|---|
| Extension Host | TypeScript (Node.js) | VSCode API の制御、LSP / WebView の橋渡し |
| Crisp LSP Server | C# (.NET 8) | パース、型チェック、ツリーレイアウト計算、テキスト編集の生成 |
| WebView | TypeScript (React) | ビジュアルツリーの描画・インタラクション |

### Extension Host のモジュール構成

```
src/extension/
  extension.ts         エントリポイント（activate / deactivate）
  lspClient.ts         LSP クライアントの生成とカスタム通知の登録
  treeViewProvider.ts  WebView パネルの管理（Side Panel + Editor Panel）
  syncController.ts    テキスト ↔ ビジュアルの同期制御
  cursorSync.ts        テキストカーソル位置の同期
  commands.ts          コマンド登録（openTreeView, fitTree）
  types.ts             設定の型定義
```

### WebView のモジュール構成

```
src/webview/
  index.tsx            エントリポイント（React createRoot）
  App.tsx              ルートコンポーネント
  components/          UI コンポーネント群
    TreeCanvas.tsx       キャンバス（パン・ズーム対応 SVG コンテナ）
    TreeRenderer.tsx     ツリー全体の描画（ノード + エッジ）
    NodeComponent.tsx    個別ノードの SVG 描画
    EdgeComponent.tsx    ベジェカーブ接続線
    NodePalette.tsx      ノード種別パレット
    PropertyPanel.tsx    プロパティパネル
    Minimap.tsx          ミニマップ
    ContextMenu.tsx      右クリックメニュー
    Toolbar.tsx          ツールバー
  hooks/               カスタムフック
    useVscodeApi.ts      Extension Host との通信
    useZoomPan.ts        パン・ズーム操作
    useDragDrop.ts       ドラッグ＆ドロップ
    useSelection.ts      ノード選択
    useTreeLayout.ts     エッジパス計算
  state/               状態管理
    treeStore.ts         ツリー状態 (Zustand)
    debugStore.ts        デバッグ状態 (Zustand)
  layout/              レイアウト関連
    constants.ts         レイアウト定数
    treeLayout.ts        ユーティリティ関数
  styles/              スタイル
    nodeStyles.ts        ノード種別ごとのスタイル定義
    theme.ts             テーマカラー
    global.css           グローバル CSS
  protocol/            通信プロトコル
    bridge.ts            acquireVsCodeApi ラッパー
    messages.ts          メッセージ型（shared からの re-export）
```

### 双方向同期の仕組み

テキストとビジュアルの同期は `SyncController` が中枢となって制御します。

```
テキスト編集 → LSP didChange → treeLayout 再計算 → WebView 更新
  ↑                                                      │
  └─── workspace.applyEdit ←── crisp/addNode 等 ←────────┘
```

**Single Source of Truth**: LSP サーバーの `treeLayout` 出力が唯一の正しいツリー構造です。テキスト起因でもビジュアル起因でも、最終的に LSP が出力するレイアウトで両者が一致します。

**ループ防止**: ビジュアル操作によるテキスト変更は `isApplyingEdit` フラグで識別されます。このフラグが立っている間は、テキスト変更イベントを無視して無限ループを防止します。ただし LSP の `didChange` は常に発火し、新しい `treeLayout` が計算されて WebView に送られます。

---

## 通信プロトコル

### Extension → WebView メッセージ

| メッセージ型 | ペイロード | 説明 |
|---|---|---|
| `treeLayout` | `TreeLayout` | ツリーレイアウト全体の更新 |
| `diagnostics` | `NodeDiagnostic[]` | 診断情報の更新 |
| `cursorSync` | `{ nodeId: number }` | テキストカーソル対応ノードの通知 |
| `debugState` | `DebugTreeState` | デバッグ状態の更新 |
| `fitToView` | — | ツリーフィットの実行要求 |

### WebView → Extension メッセージ

| メッセージ型 | ペイロード | 説明 |
|---|---|---|
| `addNode` | `{ uri, parentNodeId, position, nodeType, args? }` | ノード追加 |
| `removeNode` | `{ uri, nodeId }` | ノード削除 |
| `moveNode` | `{ uri, nodeId, newParentId, newPosition }` | ノード移動 |
| `wrapNode` | `{ uri, nodeId, decoratorType, args? }` | デコレータラップ |
| `unwrapNode` | `{ uri, nodeId }` | デコレータアンラップ |
| `nodeClicked` | `{ uri, nodeId }` | ノードクリック（テキストジャンプ） |
| `toggleBreakpoint` | `{ uri, nodeId }` | ブレークポイント切替 |

### LSP カスタム通知

| 通知名 | 方向 | ペイロード | 説明 |
|---|---|---|---|
| `crisp/treeLayout` | Server → Client | `{ uri, layout }` | ツリーレイアウトの更新通知 |
| `crisp/diagnostics` | Server → Client | `{ uri, diagnostics }` | 診断情報の更新通知 |

### LSP カスタムリクエスト

| リクエスト名 | 方向 | パラメータ | 応答 | 説明 |
|---|---|---|---|---|
| `crisp/addNode` | Client → Server | `AddNodeParams` | `EditResult` | ノード追加 |
| `crisp/removeNode` | Client → Server | `RemoveNodeParams` | `EditResult` | ノード削除 |
| `crisp/moveNode` | Client → Server | `MoveNodeParams` | `EditResult` | ノード移動 |
| `crisp/wrapNode` | Client → Server | `WrapNodeParams` | `EditResult` | ラップ |
| `crisp/unwrapNode` | Client → Server | `UnwrapNodeParams` | `EditResult` | アンラップ |

`EditResult` は `TextEdit[]` を含み、テキストエディタに適用する置換操作のリストを返します。

### データモデル

#### TreeLayout

```typescript
interface TreeLayout {
  fileUri: string;      // 対象ファイルの URI
  treeName: string;     // ツリー名
  root: TreeNode;       // ルートノード
  edges: TreeEdge[];    // 接続線のリスト
  bounds: {             // ツリー全体の境界
    width: number;
    height: number;
  };
}
```

#### TreeNode

```typescript
interface TreeNode {
  id: number;                      // ノードの一意 ID
  type: NodeType;                  // ノード種別
  label: string;                   // 表示ラベル
  x: number;                       // レイアウト X 座標
  y: number;                       // レイアウト Y 座標
  width: number;                   // ノード幅
  height: number;                  // ノード高
  origin: TextSpan;                // ソース上の位置
  resolvedType?: string;           // 式の解決型
  diagnostics: NodeDiagnostic[];   // 付与された診断
  children?: TreeNode[];           // 子ノード
  collapsed: boolean;              // 折りたたみ状態
}
```

#### TreeEdge

```typescript
interface TreeEdge {
  fromId: number;       // 親ノード ID
  toId: number;         // 子ノード ID
  fromPort: "bottom";   // 常に親の下端
  toPort: "top";        // 常に子の上端
}
```

---

## テーマ対応

ビジュアルエディタは VSCode のテーマカラーを自動的に使用し、ライトテーマ・ダークテーマの両方に対応します。

### 使用する VSCode テーマ変数

```css
--bg:       var(--vscode-editor-background)        /* 背景色 */
--fg:       var(--vscode-editor-foreground)         /* 前景色 */
--border:   var(--vscode-panel-border)              /* 枠線色 */
--accent:   var(--vscode-focusBorder)               /* アクセント色 */
--error:    var(--vscode-errorForeground)            /* エラー色 */
--warning:  var(--vscode-editorWarning-foreground)   /* 警告色 */
--font:     var(--vscode-editor-font-family)         /* フォント */
```

### ノードの色

コンポジットノードは VSCode の chart カラーを使用して塗りつぶされ、デコレータ/リーフノードは枠線のみで表現されます:

| ノード | 背景 / 枠線の色 |
|---|---|
| selector | `--vscode-charts-purple` |
| sequence | `--vscode-charts-blue` |
| parallel | `--vscode-charts-orange` |
| check | `--vscode-charts-green`（枠線のみ） |
| action | `--vscode-charts-yellow`（枠線のみ） |
| guard | `--vscode-charts-red`（枠線のみ） |

---

## 開発とテスト

### ビルド

```bash
cd crisp-editor

# 依存パッケージのインストール
npm install

# 開発ビルド
npm run build:dev

# プロダクションビルド
npm run build

# ウォッチモード（ファイル変更時に自動リビルド）
npm run watch
```

### テストの実行

```bash
# 全テストの実行
npm test

# ウォッチモード（ファイル変更時に自動テスト実行）
npm run test:watch
```

テストフレームワークは **Vitest** を使用し、React コンポーネントのテストには **@testing-library/react** を使用します。テスト環境は **jsdom** で DOM をシミュレートします。

### テストの構成

| テストファイル | テスト数 | 対象 |
|---|---|---|
| `test/extension/lspClient.test.ts` | 14 | LSP 通知・リクエストの構造 |
| `test/extension/syncController.test.ts` | 16 | 同期制御、ループ防止、ノード検索 |
| `test/webview/protocol.test.ts` | 11 | メッセージ送受信、リスナー解除 |
| `test/webview/treeLayout.test.ts` | 25 | エッジパス計算、フィット変換、ノード検索 |
| `test/webview/TreeRenderer.test.tsx` | 17 | ノード種別メタデータ、スタイル、コンテキストメニュー |
| `test/webview/useDragDrop.test.ts` | 8 | ドラッグ検証（子孫チェック） |

### Webpack バンドル構成

Webpack は 2 つのターゲットでバンドルします:

| ターゲット | 出力 | 対象 | 説明 |
|---|---|---|---|
| `node` | `dist/extension.js` | Extension Host | Node.js 環境で動作する拡張本体 |
| `web` | `dist/webview.js` | WebView | ブラウザ環境で動作する React アプリ |

### セキュリティ

WebView の HTML には Content Security Policy (CSP) が設定されており、外部スクリプトの実行を防止します。スクリプトタグには `nonce` 属性が付与され、拡張が生成したスクリプトのみが実行を許可されます。

---

## トラブルシューティング

### ビジュアルエディタが表示されない

1. `.crisp` ファイルが開かれているか確認してください
2. LSP サーバーが起動しているか、出力パネル（「Crisp Language Server」）を確認してください
3. `crisp.lsp.path` 設定が正しいか確認してください

### ツリーが更新されない

1. テキストに構文エラーがないか確認してください（エラーがあると treeLayout が生成されない場合があります）
2. LSP サーバーの出力パネルでエラーを確認してください

### ノードの操作が反映されない

ビジュアル操作は LSP サーバーを経由してテキスト編集に変換されます。LSP サーバーが応答していない場合、操作は反映されません。VSCode の出力パネルで LSP サーバーの状態を確認してください。

### パフォーマンスが遅い

大きなツリー（100 ノード以上）では、以下の設定を調整してください:

- `crisp.treeView.showMinimap: false` — ミニマップを無効化
- `crisp.treeView.animateDebugState: false` — デバッグアニメーションを無効化
- `crisp.treeView.autoFitOnChange: false` — 自動フィットを無効化
