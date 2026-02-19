# アーキテクチャ概要

このドキュメントでは、Crisp コンパイラの内部アーキテクチャを説明します。コンパイルパイプラインの各段階、パッケージ間の依存関係、増分計算の仕組みについて解説します。

## 目次

- [パッケージ構成](#パッケージ構成)
- [コンパイルパイプライン](#コンパイルパイプライン)
- [フェーズ 1: 構文解析](#フェーズ-1-構文解析)
- [フェーズ 2: AST 変換](#フェーズ-2-ast-変換)
- [フェーズ 3: セマンティック解析](#フェーズ-3-セマンティック解析)
- [フェーズ 4: コード生成](#フェーズ-4-コード生成)
- [QueryDatabase: 増分計算エンジン](#querydatabase-増分計算エンジン)
- [Source Generator と LSP の統合](#source-generator-と-lsp-の統合)
- [ランタイムアーキテクチャ](#ランタイムアーキテクチャ)
- [データ表現の変遷](#データ表現の変遷)

## パッケージ構成

```
┌───────────────────────────────────────────────────────────────────┐
│                        アプリケーション層                          │
│                                                                   │
│   ┌──────────────────┐      ┌──────────────────┐                 │
│   │ Crisp.Lsp        │      │ Crisp.Source      │                 │
│   │ (LSP サーバー)    │      │ Generator         │                 │
│   │ net8.0           │      │ (コード生成)       │                 │
│   └────────┬─────────┘      │ netstandard2.0    │                 │
│            │                └────────┬─────────┘                 │
│            │                         │                            │
├────────────┼─────────────────────────┼────────────────────────────┤
│            │         共有層          │                            │
│            │                         │                            │
│            └──────────┬──────────────┘                            │
│                       ▼                                           │
│            ┌──────────────────┐                                   │
│            │ Crisp.Query      │                                   │
│            │ (増分計算 DB)     │                                   │
│            │ netstandard2.0   │                                   │
│            └────────┬─────────┘                                   │
│                     │                                             │
├─────────────────────┼─────────────────────────────────────────────┤
│                     │         コア層                               │
│         ┌───────────┼───────────┐                                 │
│         ▼           ▼           ▼                                 │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐                    │
│  │ Crisp.     │ │ Crisp.     │ │ Crisp.     │                    │
│  │ Syntax     │ │ Semantics  │ │ Runtime    │                    │
│  │ (構文解析   │ │ (意味解析)  │ │ (実行基盤)  │                    │
│  │  + IR     │ │            │ │            │                    │
│  │  + 生成)   │ │ ns2.0      │ │ ns2.0;n8.0 │                    │
│  │ ns2.0;n8.0│ │            │ │            │                    │
│  └────────────┘ └────────────┘ └────────────┘                    │
│                                                                   │
└───────────────────────────────────────────────────────────────────┘
```

### 各パッケージの責務

| パッケージ | ターゲット | 責務 |
|---|---|---|
| **Crisp.Runtime** | netstandard2.0; net8.0 | `BtNode` 基底クラス（デバッグプロパティ含む）、全組み込みノード（Reactive, Async, Debug 含む）、`BtStatus`、`TickContext`、`IBtBlackboard`、`IAsyncOperation`、`IDebugSink`、`BtDebugger`・`BtNodeSnapshot`・`BtTreeSnapshot`・`BtDebugFormatter`、AOT アクセサ、属性 |
| **Crisp.Syntax** | netstandard2.0; net8.0 | Lexer、Parser、CST、AST、CstToAstLowering、MacroExpander、AstDefdecExpander、AstRefResolver、IR、IrOptimizer、CSharpEmitter、Formatter、Interpreter、CrispRuntime、CompilationPipeline、PositionMapper |
| **Crisp.Semantics** | netstandard2.0 | NameResolver（名前解決）、TypeInferer（型推論）、TypeChecker（型検査）、SemanticAnalyzer、GenericTypeResolver（ジェネリクス解決）、NullableAnalyzer（Nullable フロー解析） |
| **Crisp.Query** | netstandard2.0 | QueryDatabase — salsa-like 増分計算エンジン、FileId |
| **Crisp.SourceGenerator** | netstandard2.0 | Roslyn IIncrementalGenerator。ビルド時コード生成、AOT アクセサテーブル生成 |
| **Crisp.Lsp** | net8.0 | LSP サーバー。TextDocumentSync、Completion、Hover、Definition、Formatting、SemanticTokens、TreeLayout、TreeEdit の各ハンドラー |
| **crisp-editor** | TypeScript (Node.js / React) | VSCode 拡張。LSP クライアント、WebView ベースのビジュアルツリーエディタ、双方向同期制御。詳細は [ビジュアルエディタ](visual-editor.md) を参照 |

`netstandard2.0` は Roslyn Source Generator の要件です。Source Generator は .NET Standard 2.0 のアセンブリのみを参照できるため、コアパッケージはこのターゲットを含んでいます。

## コンパイルパイプライン

DSL ソースコードから C# コードが生成されるまでの全体フローです。

```
ソースコード (.crisp / inline)
    │
    ▼
┌──────────┐     ┌──────────┐     ┌──────────────┐
│  Lexer   │ ──▶ │  Parser  │ ──▶ │CstToAst      │
│ (トークン │     │ (CST     │     │Lowering      │
│  化)     │     │  構築)    │     │(CST→AST 変換) │
└──────────┘     └──────────┘     └──────┬───────┘
                                          │
                      ┌───────────────────┘
                      ▼
              ┌──────────────┐
              │ AST 変換パス   │
              │              │
              │ 1. Defdec    │ ← defdec 展開
              │    Expander  │
              │ 2. Macro     │ ← マクロ展開
              │    Expander  │
              │ 3. Ref       │ ← ツリー参照解決
              │    Resolver  │
              └──────┬───────┘
                     │
                     ▼
          ┌──────────────────┐
          │ SemanticAnalyzer  │
          │ ┌──────────────┐ │
          │ │NameResolver  │ │  ← Roslyn INamedTypeSymbol
          │ │(名前解決)     │ │
          │ ├──────────────┤ │
          │ │TypeInferer   │ │  ← ボトムアップ型推論
          │ │(型推論)      │ │
          │ ├──────────────┤ │
          │ │TypeChecker   │ │  ← トップダウン型検査
          │ │(型検査)      │ │
          │ ├──────────────┤ │
          │ │Generic       │ │  ← ジェネリクス型解決
          │ │TypeResolver  │ │
          │ ├──────────────┤ │
          │ │Nullable      │ │  ← Nullable フロー解析
          │ │Analyzer      │ │
          │ └──────────────┘ │
          └────────┬─────────┘
                   │
          ┌────────┴────────────┐
          ▼                     ▼
 ┌──────────────────┐  ┌──────────────┐
 │ AstToIrLowering  │  │  Evaluator   │
 │ (AST→IR 変換)     │  │ (インタプリタ) │
 └────────┬─────────┘  └──────────────┘
          │
          ▼
 ┌──────────────────┐
 │  IrOptimizer     │
 │ (IR 最適化)       │
 │ ┌──────────────┐ │
 │ │定数畳み込み   │ │
 │ │到達不能除去   │ │
 │ │単一子畳み込み │ │
 │ │型変換統合     │ │
 │ └──────────────┘ │
 └────────┬─────────┘
          │
 ┌────────┴────────────┐
 ▼                     ▼
┌──────────────┐  ┌──────────────┐
│CSharpEmitter │  │AOT Accessor  │
│(C# コード出力)│  │Generator     │
└──────────────┘  │(アクセサ生成) │
                  └──────────────┘
```

## フェーズ 1: 構文解析

### Lexer（トークン化）

ソーステキストをトークン列に変換します。

```
"(tree T (.Patrol))"
  ↓
[LeftParen, Identifier("tree"), Identifier("T"),
 LeftParen, MemberAccess(".Patrol"), RightParen,
 RightParen, EndOfFile]
```

**特徴:**
- 1 パスのスキャン、バックトラックなし
- メンバーアクセス（`.Foo.Bar`）は単一トークンとして認識
- ブラックボードアクセス（`$.Foo`）は `DollarMemberAccess` トークン
- 負の数値リテラル（`-3`）をリテラルとして認識
- `<body>` プレースホルダーは `BodyPlaceholder` トークン
- 列挙型リテラル（`::Type.Member`）は `EnumLiteral` トークン
- トリビア（空白、コメント）をトークンに付随させて保持

### Parser（構文解析 → CST）

トークン列を CST (Concrete Syntax Tree) に変換します。CST はソースコードの完全な情報（空白、コメント、括弧の位置）を保持するロスレスな表現です。

```
CST
└─ CstProgram
   └─ CstTree "T"
      └─ CstCall
         └─ CstMemberAccess ".Patrol"
```

**CST ノード種別:**

| カテゴリ | ノード型 |
|---|---|
| トップレベル | `CstProgram`, `CstTree` |
| コンポジット | `CstSelect`, `CstSequence`, `CstParallel` |
| デコレータ | `CstCheck`, `CstGuard`, `CstIf`, `CstInvert`, `CstRepeat`, `CstTimeout`, `CstCooldown`, `CstWhile` |
| リアクティブ | `CstReactive`, `CstReactiveSelect` |
| アクセス | `CstMemberAccess`, `CstCall`, `CstBlackboardAccess` |
| 式 | `CstBinaryExpr`, `CstUnaryExpr`, `CstLogicExpr` |
| リテラル | `CstIntLiteral`, `CstFloatLiteral`, `CstBoolLiteral`, `CstStringLiteral`, `CstNullLiteral`, `CstEnumLiteral` |
| ユーザー定義 | `CstDefdec`, `CstDefdecCall`, `CstBodyPlaceholder`, `CstDefmacro` |
| マルチツリー | `CstRef`, `CstImport` |
| エラーリカバリ | `CstMissing`, `CstError` |

**エラーリカバリ:** パーサーは構文エラーがあっても可能な限り CST を構築します。エラー箇所は `CstError`（予期しないトークン）や `CstMissing`（期待されたトークンの欠落）として表現されます。

### CstToAstLowering（CST → AST）

CST を AST (Abstract Syntax Tree) に変換します。AST は意味的に重要な情報のみを保持し、トリビアを除去します。

```
AST
└─ AstTree "T"
   └─ AstActionCall "Patrol"
```

**コンテキストに応じた変換:**
- ノード位置の `(.Method args...)` → `AstActionCall`
- 式位置の `(.Method args...)` → `AstCallExpr`
- ノード位置の `.Method`（括弧なし） → `AstActionCall`（引数なし）

この区別により、同じ構文がノードの位置と式の位置で異なる意味を持つことを正確に表現します。

**AST ノード種別:**

| カテゴリ | ノード型 |
|---|---|
| ツリー | `AstTree` |
| コンポジット | `AstSelector`, `AstSequence`, `AstParallel` |
| デコレータ | `AstCheck`, `AstGuard`, `AstIf`, `AstInvert`, `AstRepeat`, `AstTimeout`, `AstCooldown`, `AstWhile` |
| リアクティブ | `AstReactive`, `AstReactiveSelect` |
| ユーザー定義 | `AstDefdec`, `AstDefdecCall`, `AstBodyPlaceholder`, `AstDefmacro` |
| マルチツリー | `AstRef` |
| アクション | `AstActionCall` |
| 式 | `AstMemberAccessExpr`, `AstLiteralExpr`, `AstBinaryExpr`, `AstUnaryExpr`, `AstLogicExpr`, `AstBlackboardAccessExpr`, `AstCallExpr` |

## フェーズ 2: AST 変換

CstToAstLowering の後、3 つの変換パスが AST を書き換えます。これらはセマンティック解析の前に実行されます。

### パス 1: Defdec 展開 (AstDefdecExpander)

`defdec`（decorator definition）の呼び出しを、定義本体に展開します。

```
展開前:
  (defdec low-health (threshold)
    (guard (< .Health threshold) <body>))
  (tree T (low-health 30 (.Flee)))

展開後:
  (tree T (guard (< .Health 30) (.Flee)))
```

**処理内容:**
- `AstDefdecCall` ノードを検出し、対応する `AstDefdec` 定義を検索
- パラメータを実引数に置換
- `<body>` プレースホルダーを呼び出し側の子ノードに置換
- 再帰展開に対応（最大深度: 100、BS0025 で循環参照を検出）
- パラメータ数の検証（BS0024）

### パス 2: マクロ展開 (MacroExpander)

`defmacro` の呼び出しを、定義本体に展開します。defdec との違いは、マクロは CST レベルのテンプレート置換を行う点です。

```
展開前:
  (defmacro debug-guard (cond)
    (seq (check cond) <body>))
  (tree T (debug-guard .IsReady (.Attack)))

展開後:
  (tree T (seq (check .IsReady) (.Attack)))
```

**処理内容:**
- マクロ呼び出しを検出し、CST テンプレートのパラメータを置換
- 置換後の CST を再度 AST に変換（ネストされたマクロに対応）
- 再帰深度制限: 100（BS0033）、循環参照検出（BS0034）

### パス 3: ツリー参照解決 (AstRefResolver)

`ref` ノードを、参照先ツリーの本体に解決します。

```
解決前:
  (tree Flee (seq (check (< .Health 30)) (.Flee)))
  (tree T (select (ref Flee) (.Patrol)))

解決後:
  (tree T (select
    [resolved: Flee → seq(check(...), .Flee)]
    (.Patrol)))
```

**処理内容:**
- 同一ファイル内のツリー名を検索
- `import` で読み込まれた外部ファイルのツリー名を検索
- 未解決の参照は BS0036 エラー
- 循環参照は BS0038 エラー

## フェーズ 3: セマンティック解析

セマンティック解析は複数のパスで構成されます。

### パス 1: 名前解決 (NameResolver)

AST 内のメンバー参照を Roslyn の `ISymbol` に解決します。

```
.Health  →  IPropertySymbol { Name: "Health", Type: int }
.Attack  →  IMethodSymbol { Name: "Attack", ReturnType: BtStatus }
$.Alarm  →  IPropertySymbol { Name: "Alarm", Type: bool } (ブラックボード上)
```

**処理内容:**
- メンバーパス（`.Target.Position`）の各セグメントを順に解決
- `NameConversion.GenerateCandidates()` でケバブケース → PascalCase 候補を生成し、優先順位順に照合
- メソッドの引数の数によるオーバーロード解決
- 列挙型リテラル（`::AIState.Combat`）の解決
- ブラックボードメンバーの解決（`$` プレフィックス → ブラックボード型から検索）
- `[Obsolete]` メンバーへの参照時に BS0010 警告
- あいまいなメンバー名（複数候補）時に BS0104 警告

**結果:** `SemanticModel` に AST ノード → ISymbol のマッピングを格納

### パス 2: 型推論 (TypeInferer)

名前解決の結果を使って、各式のボトムアップ型推論を行います。

```
(.Health)        → int（プロパティの型）
(< .Health 30)   → bool（比較式の結果型）
30               → int（リテラルの型）
```

**処理内容:**
- リテラルの型決定（`null` リテラル含む）
- 解決済みシンボルの型取得
- 二項演算子の結果型決定
- 暗黙的型変換（int → float）の挿入

**結果:** `SemanticModel` に AST 式 → CrispType のマッピングを格納

### パス 3: 型検査 (TypeChecker)

推論された型が制約を満たすか検証します。

**検査項目:**
- `check` の式が `bool` 型か（BS0007）
- アクションメソッドが `BtStatus` または `BtNode` を返すか（BS0008）
- 比較演算子の両辺が比較可能か（BS0003）
- 算術演算子の適用可能性（BS0004）
- メソッド引数の型一致（BS0006）

**ErrorType カスケード抑制:** 名前解決に失敗した式は `ErrorType` として扱われ、`ErrorType` を含む式に対する追加の型エラーは報告されません。

### パス 4: ジェネリクス型解決 (GenericTypeResolver)

コンテキスト型がジェネリクスの場合の型パラメータを解決・検証します。

**処理内容:**
- `StateMachineAI<EnemyState>` のようなジェネリクス型名を解析
- 型引数の数の検証（BS0042）
- 型制約の検証: 値型/参照型制約、基底型制約、インターフェース制約（BS0041）
- 未束縛ジェネリクス型の検出（BS0043）

### パス 5: Nullable フロー解析 (NullableAnalyzer)

Nullable 参照型のフロー解析を行い、null 安全性の警告を出します。

**ナローイング（型の絞り込み）パターン:**
- `(!= .Member null)` → guard/if 内で non-null に絞り込み
- `(not (== .Member null))` → 同上
- `(and cond1 cond2 ...)` → 累積的な絞り込み

**報告される診断:**
- BS0044: null デリファレンスの可能性（warning）
- BS0045: null 比較が常に true（warning）
- BS0046: null 比較が常に false（warning）
- BS0047: non-nullable メンバーへの不要な null チェック（info）

## フェーズ 4: コード生成

### AST → IR 変換 (AstToIrLowering)

AST を中間表現 (IR) に変換します。IR はバックエンド非依存の表現で、コード生成とインタプリタの両方で使用されます。

```
IR
└─ IrTree "T"
   └─ IrAction target:"this" method:"Patrol" args:[] type:"BtStatus"
```

**IR の特徴:**
- Roslyn シンボルを文字列参照（型名、メソッド名）に変換
- 暗黙的型変換を明示的な `IrConvert` ノードとして表現
- 非同期メソッドは `IrAction` の `IsAsync` フラグで区別
- `BtNode` を返すサブツリーメソッドは `IrAction` の `IsSubtree` フラグで区別
- S 式形式でシリアライズ / デシリアライズ可能

**IR ノード種別:**

| カテゴリ | ノード型 |
|---|---|
| ツリー | `IrTree` |
| コンポジット | `IrSelector`, `IrSequence`, `IrParallel` |
| 制御 | `IrCondition`, `IrGuard`, `IrIf`, `IrInvert`, `IrRepeat`, `IrTimeout`, `IrCooldown`, `IrWhile`, `IrAction` |
| リアクティブ | `IrReactive`, `IrReactiveSelect` |
| マルチツリー | `IrTreeRef` |
| 式 | `IrMemberLoad`, `IrLiteral`, `IrBinaryOp`, `IrUnaryOp`, `IrLogicOp`, `IrCall`, `IrConvert`, `IrBlackboardLoad` |

### IR 最適化 (IrOptimizer)

コード生成前に、正当性を保ったまま実行時のオーバーヘッドを削減する最適化パスを適用します。

| パス | 名称 | 説明 | 例 |
|---|---|---|---|
| Pass 1 | 定数畳み込み | リテラル同士の演算をコンパイル時に計算 | `(+ 10 20)` → `30` |
| Pass 2 | 到達不能除去 | 常に成功/失敗するノードの後を除去 | `(seq (check false) ...)` → `(check false)` |
| Pass 3 | 単一子畳み込み | 子が 1 つのコンポジットノードと二重反転を簡約 | `(select child)` → `child` |
| Pass 4 | 型変換統合 | 冗長な型変換を統合、リテラル変換をインライン化 | `(convert 42 float)` → `42.0` |

最適化パスは `IIrPass` インターフェースでプラグイン可能に設計されており、`IrPassManager` が順次適用します。

### C# Emitter (CSharpEmitter)

IR から C# ソースコードを生成します。

**生成規則:**
- コンポジットノード → `new SelectorNode(...)` / `new SequenceNode(...)` / `new ParallelNode(...)`
- リアクティブノード → `new ReactiveNode(() => ..., ...)` / `new ReactiveSelectorNode(...)`
- デコレータノード → `new GuardNode(() => ..., ...)` / `new IfNode(...)` 等
- 条件 → `new ConditionNode(() => ..., "debugLabel")`
- 同期アクション → `new ActionNode(() => this.Method(...), "debugLabel")`
- サブツリーアクション → `this.Method()`（BtNode を返すメソッドの直接呼び出し）
- 非同期アクション → `new AsyncActionNode(ct => this.Method(ct), "debugLabel")`
- デバッグラップ → `new DebugProxyNode(inner, nodeId, sink)`
- メンバーアクセス → `this.Property` / `this.Prop.Nested`
- ブラックボードアクセス → `__bb.Property`
- 暗黙変換 → `(float)expr`
- マルチツリー参照 → 別メソッド呼び出し

### AOT アクセサ生成

NativeAOT / IL2CPP 環境でリフレクション不要のメンバーアクセスを可能にするアクセサテーブルを自動生成します。コンテキスト型ごとに 1 つのアクセサファイルが生成されます。

### Interpreter (Evaluator)

IR を直接実行するインタプリタです。テスト、デバッグ、ホットリロードに使用されます。リフレクションを使ってコンテキスト型のメンバーにアクセスします。

## QueryDatabase: 増分計算エンジン

`QueryDatabase` は Rust の [salsa](https://github.com/salsa-rs/salsa) クレートに着想を得た増分計算エンジンです。Source Generator と LSP サーバーの両方がこのデータベースを通じてコンパイルパイプラインを実行します。

### 設計原則

```
入力クエリ                    派生クエリ
┌──────────────┐     ┌────────────────────────────────────┐
│ SourceText   │ ──▶ │ Lex → Parse → Lower               │
│ ContextType  │ ──▶ │         → DefdecExpand             │
│ Compilation  │ ──▶ │         → MacroExpand → RefResolve │
└──────────────┘     │         → Resolve → TypeCheck      │
                     │                    → EmitIR        │
                     │                    → Diagnostics   │
                     └────────────────────────────────────┘
```

### 入力クエリ

外部から設定される値です。

| クエリ | 型 | 説明 |
|---|---|---|
| `SourceText` | `string` | DSL のソースコード |
| `ContextType` | `INamedTypeSymbol` | Roslyn のコンテキスト型シンボル |
| `RoslynCompilation` | `Compilation` | Roslyn コンパイル（型解決用） |

### 派生クエリ

入力クエリから計算される値です。結果はキャッシュされ、依存する入力が変更されるまで再利用されます。

| クエリ | 入力依存 | 出力 |
|---|---|---|
| `Lex` | SourceText | Token[] |
| `Parse` | Lex | CstNode[] |
| `Lower` | Parse | AstTree[] |
| `Resolve` | Lower, ContextType, Compilation | SemanticModel |
| `TypeCheck` | Resolve | Diagnostics |
| `EmitIR` | Lower | IrTree[] |
| `AllDiagnostics` | Parse, TypeCheck | Diagnostic[] |

### リビジョン追跡

```
    SetSourceText("v1")     SetSourceText("v2")
         │                       │
    rev=1                   rev=2
         │                       │
    Lex(v1) → cached        Lex(v2) → 再計算
    Parse(v1) → cached      Parse(v2) → 再計算
    Resolve → cached        Resolve → 再計算
         │                       │
    EmitIR → result1        EmitIR → result2
```

- 入力クエリが変更されるとグローバルリビジョンが増加
- 同じ入力値で再設定された場合はリビジョン不変（等値比較）
- 派生クエリは依存先のリビジョンを追跡し、変更がなければキャッシュを再利用
- 依存関係はスタックベースで自動記録される

### ファイル単位の管理

`QueryDatabase` は `FileId` をキーとしてファイル単位でデータを管理します。

```csharp
// インライン DSL の場合
var fileId = FileId.ForInline("MyClass", "BuildTree");
// → "inline://MyClass/BuildTree"

// 外部ファイルの場合
var fileId = FileId.ForFile("/path/to/Combat.crisp");
```

複数のファイルは独立して処理され、あるファイルの変更が他のファイルの計算に影響を与えることはありません。

## Source Generator と LSP の統合

Source Generator と LSP サーバーは同じ `QueryDatabase` クラスを使用して、コンパイルパイプラインを共有しています。

```
┌───────────────────────┐     ┌────────────────────────┐
│   Source Generator     │     │      LSP Server        │
│   (ビルド時)           │     │   (エディタ常駐)       │
│                        │     │                        │
│  ┌──────────────────┐  │     │  ┌──────────────────┐  │
│  │  QueryDatabase   │  │     │  │  QueryDatabase   │  │
│  │ (ビルド毎に      │  │     │  │ (サーバー寿命    │  │
│  │  新規作成)       │  │     │  │  で共有)         │  │
│  └──────────────────┘  │     │  └──────────────────┘  │
│          │             │     │          │             │
│   同じパイプライン      │     │   同じパイプライン      │
│   Lex → Parse → Lower  │     │   Lex → Parse → Lower  │
│   → Resolve → EmitIR   │     │   → Resolve → Diag     │
│          │             │     │          │             │
│   ┌──────┴──────┐      │     │   ┌──────┴──────────┐  │
│   │ C# コード   │      │     │   │エディタへ通知    │  │
│   │ AOT アクセサ │      │     │   │ - 診断          │  │
│   └─────────────┘      │     │   │ - 補完          │  │
│                        │     │   │ - ホバー        │  │
└────────────────────────┘     │   │ - ツリーレイアウト│  │
                               │   └─────────────────┘  │
                               └────────────────────────┘
```

**利点:**
- 意味的不整合の防止: エディタ上の診断とビルド時の診断が一致
- コードの重複排除: コンパイルロジックが 1 箇所に集約
- 増分計算の恩恵: LSP はファイル変更時の差分のみを再計算

**違い:**
- Source Generator: ビルド毎に新しい `QueryDatabase` を作成。最終出力は C# コードと AOT アクセサ
- LSP: サーバーの寿命で 1 つの `QueryDatabase` を共有。最終出力は診断・補完・ホバー・ツリーレイアウト情報

## ランタイムアーキテクチャ

### ノード階層

```
BtNode (abstract)
│  LastStatus: BtStatus?      ─ 最後の Tick 結果（デバッグ用）
│  DebugChildren: IReadOnlyList<BtNode>  ─ 子ノード一覧（デバッグ用）
│  DebugNodeType: string      ─ ノード種別名（デバッグ用）
│  DebugLabel: string?        ─ 人間可読ラベル（デバッグ用）
│
├── SelectorNode          ─ 子を順に評価、最初の Success/Running を返す
├── SequenceNode          ─ 子を順に評価、全て Success で成功
├── ParallelNode          ─ 全子を毎ティック評価、ポリシーで判定
├── ConditionNode         ─ bool 式を評価（debugLabel 引数対応）
├── ActionNode            ─ BtStatus を返すアクション実行（debugLabel 引数対応）
├── GuardNode             ─ 条件付き実行
├── IfNode                ─ 条件分岐（then/else）
├── InvertNode            ─ 結果反転（Success ↔ Failure）
├── RepeatNode            ─ N 回繰り返し
├── TimeoutNode           ─ 制限時間付き実行
├── CooldownNode          ─ 最小実行間隔
├── WhileNode             ─ 条件ループ
├── ReactiveNode          ─ 毎ティック条件再評価、Running 中断
├── ReactiveSelectorNode  ─ 毎ティック先頭から再評価、優先度切替
├── AsyncActionNode       ─ async/await ブリッジ（debugLabel 引数対応）
└── DebugProxyNode        ─ デバッグイベント通知ラッパー（デバッグプロパティ透過）
```

### デバッグインフラ

`BtNode` の 4 つのデバッグプロパティにより、実行中のツリーを外部から非破壊的にインスペクションできます。

```
BtDebugger
    │
    │  Capture()
    ▼
BtNode.DebugChildren ──── 再帰走査 ──── BtNodeSnapshot ツリー
    │                                        │
    ├─ DebugNodeType ──── NodeType            │
    ├─ DebugLabel ──────── Label              │
    └─ LastStatus ──────── LastStatus         │
                                              ▼
                                    BtTreeSnapshot
                                    │  Root: BtNodeSnapshot
                                    │  BlackboardValues: リフレクションで読み取り
                                    │
                                    ▼
                            BtDebugFormatter.Format()
                                    │
                                    ▼
                            テキスト出力（ASCII ツリー図）
```

**Source Generator とのデバッグラベル連携:**

CSharpEmitter は `ConditionNode`・`ActionNode`・`AsyncActionNode` の生成時に、`TreeLayoutBuilder.FormatExpr()` / `FormatAction()` で式のテキスト表現を生成し、`debugLabel` 引数として渡します。これにより、Source Generator で生成されたツリーには自動的に人間可読なラベルが付与されます。

### Tick / Reset / Abort ライフサイクル

```
  ┌─────────────┐
  │   Tick()    │
  │ TickContext │
  └──────┬──────┘
         │
    ┌────┴────┐
    ▼         ▼
 Success   Running ──── 次ティックで再開
 Failure        │
    │      ┌────┴────┐
    ▼      ▼         ▼
 Reset()  Abort()   (外部中断)
 (正常完了) (リアクティブ条件 false、
             タイムアウト等)
```

- **Tick(ctx):** 1 ティック分の評価を実行し、`BtStatus` (Success / Failure / Running) を返す
- **Reset():** 正常完了後の状態クリア。Parallel の確定時に全子（Running 含む）が Reset される
- **Abort():** 外部要因による強制中断。ReactiveNode の条件が false になった場合や、タイムアウト時に呼ばれる。デフォルトでは `Reset()` にフォールバック

## データ表現の変遷

ソースコードが各段階でどのように変換されるかを、具体例で示します。

### 入力: DSL ソース

```lisp
(defdec low-health (threshold)
  (guard (< .Health threshold) <body>))

(tree T
  (reactive-select
    (low-health 30 (.Flee))
    (.Patrol)))
```

### Stage 1: Token 列

```
LeftParen, Identifier("defdec"), Identifier("low-health"),
LeftParen, Identifier("threshold"), RightParen,
LeftParen, Identifier("guard"), LeftParen, LessThan,
MemberAccess(".Health"), Identifier("threshold"), RightParen,
BodyPlaceholder, RightParen, RightParen,

LeftParen, Identifier("tree"), Identifier("T"),
LeftParen, Identifier("reactive-select"),
LeftParen, Identifier("low-health"), IntLiteral(30),
LeftParen, MemberAccess(".Flee"), RightParen, RightParen,
LeftParen, MemberAccess(".Patrol"), RightParen,
RightParen, RightParen, EndOfFile
```

### Stage 2: CST (Concrete Syntax Tree)

```
CstProgram
├─ CstDefdec "low-health" [threshold]
│  └─ CstGuard
│     ├─ CstBinaryExpr (<)
│     │  ├─ CstMemberAccess ".Health"
│     │  └─ CstIdentifier "threshold"
│     └─ CstBodyPlaceholder
└─ CstTree "T"
   └─ CstReactiveSelect
      ├─ CstDefdecCall "low-health" [30]
      │  └─ CstCall [.Flee]
      └─ CstCall [.Patrol]
```

### Stage 3: AST (After CstToAstLowering)

```
AstDefdec "low-health" [threshold]
  └─ AstGuard ...
AstTree "T"
  └─ AstReactiveSelect
     ├─ AstDefdecCall "low-health" [30]
     │  └─ AstActionCall "Flee"
     └─ AstActionCall "Patrol"
```

### Stage 4: AST (After Defdec Expansion)

```
AstTree "T"
  └─ AstReactiveSelect
     ├─ AstGuard
     │  ├─ AstBinaryExpr (LessThan)
     │  │  ├─ AstMemberAccessExpr [Health]
     │  │  └─ AstLiteralExpr 30
     │  └─ AstActionCall "Flee"
     └─ AstActionCall "Patrol"
```

### Stage 5: SemanticModel

```
AstMemberAccessExpr → IPropertySymbol "Health" : int
AstActionCall "Flee" → IMethodSymbol "Flee" : BtStatus
AstActionCall "Patrol" → IMethodSymbol "Patrol" : BtStatus
AstLiteralExpr 30 → CrispType.Int
AstBinaryExpr → CrispType.Bool
```

### Stage 6: IR (Intermediate Representation)

```
IrTree "T"
└─ IrReactiveSelect
   ├─ IrGuard
   │  ├─ IrBinaryOp :lt
   │  │  ├─ IrMemberLoad "Health" :int
   │  │  └─ IrLiteral 30 :int
   │  └─ IrAction "this" "Flee" () :BtStatus
   └─ IrAction "this" "Patrol" () :BtStatus
```

### Stage 7: C# 出力

```csharp
return new Crisp.Runtime.Nodes.ReactiveSelectorNode(
    new Crisp.Runtime.Nodes.GuardNode(
        () => (this.Health < 30),
        new Crisp.Runtime.Nodes.ActionNode(() => this.Flee(), "Flee()")),
    new Crisp.Runtime.Nodes.ActionNode(() => this.Patrol(), "Patrol()"));
```
