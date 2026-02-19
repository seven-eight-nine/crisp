# Crisp — Detailed Architecture Document

> **Version**: 0.1.0-draft
> **Status**: Design Phase
> **Last Updated**: 2026-02-16

---

## 目次

1. [プロジェクト概要](#1-プロジェクト概要)
2. [言語仕様](#2-言語仕様)
3. [パッケージ構成](#3-パッケージ構成)
4. [Phase 1: 構文基盤](#4-phase-1-構文基盤)
5. [Phase 2: セマンティクス基盤](#5-phase-2-セマンティクス基盤)
6. [Phase 3: IR とバックエンド](#6-phase-3-ir-とバックエンド)
7. [Phase 4: 統合](#7-phase-4-統合)
8. [ランタイム](#8-ランタイム)
9. [エラー設計](#9-エラー設計)
10. [テスト戦略](#10-テスト戦略)
11. [未決定事項・将来拡張](#11-未決定事項将来拡張)

---

## 1. プロジェクト概要

### 1.1 Crispとは

Crisp は S式ベースのビヘイビアツリー DSL である。
C# Source Generator によりコンパイル時に型安全な C# コードへ変換される。

核となる設計思想:
- **S式による合成可能性**: ビヘイビアツリーの本質がLisp的な入れ子評価であることを活かし、S式で直接記述する
- **`this` スコープ**: DSLのスコープは、それが属するC#クラスの `this` と等価。private含む全メンバーにアクセス可能
- **静的型安全性**: Source Generator が Roslyn のセマンティックモデルを通じて、全てのメンバー参照・式・引数を コンパイル時に型検査する
- **ロスレスCST**: Roslyn と同じ方式で、空白・コメントを含む完全なCSTを保持し、フォーマッタ・LSPの基盤とする
- **増分計算**: salsa 相当のクエリベース増分計算エンジンにより、LSPでのインタラクティブ編集とSource Generatorでのビルド時コンパイルを統一的に扱う
- **バックエンド非依存IR**: S式で直列化可能な中間表現を介して、C#コード生成・インタープリタ等の任意のバックエンドに対応する

### 1.2 ユースケース

**プライマリ**: Unity上のゲームAI (ビヘイビアツリー)
**セカンダリ**: ゲームAIに限らない汎用的な非同期状態マシン (UIフロー、ダイアログシステム、ロボティクス等)

### 1.3 動作環境

- .NET 8+ (Source Generator のホスト)
- Unity 2022.3+ (ランタイムのターゲット)
- Roslyn 4.x+ (セマンティック解析)
- LSP対応エディタ (VSCode, Rider 等)

### 1.4 用語集

| 用語 | 定義 |
|------|------|
| CST (Concrete Syntax Tree) | パーサーが生成する、空白・コメントを含むロスレスな構文木 |
| AST (Abstract Syntax Tree) | CSTからloweringされた、セマンティックな構文木。型スロットを持つ |
| IR (Intermediate Representation) | ASTから生成されるバックエンド非依存な中間表現 |
| Trivia | 空白、改行、コメント等のセマンティックに無意味なトークン |
| Query DB | salsa相当の依存関係追跡付きメモ化クエリデータベース |
| BtStatus | ビヘイビアツリーのtick結果: Success, Failure, Running |
| Context | DSLのスコープとなるC#クラスのインスタンス (`this`) |
| Member Path | ドットで連鎖するメンバーアクセスのパス (例: `.Target.Position.x`) |
| Tick | ビヘイビアツリーの1回の評価サイクル |

---

## 2. 言語仕様

### 2.1 字句構造 (Lexical Structure)

#### 2.1.1 トークン種別

```
TokenKind:
  // 区切り
  LeftParen        "("
  RightParen       ")"

  // リテラル
  IntLiteral       ["-"] digit+
  FloatLiteral     ["-"] digit+ "." digit+
  StringLiteral    '"' (<any char except '"' and '\\'> | '\\' escape_char)* '"'
  BoolTrue         "true"
  BoolFalse        "false"
  NullLiteral      "null"

  // 識別子・キーワード
  Identifier       (letter | "_") (letter | digit | "_" | "-")*
  MemberAccess     "." identifier ("." identifier)*
  EnumLiteral      "::" identifier "." identifier

  // キーワード引数
  Keyword          ":" identifier

  // 演算子
  Plus             "+"
  Minus            "-"
  Star             "*"
  Slash            "/"
  Percent          "%"
  LessThan         "<"
  GreaterThan      ">"
  LessEqual        "<="
  GreaterEqual     ">="
  Equal            "="
  NotEqual         "!="

  // 予約語 (Identifierのサブセット)
  KwTree           "tree"
  KwSelect         "select"
  KwSeq            "seq"
  KwParallel       "parallel"
  KwCheck          "check"
  KwGuard          "guard"
  KwIf             "if"
  KwWhile          "while"
  KwRepeat         "repeat"
  KwTimeout        "timeout"
  KwCooldown       "cooldown"
  KwInvert         "invert"
  KwAnd            "and"
  KwOr             "or"
  KwNot            "not"

  // Trivia (CSTでは保持、ASTでは破棄)
  Whitespace       [ \t\r]+
  Newline          \n
  Comment          ";" <any char until newline>

  // 特殊
  EndOfFile
  Error            レクサーが認識できなかった文字列
```

#### 2.1.2 字句ルール

- **識別子**: 先頭は英字または `_`。以降は英数字、`_`、`-` を許容する。
  ケバブケース (`is-enemy-visible`) をDSL側の標準とし、C#のPascalCase (`IsEnemyVisible`) への変換はセマンティクス層で行う。
- **メンバーアクセス**: `.` で始まる識別子チェーン。レクサーはドットから始まる連鎖を **単一トークン** として認識する。
  理由: `(.Foo.Bar.Baz arg)` を正しくパースするために、`.Foo.Bar.Baz` が一塊である必要がある。
  ただし、CSTノードとしては各セグメントを分解して保持する (LSPの部分的なホバー表示等のため)。
- **数値リテラル**: 負号 `-` はリテラルの一部として扱う (単項マイナス演算子ではない)。
  `(- 3 x)` の `-` は演算子、`-3` はリテラル。区別はパーサーが文脈で行う。
- **文字列リテラル**: エスケープシーケンスは `\\`, `\"`, `\n`, `\t`, `\r` を認識する。
- **コメント**: `;` から行末まで。ブロックコメントは初期バージョンでは提供しない。
- **エラートークン**: レクサーがどのパターンにも合致しない文字に遭遇した場合、Error トークンを生成してスキップする。
  エラー回復: 次の空白または括弧まで読み飛ばして Error トークンに含める。

#### 2.1.3 予約語とIdentifierの関係

`select`, `seq`, `check` 等の予約語は、トークン分類時点では通常の Identifier として認識する。
パーサーがリストの先頭要素の Identifier を見て、予約語かどうかを判定する。
これにより、予約語と同名のメンバーが文脈上衝突しないことを保証する。

```
;; "select" は先頭なので予約語
(select ...)

;; ".select" はメンバーアクセスなので予約語ではない
(.select)
```

### 2.2 文法 (Grammar)

#### 2.2.1 EBNF

```ebnf
(* ========== トップレベル ========== *)
program         = tree* EOF ;
tree            = "(" "tree" identifier node+ ")" ;

(* ========== ノード ========== *)
node            = composite
                | decorator
                | check_node
                | action_call
                | member_access    (* 引数なしのアクション/条件 *)
                ;

(* ========== 複合ノード ========== *)
composite       = selector | sequence | parallel ;
selector        = "(" "select"   node node+ ")" ;
sequence        = "(" "seq"      node node+ ")" ;
parallel        = "(" "parallel" parallel_policy node node+ ")" ;
parallel_policy = ":all" | ":any" | ":n" int_literal ;

(* ========== デコレータ ========== *)
decorator       = guard | if_node | invert | repeat
                | timeout | cooldown | while_node ;

guard           = "(" "guard"    expr node ")" ;
if_node         = "(" "if"       expr node node? ")" ;
invert          = "(" "invert"   node ")" ;
repeat          = "(" "repeat"   int_literal node ")" ;
timeout         = "(" "timeout"  number_literal node ")" ;
cooldown        = "(" "cooldown" number_literal node ")" ;
while_node      = "(" "while"    expr node ")" ;

(* ========== 条件チェック ========== *)
check_node      = "(" "check" expr ")" ;

(* ========== アクション呼び出し ========== *)
(* 先頭がメンバーアクセスで、引数が1つ以上あるリスト *)
action_call     = "(" member_access expr+ ")" ;

(* ========== 式 ========== *)
expr            = member_access
                | literal
                | binary_expr
                | unary_expr
                | logic_expr
                | call_expr
                ;

member_access   = "." identifier ( "." identifier )* ;

literal         = int_literal
                | float_literal
                | bool_literal
                | string_literal
                | null_literal
                | enum_literal
                ;

int_literal     = ["-"] digit+ ;
float_literal   = ["-"] digit+ "." digit+ ;
bool_literal    = "true" | "false" ;
string_literal  = '"' char* '"' ;
null_literal    = "null" ;
enum_literal    = "::" identifier "." identifier ;

binary_expr     = "(" binary_op expr expr ")" ;
binary_op       = "+" | "-" | "*" | "/" | "%"
                | "<" | ">" | "<=" | ">=" | "=" | "!=" ;

unary_expr      = "(" unary_op expr ")" ;
unary_op        = "not" | "-" ;

logic_expr      = "(" logic_op expr expr+ ")" ;
logic_op        = "and" | "or" ;

(* メソッド呼び出し (式コンテキスト) *)
call_expr       = "(" member_access expr+ ")" ;

(* ========== 字句要素 ========== *)
identifier      = (letter | "_") (letter | digit | "_" | "-")* ;
number_literal  = int_literal | float_literal ;
digit           = "0"-"9" ;
letter          = "a"-"z" | "A"-"Z" ;
```

#### 2.2.2 文法の曖昧性と解決規則

**問題1: `action_call` vs `call_expr`**

```
(.Attack .Target)
```

これは「ノードとしてのアクション呼び出し」か「式としてのメソッド呼び出し」か。

**解決**: パース時ではなく、**lowering時に文脈で決定**する。
- 親がcomposite/decoratorの子ノード位置 → `AstActionCall`
- 親がexprを期待する位置 (check, guard条件, 二項演算オペランド等) → `AstCallExpr`

CSTレベルでは `CstCall` として統一的にパースし、CSTノードの種別は区別しない。

**問題2: 単項マイナス vs 負のリテラル**

```
(- 3 x)    ;; 二項引き算: 3 - x
-3         ;; 負のリテラル
(- x)      ;; 単項マイナス: -x
```

**解決**:
- レクサーレベル: `-` の直後に数字が続き、かつ `-` の直前が `(` または空白の場合は負のリテラルとして認識する。
  ただし `(` の直後の `-` はパーサーに委ねる (二項演算の可能性があるため)。
- パーサーレベル: `(` の直後の `-` は、後続トークンが2つ以上あれば二項演算、1つなら単項演算として解釈する。

**問題3: `node` vs `expr` の区別**

```
(select
  (.Patrol)           ;; node: アクション呼び出し
  (check (< .hp 30))) ;; node: checkノード
```

vs

```
(check
  (< (.DistanceTo .Target) 5.0))  ;; expr: メソッド呼び出しが式内に
```

**解決**: パーサーは「ノード位置」と「式位置」を文脈で追跡する。
- composite の子 → ノード位置
- `check`, `guard`, `if` の条件 → 式位置
- 二項演算のオペランド → 式位置
- ノード位置でリストの先頭が予約語でもメンバーアクセスでもない → パースエラー

#### 2.2.3 パーサーのエラー回復戦略

S式ベースの文法はエラー回復が比較的容易である。

**戦略1: 括弧バランシング**
閉じ括弧が不足している場合、現在のネストレベルに対応する閉じ括弧が来るまでスキップする。
過剰な閉じ括弧は無視して Error ノードとしてCSTに含める。

**戦略2: 予期しないトークン**
ノード位置で予期しないトークンに遭遇した場合:
1. そのトークンを `CstError` ノードとしてCSTに含める
2. 次の `(` または `)` までスキップ
3. Diagnostic を生成して続行

**戦略3: 不完全な式**
`(< .health` のように式が不完全な場合:
1. 不足部分を `CstMissing` ノードで補完
2. Diagnostic を生成
3. 閉じ括弧を探してパースを続行

これにより、構文エラーがあっても**CSTは常に生成される**。
LSPがエラー状態でも補完・ホバー等を提供できる。

### 2.3 型システム

#### 2.3.1 型の分類

```
CrispType (abstract)
├── PrimitiveType
│   ├── IntType           C#: int (System.Int32)
│   ├── FloatType         C#: float (System.Single)
│   ├── BoolType          C#: bool (System.Boolean)
│   ├── StringType        C#: string (System.String)
│   └── VoidType          C#: void (戻り値なし)
├── SpecialType
│   ├── BtStatusType      C#: BtStatus (enum)
│   ├── NullType          null リテラルの型
│   └── ErrorType         型エラー時のセンチネル (エラー伝播を止める)
├── EnumType              C#: 任意のenum型
│   Name: string
│   UnderlyingType: PrimitiveType
├── ReferenceType         C#: 任意のclass/struct/interface
│   Symbol: INamedTypeSymbol (Roslyn)
└── NullableType          C#: T? (Nullable<T>)
    Inner: CrispType
```

`ErrorType` は重要なセンチネル型で、型エラーが発生した式に付与される。
ErrorType を含む上位の式はさらなるエラーを報告しない (カスケードエラー防止)。

#### 2.3.2 型推論ルール

**リテラル:**

| 式 | 推論型 |
|----|--------|
| `42` | IntType |
| `3.14` | FloatType |
| `true` / `false` | BoolType |
| `"hello"` | StringType |
| `null` | NullType |
| `::Foo.Bar` | EnumType(Foo) |

**メンバーアクセス:**

```
.Foo        → typeof(this.Foo)  // Roslynで解決
.Foo.Bar    → typeof(this.Foo.Bar)
```

Source GeneratorはRoslynの `INamedTypeSymbol` を辿り、各セグメントの型を解決する。
存在しないメンバーは `ErrorType` + Diagnostic `BS0001`。

**算術式の型昇格:**

```
         int     float   string  bool    other
int      int     float   ERROR   ERROR   ERROR
float    float   float   ERROR   ERROR   ERROR
string   ERROR   ERROR   ERROR*  ERROR   ERROR
bool     ERROR   ERROR   ERROR   ERROR   ERROR
other    ERROR   ERROR   ERROR   ERROR   ERROR

* string + string は将来的に文字列結合として許可する可能性あり。初期バージョンではERROR。
```

演算子ごとの適用可能型:

| 演算子 | 適用可能型 | 結果型 |
|--------|-----------|--------|
| `+` `-` `*` `/` | int, float | 昇格後の数値型 |
| `%` | int, float | 昇格後の数値型 |
| `<` `>` `<=` `>=` | int, float | BoolType |
| `=` `!=` | 任意 (両辺が同じ型または暗黙変換可能) | BoolType |
| `and` `or` | bool | BoolType |
| `not` | bool | BoolType |

**暗黙型変換:**

```
int → float     ✓ (無損失)
float → int     ✗ (精度損失、明示的変換が必要)
null → 参照型    ✓
null → 値型      ✗
null → Nullable  ✓
enum → int       ✗ (明示的変換が必要)
それ以外          ✗
```

**メソッド呼び出し:**

```
(.Method arg1 arg2)

1. this の型から Method を探索
2. 引数の型を推論
3. オーバーロード解決 (C#と同じルール)
4. 戻り値型を式の型とする
```

オーバーロード解決の詳細:
- 完全一致を最優先
- 暗黙変換 (int→float) を許容
- 曖昧な場合は Diagnostic `BS0012`
- 見つからない場合は Diagnostic `BS0001`

#### 2.3.3 名前解決ルール

DSL内の名前は以下の優先順位で解決する:

```
1. メンバーアクセス (.Foo)
   → this のメンバーを探索
   → フィールド、プロパティ、メソッド (アクセス修飾子不問)
   → 見つからなければ BS0001

2. ケバブケース変換
   DSL内の名前       → C#候補
   .health          → Health, _health, health
   .is-visible      → IsVisible, _isVisible, isVisible, is_visible
   .max-hp          → MaxHp, _maxHp, maxHp, max_hp

   変換ルール:
   a. そのまま (大文字小文字を区別しない、ケース非感知)
   b. ケバブケース → PascalCase
   c. ケバブケース → camelCase
   d. ケバブケース → _camelCase (privateフィールド慣習)
   e. ケバブケース → snake_case
   最初にマッチしたものを採用。
   複数マッチする場合は完全一致 > PascalCase > camelCase > _camelCase > snake_case の優先順位。

3. enum リテラル (::Foo.Bar)
   → コンテキスト型と同じ名前空間から Foo を探索
   → Foo が enum であることを確認
   → Bar が Foo のメンバーであることを確認
   → 見つからなければ BS0001

4. メソッド引数の型解決
   (.Attack .Target 10.0)
   → Attack メソッドのパラメータ型と引数の推論型を照合
```

#### 2.3.4 ノードの型制約

各ASTノードは、子ノード・式に対して型制約を持つ:

| ノード | 制約 | 違反時 |
|--------|------|--------|
| `check` | 子exprが `BoolType` | BS0007 |
| `guard` | 条件exprが `BoolType` | BS0007 |
| `if` | 条件exprが `BoolType` | BS0007 |
| `while` | 条件exprが `BoolType` | BS0007 |
| `repeat` | 引数が `IntType` の正整数リテラル | BS0013 |
| `timeout` | 引数が `FloatType` または `IntType` の正数リテラル | BS0014 |
| `cooldown` | 引数が `FloatType` または `IntType` の正数リテラル | BS0014 |
| `action_call` | 呼び出し先メソッドの戻り値が `BtStatus` | BS0008 |
| `select` | 子が2つ以上 | BS0015 |
| `seq` | 子が2つ以上 | BS0015 |
| `parallel` | 子が2つ以上 | BS0015 |

---

## 3. パッケージ構成

```
Crisp.sln
│
├── src/
│   ├── Crisp.Syntax/              # Phase 1
│   │   ├── Token.cs               #   トークン型定義
│   │   ├── Lexer.cs               #   字句解析器
│   │   ├── Cst/                   #   CSTノード定義
│   │   │   ├── CstNode.cs         #     基底クラス
│   │   │   ├── CstNodes.cs        #     各ノード型
│   │   │   └── CstTrivia.cs       #     Trivia型
│   │   ├── Parser.cs              #   構文解析器
│   │   ├── Ast/                   #   ASTノード定義
│   │   │   ├── AstNode.cs         #     基底クラス
│   │   │   ├── AstNodes.cs        #     各ノード型
│   │   │   └── AstExpr.cs         #     式ノード型
│   │   ├── Lowering/              #   CST→AST変換
│   │   │   └── CstToAstLowering.cs
│   │   ├── Formatting/            #   フォーマッタ
│   │   │   ├── Formatter.cs
│   │   │   └── FormattingOptions.cs
│   │   └── Visitors/              #   ビジター基盤
│   │       ├── CstVisitor.cs
│   │       └── AstVisitor.cs
│   │
│   ├── Crisp.Query/               # Phase 2
│   │   ├── Database.cs            #   クエリデータベース
│   │   ├── QueryKey.cs            #   クエリキー定義
│   │   ├── Revision.cs            #   リビジョン追跡
│   │   ├── Inputs/                #   入力クエリ
│   │   │   └── InputQueries.cs
│   │   └── Derived/               #   派生クエリ
│   │       └── DerivedQueries.cs
│   │
│   ├── Crisp.Semantics/           # Phase 2
│   │   ├── Resolution/            #   名前解決
│   │   │   ├── NameResolver.cs
│   │   │   └── NameConversion.cs  #   ケバブ↔Pascal変換
│   │   ├── TypeCheck/             #   型検査
│   │   │   ├── TypeInferer.cs
│   │   │   ├── TypeChecker.cs
│   │   │   └── CrispType.cs       #   型定義
│   │   ├── Symbols/               #   シンボルテーブル
│   │   │   └── SymbolTable.cs
│   │   └── Diagnostics/           #   診断メッセージ
│   │       ├── Diagnostic.cs
│   │       ├── DiagnosticBag.cs
│   │       └── DiagnosticDescriptors.cs
│   │
│   ├── Crisp.Lsp/                 # Phase 2
│   │   ├── Server.cs              #   LSPサーバーエントリ
│   │   ├── Handlers/              #   リクエストハンドラ
│   │   │   ├── CompletionHandler.cs
│   │   │   ├── HoverHandler.cs
│   │   │   ├── DefinitionHandler.cs
│   │   │   ├── DiagnosticsHandler.cs
│   │   │   ├── FormattingHandler.cs
│   │   │   └── SemanticTokensHandler.cs
│   │   └── Mapping/               #   位置情報の変換
│   │       └── PositionMapper.cs
│   │
│   ├── Crisp.IR/                  # Phase 3
│   │   ├── Nodes/                 #   IRノード定義
│   │   │   ├── IrNode.cs
│   │   │   └── IrExpr.cs
│   │   ├── Refs/                  #   シンボル参照 (文字列ベース)
│   │   │   ├── MethodRef.cs
│   │   │   ├── MemberRef.cs
│   │   │   └── TypeRef.cs
│   │   ├── Lowering/              #   AST→IR変換
│   │   │   └── AstToIrLowering.cs
│   │   └── Serialization/         #   IR直列化
│   │       ├── IrSerializer.cs    #     IR → S式テキスト
│   │       └── IrDeserializer.cs  #     S式テキスト → IR
│   │
│   ├── Crisp.Backend.CSharp/      # Phase 3
│   │   ├── Emitter/               #   IR→C#変換
│   │   │   └── CSharpEmitter.cs
│   │   └── SourceGenerator/       #   Roslyn Source Generator
│   │       └── CrispSourceGenerator.cs
│   │
│   ├── Crisp.Backend.Interpreter/ # Phase 3
│   │   ├── Evaluator.cs           #   IR直接評価
│   │   └── InterpreterRuntime.cs  #   tick駆動ランタイム
│   │
│   └── Crisp.Runtime/             # 共通ランタイム
│       ├── BtStatus.cs
│       ├── BtNode.cs
│       ├── Nodes/                 #   組み込みノード型
│       │   ├── SelectorNode.cs
│       │   ├── SequenceNode.cs
│       │   ├── ParallelNode.cs
│       │   ├── ConditionNode.cs
│       │   ├── ActionNode.cs
│       │   └── DecoratorNodes.cs
│       └── Attributes/            #   ユーザー用属性
│           ├── BehaviorTreeAttribute.cs
│           └── BtActionAttribute.cs
│
└── tests/
    ├── Crisp.Syntax.Tests/
    │   ├── LexerTests.cs
    │   ├── ParserTests.cs
    │   ├── LoweringTests.cs
    │   ├── FormatterTests.cs
    │   └── Snapshots/              # スナップショットテスト用
    │
    ├── Crisp.Semantics.Tests/
    │   ├── ResolutionTests.cs
    │   ├── TypeCheckTests.cs
    │   └── Snapshots/
    │
    ├── Crisp.IR.Tests/
    │   ├── LoweringTests.cs
    │   ├── SerializationTests.cs
    │   └── Snapshots/
    │
    ├── Crisp.Backend.Tests/
    │   ├── CSharpEmitterTests.cs
    │   ├── InterpreterTests.cs
    │   └── Snapshots/
    │
    └── Crisp.Integration.Tests/
        ├── EndToEndTests.cs
        └── SourceGeneratorTests.cs
```

---

## 4. Phase 1: 構文基盤

### 4.1 レクサー (Lexer)

#### 4.1.1 設計方針

- **手書きレクサー**: S式の字句構造は単純なため、ジェネレータ不要
- **ストリーミング**: `ReadOnlySpan<char>` ベースでアロケーション最小化
- **位置追跡**: 各トークンに `TextSpan(Start, Length)` と `LinePosition(Line, Column)` を付与
- **Trivia分離**: 各トークンに `LeadingTrivia` と `TrailingTrivia` を付与

#### 4.1.2 Token 構造

```csharp
public readonly record struct Token(
    TokenKind Kind,
    string Text,               // トークンの原文
    TextSpan Span,             // ソース上の位置
    ImmutableArray<Trivia> LeadingTrivia,
    ImmutableArray<Trivia> TrailingTrivia);

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public readonly record struct Trivia(
    TriviaKind Kind,           // Whitespace, Newline, Comment
    string Text,
    TextSpan Span);
```

#### 4.1.3 Trivia の帰属ルール

Roslynと同じ方式:
- **Leading Trivia**: トークンの直前にある空白・コメント
- **Trailing Trivia**: トークンの直後、次の改行まで（改行自身を含む）の空白・コメント
- 改行の次からは、次のトークンの Leading Trivia になる

```
;; comment A       ← 次の ( の LeadingTrivia
  (select          ← ( の TrailingTrivia は無し、select の TrailingTrivia は改行まで
    .Patrol)       ← ) の TrailingTrivia は改行
                   ← 空行は次のトークンの LeadingTrivia
```

### 4.2 パーサー (Parser)

#### 4.2.1 設計方針

- **再帰下降パーサー**: S式は再帰下降で十分パース可能
- **ロスレスCST生成**: 全トークン（Trivia含む）をCSTに保持
- **エラー回復**: CSTは常に生成される。エラーノードを含みうる
- **ゼロコピー**: ソーステキストは `ReadOnlyMemory<char>` で保持し、各ノードは `TextSpan` で参照

#### 4.2.2 パーサーの状態

```csharp
public class Parser
{
    private readonly ImmutableArray<Token> _tokens;
    private int _position;
    private readonly DiagnosticBag _diagnostics;

    // 現在のパースコンテキスト
    // ノード位置か式位置かを追跡する
    private ParseContext _context; // Node | Expr
}
```

#### 4.2.3 パースアルゴリズム

```
ParseProgram():
  while not EOF:
    if peek is '(':
      tree = ParseTree()
      add tree to program
    else:
      report unexpected token
      skip to next '(' or EOF

ParseTree():
  expect '('
  expect 'tree'
  name = expect Identifier
  nodes = []
  while peek is not ')' and not EOF:
    nodes.add(ParseNode())
  expect ')'
  return CstTree(name, nodes)

ParseNode():  [context = Node]
  if peek is MemberAccess:
    return ParseMemberAccessOrCall()
  if peek is '(':
    return ParseList()
  report error, return CstError

ParseList():
  expect '('
  head = peek
  if head is keyword:
    switch head:
      'select':   return ParseSelect()
      'seq':      return ParseSequence()
      'parallel': return ParseParallel()
      'check':    return ParseCheck()
      'guard':    return ParseGuard()
      'if':       return ParseIf()
      ...etc
  elif head is MemberAccess:
    return ParseCallNode()   // (.Method args...)
  elif head is Operator:
    return ParseExprInner()  // 式コンテキストに入る (should not happen at node level)
  else:
    report error
    skip to matching ')'
    return CstError

ParseExpr():  [context = Expr]
  if peek is MemberAccess:
    return CstMemberAccess(...)
  if peek is literal:
    return CstLiteral(...)
  if peek is '(':
    return ParseExprList()
  report error, return CstError

ParseExprList():
  expect '('
  head = peek
  if head is Operator or 'and' or 'or' or 'not':
    return ParseOperatorExpr()
  elif head is MemberAccess:
    return ParseCallExpr()   // (.Method args...)
  else:
    report error
    skip to matching ')'
    return CstError
```

### 4.3 CST ノード体系

#### 4.3.1 基底クラス

```csharp
public abstract class CstNode
{
    // ソース上の位置 (Trivia除く)
    public TextSpan Span { get; }

    // Trivia含むフルスパン
    public TextSpan FullSpan { get; }

    // 親ノード (ルートはnull)
    public CstNode? Parent { get; internal set; }

    // 子ノード列挙 (Trivia含まない)
    public abstract IEnumerable<CstNode> Children { get; }

    // 全トークン列挙 (Trivia含む、ソース復元用)
    public abstract IEnumerable<Token> DescendantTokens();
}
```

#### 4.3.2 CSTノード一覧

```
CstNode
├── CstProgram                   # ルート
│   Trees: CstTree[]
│
├── CstTree                      # (tree Name ...)
│   OpenParen: Token
│   TreeKeyword: Token
│   Name: Token (Identifier)
│   Body: CstNode[]             # 子ノード群
│   CloseParen: Token
│
├── CstSelect                    # (select ...)
│   OpenParen: Token
│   Keyword: Token
│   Children: CstNode[]
│   CloseParen: Token
│
├── CstSequence                  # (seq ...)
│   (同上)
│
├── CstParallel                  # (parallel :policy ...)
│   OpenParen: Token
│   Keyword: Token
│   Policy: Token               # :all, :any, :n
│   PolicyArg: Token?           # :n の場合の数値
│   Children: CstNode[]
│   CloseParen: Token
│
├── CstCheck                     # (check expr)
│   OpenParen: Token
│   Keyword: Token
│   Expr: CstNode               # 式
│   CloseParen: Token
│
├── CstGuard                     # (guard expr node)
│   OpenParen: Token
│   Keyword: Token
│   Condition: CstNode          # 条件式
│   Body: CstNode               # 子ノード
│   CloseParen: Token
│
├── CstIf                        # (if expr then else?)
│   OpenParen: Token
│   Keyword: Token
│   Condition: CstNode
│   Then: CstNode
│   Else: CstNode?
│   CloseParen: Token
│
├── CstInvert                    # (invert node)
│   OpenParen: Token
│   Keyword: Token
│   Child: CstNode
│   CloseParen: Token
│
├── CstRepeat                    # (repeat n node)
│   OpenParen: Token
│   Keyword: Token
│   Count: Token                # IntLiteral
│   Child: CstNode
│   CloseParen: Token
│
├── CstTimeout                   # (timeout t node)
│   OpenParen: Token
│   Keyword: Token
│   Seconds: Token              # FloatLiteral or IntLiteral
│   Child: CstNode
│   CloseParen: Token
│
├── CstCooldown                  # (cooldown t node)
│   (CstTimeoutと同構造)
│
├── CstWhile                     # (while expr node)
│   OpenParen: Token
│   Keyword: Token
│   Condition: CstNode
│   Body: CstNode
│   CloseParen: Token
│
├── CstCall                      # (.Member arg*) — 統一的な呼び出し
│   OpenParen: Token
│   Member: CstMemberAccess
│   Args: CstNode[]
│   CloseParen: Token
│
├── CstBinaryExpr                # (op lhs rhs)
│   OpenParen: Token
│   Operator: Token
│   Lhs: CstNode
│   Rhs: CstNode
│   CloseParen: Token
│
├── CstUnaryExpr                 # (not expr) or (- expr)
│   OpenParen: Token
│   Operator: Token
│   Operand: CstNode
│   CloseParen: Token
│
├── CstLogicExpr                 # (and/or expr expr+)
│   OpenParen: Token
│   Operator: Token
│   Operands: CstNode[]
│   CloseParen: Token
│
├── CstMemberAccess              # .Foo.Bar.Baz
│   DotTokens: Token[]          # 各 "." トークン
│   Segments: Token[]           # 各 Identifier トークン
│   (DotTokensとSegmentsは交互に並ぶ)
│
├── CstIntLiteral                # 42
│   Token: Token
│
├── CstFloatLiteral              # 3.14
│   Token: Token
│
├── CstBoolLiteral               # true / false
│   Token: Token
│
├── CstStringLiteral             # "hello"
│   Token: Token
│
├── CstNullLiteral               # null
│   Token: Token
│
├── CstEnumLiteral               # ::Foo.Bar
│   ColonColon: Token
│   TypeName: Token
│   Dot: Token
│   MemberName: Token
│
├── CstKeywordArg                # :all, :any, :n
│   Token: Token
│
├── CstMissing                   # エラー回復で挿入された欠損ノード
│   ExpectedKind: string
│
└── CstError                     # パースエラーを含むノード
    Tokens: Token[]              # エラー範囲のトークン
    Diagnostic: Diagnostic
```

### 4.4 AST ノード体系

#### 4.4.1 基底クラス

```csharp
public abstract class AstNode
{
    // CSTへの逆参照 (エラー報告用)
    public CstNode CstOrigin { get; }

    // セマンティクス層が埋める型情報 (Phase 2)
    public CrispType? ResolvedType { get; set; }

    // セマンティクス層が埋めるシンボル情報 (Phase 2)
    public ISymbol? ResolvedSymbol { get; set; }
}
```

#### 4.4.2 ASTノード一覧

```
AstNode
├── AstTree
│   Name: string
│   Body: AstNode[]
│
├── AstSelector
│   Children: AstNode[]
│
├── AstSequence
│   Children: AstNode[]
│
├── AstParallel
│   Policy: ParallelPolicy       # All | Any | N(int)
│   Children: AstNode[]
│
├── AstCheck
│   Expr: AstExpr
│
├── AstGuard
│   Condition: AstExpr
│   Body: AstNode
│
├── AstIf
│   Condition: AstExpr
│   Then: AstNode
│   Else: AstNode?               # null = Failure
│
├── AstInvert
│   Child: AstNode
│
├── AstRepeat
│   Count: int
│   Child: AstNode
│
├── AstTimeout
│   Seconds: float
│   Child: AstNode
│
├── AstCooldown
│   Seconds: float
│   Child: AstNode
│
├── AstWhile
│   Condition: AstExpr
│   Body: AstNode
│
└── AstActionCall
    Member: MemberPath
    Args: AstExpr[]

AstExpr
├── AstMemberAccessExpr
│   Path: MemberPath              # ["Target", "Position", "x"]
│
├── AstLiteralExpr
│   Value: object                 # boxed value
│   LiteralKind: LiteralKind     # Int, Float, Bool, String, Null, Enum
│
├── AstBinaryExpr
│   Op: BinaryOp                  # Add, Sub, Mul, Div, Mod, Lt, Gt, Le, Ge, Eq, Ne
│   Lhs: AstExpr
│   Rhs: AstExpr
│
├── AstUnaryExpr
│   Op: UnaryOp                   # Not, Negate
│   Operand: AstExpr
│
├── AstLogicExpr
│   Op: LogicOp                   # And, Or
│   Operands: AstExpr[]
│
└── AstCallExpr
    Member: MemberPath
    Args: AstExpr[]

MemberPath:
  Segments: string[]              # 元のDSL名 (ケバブケース)
  ResolvedSegments: ISymbol[]?    # Phase 2で解決 (Roslynシンボル)
```

### 4.5 CST → AST Lowering

#### 4.5.1 変換ルール

```
CstProgram       → AstTree[] (各CstTreeを変換)
CstTree          → AstTree(Name, Body.map(lowerNode))
CstSelect        → AstSelector(Children.map(lowerNode))
CstSequence      → AstSequence(Children.map(lowerNode))
CstParallel      → AstParallel(parsePolicy(), Children.map(lowerNode))
CstCheck         → AstCheck(lowerExpr(Expr))
CstGuard         → AstGuard(lowerExpr(Condition), lowerNode(Body))
CstIf            → AstIf(lowerExpr(Condition), lowerNode(Then), Then?.let(lowerNode))
CstInvert        → AstInvert(lowerNode(Child))
CstRepeat        → AstRepeat(parseInt(Count), lowerNode(Child))
CstTimeout       → AstTimeout(parseFloat(Seconds), lowerNode(Child))
CstCooldown      → AstCooldown(parseFloat(Seconds), lowerNode(Child))
CstWhile         → AstWhile(lowerExpr(Condition), lowerNode(Body))

CstCall (ノード位置) → AstActionCall(lowerMember(Member), Args.map(lowerExpr))
CstCall (式位置)     → AstCallExpr(lowerMember(Member), Args.map(lowerExpr))

CstMemberAccess  → AstMemberAccessExpr(Segments.map(text))
CstBinaryExpr    → AstBinaryExpr(parseOp(Operator), lowerExpr(Lhs), lowerExpr(Rhs))
CstUnaryExpr     → AstUnaryExpr(parseOp(Operator), lowerExpr(Operand))
CstLogicExpr     → AstLogicExpr(parseOp(Operator), Operands.map(lowerExpr))
CstIntLiteral    → AstLiteralExpr(parseInt(Token), Int)
CstFloatLiteral  → AstLiteralExpr(parseFloat(Token), Float)
CstBoolLiteral   → AstLiteralExpr(parseBool(Token), Bool)
CstStringLiteral → AstLiteralExpr(parseString(Token), String)
CstNullLiteral   → AstLiteralExpr(null, Null)
CstEnumLiteral   → AstLiteralExpr(EnumRef(TypeName, MemberName), Enum)

CstMissing       → 対応するAST型のエラーノード + Diagnostic
CstError         → 無視 (Diagnosticは既にパーサーが生成済み)

Trivia           → 全て破棄
```

#### 4.5.2 CstCall のノード/式判定

CstCallは文脈によってAstActionCallまたはAstCallExprに変換される:

```
lowerNode(CstCall call):
  → AstActionCall(...)  // ノード位置

lowerExpr(CstCall call):
  → AstCallExpr(...)    // 式位置

引数なしの CstMemberAccess がノード位置にある場合:
  → AstActionCall(member, args=[])  // 引数なしアクション
```

### 4.6 フォーマッタ

#### 4.6.1 設計方針

- CSTに対して動作する (ASTではない)
- 元のTrivia情報を参考にしつつ、正規化されたフォーマットを出力
- 設定可能なオプションを持つ

#### 4.6.2 フォーマットオプション

```csharp
public record FormattingOptions(
    int IndentSize = 2,              // インデント幅
    int MaxLineWidth = 80,           // 折り返し幅
    bool AlignCloseParen = false,    // 閉じ括弧を揃えるか (Lisp標準: false)
    bool InsertBlankLineBetweenTrees = true,
    TriviaHandling Comments = TriviaHandling.Preserve);

public enum TriviaHandling { Preserve, Remove }
```

#### 4.6.3 フォーマットアルゴリズム

```
Format(CstNode node, indent):
  flat = tryFlatFormat(node)
  if flat.length + indent <= MaxLineWidth:
    return flat                     // 1行に収まればフラット
  else:
    return multiLineFormat(node, indent)  // 複数行

tryFlatFormat(CstNode):
  各子ノードをフラットにフォーマットし、スペースで連結
  例: "(select (.Patrol) (.Idle))"

multiLineFormat(CstNode, indent):
  先頭行: "(keyword" + (短い第一子は同一行に)
  残りの子: 改行 + indent+IndentSize + format(child)
  閉じ括弧: 最後の子の行末

  例:
  (select
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol))
```

---

## 5. Phase 2: セマンティクス基盤

### 5.1 増分計算エンジン (Query Database)

#### 5.1.1 設計思想

RustのsalsaクレートをC#で再実装する。核となる概念:

- **Input Query**: 外部から設定される値。変更されるとリビジョンが上がる
- **Derived Query**: 入力に依存して計算される値。依存元が変更されなければキャッシュを返す
- **Revision**: グローバルなカウンター。入力が変更されるたびにインクリメント
- **Dependency Tracking**: Derived Queryの実行中にアクセスした他のクエリを自動記録

#### 5.1.2 クエリ定義

```csharp
// 入力クエリ
public interface IInputQueries
{
    // ソーステキスト (.crisp内容 or 属性文字列)
    string SourceText(FileId fileId);

    // C#側のコンテキスト型 (Roslynシンボル)
    INamedTypeSymbol? ContextType(FileId fileId);

    // Roslynコンパイレーション (型解決に必要)
    Compilation? RoslynCompilation(FileId fileId);
}

// 派生クエリ
public interface IDerivedQueries
{
    // Phase 1
    ImmutableArray<Token> Lex(FileId fileId);       // depends: SourceText
    CstProgram Parse(FileId fileId);                 // depends: Lex
    AstTree Lower(FileId fileId);                    // depends: Parse

    // Phase 2
    ResolvedTree Resolve(FileId fileId);             // depends: Lower, ContextType
    ImmutableArray<Diagnostic> TypeCheck(FileId fileId);  // depends: Resolve, RoslynCompilation
    ImmutableArray<Diagnostic> AllDiagnostics(FileId fileId); // depends: Parse (構文), TypeCheck (意味)

    // Phase 3
    IrTree EmitIR(FileId fileId);                    // depends: Resolve
}
```

#### 5.1.3 FileId

```csharp
// ファイルの一意識別子
public readonly record struct FileId(string Path)
{
    // インライン属性の場合: "inline://FullyQualifiedTypeName/MethodName"
    // 外部ファイルの場合:   ファイルの絶対パスまたはプロジェクト相対パス
}
```

#### 5.1.4 リビジョン追跡

```csharp
public class Database
{
    private long _currentRevision = 0;

    // 入力の変更
    public void SetSourceText(FileId id, string text)
    {
        if (_sourceTexts[id] != text)
        {
            _sourceTexts[id] = text;
            _sourceTextRevisions[id] = ++_currentRevision;
        }
    }

    // 派生クエリの取得
    public CstProgram Parse(FileId id)
    {
        var cached = _parseCache[id];
        if (cached != null && !IsDirty(cached))
            return cached.Value;

        // 再計算
        var tokens = Lex(id);           // 依存記録
        var result = Parser.Parse(tokens);
        _parseCache[id] = new CacheEntry(result, _currentRevision, dependencies);
        return result;
    }

    private bool IsDirty(CacheEntry entry)
    {
        // 依存する入力のリビジョンがキャッシュ時より新しいか
        return entry.Dependencies.Any(dep => dep.Revision > entry.ComputedAtRevision);
    }
}
```

#### 5.1.5 Source GeneratorとLSPでのDB共有

```
Source Generator:
  - ビルド時に1回だけ実行
  - 全入力を設定 → 全ファイルの派生クエリを計算
  - DBはビルドごとに新規作成 (増分の恩恵は薄い)
  - ただしプロジェクト内の複数 .crisp 間で共有可能

LSP:
  - ファイル変更のたびに入力を更新
  - 変更されたファイルの派生クエリのみ再計算 (増分の恩恵大)
  - DBはサーバーのライフタイム中保持
```

### 5.2 名前解決 (Resolution)

#### 5.2.1 処理フロー

```
入力: AstTree + INamedTypeSymbol (コンテキスト型)
出力: ResolvedTree (全てのMemberPathにRoslynシンボルが付与された木)

1. AstTreeを走査
2. 各 AstMemberAccessExpr / AstActionCall / AstCallExpr に対して:
   a. MemberPathの先頭セグメントをコンテキスト型から探索
   b. 見つかったシンボルの型から次のセグメントを探索
   c. 最後のセグメントまで繰り返す
   d. 全セグメントにResolvedSymbolを設定
```

#### 5.2.2 名前変換 (Name Conversion)

```csharp
public static class NameConversion
{
    /// DSL名からC#メンバー名の候補を生成する
    public static IEnumerable<string> GenerateCandidates(string dslName)
    {
        // 1. そのまま (大文字小文字無視で比較)
        yield return dslName;

        // 2. ケバブケース → PascalCase
        //    "is-enemy-visible" → "IsEnemyVisible"
        yield return ToPascalCase(dslName);

        // 3. ケバブケース → camelCase
        //    "is-enemy-visible" → "isEnemyVisible"
        yield return ToCamelCase(dslName);

        // 4. ケバブケース → _camelCase
        //    "is-enemy-visible" → "_isEnemyVisible"
        yield return "_" + ToCamelCase(dslName);

        // 5. ケバブケース → snake_case
        //    "is-enemy-visible" → "is_enemy_visible"
        yield return ToSnakeCase(dslName);
    }

    /// 候補をコンテキスト型のメンバーと照合し、最初にマッチしたものを返す
    public static ISymbol? Resolve(
        string dslName,
        INamedTypeSymbol contextType,
        SymbolKind? expectedKind = null)
    {
        foreach (var candidate in GenerateCandidates(dslName))
        {
            var members = contextType.GetMembers()
                .Where(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase));

            if (expectedKind.HasValue)
                members = members.Where(m => m.Kind == expectedKind.Value);

            var match = members.FirstOrDefault();
            if (match != null) return match;
        }
        return null;
    }
}
```

#### 5.2.3 メソッドオーバーロード解決

```
(.Method arg1 arg2)

1. コンテキスト型からMethodという名前のメソッドを全て取得
2. 引数の数でフィルタ
3. 各引数について:
   a. 引数の推論型とパラメータ型が一致 → 完全一致スコア
   b. 引数の推論型がパラメータ型に暗黙変換可能 → 変換スコア
   c. どちらでもない → 候補から除外
4. 完全一致が多い候補を優先
5. 候補が1つ → 解決成功
6. 候補が0 → BS0001 or BS0005 or BS0006
7. 候補が2以上 → BS0012 (曖昧)
```

#### 5.2.4 アクセシビリティ

DSLのスコープはthisであるため、生成コードもpartialクラスの一部として出力される。
したがって:
- private メンバー: アクセス可能 (同一クラス)
- protected メンバー: アクセス可能 (同一クラス)
- internal メンバー: アクセス可能 (同一アセンブリ)
- public メンバー: アクセス可能

**全てのアクセス修飾子のメンバーにアクセス可能**。
名前解決時にアクセシビリティによるフィルタは行わない。

### 5.3 型推論・型検査

#### 5.3.1 処理フロー

```
入力: ResolvedTree (シンボル解決済み)
出力: TypedTree (全ノードに型情報が付与された木) + Diagnostics

Pass 1: 型推論 (ボトムアップ)
  リーフから根に向かって各ノードの型を推論する。

Pass 2: 型検査 (トップダウン)
  各ノードの型制約を検証する。
```

#### 5.3.2 型推論の詳細

```csharp
public class TypeInferer : AstVisitor<CrispType>
{
    public override CrispType Visit(AstLiteralExpr node) => node.LiteralKind switch
    {
        LiteralKind.Int    => CrispType.Int,
        LiteralKind.Float  => CrispType.Float,
        LiteralKind.Bool   => CrispType.Bool,
        LiteralKind.String => CrispType.String,
        LiteralKind.Null   => CrispType.Null,
        LiteralKind.Enum   => ResolveEnumType(node),
        _ => CrispType.Error
    };

    public override CrispType Visit(AstMemberAccessExpr node)
    {
        // 最後のセグメントのResolvedSymbolの型を返す
        var lastSymbol = node.Path.ResolvedSegments?.Last();
        return lastSymbol switch
        {
            IPropertySymbol prop => MapType(prop.Type),
            IFieldSymbol field   => MapType(field.Type),
            IMethodSymbol method => MapType(method.ReturnType),
            _ => CrispType.Error  // BS0001 は Resolution で報告済み
        };
    }

    public override CrispType Visit(AstBinaryExpr node)
    {
        var lhs = Visit(node.Lhs);
        var rhs = Visit(node.Rhs);

        // ErrorType伝播
        if (lhs is ErrorType || rhs is ErrorType) return CrispType.Error;

        return node.Op switch
        {
            // 算術演算: 型昇格
            BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul
            or BinaryOp.Div or BinaryOp.Mod
                => PromoteArithmetic(lhs, rhs, node),

            // 比較演算: bool
            BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le
            or BinaryOp.Ge or BinaryOp.Eq or BinaryOp.Ne
                => CheckComparable(lhs, rhs, node) ? CrispType.Bool : CrispType.Error,

            _ => CrispType.Error
        };
    }

    public override CrispType Visit(AstCallExpr node)
    {
        // オーバーロード解決済みのメソッドの戻り値型
        var method = node.ResolvedSymbol as IMethodSymbol;
        return method != null ? MapType(method.ReturnType) : CrispType.Error;
    }

    private CrispType PromoteArithmetic(CrispType lhs, CrispType rhs, AstNode node)
    {
        if (lhs == CrispType.Int && rhs == CrispType.Int) return CrispType.Int;
        if (lhs == CrispType.Float && rhs == CrispType.Float) return CrispType.Float;
        if ((lhs == CrispType.Int && rhs == CrispType.Float) ||
            (lhs == CrispType.Float && rhs == CrispType.Int)) return CrispType.Float;

        _diagnostics.Report(BS0004, node, lhs, rhs);
        return CrispType.Error;
    }
}
```

#### 5.3.3 型検査の詳細

```csharp
public class TypeChecker : AstVisitor
{
    public override void Visit(AstCheck node)
    {
        var type = _inferer.Visit(node.Expr);
        if (type != CrispType.Bool && type != CrispType.Error)
            _diagnostics.Report(BS0007, node, type);
    }

    public override void Visit(AstGuard node)
    {
        var type = _inferer.Visit(node.Condition);
        if (type != CrispType.Bool && type != CrispType.Error)
            _diagnostics.Report(BS0007, node, type);
        Visit(node.Body);
    }

    public override void Visit(AstActionCall node)
    {
        var method = node.ResolvedSymbol as IMethodSymbol;
        if (method != null)
        {
            var returnType = MapType(method.ReturnType);
            if (returnType != CrispType.BtStatus)
                _diagnostics.Report(BS0008, node, method.Name);

            // 引数の型チェック
            for (int i = 0; i < node.Args.Length; i++)
            {
                var argType = _inferer.Visit(node.Args[i]);
                var paramType = MapType(method.Parameters[i].Type);
                if (!IsAssignable(argType, paramType))
                    _diagnostics.Report(BS0006, node, i, method.Name, paramType, argType);
            }
        }
    }

    public override void Visit(AstSelector node)
    {
        if (node.Children.Length < 2)
            _diagnostics.Report(BS0015, node, "select", 2);
        foreach (var child in node.Children)
            Visit(child);
    }
}
```

### 5.4 LSP サーバー

#### 5.4.1 サポートする機能

| LSP メソッド | 機能 | 依存するQuery |
|-------------|------|--------------|
| `textDocument/didOpen` | ファイルオープン | SetSourceText |
| `textDocument/didChange` | ファイル変更 | SetSourceText |
| `textDocument/completion` | メンバー補完 | Resolve |
| `textDocument/hover` | 型情報ホバー | Resolve, TypeCheck |
| `textDocument/definition` | C#定義ジャンプ | Resolve |
| `textDocument/publishDiagnostics` | エラー表示 | AllDiagnostics |
| `textDocument/formatting` | フォーマット | Parse |
| `textDocument/semanticTokens` | ハイライト | Lex |

#### 5.4.2 補完 (Completion)

```
トリガー: ユーザーが "." を入力

1. カーソル位置のCSTノードを特定
2. "." の前のコンテキストを解析:
   a. トップレベルの "." → コンテキスト型の全メンバーを列挙
   b. ".Foo." → Foo の型のメンバーを列挙
3. メンバーをCompletionItemに変換:
   - プロパティ/フィールド → アイコン: Property
   - メソッド (BtStatus戻り) → アイコン: Method, ラベルに "()" 付加
   - メソッド (その他) → アイコン: Function
4. ケバブケースの候補名も同時に提示
   例: Health → .Health, .health (フィルタリング用)
```

#### 5.4.3 ホバー (Hover)

```
1. カーソル位置のCSTノードを特定
2. 対応するASTノードのResolvedSymbol/ResolvedTypeを取得
3. ホバー情報を構築:
   - メンバー: "プロパティ float EnemyAI.Health"
   - メソッド: "メソッド BtStatus EnemyAI.Attack(Entity target)"
   - 式: "型: bool"
   - リテラル: "型: int, 値: 42"
```

#### 5.4.4 セマンティックトークン (Semantic Tokens)

```
トークン種別 → セマンティックトークンタイプ:
  予約語 (select, seq, etc.)   → keyword
  メンバーアクセス (.Foo)      → property (解決後にmethod等に変更可能)
  IntLiteral                    → number
  FloatLiteral                  → number
  BoolLiteral                   → keyword
  StringLiteral                 → string
  EnumLiteral (::Foo.Bar)       → enumMember
  演算子                        → operator
  コメント                      → comment
  Keyword引数 (:all)            → parameter
```

---

## 6. Phase 3: IR とバックエンド

### 6.1 IR (中間表現)

#### 6.1.1 設計思想

- **Roslynシンボルからの脱却**: IRはRoslynに依存しない。シンボル参照は全て文字列ベースの `Ref` に変換
- **バックエンド非依存**: C#生成もインタープリタ実行も同じIRから行う
- **S式で直列化可能**: デバッグ・テスト・中間出力として読み書きできる
- **型情報付き**: 各ノード・式に型情報が付与されている (TypeRefとして)

#### 6.1.2 Ref 型

```csharp
/// メソッドへの参照
public record MethodRef(
    string DeclaringType,        // "EnemyAI"
    string Name,                 // "Attack"
    ImmutableArray<string> ParamTypes);  // ["Entity", "System.Single"]

/// メンバー (プロパティ/フィールド) チェーンへの参照
public record MemberRef(
    ImmutableArray<string> Chain);  // ["_target", "Position", "x"]

/// 型への参照
public record TypeRef(
    string FullName);             // "System.Single", "UnityEngine.Vector3"
```

#### 6.1.3 IR ノード

```
IrNode (abstract)
  TypeRef: TypeRef?               # ノードの結果型 (BtStatus等)
│
├── IrSelector
│   Children: IrNode[]
│
├── IrSequence
│   Children: IrNode[]
│
├── IrParallel
│   Policy: ParallelPolicy
│   Children: IrNode[]
│
├── IrCondition
│   Expr: IrExpr
│
├── IrAction
│   Method: MethodRef
│   Args: IrExpr[]
│
├── IrGuard
│   Condition: IrExpr
│   Body: IrNode
│
├── IrIf
│   Condition: IrExpr
│   Then: IrNode
│   Else: IrNode?
│
├── IrInvert
│   Child: IrNode
│
├── IrRepeat
│   Count: int
│   Child: IrNode
│
├── IrTimeout
│   Seconds: float
│   Child: IrNode
│
├── IrCooldown
│   Seconds: float
│   Child: IrNode
│
└── IrWhile
    Condition: IrExpr
    Body: IrNode

IrExpr (abstract)
  TypeRef: TypeRef                # 式の型
│
├── IrMemberLoad
│   Ref: MemberRef
│
├── IrLiteral
│   Value: object
│   Kind: LiteralKind
│
├── IrBinaryOp
│   Op: BinaryOp
│   Lhs: IrExpr
│   Rhs: IrExpr
│
├── IrUnaryOp
│   Op: UnaryOp
│   Operand: IrExpr
│
├── IrLogicOp
│   Op: LogicOp
│   Operands: IrExpr[]
│
├── IrCall
│   Method: MethodRef
│   Args: IrExpr[]
│
└── IrConvert                     # 暗黙型変換
    Operand: IrExpr
    TargetType: TypeRef
```

`IrConvert` は重要: AST→IR lowering 時に暗黙型変換が挿入される。
例: `(< .Health 30)` で `.Health` が float、`30` が int の場合、
IRでは `IrConvert(IrLiteral(30, Int), Float)` が挿入される。
これによりバックエンドは型変換を意識する必要がなくなる。

#### 6.1.4 AST → IR Lowering

```
AstSelector       → IrSelector(children.map(lowerNode))
AstSequence       → IrSequence(children.map(lowerNode))
AstParallel       → IrParallel(policy, children.map(lowerNode))
AstCheck          → IrCondition(lowerExpr(expr))
AstGuard          → IrGuard(lowerExpr(condition), lowerNode(body))
AstIf             → IrIf(lowerExpr(condition), lowerNode(then), lowerNode(else))
AstInvert         → IrInvert(lowerNode(child))
AstRepeat         → IrRepeat(count, lowerNode(child))
AstTimeout        → IrTimeout(seconds, lowerNode(child))
AstCooldown       → IrCooldown(seconds, lowerNode(child))
AstWhile          → IrWhile(lowerExpr(condition), lowerNode(body))
AstActionCall     → IrAction(toMethodRef(symbol), args.map(lowerExpr))

AstMemberAccessExpr → IrMemberLoad(toMemberRef(symbols))
AstLiteralExpr      → IrLiteral(value, kind)
AstBinaryExpr       → IrBinaryOp(op, lowerExpr(lhs), lowerExpr(rhs))
                      ※ 必要に応じて IrConvert を挿入
AstUnaryExpr        → IrUnaryOp(op, lowerExpr(operand))
AstLogicExpr        → IrLogicOp(op, operands.map(lowerExpr))
AstCallExpr         → IrCall(toMethodRef(symbol), args.map(lowerExpr))

toMethodRef(IMethodSymbol s):
  MethodRef(s.ContainingType.Name, s.Name, s.Parameters.Select(p => p.Type.FullName))

toMemberRef(ISymbol[] chain):
  MemberRef(chain.Select(s => s.Name))
```

#### 6.1.5 IR の S式直列化

```
;; 直列化形式
(ir-selector
  (ir-sequence
    (ir-condition
      (ir-binary-op :lt
        (ir-member-load ("_health") :type "System.Single")
        (ir-convert
          (ir-literal 30 :int)
          :to "System.Single")))
    (ir-action "EnemyAI" "Flee" () :type "Crisp.Runtime.BtStatus"))
  (ir-action "EnemyAI" "Patrol" () :type "Crisp.Runtime.BtStatus"))
```

この形式は:
- テストのスナップショットとして使用
- デバッグ時の中間出力として使用
- 将来的にIRレベルの最適化パスの入出力として使用

### 6.2 C# バックエンド

#### 6.2.1 Emitter

```
IR → C# ソース変換:

IrSelector(children)
  → new SelectorNode(child1, child2, ...)

IrSequence(children)
  → new SequenceNode(child1, child2, ...)

IrParallel(policy, children)
  → new ParallelNode(ParallelPolicy.{policy}, child1, child2, ...)

IrCondition(expr)
  → new ConditionNode(() => {expr})

IrAction(method, args)
  → new ActionNode(() => this.{Method}({args}))

IrGuard(cond, body)
  → new GuardNode(() => {cond}, {body})

IrIf(cond, then, else)
  → new IfNode(() => {cond}, {then}, {else})

IrInvert(child)
  → new InvertNode({child})

IrRepeat(n, child)
  → new RepeatNode({n}, {child})

IrTimeout(t, child)
  → new TimeoutNode({t}f, {child})

IrCooldown(t, child)
  → new CooldownNode({t}f, {child})

IrWhile(cond, body)
  → new WhileNode(() => {cond}, {body})

IrMemberLoad(chain)
  → this.{chain[0]}.{chain[1]}...

IrLiteral(value, Int)
  → {value}

IrLiteral(value, Float)
  → {value}f

IrLiteral(value, Bool)
  → true / false

IrLiteral(value, String)
  → "{value}"

IrBinaryOp(op, lhs, rhs)
  → ({lhs} {op} {rhs})

IrConvert(expr, targetType)
  → (({targetType}){expr})

IrCall(method, args)
  → this.{Method}({args})
```

#### 6.2.2 生成コードの形式

```csharp
// <auto-generated by Crisp Source Generator/>
// Source: EnemyCombat.crisp
#nullable enable

partial class EnemyAI
{
    public partial BtNode BuildCombatTree()
    {
        return new SelectorNode(
            new SequenceNode(
                new ConditionNode(() => this._health < (float)30),
                new ActionNode(() => this.Flee())),
            new ActionNode(() => this.Patrol()));
    }
}
```

#### 6.2.3 Source Generator エントリポイント

```csharp
[Generator]
public class CrispSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. [BehaviorTree] 属性付きメソッドを検出
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Crisp.Runtime.BehaviorTreeAttribute",
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (ctx, ct) => ExtractInfo(ctx, ct));

        // 2. AdditionalFiles から .crisp を収集
        var bscriptFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".crisp"));

        // 3. メソッド情報 + ファイル内容を結合
        var combined = methods.Combine(bscriptFiles.Collect());

        // 4. 各メソッドに対してコード生成
        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (method, files) = pair;
            var db = new Database();

            // 入力設定
            var fileId = ResolveFileId(method);
            db.SetSourceText(fileId, ResolveSource(method, files));
            db.SetContextType(fileId, method.ContainingType);
            db.SetRoslynCompilation(fileId, method.Compilation);

            // 診断取得
            var diagnostics = db.AllDiagnostics(fileId);
            foreach (var diag in diagnostics)
                spc.ReportDiagnostic(diag.ToRoslynDiagnostic());

            // エラーがなければコード生成
            if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var ir = db.EmitIR(fileId);
                var source = CSharpEmitter.Emit(ir, method);
                spc.AddSource($"{method.ContainingType.Name}.{method.MethodName}.g.cs", source);
            }
        });
    }
}
```

### 6.3 インタープリタバックエンド

#### 6.3.1 設計方針

- Source Generator なしで BT の動作検証を行うための PoC
- IR を直接 tick 駆動で評価する
- リフレクションでコンテキストオブジェクトのメンバーにアクセス

#### 6.3.2 評価器

```csharp
public class Evaluator
{
    private readonly object _context;  // this に相当するオブジェクト

    public BtStatus Tick(IrNode node) => node switch
    {
        IrSelector sel   => EvalSelector(sel),
        IrSequence seq   => EvalSequence(seq),
        IrCondition cond => EvalExpr(cond.Expr) is true ? BtStatus.Success : BtStatus.Failure,
        IrAction act     => InvokeAction(act),
        IrGuard guard    => EvalExpr(guard.Condition) is true ? Tick(guard.Body) : BtStatus.Failure,
        IrInvert inv     => Tick(inv.Child) switch
        {
            BtStatus.Success => BtStatus.Failure,
            BtStatus.Failure => BtStatus.Success,
            var s => s
        },
        _ => throw new NotSupportedException()
    };

    private BtStatus EvalSelector(IrSelector sel)
    {
        foreach (var child in sel.Children)
        {
            var result = Tick(child);
            if (result != BtStatus.Failure) return result;
        }
        return BtStatus.Failure;
    }

    private BtStatus EvalSequence(IrSequence seq)
    {
        foreach (var child in seq.Children)
        {
            var result = Tick(child);
            if (result != BtStatus.Success) return result;
        }
        return BtStatus.Success;
    }

    private object? EvalExpr(IrExpr expr) => expr switch
    {
        IrLiteral lit      => lit.Value,
        IrMemberLoad mem   => ResolveMemberChain(mem.Ref),
        IrBinaryOp bin     => EvalBinary(bin),
        IrUnaryOp un       => EvalUnary(un),
        IrCall call        => InvokeMethod(call),
        IrConvert conv     => Convert.ChangeType(EvalExpr(conv.Operand), ResolveType(conv.TargetType)),
        _ => throw new NotSupportedException()
    };

    private object? ResolveMemberChain(MemberRef memberRef)
    {
        object? current = _context;
        foreach (var segment in memberRef.Chain)
        {
            if (current == null) return null;
            var type = current.GetType();
            var member = type.GetProperty(segment) ?? (MemberInfo?)type.GetField(segment);
            current = member switch
            {
                PropertyInfo p => p.GetValue(current),
                FieldInfo f    => f.GetValue(current),
                _ => throw new MemberAccessException($"Member '{segment}' not found on {type.Name}")
            };
        }
        return current;
    }

    private BtStatus InvokeAction(IrAction action)
    {
        var type = _context.GetType();
        var method = type.GetMethod(action.Method.Name);
        var args = action.Args.Select(EvalExpr).ToArray();
        return (BtStatus)method!.Invoke(_context, args)!;
    }
}
```

---

## 7. Phase 4: 統合

### 7.1 全パイプライン結合

```
最終的なデータフロー:

[.crisp / inline string]
        │
        ▼
   Lexer (Crisp.Syntax)
        │ Token[]
        ▼
   Parser (Crisp.Syntax)
        │ CstProgram
        ├──→ Formatter (Crisp.Syntax)  → フォーマット済みテキスト
        ▼
   CstToAstLowering (Crisp.Syntax)
        │ AstTree
        ▼
  ┌─── Query Database (Crisp.Query) ───────────────────────┐
  │     │                                                    │
  │     ├──→ NameResolver (Crisp.Semantics)                 │
  │     │         │ ResolvedTree                             │
  │     │         ▼                                          │
  │     ├──→ TypeInferer + TypeChecker (Crisp.Semantics)    │
  │     │         │ TypedTree + Diagnostics                  │
  │     │         ├──→ LSP Server (Crisp.Lsp)               │
  │     │         │                                          │
  │     │         ▼                                          │
  │     └──→ AstToIrLowering (Crisp.IR)                    │
  │               │ IrTree                                   │
  └───────────────┼──────────────────────────────────────────┘
                  │
         ┌───────┴────────┐
         ▼                ▼
   CSharpEmitter     Evaluator
   (Backend.CSharp)  (Backend.Interpreter)
         │                │
         ▼                ▼
   C# Source         直接実行
   (Source Generator)
```

### 7.2 Source Generator パフォーマンス要件

Source Generator はビルドのクリティカルパスに入るため、パフォーマンスが重要:

- **目標**: 1ファイルあたり < 50ms
- **キャッシュ**: Incremental Generator の equatable 比較でキャッシュ活用
- **アロケーション**: immutable 構造体を多用し、GC圧を低減
- **並列**: 複数ファイルの処理は独立なので並列化可能

### 7.3 LSP + Source Generator の Query DB 共有

```
共通の Query 定義 (Crisp.Query) を:
- Source Generator 側: Compilation から入力を設定
- LSP 側: ファイル変更イベントから入力を設定

同じ Derived Query ロジックが両方で動くため、
「LSPでは型エラーが出るのにビルドでは出ない」という不整合が原理的に発生しない。
```

---

## 8. ランタイム

### 8.1 BtStatus

```csharp
public enum BtStatus
{
    Success,
    Failure,
    Running
}
```

### 8.2 BtNode 基底クラス

```csharp
public abstract class BtNode
{
    public abstract BtStatus Tick();

    // デコレータ用: 状態リセット
    public virtual void Reset() { }
}
```

### 8.3 組み込みノード

```csharp
public class SelectorNode : BtNode
{
    private readonly BtNode[] _children;
    private int _runningIndex = -1;   // Running状態の子を記憶

    public override BtStatus Tick()
    {
        int start = _runningIndex >= 0 ? _runningIndex : 0;
        for (int i = start; i < _children.Length; i++)
        {
            var status = _children[i].Tick();
            switch (status)
            {
                case BtStatus.Success:
                    _runningIndex = -1;
                    return BtStatus.Success;
                case BtStatus.Running:
                    _runningIndex = i;
                    return BtStatus.Running;
                // Failure: 次の子へ
            }
        }
        _runningIndex = -1;
        return BtStatus.Failure;
    }

    public override void Reset()
    {
        _runningIndex = -1;
        foreach (var child in _children) child.Reset();
    }
}

public class SequenceNode : BtNode
{
    private readonly BtNode[] _children;
    private int _runningIndex = -1;

    public override BtStatus Tick()
    {
        int start = _runningIndex >= 0 ? _runningIndex : 0;
        for (int i = start; i < _children.Length; i++)
        {
            var status = _children[i].Tick();
            switch (status)
            {
                case BtStatus.Failure:
                    _runningIndex = -1;
                    return BtStatus.Failure;
                case BtStatus.Running:
                    _runningIndex = i;
                    return BtStatus.Running;
                // Success: 次の子へ
            }
        }
        _runningIndex = -1;
        return BtStatus.Success;
    }
}

public class ParallelNode : BtNode
{
    private readonly BtNode[] _children;
    private readonly ParallelPolicy _policy;

    public override BtStatus Tick()
    {
        int successCount = 0, failureCount = 0, runningCount = 0;
        foreach (var child in _children)
        {
            switch (child.Tick())
            {
                case BtStatus.Success: successCount++; break;
                case BtStatus.Failure: failureCount++; break;
                case BtStatus.Running: runningCount++; break;
            }
        }
        return _policy switch
        {
            ParallelPolicy.All =>
                failureCount > 0 ? BtStatus.Failure :
                runningCount > 0 ? BtStatus.Running :
                BtStatus.Success,
            ParallelPolicy.Any =>
                successCount > 0 ? BtStatus.Success :
                runningCount > 0 ? BtStatus.Running :
                BtStatus.Failure,
            ParallelPolicy.N(var n) =>
                successCount >= n ? BtStatus.Success :
                failureCount > _children.Length - n ? BtStatus.Failure :
                BtStatus.Running,
            _ => throw new InvalidOperationException()
        };
    }
}

public class ConditionNode : BtNode
{
    private readonly Func<bool> _condition;
    public override BtStatus Tick() => _condition() ? BtStatus.Success : BtStatus.Failure;
}

public class ActionNode : BtNode
{
    private readonly Func<BtStatus> _action;
    public override BtStatus Tick() => _action();
}

public class GuardNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _child;

    public override BtStatus Tick()
        => _condition() ? _child.Tick() : BtStatus.Failure;
}

public class IfNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _then;
    private readonly BtNode? _else;

    public override BtStatus Tick()
        => _condition() ? _then.Tick() : (_else?.Tick() ?? BtStatus.Failure);
}

public class InvertNode : BtNode
{
    private readonly BtNode _child;

    public override BtStatus Tick() => _child.Tick() switch
    {
        BtStatus.Success => BtStatus.Failure,
        BtStatus.Failure => BtStatus.Success,
        var s => s  // Running はそのまま
    };
}

public class RepeatNode : BtNode
{
    private readonly int _count;
    private readonly BtNode _child;
    private int _current;

    public override BtStatus Tick()
    {
        while (_current < _count)
        {
            var status = _child.Tick();
            if (status == BtStatus.Running) return BtStatus.Running;
            if (status == BtStatus.Failure) { _current = 0; return BtStatus.Failure; }
            _current++;
        }
        _current = 0;
        return BtStatus.Success;
    }
}

public class TimeoutNode : BtNode
{
    private readonly float _seconds;
    private readonly BtNode _child;
    private float _elapsed;

    // 注: DeltaTime は外部から設定される必要がある
    public float DeltaTime { get; set; }

    public override BtStatus Tick()
    {
        _elapsed += DeltaTime;
        if (_elapsed >= _seconds) { Reset(); return BtStatus.Failure; }
        var status = _child.Tick();
        if (status != BtStatus.Running) Reset();
        return status;
    }

    public override void Reset() { _elapsed = 0; _child.Reset(); }
}

public class CooldownNode : BtNode
{
    private readonly float _seconds;
    private readonly BtNode _child;
    private float _remaining;

    public float DeltaTime { get; set; }

    public override BtStatus Tick()
    {
        _remaining -= DeltaTime;
        if (_remaining > 0) return BtStatus.Failure;
        var status = _child.Tick();
        if (status == BtStatus.Success) _remaining = _seconds;
        return status;
    }
}

public class WhileNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _body;

    public override BtStatus Tick()
    {
        if (!_condition()) return BtStatus.Failure;
        return _body.Tick();
    }
}
```

### 8.4 属性

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class BehaviorTreeAttribute : Attribute
{
    /// <summary>
    /// S式文字列 (インライン) または .crisp ファイルパス (外部ファイル)
    /// </summary>
    public string Source { get; }
    public BehaviorTreeAttribute(string source) => Source = source;
}

[AttributeUsage(AttributeTargets.Method)]
public class BtActionAttribute : Attribute { }
```

### 8.5 DeltaTime の注入

TimeoutNode, CooldownNode は DeltaTime を必要とする。

```csharp
// 方式: TickContext を通じて注入
public class TickContext
{
    public float DeltaTime { get; set; }
}

// BtNode.Tick() のシグネチャを変更するか、
// あるいは BtNode がコンストラクタで TickContext を受け取る

// 選択肢A: Tick引数
public abstract BtStatus Tick(TickContext ctx);

// 選択肢B: コンストラクタ注入
public TimeoutNode(float seconds, BtNode child, TickContext ctx);

// → 未決定事項 (セクション11参照)
```

---

## 9. エラー設計

### 9.1 Diagnostic 構造

```csharp
public record Diagnostic(
    DiagnosticDescriptor Descriptor,
    TextSpan Span,                    // ソース上の位置
    string? FilePath,                 // ファイルパス
    ImmutableArray<object> Args);     // フォーマット引数

public record DiagnosticDescriptor(
    string Id,                        // "BS0001"
    string Title,                     // "Member not found"
    string MessageFormat,             // "Member '{0}' not found on type '{1}'"
    DiagnosticSeverity Severity,      // Error, Warning, Info
    DiagnosticCategory Category);     // Syntax, Semantics, etc.
```

### 9.2 全Diagnostic一覧

#### 9.2.1 構文エラー (BS00xx)

| ID | Severity | メッセージ | 発生条件 |
|----|----------|-----------|---------|
| BS0009 | Error | Parse error at {0}:{1}: {2} | パーサーが構文エラーを検出 |
| BS0016 | Error | Expected '{0}', found '{1}' | 期待されたトークンと異なる |
| BS0017 | Error | Unterminated string literal | 文字列リテラルの閉じ引用符がない |
| BS0018 | Error | Unmatched '(' | 閉じ括弧がない |
| BS0019 | Error | Unexpected ')' | 対応する開き括弧がない |
| BS0020 | Warning | Unused tree '{0}' | 定義されたツリーが参照されていない |

#### 9.2.2 名前解決エラー (BS01xx)

| ID | Severity | メッセージ | 発生条件 |
|----|----------|-----------|---------|
| BS0001 | Error | Member '{0}' not found on type '{1}' | メンバーが存在しない |
| BS0011 | Error | External file '{0}' not found | .crispファイルが見つからない |
| BS0012 | Error | Overload resolution ambiguous for '{0}' with arguments ({1}) | オーバーロード曖昧 |
| BS0101 | Error | Context type '{0}' does not implement IBtContext | インターフェース未実装 |
| BS0102 | Error | Enum type '{0}' not found | enumリテラルの型が見つからない |
| BS0103 | Error | Enum member '{0}.{1}' not found | enumメンバーが見つからない |

#### 9.2.3 型エラー (BS02xx)

| ID | Severity | メッセージ | 発生条件 |
|----|----------|-----------|---------|
| BS0002 | Error | Type mismatch: expected '{0}', got '{1}' | 一般的な型不一致 |
| BS0003 | Error | Cannot compare '{0}' with '{1}' | 比較不能な型 |
| BS0004 | Error | Arithmetic operator '{0}' not applicable to '{1}' | 算術演算に不適切な型 |
| BS0005 | Error | Method '{0}' expects {1} arguments, got {2} | 引数の数が不一致 |
| BS0006 | Error | Argument {0} of '{1}': expected '{2}', got '{3}' | 引数の型が不一致 |
| BS0007 | Error | Expression in '{0}' must be bool, got '{1}' | check/guard/if/whileの条件が非bool |
| BS0008 | Error | Action method '{0}' must return BtStatus | アクションの戻り値が不正 |

#### 9.2.4 構造エラー (BS03xx)

| ID | Severity | メッセージ | 発生条件 |
|----|----------|-----------|---------|
| BS0013 | Error | 'repeat' count must be a positive integer literal | repeatの引数が不正 |
| BS0014 | Error | '{0}' duration must be a positive number literal | timeout/cooldownの引数が不正 |
| BS0015 | Error | '{0}' requires at least {1} children | 子ノード数が不足 |
| BS0301 | Warning | Unreachable node after unconditional Success | 到達不能なノード |
| BS0302 | Warning | 'if' without else branch always returns Failure on false | elseなしifの警告 |

#### 9.2.5 その他 (BS09xx)

| ID | Severity | メッセージ | 発生条件 |
|----|----------|-----------|---------|
| BS0010 | Warning | Member '{0}' is obsolete: {1} | [Obsolete]メンバーの使用 |
| BS0901 | Error | Internal compiler error: {0} | 予期しないエラー |

### 9.3 エラー回復とカスケード防止

```
原則:
1. 1つのエラーから可能な限り多くのエラーを報告する (パース続行)
2. ただし、同じ原因に起因するカスケードエラーは抑制する

具体策:
- ErrorType: 型エラーが発生した式にErrorTypeを付与。
  ErrorTypeを含む上位の式は追加エラーを報告しない。
- CstMissing: 欠損ノードは1回だけDiagnosticを生成。
  lowering時にMissingノードから派生するASTは型チェックをスキップ。
- 同一Span: 同じTextSpanに対して同じIDのDiagnosticは1回のみ。
```

---

## 10. テスト戦略

### 10.1 テストの種類

| 種類 | 対象 | 手法 |
|------|------|------|
| ユニットテスト | 各クラスの個別メソッド | xUnit |
| スナップショットテスト | パーサー出力、IR出力、生成コード | Verify (verify.that) |
| プロパティベーステスト | パーサーのラウンドトリップ | FsCheck / CsCheck |
| 統合テスト | end-to-end パイプライン | xUnit + Source Generator テストヘルパー |

### 10.2 スナップショットテストの構成

```
tests/
├── Crisp.Syntax.Tests/
│   └── Snapshots/
│       ├── Parser/
│       │   ├── SimpleSelect.input.crisp      # 入力
│       │   ├── SimpleSelect.cst.verified      # 期待CST (テキスト表現)
│       │   └── SimpleSelect.ast.verified      # 期待AST
│       ├── Formatter/
│       │   ├── Unformatted.input.crisp
│       │   └── Unformatted.formatted.verified
│       └── Errors/
│           ├── MissingParen.input.crisp
│           └── MissingParen.diagnostics.verified
│
├── Crisp.Semantics.Tests/
│   └── Snapshots/
│       ├── TypeCheck/
│       │   ├── ValidTree.input.crisp
│       │   ├── ValidTree.context.cs           # C#コンテキスト型
│       │   └── ValidTree.types.verified       # 期待される型情報
│       └── Errors/
│           ├── MemberNotFound.input.crisp
│           ├── MemberNotFound.context.cs
│           └── MemberNotFound.diagnostics.verified
│
└── Crisp.IR.Tests/
    └── Snapshots/
        ├── Lowering/
        │   ├── CombatTree.input.crisp
        │   └── CombatTree.ir.verified         # 期待IR (S式)
        └── CSharp/
            ├── CombatTree.ir.crisp            # 入力IR (S式)
            └── CombatTree.cs.verified         # 期待C#コード
```

### 10.3 パーサーのラウンドトリップテスト

```
性質: 任意のソーステキストに対して
  source == CST.ToFullString()

つまり、CSTからソーステキストを復元すると元と完全一致する。
これはロスレスCSTの核心的な不変条件。

実装:
[Property]
public Property RoundTrip(string source)
{
    var cst = Parser.Parse(source);
    return (cst.ToFullString() == source).ToProperty();
}

※ ランダム生成はS式の文法に従うカスタムジェネレータを使用
```

### 10.4 型チェックのテスト方針

```csharp
// テストヘルパー: C#コンテキストとDSLを組み合わせて型チェック
[Fact]
public void TypeCheck_MemberNotFound_ReportsBS0001()
{
    var context = CompileContext(@"
        public partial class TestContext : IBtContext
        {
            public float Health { get; set; }
        }");

    var diagnostics = CrispCompiler.TypeCheck(
        "(tree Test (check (< .Mana 30)))",  // .Mana は存在しない
        context);

    Assert.Single(diagnostics);
    Assert.Equal("BS0001", diagnostics[0].Id);
    Assert.Contains("Mana", diagnostics[0].Message);
    Assert.Contains("TestContext", diagnostics[0].Message);
}
```

### 10.5 Source Generator のテスト

```csharp
// Microsoft.CodeAnalysis.Testing を使用
[Fact]
public async Task SourceGenerator_GeneratesCorrectCode()
{
    var test = new CSharpSourceGeneratorTest<CrispSourceGenerator>
    {
        TestState =
        {
            Sources =
            {
                @"
                using Crisp.Runtime;

                public partial class TestAI : IBtContext
                {
                    private float _health;

                    [BtAction]
                    public BtStatus Patrol() => BtStatus.Success;

                    [BehaviorTree(""(tree Test (.Patrol))"")]
                    public partial BtNode BuildTree();
                }
                "
            },
            GeneratedSources =
            {
                // 期待される生成コード
                (typeof(CrispSourceGenerator),
                 "TestAI.BuildTree.g.cs",
                 ExpectedGeneratedSource)
            }
        }
    };
    await test.RunAsync();
}
```

---

## 11. 未決定事項・将来拡張

### 11.1 未決定事項

| # | 項目 | 選択肢 | 備考 |
|---|------|--------|------|
| U1 | DeltaTimeの注入方法 | A: `Tick(TickContext)` / B: コンストラクタ注入 / C: static ThreadLocal | Aが最も明示的だが全ノードのシグネチャが変わる |
| U2 | Running状態の記憶方式 | A: ノード内インデックス (現在の設計) / B: 外部Cursor / C: Zipper | Bはステートレスノードを可能にするが複雑 |
| U3 | `IBtContext`の要否 | A: マーカーインターフェース必須 / B: 属性のみで十分 / C: 不要 | Cはユーザーの制約を最小化するが検証が甘くなる |
| U4 | Parallel内Running子の扱い | A: 全子を毎tick評価 / B: Running子のみ再評価 | ゲームAIではAが一般的 |
| U5 | サブツリー参照 | A: `(ref TreeName)` / B: `(include "file.crisp")` / C: 両方 | 異なるContext型の参照をどう扱うか |
| U6 | ケバブ→C#名変換の厳密さ | A: 曖昧ならエラー / B: 警告+最初の候補を採用 | Aのほうが安全 |

### 11.2 将来の拡張候補

| # | 項目 | 概要 |
|---|------|------|
| F1 | Reactive reevaluation | 上位ノードの条件が変わったら子のRunningを中断して再評価 |
| F2 | Decorator DSL | ユーザー定義デコレータを DSL 側で定義できるようにする |
| F3 | Blackboard 型 | Context以外のデータストアを型安全にアクセス |
| F4 | Visual Editor | LSP + WebView でビジュアルなツリーエディタ |
| F5 | IR最適化パス | 定数畳み込み、到達不能ノード除去等 |
| F6 | AOTサポート | NativeAOT環境でのインタープリタ (リフレクション排除) |
| F7 | デバッガ | ツリーの実行をステップ実行できるデバッガ |
| F8 | async/await統合 | UniTask/ValueTask ベースの非同期アクション |
| F9 | Hot Reload | 実行中に .crisp を変更して即反映 |
| F10 | マクロシステム | DSL内でマクロ定義、展開 (Lisp的) |
| F11 | 複数ツリー定義 | 1ファイルに複数のtreeを定義し、相互参照 |
| F12 | ジェネリクス | Context型のジェネリック引数対応 |
