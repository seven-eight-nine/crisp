# Crisp

**S 式ベースのビヘイビアツリー DSL for C#**

Crisp は、S 式（Lisp 風）の簡潔な構文でビヘイビアツリーを記述し、C# Source Generator によりコンパイル時に型安全な C# コードへ変換するドメイン固有言語です。ゲーム AI やロボット制御など、階層的な意思決定ロジックを宣言的に表現できます。

## 主な特徴

- **S 式構文** — 括弧ベースの簡潔で構造化された記法。ネストした意思決定ロジックを直感的に記述
- **コンパイル時コード生成** — C# Source Generator により、ビルド時に型安全な C# コードを自動生成。実行時リフレクション不要
- **型安全なセマンティクス** — Roslyn 統合による名前解決・型推論・型検査。コンテキスト型のメンバーを自動的に参照
- **47 種類の診断メッセージ** — 構文エラーから型不一致、Nullable 警告まで、開発者に分かりやすいエラー・警告を提供
- **LSP サーバー** — 補完・ホバー・定義ジャンプ・フォーマット・セマンティックハイライトをサポート
- **VSCode 拡張 (ビジュアルエディタ)** — ビヘイビアツリーをノードグラフとして可視化・編集。テキストとビジュアルの双方向同期
- **ケバブケース対応** — `is-enemy-visible` のようなケバブケース名を C# の `IsEnemyVisible` へ自動変換
- **リアクティブノード** — 条件変化時に Running 中のサブツリーを自動中断する `reactive` / `reactive-select`
- **マクロ & ユーザー定義デコレータ** — `defdec` と `defmacro` による DSL レベルの抽象化
- **マルチツリー** — `ref` / `import` でツリー間の相互参照、ファイル間のツリー共有
- **サブツリー埋め込み** — `BtNode` を返すメソッドをアクション位置で呼び出し、C# コードで構築したサブツリーを直接埋め込み
- **Typed Blackboard** — `$` プレフィクスで Context 外の共有データストアに型安全にアクセス
- **非同期アクション** — `IAsyncOperation` で C# の async/await をビヘイビアツリーに統合
- **ホットリロード** — 実行中に `.crisp` を変更し、ゲームを再起動せずに AI を調整
- **デバッガ** — `IDebugSink` によるブレークポイント、ステップ実行、ノードトレース
- **IR 最適化** — 定数畳み込み、到達不能ノード除去、単一子畳み込み、型変換統合
- **AOT 対応** — NativeAOT / IL2CPP 環境でリフレクション不要の Source-Generated Accessor
- **ジェネリクス対応** — ジェネリック型引数を持つコンテキスト型に対応
- **Nullable 解析** — C# の `#nullable enable` を尊重し、null 安全性をコンパイル時に検証

## ビジュアルエディタ

VSCode 拡張 `crisp-editor` により、テキストエディタとビジュアルツリーエディタを並べて使用できます。テキストの編集がリアルタイムでビジュアルに反映され、ビジュアル上のノード操作がテキストに反映されます。

```
┌─ Editor ─────────────────────┬─ Tree View ────────────────────┐
│                               │                                │
│  (tree EnemyCombat            │       ┌──────────┐             │
│    (select                    │       │ selector │             │
│      (seq                     │       └────┬─────┘             │
│        (check (< .Health 30)) │       ┌────┴────┐              │
│        (.Flee))               │       ▼         ▼              │
│      (.Patrol)))              │   ┌──────┐  ┌────────┐        │
│                               │   │  seq │  │ Patrol │        │
│                               │   └──┬───┘  └────────┘        │
│                               │   ┌──┴──┐                      │
│                               │   ▼     ▼                      │
│                               │ ┌─────┐┌──────┐               │
│                               │ │check││ Flee │               │
│                               │ └─────┘└──────┘               │
└───────────────────────────────┴────────────────────────────────┘
```

主な機能:

- ノードの種別に応じた色分け表示（コンポジット/デコレータ/リーフ）
- テキストカーソルとビジュアルノードの双方向同期
- パン・ズーム・ミニマップによるナビゲーション
- ドラッグ＆ドロップによるノード追加・移動
- 右クリックメニューによるデコレータラップ/アンラップ
- デバッグ時のリアルタイム状態可視化
- プロパティパネルでノードの型・式・診断情報を確認

詳細は [ビジュアルエディタ](docs/visual-editor.md) を参照してください。

## クイックスタート

### 1. パッケージの追加

```xml
<PackageReference Include="Crisp.Runtime" Version="*" />
<PackageReference Include="Crisp.SourceGenerator" Version="*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### 2. コンテキストクラスの定義

```csharp
using Crisp.Runtime;

public partial class EnemyAI
{
    public int Health { get; set; }
    public bool IsEnemyVisible { get; set; }

    // BtStatus を返すメソッド → 毎 tick 実行されるアクション
    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Success;
    public BtStatus Attack() => BtStatus.Success;

    // BtNode を返すメソッド → ビルド時にサブツリーとして埋め込み
    public BtNode BuildCombat() => new SequenceNode(
        new ConditionNode(() => this.IsEnemyVisible),
        new ActionNode(() => this.Attack()));

    [BehaviorTree("(tree Combat (select (seq (check (< .Health 30)) (.Flee)) (.Patrol)))")]
    public partial BtNode BuildTree();
}
```

### 3. ビヘイビアツリーの実行

```csharp
var ai = new EnemyAI { Health = 20, IsEnemyVisible = true };
var tree = ai.BuildTree();

var ctx = new TickContext(DeltaTime: 0.016f);
var status = tree.Tick(ctx);
// Health < 30 なので Flee が実行され、status == BtStatus.Success
```

### 外部ファイルを使う場合

```csharp
// .csproj に追加
// <AdditionalFiles Include="EnemyCombat.crisp" />

[BehaviorTree("EnemyCombat.crisp")]
public partial BtNode BuildTree();
```

```lisp
;; EnemyCombat.crisp
(tree Combat
  (select
    ;; 体力が低ければ逃げる
    (seq
      (check (< .Health 30))
      (.Flee))
    ;; 敵が見えたら攻撃
    (seq
      (check .IsEnemyVisible)
      (.Attack))
    ;; デフォルト: パトロール
    (.Patrol)))
```

## DSL 構文例

```lisp
;; リアクティブな戦闘 AI
(tree ReactiveCombat
  (reactive-select
    ;; 最優先: HP が低ければ逃走（他の行動を中断）
    (seq
      (check (< .Health 20))
      (.Flee))
    ;; 敵が見えたら攻撃（パトロールを中断）
    (reactive .IsEnemyVisible
      (seq
        (.Approach .Target)
        (.Attack)))
    ;; デフォルト: パトロール
    (.Patrol)))

;; ユーザー定義デコレータ
(defdec safe-action (duration cd)
  (guard .IsAlive
    (timeout duration
      (cooldown cd
        <body>))))

;; マルチツリーと Blackboard
(tree TeamAI
  :blackboard WorldState
  (select
    (seq
      (check $.IsAlarmTriggered)
      (ref AlertBehavior))
    (.Patrol)))

;; サブツリー埋め込み（BtNode を返すメソッドを直接呼び出し）
(tree MainAI
  (select
    (.BuildCombat)    ; BtNode を返す → サブツリーとして埋め込み
    (.Patrol)))       ; BtStatus を返す → 毎 tick アクション

;; 並列実行
(tree ParallelSearch
  (parallel :any
    (.SearchForEnemy)
    (.ListenForSound)
    (.CheckRadar)))
```

## ビルドとテスト

```bash
# 前提条件: .NET 8 SDK

# ビルド
dotnet build

# テスト実行
dotnet test

# 特定のテストプロジェクトを実行
dotnet test tests/Crisp.Runtime.Tests
```

### VSCode 拡張のビルド

```bash
# 前提条件: Node.js 20+

cd crisp-editor
npm install
npm run build
npm test
```

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [はじめに](docs/getting-started.md) | インストール、チュートリアル、最初のツリー作成 |
| [言語リファレンス](docs/language-reference.md) | 構文、文法、全ノード型、式、名前解決、型システム |
| [ランタイム API](docs/runtime-api.md) | BtNode, BtStatus, TickContext, 全組み込みノード, Blackboard, Async, Debug |
| [Source Generator](docs/source-generator.md) | セットアップ、コード生成、AOT アクセサ、外部ファイル |
| [ビジュアルエディタ](docs/visual-editor.md) | VSCode 拡張、ツリー可視化、双方向同期、キーボード操作 |
| [診断メッセージ一覧](docs/diagnostics.md) | 全 47 診断コードの説明と対処法 |
| [エディタ統合 (LSP)](docs/lsp.md) | 補完、ホバー、定義ジャンプ、フォーマット |
| [アーキテクチャ](docs/architecture.md) | パッケージ構成、コンパイルパイプライン、増分計算 |

## プロジェクト構成

```
src/
  Crisp.Runtime/          # BtNode 基底クラス、組み込みノード、属性、AOT、Async、Debug
  Crisp.Syntax/           # Lexer, Parser, CST, AST, IR, Emitter, Formatter, Interpreter
  Crisp.Semantics/        # 名前解決、型推論、型検査、ジェネリクス解決、Nullable 解析
  Crisp.Query/            # salsa-like 増分計算データベース
  Crisp.SourceGenerator/  # C# Source Generator
  Crisp.Lsp/              # Language Server Protocol サーバー
crisp-editor/             # VSCode 拡張 (TypeScript / React)
  src/extension/          #   Extension Host — LSP クライアント、同期制御
  src/webview/            #   WebView — ビジュアルツリーエディタ (React + Zustand)
  src/shared/             #   共有型定義・プロトコル
tests/
  Crisp.Runtime.Tests/    # ランタイムノードのユニットテスト
  Crisp.Syntax.Tests/     # Lexer/Parser/Lowering/Emitter/Evaluator のテスト
  Crisp.Semantics.Tests/  # セマンティック解析のテスト
  Crisp.Query.Tests/      # クエリデータベースのテスト
  Crisp.SourceGenerator.Tests/  # Source Generator のテスト
  Crisp.Lsp.Tests/        # LSP ハンドラーのテスト
  Crisp.Snapshot.Tests/   # スナップショット統合テスト
  Crisp.Integration.Tests/ # end-to-end 統合テスト
```

## ライセンス

[MIT License](LICENSE)
