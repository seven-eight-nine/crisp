# 言語リファレンス

Crisp は S 式（Lisp 風の括弧表記）をベースとしたビヘイビアツリー記述言語です。このドキュメントでは、言語の全構文と意味論を定義します。

## 目次

- [構文概要](#構文概要)
- [レキシカル要素](#レキシカル要素)
- [文法](#文法)
- [プログラム構造](#プログラム構造)
- [ツリー定義](#ツリー定義)
- [コンポジットノード](#コンポジットノード)
- [デコレータノード](#デコレータノード)
- [リアクティブノード](#リアクティブノード)
- [リーフノード](#リーフノード)
- [マルチツリー](#マルチツリー)
- [ユーザー定義デコレータ (defdec)](#ユーザー定義デコレータ-defdec)
- [マクロ (defmacro)](#マクロ-defmacro)
- [式](#式)
- [Blackboard アクセス](#blackboard-アクセス)
- [名前解決](#名前解決)
- [型システム](#型システム)
- [Nullable 解析](#nullable-解析)

## 構文概要

Crisp のソースコードはツリー定義、デコレータ定義、マクロ定義、インポート文で構成されます。各要素は S 式（括弧で囲まれたリスト）で記述します。

```lisp
;; これはコメント
(tree MyTree
  (select
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol)))
```

主な特徴:
- 括弧 `(` `)` でリストを構成する前置記法
- 演算子も前置記法: `(< .Health 30)` は `Health < 30` に相当
- `.` プレフィックスでコンテキスト型のメンバーにアクセス
- `$` プレフィックスで Blackboard のメンバーにアクセス
- `::` プレフィックスで列挙型リテラルを参照
- `;` から行末までがコメント

## レキシカル要素

### リテラル

| 種別 | 例 | 説明 |
|---|---|---|
| 整数 | `42`, `-3`, `0` | 符号付き整数（C# の `int`） |
| 浮動小数点 | `3.14`, `-0.5`, `1.0` | 小数点を含む数値（C# の `float`） |
| 文字列 | `"hello"`, `"say \"hi\""` | ダブルクォートで囲む。エスケープ: `\"`, `\\`, `\n`, `\t` |
| 真偽値 | `true`, `false` | C# の `bool` に対応 |
| null | `null` | C# の `null` に対応 |
| 列挙型 | `::AIState.Combat` | `::型名.メンバー名` の形式で C# の enum 値を参照 |

### 整数リテラルと負数

`-3` のようにマイナス記号が数字の直前に付く場合は負の整数リテラルとして解釈されます。`(- a b)` のようにスペースを挟む場合は減算演算子として解釈されます。

```lisp
(check (> .Health -1))    ; -1 は負の整数リテラル
(check (> (- .Max .Cur) 0)) ; - は減算演算子
```

### 識別子

| 種別 | 例 | 説明 |
|---|---|---|
| 識別子 | `tree`, `Combat`, `my-tree` | ノード種別やツリー名。英数字、アンダースコア、ハイフンを使用可 |
| メンバーアクセス | `.Health`, `.Target.Position` | ドットプレフィックスでメンバー参照。チェーン可能 |
| Blackboard アクセス | `$.Alarm`, `$.State.Count` | `$` プレフィックスで Blackboard メンバー参照 |
| キーワード | `:any`, `:all`, `:n`, `:blackboard` | コロンプレフィックス。ノードのオプション指定 |

### 演算子

| 分類 | 演算子 | 説明 |
|---|---|---|
| 算術 | `+`, `-`, `*`, `/`, `%` | 加減乗除と剰余 |
| 比較 | `<`, `>`, `<=`, `>=` | 大小比較 |
| 等値 | `=`, `!=` | 等価・非等価（C# の `==`, `!=` に対応） |

全ての演算子は前置記法で使用します:

```lisp
(+ .Attack .Bonus)    ; Attack + Bonus
(< .Health 30)        ; Health < 30
(= .State ::AIState.Idle) ; State == AIState.Idle
```

### 論理演算子

| 演算子 | 説明 |
|---|---|
| `and` | 論理 AND（2 つ以上のオペランド、短絡評価） |
| `or` | 論理 OR（2 つ以上のオペランド、短絡評価） |
| `not` | 論理 NOT（単項） |

```lisp
(and .IsAlive .HasTarget (> .Health 0))
(or .IsFleeing .IsStunned)
(not .IsInvulnerable)
```

### コメント

`;`（セミコロン）から行末までがコメントです。

```lisp
;; これは行コメント
(tree T
  ; ここもコメント
  (.Patrol))  ; 行末コメントも可能
```

### 特殊トークン

| トークン | 説明 |
|---|---|
| `<body>` | `defdec` のボディプレースホルダー。テンプレート内で子ノードの挿入位置を示す |

## 文法

以下に Crisp の文法を EBNF 風に定義します。

```ebnf
Program       = (TreeDef | Defdec | Defmacro | Import)* EOF

(* === トップレベル定義 === *)
TreeDef       = "(" "tree" Identifier [":blackboard" Identifier] Node+ ")"
Defdec        = "(" "defdec" Identifier "(" Identifier* ")" Node ")"
Defmacro      = "(" "defmacro" Identifier "(" Identifier* ")" Node ")"
Import        = "(" "import" StringLiteral ")"

(* === ノード === *)
Node          = Composite | Decorator | Reactive | Leaf | Ref | DefdecCall

(* === コンポジットノード === *)
Composite     = Selector | Sequence | Parallel
Selector      = "(" "select" Node+ ")"
Sequence      = "(" "seq" Node+ ")"
Parallel      = "(" "parallel" ParallelPolicy Node+ ")"
ParallelPolicy = ":any" | ":all" | ":n" IntLiteral

(* === デコレータノード === *)
Decorator     = Guard | If | Invert | Repeat | Timeout | Cooldown | While
Guard         = "(" "guard" Expr Node ")"
If            = "(" "if" Expr Node Node? ")"
Check         = "(" "check" Expr ")"
Invert        = "(" "invert" Node ")"
Repeat        = "(" "repeat" IntLiteral Node ")"
Timeout       = "(" "timeout" NumberLiteral Node ")"
Cooldown      = "(" "cooldown" NumberLiteral Node ")"
While         = "(" "while" Expr Node ")"

(* === リアクティブノード === *)
Reactive        = "(" "reactive" Expr Node ")"
ReactiveSelect  = "(" "reactive-select" Node+ ")"

(* === マルチツリー === *)
Ref           = "(" "ref" Identifier ")"

(* === リーフノード === *)
Leaf          = Check | ActionCall
ActionCall    = MemberAccess                      (* 引数なし: .Patrol *)
              | "(" MemberAccess Expr* ")"        (* 引数あり: (.Attack .Target) *)

(* === ユーザー定義デコレータ呼び出し === *)
DefdecCall    = "(" Identifier Expr* Node ")"

(* === 式 === *)
Expr          = BinaryExpr | UnaryExpr | LogicExpr
              | CallExpr | MemberAccess | BlackboardAccess | Literal

BinaryExpr    = "(" BinaryOp Expr Expr ")"
BinaryOp      = "+" | "-" | "*" | "/" | "%" | "<" | ">" | "<=" | ">=" | "=" | "!="

UnaryExpr     = "(" "-" Expr ")"                  (* 単項マイナス *)

LogicExpr     = "(" "and" Expr Expr+ ")"
              | "(" "or" Expr Expr+ ")"
              | "(" "not" Expr ")"

CallExpr      = "(" MemberAccess Expr* ")"        (* 式位置でのメソッド呼び出し *)

MemberAccess      = "." Identifier ( "." Identifier )*
BlackboardAccess  = "$" MemberAccess

Literal       = IntLiteral | FloatLiteral | StringLiteral
              | "true" | "false" | "null" | EnumLiteral

NumberLiteral = IntLiteral | FloatLiteral
EnumLiteral   = "::" Identifier "." Identifier
```

## プログラム構造

1 つの `.crisp` ファイルには、以下のトップレベル要素を任意の順序で記述できます:

```lisp
;; インポート文
(import "shared-behaviors.crisp")

;; ユーザー定義デコレータ
(defdec safe-action (duration)
  (guard .IsAlive (timeout duration <body>)))

;; マクロ定義
(defmacro with-retry (n)
  (repeat n <body>))

;; ツリー定義（複数可）
(tree MainBehavior
  (select
    (ref CombatBehavior)
    (.Patrol)))

(tree CombatBehavior
  (safe-action 5.0
    (.Attack)))
```

## ツリー定義

ツリーは `(tree 名前 ノード...)` の形式で定義します。名前は C# コード生成時のメソッド名に使用されます。

```lisp
(tree CombatAI
  (select
    (.Attack)
    (.Patrol)))
```

### Blackboard 型の宣言

ツリー定義で `:blackboard` キーワードを使い、Blackboard 型を宣言できます:

```lisp
(tree TeamAI
  :blackboard WorldState
  (select
    (check $.IsAlarmTriggered)
    (.Patrol)))
```

Blackboard を宣言すると、`$` プレフィックスでそのメンバーにアクセスできます。詳細は [Blackboard アクセス](#blackboard-アクセス) を参照してください。

## コンポジットノード

コンポジットノードは複数の子ノードを持ち、特定の規則に従って子を評価します。

### select（セレクター）

子ノードを上から順に評価し、**最初に Success または Running を返した子の結果**を返します。全ての子が Failure を返した場合、セレクター自身も Failure を返します。

「OR」的なロジック — いずれか 1 つが成功すればよい場合に使います。

```lisp
(select
  (.AttackEnemy)    ; まず攻撃を試みる
  (.CollectItem)    ; 攻撃できなければアイテム収集
  (.Patrol))        ; 何もなければパトロール
```

| 子1の結果 | 子2の結果 | select の結果 |
|---|---|---|
| Success | (評価しない) | Success |
| Failure | Success | Success |
| Running | (評価しない) | Running |
| Failure | Failure | Failure |

**Running 状態の記憶:** Running を返した子のインデックスを記憶し、次の Tick ではその子から評価を再開します。

### seq（シーケンス）

子ノードを上から順に評価し、**全ての子が Success を返した場合に Success** を返します。いずれかの子が Failure を返した時点で即座に Failure を返します。

「AND」的なロジック — 全てが成功する必要がある場合に使います。

```lisp
(seq
  (check .IsEnemyVisible)  ; 敵が見えるか確認
  (.AimAtTarget)            ; 照準を合わせる
  (.Fire))                  ; 発射
```

| 子1の結果 | 子2の結果 | seq の結果 |
|---|---|---|
| Success | Success | Success |
| Success | Failure | Failure |
| Failure | (評価しない) | Failure |
| Success | Running | Running |

**Running 状態の記憶:** Running を返した子のインデックスを記憶し、次の Tick ではその子から評価を再開します。

### parallel（パラレル）

全ての子ノードを毎 Tick 評価し、ポリシーに基づいて結果を決定します。

```lisp
;; いずれか1つが成功したら全体を Success
(parallel :any
  (.SearchForEnemy)
  (.ListenForSound)
  (.CheckRadar))

;; 全てが成功したら全体を Success
(parallel :all
  (.AimAtTarget)
  (.ChargeWeapon))

;; N 個が成功したら全体を Success
(parallel :n 2
  (.NotifyAlly1)
  (.NotifyAlly2)
  (.NotifyAlly3))
```

**ポリシー:**

| ポリシー | 構文 | Success 条件 | Failure 条件 |
|---|---|---|---|
| Any | `:any` | いずれか 1 つの子が Success | 全ての子が Failure |
| All | `:all` | 全ての子が Success | いずれか 1 つの子が Failure |
| N | `:n 数値` | N 個以上の子が Success | 成功不可能になった時点 |

**リセット動作:** Parallel が最終結果（Success または Failure）を確定した時点で、Running 状態の子を含む全ての子ノードの `Reset()` を呼び出します。これにより、長時間実行中のアクション（移動、アニメーション等）が確実にキャンセルされます。

## デコレータノード

デコレータノードは 1 つの子ノードを持ち、その実行を修飾します。

### guard（ガード）

条件式が `true` の場合のみ子ノードを評価します。`false` の場合は Failure を返します。

```lisp
(guard .IsAlive
  (.Attack))
```

### if（条件分岐）

条件式に基づいて 2 つのノードを切り替えます。else 節は省略可能です。

```lisp
;; if-else
(if .IsEnemyVisible
  (.Attack)    ; true: 攻撃
  (.Patrol))   ; false: パトロール

;; if のみ（else 省略 → false 時は Failure）
(if .HasAmmo
  (.Shoot))
```

else 節を省略した場合、条件が `false` のとき Failure を返します。コンパイラは BS0302（if without else）警告を報告します。

### invert（反転）

子ノードの結果を反転します。Success と Failure が入れ替わり、Running はそのままです。

```lisp
(invert (check .IsStunned))
```

| 子の結果 | invert の結果 |
|---|---|
| Success | Failure |
| Failure | Success |
| Running | Running |

### repeat（繰り返し）

子ノードを指定回数繰り返します。子が Failure を返した場合は即座に Failure を返します。全回数 Success で完了した場合に Success を返します。

```lisp
(repeat 3 (.Attack))
```

回数は正の整数リテラルで指定します。変数や式は使用できません。

### timeout（タイムアウト）

子ノードの実行に時間制限を設けます。制限時間内に子が完了しない場合、Failure を返します。

```lisp
(timeout 5.0 (.MoveToTarget))
```

時間は秒単位の正の数値リテラル（整数または浮動小数点）で指定します。内部では `TickContext.DeltaTime` を積算して時間を計測します。

### cooldown（クールダウン）

子ノードが Success を返した後、指定時間はその子を再実行せず Failure を返します。

```lisp
(cooldown 2.5 (.UseAbility))
```

### while（ループ）

条件が `true` の間、子ノードを繰り返し評価します。条件が `false` になった時点で Failure を返します。

```lisp
(while (> .Stamina 0) (.Sprint))
```

## リアクティブノード

リアクティブノードは毎 Tick 条件を再評価し、条件変化時に Running 中のサブツリーを自動的に中断（Abort）します。

### reactive

毎 Tick 条件を再評価し、`false` になった場合は Running 中の子ノードを `Abort()` して Failure を返します。

```lisp
;; 敵が見えなくなったら攻撃シーケンスを即中断
(reactive .IsEnemyVisible
  (seq
    (.Approach .Target)
    (.Attack .Target)))
```

**セマンティクス:**

1. 条件を評価
2. 条件が `false` → Running 中の子に `Abort()` を呼び、Failure を返す
3. 条件が `true` → 子を Tick し、結果をそのまま返す
4. 子が完了（Success/Failure）→ 子を `Reset()` する

通常の `guard` との違い: `guard` は Running 中の子を記憶して次 Tick で続行しますが、`reactive` は毎 Tick 条件を再チェックし、`false` になれば即座に中断します。

### reactive-select

毎 Tick 先頭から全ての子を再評価します。上位の子が Success/Running を返した場合、下位で Running 中の子を全て `Abort()` します。

```lisp
(reactive-select
  ;; 最優先: HP が低ければ逃走
  (seq
    (check (< .Health 20))
    (.Flee))
  ;; 次: 敵が見えたら攻撃
  (seq
    (guard .IsEnemyVisible
      (.Attack .Target)))
  ;; 最低: パトロール
  (.Patrol))
```

通常の `select` との違い:
- `select`: Running の子を記憶し、次 Tick ではその子から再開
- `reactive-select`: **毎 Tick 先頭から**再評価。上位の子が復活したら下位を中断

## リーフノード

### check（条件チェック）

式を評価し、`true` なら Success、`false` なら Failure を返します。

```lisp
(check .IsAlive)                          ; bool プロパティ
(check (< .Health 30))                     ; 比較式
(check (and .IsEnemyVisible (not .IsFleeing))) ; 論理式
(check (= .CurrentState ::AIState.Combat))     ; 列挙型比較
(check (!= .Target null))                  ; null チェック
```

`check` の式は `bool` 型に評価される必要があります。`bool` 以外の型の場合は BS0007 エラーが報告されます。

### アクション呼び出し

コンテキスト型のメソッドを呼び出します。メソッドは `BtStatus` または `BtNode` を返す必要があります。

```lisp
.Patrol                        ; 引数なし（括弧なし）
(.Patrol)                      ; 引数なし（括弧あり、同じ意味）
(.Attack .Target)              ; 引数あり
(.MoveTo .Target.Position)     ; チェーンアクセスの引数
(.SetState ::AIState.Combat)   ; 列挙型引数
(.Say "hello")                 ; 文字列引数
```

**`BtStatus` を返すメソッド（アクション）:**

毎 tick 呼び出されるラムダとしてラップされます。実行時にメソッドを評価し、その結果（Success / Failure / Running）がそのままノードの結果となります。

```csharp
// 生成コード: new ActionNode(() => this.Patrol(), "Patrol()")
public BtStatus Patrol() => BtStatus.Success;
```

**`BtNode` を返すメソッド（サブツリー）:**

ツリー構築時に 1 回だけ呼び出され、返された `BtNode` がツリーに直接埋め込まれます。ロジックの再利用やコードによるサブツリー構築に使用します。

```csharp
// 生成コード: this.BuildCombat()（ActionNode でラップされない）
public BtNode BuildCombat()
{
    return new SequenceNode(
        new ConditionNode(() => this.IsEnemyVisible),
        new ActionNode(() => this.Attack()));
}
```

```lisp
(tree MainAI
  (select
    (.BuildCombat)   ; BtNode を返す → サブツリーとして埋め込み
    (.Patrol)))      ; BtStatus を返す → アクションとして実行
```

`BtStatus` でも `BtNode` でもないメソッドをアクション位置で呼び出すと、BS0008 エラーが報告されます。

## マルチツリー

### ref（ツリー参照）

同一ファイルまたはインポートしたファイル内の別ツリーを参照します。生成コードではメソッド呼び出しとして展開されます。

```lisp
(tree MainBehavior
  (select
    (ref CombatBehavior)
    (ref PatrolBehavior)
    (.Idle)))

(tree CombatBehavior
  (seq
    (check .IsEnemyVisible)
    (.Attack .Target)))

(tree PatrolBehavior
  (.Patrol))
```

生成される C# コード:

```csharp
partial class MyAI
{
    public partial BtNode BuildMainBehavior()
    {
        return new SelectorNode(
            BuildCombatBehavior(),
            BuildPatrolBehavior(),
            new ActionNode(() => this.Idle(), "Idle()"));
    }
}
```

**循環参照の検出:** `(tree A (ref B))` と `(tree B (ref A))` のような循環参照は BS0037 エラーとして検出されます。

### import（ファイルインポート）

別ファイルのツリー定義をスコープに取り込みます。

```lisp
;; SharedBehaviors.crisp のツリーを使用可能にする
(import "SharedBehaviors.crisp")

(tree EnemyMain
  (select
    (ref FleeSequence)   ; SharedBehaviors.crisp 内のツリー
    (.Patrol)))
```

## ユーザー定義デコレータ (defdec)

`defdec` で再利用可能なデコレータテンプレートを定義できます。C# 側にノードクラスを追加することなく、DSL の組み合わせで新しいパターンを作成できます。

### 定義

```lisp
(defdec safe-timed-action (duration cd)
  (guard .IsAlive
    (timeout duration
      (cooldown cd
        <body>))))
```

- `(defdec 名前 (パラメータ...) ボディ)` の形式
- `<body>` はプレースホルダー。呼び出し時に実際の子ノードに置換される
- パラメータは式のみ（リテラルまたはメンバーアクセス）。ノードは `<body>` でのみ渡せる

### 呼び出し

```lisp
(safe-timed-action 10.0 2.5
  (.Attack .Target))
```

これは以下と等価に展開されます:

```lisp
(guard .IsAlive
  (timeout 10.0
    (cooldown 2.5
      (.Attack .Target))))
```

### 制約

- `defdec` は再帰呼び出し不可（無限展開防止）
- `defdec` 内での `defdec` 定義不可（ネスト禁止）
- `<body>` はボディ内に正確に 1 回出現する必要がある
- パラメータの型は使用箇所から推論される

## マクロ (defmacro)

`defmacro` は `defdec` の上位互換として、CST レベルでの構文変換を提供します。

### 定義

```lisp
(defmacro retry-action (n)
  (repeat n <body>))
```

### 呼び出し

```lisp
(retry-action 3 (.Attack))
```

展開結果:

```lisp
(repeat 3 (.Attack))
```

### 安全性

- 展開の最大深度を制限（デフォルト: 100）
- 相互再帰を検出してエラー（BS0034）
- 展開結果が有効な構文であることを検証

## 式

式はノード内で値を計算するために使用します。主に `check` の条件、`guard` / `if` / `while` / `reactive` の条件、アクションの引数で使います。

### 算術式

```lisp
(+ .Attack .Bonus)          ; 加算
(- .MaxHealth .Health)       ; 減算
(* .BaseDamage 1.5)          ; 乗算
(/ .TotalExp .Level)         ; 除算
(% .Count 3)                 ; 剰余
```

算術演算子は `int` と `float` に対して使用できます。`int` と `float` の混合演算では、`int` が自動的に `float` に変換されます。

### 比較式

```lisp
(< .Health 30)              ; Health < 30
(> .Score 100)              ; Score > 100
(<= .Distance .AttackRange) ; Distance <= AttackRange
(>= .Level 5)               ; Level >= 5
(= .Name "Guardian")        ; Name == "Guardian"
(!= .Target null)           ; Target != null
```

比較式の結果は常に `bool` です。同じ型同士、または暗黙変換可能な型同士でのみ比較できます。

### 論理式

```lisp
(and .IsAlive .HasTarget)          ; AND（2つ以上の式を取れる）
(or .IsFleeing .IsStunned)         ; OR（2つ以上の式を取れる）
(not .IsInvulnerable)              ; NOT

;; 複合例
(and .IsEnemyVisible
     (not .IsFleeing)
     (> .Health 0))
```

`and` と `or` は 2 つ以上のオペランドを取ることができます。短絡評価が行われ、`and` は最初の `false` で、`or` は最初の `true` で評価を打ち切ります。

### メンバーアクセス

`.` プレフィックスでコンテキスト型のプロパティやフィールドにアクセスします。

```lisp
.Health                ; コンテキスト型の Health プロパティ
.Target.Position       ; Target プロパティの Position プロパティ
.Target.Position.x     ; 3段チェーン
```

### メソッド呼び出し（式位置）

式の中でメソッドを呼び出すこともできます。戻り値が式の値として使用されます。

```lisp
(check (< (.DistanceTo .Target) 10.0))
```

式位置のメソッド呼び出しは `BtStatus` を返す必要はありません。任意の型を返せます。

### 列挙型リテラル

C# の列挙型値を `::型名.メンバー名` の形式で参照します。

```lisp
(check (= .CurrentState ::AIState.Combat))
(.SetState ::AIState.Idle)
```

存在しない型は BS0102、存在しないメンバーは BS0103 エラーになります。

## Blackboard アクセス

Context（`this`）以外のデータストアに型安全にアクセスする仕組みです。複数の AI エージェント間でデータを共有する場合や、Context に含めたくないグローバルな状態にアクセスする場合に使用します。

### DSL 構文

`$` プレフィックスで Blackboard のメンバーにアクセスします:

```lisp
(tree TeamAI
  :blackboard WorldState
  (select
    ;; $ で blackboard にアクセス
    (seq
      (check $.IsAlarmTriggered)
      (.AlertAllies))
    ;; . は従来通り context (this)
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol)))
```

### スコープルール

| プレフィックス | 解決先 | 例 |
|---|---|---|
| `.` | context（this）のメンバー | `.Health` → `this.Health` |
| `$` | blackboard のメンバー | `$.IsAlarmTriggered` → `blackboard.IsAlarmTriggered` |

### C# 側の定義

Blackboard 型は `IBtBlackboard` マーカーインターフェースを実装する必要があります:

```csharp
using Crisp.Runtime;

public class WorldState : IBtBlackboard
{
    public bool IsAlarmTriggered { get; set; }
    public float GlobalThreatLevel { get; set; }
}
```

生成コードでは、`Build` メソッドに blackboard 引数が追加されます:

```csharp
public partial BtNode BuildTeamAI(WorldState blackboard)
{
    return new SelectorNode(
        new SequenceNode(
            new ConditionNode(() => blackboard.IsAlarmTriggered, "$.IsAlarmTriggered"),
            new ActionNode(() => this.AlertAllies(), "AlertAllies()")),
        ...);
}
```

## 名前解決

### 変換規則と優先順位

DSL の名前は以下の優先順位で C# のメンバー名と照合されます（大文字小文字は区別しません）:

| 優先順位 | DSL 名 → C# 候補名 | 例 |
|---|---|---|
| 1 | そのまま | `Health` → `Health` |
| 2 | ケバブ → PascalCase | `is-alive` → `IsAlive` |
| 3 | ケバブ → camelCase | `is-alive` → `isAlive` |
| 4 | ケバブ → _camelCase | `is-alive` → `_isAlive` |
| 5 | ケバブ → snake_case | `is-alive` → `is_alive` |

**ケバブケース** — DSL ではハイフン区切りの名前（`is-enemy-visible`）を使用でき、C# の PascalCase メンバー（`IsEnemyVisible`）に自動的にマッチします。

```lisp
(check .IsEnemyVisible)    ; そのまま
(check .is-enemy-visible)  ; ケバブケース → PascalCase に変換
```

### あいまいな名前の警告

複数の C# メンバーが同じ DSL 名にマッチする場合、BS0104 警告が報告され、優先順位の高い候補が使用されます。

### アクセス修飾子

コンテキスト型は `partial class` として生成されるため、`private` や `protected` を含む全てのアクセス修飾子のメンバーにアクセスできます。

## 型システム

### 基本型

| Crisp の型 | C# の対応型 | リテラル表記 |
|---|---|---|
| int | `int` (`System.Int32`) | `42`, `-3` |
| float | `float` (`System.Single`) | `3.14`, `-0.5` |
| bool | `bool` (`System.Boolean`) | `true`, `false` |
| string | `string` (`System.String`) | `"hello"` |
| null | `null` | `null` |
| enum | 各列挙型 | `::AIState.Combat` |

### 暗黙的型変換

`int` から `float` への暗黙的変換がサポートされています。

```lisp
;; .Health (int) と 30.0 (float) の比較 → .Health が float に変換される
(check (< .Health 30.0))
```

### ジェネリクス対応

コンテキスト型がジェネリック型引数を持つ場合、`:context` で具体型を指定できます:

```csharp
public partial class AI<T> where T : IComparable<T>
{
    public T CurrentState { get; set; }
}
```

```lisp
(tree FSM
  :context AI<EnemyState>
  (check (= .CurrentState ::EnemyState.Combat)))
```

型制約（`where`）に違反する型引数を指定すると BS0041 エラーが報告されます。

### ErrorType とカスケード抑制

名前解決に失敗した式は特殊な「ErrorType」として扱われます。ErrorType を含む式に対する追加の型エラーは抑制されます。これにより、1 つのエラーから大量の連鎖エラーが報告されることを防ぎます。

```lisp
;; .UnknownProp が見つからない → BS0001 のみ報告
;; (< ErrorType 30) の型不一致エラーは抑制される
(check (< .UnknownProp 30))
```

## Nullable 解析

C# の `#nullable enable` を尊重し、null 安全性をコンパイル時に検証します。

### フロー解析

```lisp
(seq
  ;; .Target が null でないことを確認
  (guard (!= .Target null)
    (seq
      ;; OK: null チェック済みの .Target を安全にアクセス
      (check (> .Target.Health 0))
      (.Attack .Target))))

;; Warning: .Target may be null（null チェックなしのアクセス）
(.Attack .Target)
```

### ナローイングルール

| パターン | ナローイング効果 |
|---|---|
| `guard (!= .X null) body` | `body` 内で `.X` は NotNull |
| `if (!= .X null) then else` | `then` 内で NotNull、`else` 内で MaybeNull |
| `check (!= .X null)` | 後続の `seq` 内で NotNull |

### 診断

| コード | 重大度 | メッセージ |
|---|---|---|
| BS0044 | Warning | 可能性のある null メンバーの逆参照 |
| BS0045 | Warning | null との比較が常に true |
| BS0046 | Warning | null との比較が常に false |
| BS0047 | Info | 非 null メンバーへの不要な null チェック |
