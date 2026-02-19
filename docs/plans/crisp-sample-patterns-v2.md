# Crisp スナップショットテスト用サンプルDSLまとめ v2

本ドキュメントは Crisp DSL の仕様・実装を固定するための **公式サンプルパターン集** である。

これらのパターンは Phase 1〜3（Syntax / Semantics / IR / Backend）を縦断する
**スナップショットテストの正とする入力集合**として使用する。

---

## 目的

- 言語仕様の曖昧さをテストで固定する
- 実装が満たすべき完成条件を明確化する
- 将来のリファクタや最適化で壊してはいけない挙動を保証する

---

## Pattern 1: Minimal — 最小構造

### カバー範囲

- tree 定義
- select / seq
- check
- 引数なしアクション（括弧あり / なし）
- プロパティアクセス
- 比較演算
- int → float 暗黙変換（IrConvert）
- コメント

### DSL

```crisp
;; 最小限の戦闘AI
(tree SimpleCombat
  (select
    (seq
      (check (< .Health 30))
      (.Flee))
    (.Patrol)))
```

### C# Context

```csharp
public partial class SimpleAI : IBtContext
{
    public float Health { get; set; }

    [BtAction]
    public BtStatus Flee() => BtStatus.Running;

    [BtAction]
    public BtStatus Patrol() => BtStatus.Success;
}
```

### 固定したい仕様

- `.Patrol` （括弧なし MemberAccess）は `AstActionCall(args=[])` に lowering される
- `(.Flee)` （括弧あり引数なし CstCall）も `AstActionCall(args=[])` に lowering される
- `(< .Health 30)` は bool 型
- `30` (int) は `.Health` (float) との比較のため、IR に `IrConvert(int→float)` が挿入される
- コメントは CST の LeadingTrivia として保持される

### スナップショット

| ファイル | 内容 |
|---|---|
| source.crisp | 上記DSL |
| cst.txt | CstTree > CstSelect > CstSequence > CstCheck > CstBinaryExpr, CstCall, CstMemberAccess |
| ast.txt | AstTree > AstSelector > AstSequence > AstCheck > AstBinaryExpr, AstActionCall |
| diagnostics.txt | （空 — エラーなし） |
| ir.txt | IrSelector > IrSequence > IrCondition(IrBinaryOp(Lt, IrMemberLoad, IrConvert(IrLiteral))) |
| generated.cs | `new SelectorNode(new SequenceNode(new ConditionNode(() => ...), ...), ...)` |

---

## Pattern 2: Full Decorators — 全デコレータ

### カバー範囲

- guard / if+else
- invert / repeat
- timeout / cooldown
- while
- 論理式（and / or / not）
- 引数ありアクション
- bool プロパティの直接参照（checkなしでguard条件に使用）

### DSL

```crisp
;; デコレータを網羅するパトロールAI
(tree DecoratorShowcase
  (select
    ;; guard: 条件付き実行
    (guard .IsAlive
      (seq
        ;; if/else
        (if (and .IsEnemyVisible (not .IsFleeing))
          (timeout 5.0
            (.Attack .Target))
          (.Patrol))

        ;; repeat
        (repeat 3
          (.TauntEnemy))

        ;; cooldown
        (cooldown 2.5
          (.UseAbility))

        ;; while
        (while (> .Stamina 0)
          (.Sprint))

        ;; invert
        (invert
          (check .IsStunned))))))
```

### C# Context

```csharp
public partial class DecoratorAI : IBtContext
{
    public bool IsAlive { get; set; }
    public bool IsEnemyVisible { get; set; }
    public bool IsFleeing { get; set; }
    public bool IsStunned { get; set; }
    public float Stamina { get; set; }
    public Entity Target { get; set; }

    [BtAction] public BtStatus Attack(Entity target) => BtStatus.Running;
    [BtAction] public BtStatus Patrol() => BtStatus.Success;
    [BtAction] public BtStatus TauntEnemy() => BtStatus.Success;
    [BtAction] public BtStatus UseAbility() => BtStatus.Success;
    [BtAction] public BtStatus Sprint() => BtStatus.Running;
}
```

### 固定したい仕様

- `guard .IsAlive` — bool プロパティを直接 guard 条件として使用可能（check不要）
- `(and .IsEnemyVisible (not .IsFleeing))` — 全オペランドが bool であること
- `timeout 5.0` — 引数は正の数値リテラル（float）
- `cooldown 2.5` — 同上
- `repeat 3` — 引数は正の整数リテラル（int）
- `(> .Stamina 0)` — int→float 暗黙変換
- `(.Attack .Target)` — 引数ありアクション。.Target の型が Attack のパラメータ型と一致すること

### スナップショット

| ファイル | 内容 |
|---|---|
| source.crisp | 上記DSL |
| cst.txt | 全デコレータ種別の CstNode |
| ast.txt | AstGuard, AstIf, AstInvert, AstRepeat, AstTimeout, AstCooldown, AstWhile |
| diagnostics.txt | （空 — エラーなし） |
| ir.txt | 全デコレータの IrNode |
| generated.cs | GuardNode, IfNode, InvertNode, RepeatNode, TimeoutNode, CooldownNode, WhileNode |

---

## Pattern 3: Expressions — 式と型推論

### カバー範囲

- 算術式（+, -, *, /）
- 比較式（<, >, <=, >=, =, !=）
- メソッド呼び出し式（式コンテキスト内の CstCall → AstCallExpr）
- メンバーチェーン（.Target.Health, .Target.Position）
- enum リテラル（::AIState.Combat）
- null リテラル
- string リテラル
- 負リテラル（-1）
- 二項マイナス（(- .MaxHealth .Health)）

### DSL

```crisp
;; 式と型推論のテスト
(tree ExpressionTest
  (select
    ;; 算術 + 比較 + メソッド呼び出し
    (seq
      (check (< (.DistanceTo .Target) (* .AttackRange 1.5)))
      (check (!= .Target null))
      (check (= .CurrentState ::AIState.Combat))
      (.Attack .Target))

    ;; メンバーチェーン
    (seq
      (check (> .Target.Health 0))
      (check (<= (- .MaxHealth .Health) 50.0))
      (.MoveTo .Target.Position))

    ;; 文字列比較 + 負リテラル + boolリテラル
    (seq
      (check (= .Name "Guardian"))
      (check (> .Health -1))
      (check (= .IsAlive true))
      (.Idle))))
```

### C# Context

```csharp
public partial class ExprAI : IBtContext
{
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public float AttackRange { get; set; }
    public string Name { get; set; }
    public bool IsAlive { get; set; }
    public Entity Target { get; set; }
    public AIState CurrentState { get; set; }

    public float DistanceTo(Entity target) => 0f;

    [BtAction] public BtStatus Attack(Entity target) => BtStatus.Success;
    [BtAction] public BtStatus MoveTo(Vector3 position) => BtStatus.Running;
    [BtAction] public BtStatus Idle() => BtStatus.Success;
}

public class Entity
{
    public float Health { get; set; }
    public Vector3 Position { get; set; }
}

public enum AIState { Idle, Patrol, Combat, Flee }
```

### 固定したい仕様

- `(.DistanceTo .Target)` — 式コンテキストの CstCall → AstCallExpr。戻り値は float
- `(* .AttackRange 1.5)` — float * float → float
- `::AIState.Combat` — enum型として解決。`(= .CurrentState ::AIState.Combat)` は同一enum型比較
- `(!= .Target null)` — 参照型と null の比較は許可
- `.Target.Health` — チェーン解決: this.Target (Entity) → .Health (float)
- `.Target.Position` — チェーン解決: this.Target (Entity) → .Position (Vector3)
- `(- .MaxHealth .Health)` — 二項演算（Sub）。両辺 float → float
- `-1` — 負の int リテラル（単項マイナスではない）。`(> .Health -1)` では int→float 暗黙変換
- `(= .IsAlive true)` — bool リテラルの直接使用
- 全ての暗黙変換が IR で `IrConvert` として明示化される

### スナップショット

| ファイル | 内容 |
|---|---|
| source.crisp | 上記DSL |
| cst.txt | CstCall(式位置), CstEnumLiteral, CstMemberAccess(チェーン), 負CstIntLiteral |
| ast.txt | AstCallExpr, AstLiteralExpr(Enum/Null/String/Bool), AstBinaryExpr(Sub), AstMemberAccessExpr(chain) |
| diagnostics.txt | （空 — エラーなし） |
| ir.txt | IrCall, IrConvert 複数箇所, IrMemberLoad(chain), IrLiteral(Enum) |
| generated.cs | `this.DistanceTo(this.Target)`, `this.Target.Health`, `AIState.Combat` 等 |

---

## Pattern 4: Parallel — 並列実行と Reset セマンティクス

### カバー範囲

- parallel :any / :all / :n
- Running / Success / Failure の各組み合わせ
- U4 で決定した Reset セマンティクス
- 同一メソッドの異なる引数での複数呼び出し

### DSL

```crisp
;; 並列実行パターン
(tree ParallelTest
  (seq
    ;; :any — いずれか成功で全体Success
    (parallel :any
      (.SearchForEnemy)
      (.ListenForSound)
      (.CheckRadar))

    ;; :all — 全て成功で全体Success
    (parallel :all
      (.AimAtTarget .Target)
      (.ChargeWeapon))

    ;; :n — N個成功で全体Success
    (parallel :n 2
      (.NotifyAlly .Ally1)
      (.NotifyAlly .Ally2)
      (.NotifyAlly .Ally3))

    (.ExecuteAttack)))
```

### C# Context

```csharp
public partial class ParallelAI : IBtContext
{
    public Entity Target { get; set; }
    public Entity Ally1 { get; set; }
    public Entity Ally2 { get; set; }
    public Entity Ally3 { get; set; }

    [BtAction] public BtStatus SearchForEnemy() => BtStatus.Running;
    [BtAction] public BtStatus ListenForSound() => BtStatus.Running;
    [BtAction] public BtStatus CheckRadar() => BtStatus.Success;
    [BtAction] public BtStatus AimAtTarget(Entity target) => BtStatus.Running;
    [BtAction] public BtStatus ChargeWeapon() => BtStatus.Running;
    [BtAction] public BtStatus NotifyAlly(Entity ally) => BtStatus.Running;
    [BtAction] public BtStatus ExecuteAttack() => BtStatus.Success;
}
```

### 固定したい仕様

- `:any`, `:all`, `:n 2` の3ポリシーが正しくパースされること
- `:n` の引数は正の整数リテラル
- `(.NotifyAlly .Ally1)` と `(.NotifyAlly .Ally2)` — 同一メソッドを異なる引数で呼び出し可能
- **Reset セマンティクス（U4）**:
  - Parallel 自身が Running → 子の状態を維持して続行
  - Parallel 自身が Success / Failure → **全子を Reset（Running 含む）**
  - 親からの再突入時に前回の状態が漏れない

### スナップショット

| ファイル | 内容 |
|---|---|
| source.crisp | 上記DSL |
| cst.txt | CstParallel + CstKeywordArg(:any, :all, :n) + 数値引数 |
| ast.txt | AstParallel(Policy.Any), AstParallel(Policy.All), AstParallel(Policy.N(2)) |
| diagnostics.txt | （空 — エラーなし） |
| ir.txt | IrParallel(Any/All/N, children) |
| generated.cs | `new ParallelNode(ParallelPolicy.Any, ...)` 等 |

### ランタイムテスト（スナップショットとは別に実施）

| ケース | :any の期待結果 | :all の期待結果 |
|---|---|---|
| Running + Success | Success, 全子Reset | Running |
| Running + Failure | Running | Failure, 全子Reset |
| Running + Running | Running | Running |
| Success + Success | Success, 全子Reset | Success, 全子Reset |
| Failure + Failure | Failure, 全子Reset | Failure, 全子Reset |

---

## Pattern 5: Errors — エラーと回復

### カバー範囲

- パースエラー回復（CST は常に生成される）
- 名前解決エラー（BS0001）
- 型エラー（BS0003, BS0004, BS0005, BS0006, BS0007, BS0008）
- ErrorType によるカスケード抑止
- **IR は生成しない**（エラーがあるため）

### 5a: 構文エラー（パーサーのエラー回復）

```crisp
;; 閉じ括弧不足
(tree SyntaxError1
  (select
    (.Patrol)
    (seq
      (check (< .Health 30))

;; 予期しないトークン
(tree SyntaxError2
  (select
    42
    (.Patrol)))
```

#### 期待される Diagnostics

| Code | メッセージ |
|---|---|
| BS0018 | Unmatched '(' |
| BS0016 | Expected node, found IntLiteral |

#### 固定したい仕様

- **CSTは常に生成される**（エラーがあっても）
- CstMissing ノード（閉じ括弧不足）と CstError ノード（予期しないトークン）が CST に含まれる
- エラー後もパースが続行され、SyntaxError2 のツリーもパースされる
- **AST は lowering 可能な部分のみ生成**。CstError / CstMissing に対応する部分はスキップ

### 5b: 型エラー・名前解決エラー

```crisp
;; 存在しないメンバー、型不一致、引数エラー
(tree TypeError
  (select
    ;; BS0001: メンバー未解決
    (check .NonExistent)

    ;; BS0007: checkにboolでない式
    (check .Health)

    ;; BS0003: 比較不能な型
    (check (< .Health "hello"))

    ;; BS0005: 引数の数が不一致
    (.Attack)

    ;; BS0006: 引数の型が不一致
    (.Attack "not_an_entity")

    ;; BS0008: 戻り値がBtStatusでない
    (.DistanceTo .Target)

    ;; BS0004: 算術不能な型 + カスケード抑止テスト
    (check (> (+ .Name 1) 0))

    (.Patrol)))
```

#### C# Context

```csharp
public partial class ErrorAI : IBtContext
{
    public float Health { get; set; }
    public string Name { get; set; }
    public Entity Target { get; set; }

    [BtAction] public BtStatus Attack(Entity target) => BtStatus.Success;
    [BtAction] public BtStatus Patrol() => BtStatus.Success;

    // BtActionなし、戻り値がfloat — アクション位置で呼ぶとBS0008
    public float DistanceTo(Entity target) => 0f;
}
```

#### 期待される Diagnostics

| Code | メッセージ | 対象の式 |
|---|---|---|
| BS0001 | Member 'NonExistent' not found on type 'ErrorAI' | `.NonExistent` |
| BS0007 | Expression in 'check' must be bool, got 'float' | `(check .Health)` |
| BS0003 | Cannot compare 'float' with 'string' | `(< .Health "hello")` |
| BS0005 | Method 'Attack' expects 1 argument, got 0 | `(.Attack)` |
| BS0006 | Argument 0 of 'Attack': expected 'Entity', got 'string' | `(.Attack "not_an_entity")` |
| BS0008 | Action method 'DistanceTo' must return BtStatus | `(.DistanceTo .Target)` |
| BS0004 | Arithmetic operator '+' not applicable to 'string' | `(+ .Name 1)` |

#### カスケード抑止テスト

```
(+ .Name 1)
  → BS0004 報告、結果は ErrorType

(> (+ .Name 1) 0)
  → 左辺が ErrorType なので追加エラーなし、結果も ErrorType

(check (> (+ .Name 1) 0))
  → 子が ErrorType なので BS0007 を報告しない
```

**BS0004 の 1件のみが報告される。BS0003 や BS0007 は発生しない。**

### スナップショット

#### 5a (構文エラー)

| ファイル | 内容 |
|---|---|
| source.crisp | 上記 5a の DSL |
| cst.txt | CstMissing / CstError を含む CST（**生成される**） |
| ast.txt | lowering 可能な部分のみ（不完全な AST） |
| diagnostics.txt | BS0018, BS0016 |
| ir.txt | **生成しない** |
| generated.cs | **生成しない** |

#### 5b (型エラー)

| ファイル | 内容 |
|---|---|
| source.crisp | 上記 5b の DSL |
| context.cs | ErrorAI のコンテキスト定義 |
| cst.txt | エラーなし（構文は正しい） |
| ast.txt | 全ノードが AST に変換される |
| diagnostics.txt | BS0001, BS0007, BS0003, BS0005, BS0006, BS0008, BS0004（計7件） |
| ir.txt | **生成しない**（エラーがあるため） |
| generated.cs | **生成しない**（エラーがあるため） |

---

## パターン一覧

| # | 名前 | 主な役割 | IR生成 | C#生成 |
|---|------|---------|:------:|:------:|
| 1 | Minimal | 最小構造・基礎仕様の固定 | ✓ | ✓ |
| 2 | Full Decorators | 全ノード種別・制御構造 | ✓ | ✓ |
| 3 | Expressions | 型推論・式システムの完成条件 | ✓ | ✓ |
| 4 | Parallel | 並列実行・Reset セマンティクス | ✓ | ✓ |
| 5a | Syntax Errors | パースエラー回復・CST堅牢性 | ✗ | ✗ |
| 5b | Type Errors | 型診断・カスケード抑止 | ✗ | ✗ |

**IR/C#生成の可否自体がテストになっている**: Pattern 1〜4 は生成されること、Pattern 5a/5b は生成されないこと。

---

## 機能カバレッジマトリクス

| 機能 | P1 | P2 | P3 | P4 | P5 |
|------|:--:|:--:|:--:|:--:|:--:|
| tree定義 | ✓ | ✓ | ✓ | ✓ | ✓ |
| select | ✓ | ✓ | ✓ | | ✓ |
| seq | ✓ | ✓ | ✓ | ✓ | |
| parallel(:any/:all/:n) | | | | ✓ | |
| check | ✓ | ✓ | ✓ | | ✓ |
| guard | | ✓ | | | |
| if/else | | ✓ | | | |
| invert | | ✓ | | | |
| repeat | | ✓ | | | |
| timeout | | ✓ | | | |
| cooldown | | ✓ | | | |
| while | | ✓ | | | |
| 引数なしアクション | ✓ | ✓ | ✓ | ✓ | ✓ |
| 引数ありアクション | | ✓ | ✓ | ✓ | ✓ |
| プロパティアクセス | ✓ | ✓ | ✓ | | ✓ |
| メンバーチェーン | | | ✓ | | |
| メソッド呼び出し(式) | | | ✓ | | |
| 算術式 | | | ✓ | | ✓ |
| 比較式 | ✓ | ✓ | ✓ | | ✓ |
| 論理式(and/or/not) | | ✓ | | | |
| intリテラル | ✓ | ✓ | ✓ | ✓ | ✓ |
| floatリテラル | | ✓ | ✓ | | |
| boolリテラル | | | ✓ | | |
| stringリテラル | | | ✓ | | ✓ |
| nullリテラル | | | ✓ | | |
| enumリテラル | | | ✓ | | |
| 負リテラル | | | ✓ | | |
| コメント | ✓ | ✓ | | | ✓ |
| IrConvert(暗黙変換) | ✓ | ✓ | ✓ | | |
| エラー回復 | | | | | ✓ |
| カスケード抑止 | | | | | ✓ |
| Reset セマンティクス | | | | ✓ | |

---

## スナップショットディレクトリ構成

```
tests/Crisp.Snapshot/
├── Pattern1_Minimal/
│   ├── source.crisp
│   ├── context.cs
│   ├── cst.verified.txt
│   ├── ast.verified.txt
│   ├── diagnostics.verified.txt    ← 空
│   ├── ir.verified.txt
│   └── generated.verified.cs
│
├── Pattern2_FullDecorators/
│   ├── source.crisp
│   ├── context.cs
│   ├── cst.verified.txt
│   ├── ast.verified.txt
│   ├── diagnostics.verified.txt    ← 空
│   ├── ir.verified.txt
│   └── generated.verified.cs
│
├── Pattern3_Expressions/
│   ├── source.crisp
│   ├── context.cs
│   ├── cst.verified.txt
│   ├── ast.verified.txt
│   ├── diagnostics.verified.txt    ← 空
│   ├── ir.verified.txt
│   └── generated.verified.cs
│
├── Pattern4_Parallel/
│   ├── source.crisp
│   ├── context.cs
│   ├── cst.verified.txt
│   ├── ast.verified.txt
│   ├── diagnostics.verified.txt    ← 空
│   ├── ir.verified.txt
│   └── generated.verified.cs
│
├── Pattern5a_SyntaxErrors/
│   ├── source.crisp
│   ├── cst.verified.txt            ← CstError/CstMissing含む
│   ├── ast.verified.txt            ← 部分的
│   └── diagnostics.verified.txt    ← BS0018, BS0016
│                                   ← ir/generated なし（エラー時は生成しない）
│
└── Pattern5b_TypeErrors/
    ├── source.crisp
    ├── context.cs
    ├── cst.verified.txt            ← 構文は正しい
    ├── ast.verified.txt            ← 全ノード変換される
    └── diagnostics.verified.txt    ← BS0001,0003,0004,0005,0006,0007,0008
                                    ← ir/generated なし（エラー時は生成しない）
```

### ファイル存在ルール

| 条件 | ir.verified.txt | generated.verified.cs |
|---|:---:|:---:|
| Diagnostics に Error がない | 生成する | 生成する |
| Diagnostics に Error がある | **存在しない** | **存在しない** |

**ファイルの有無自体がテストのアサーション**となる:
- Pattern 1〜4: 6ファイル全て存在 → 全パイプライン正常
- Pattern 5a/5b: ir/generated が存在しない → エラー時にIR/コード生成が走らないこと

---

## 実装順序との対応

| Phase | 通すべきパターン | 通すべきスナップショット |
|---|---|---|
| 1a (Lexer) | Pattern 1 | source → tokens（※別途トークンスナップショット） |
| 1b (Parser→CST) | Pattern 1, 5a | source → cst.txt |
| 1c (Lowering→AST) | Pattern 1, 5a | cst → ast.txt |
| 1d (Formatter) | Pattern 1 | source → formatted → source（ラウンドトリップ） |
| 2a-c (Semantics) | Pattern 1〜3, 5b | ast → diagnostics.txt |
| 3a-b (IR) | Pattern 1〜4 | ast → ir.txt |
| 3c (C# Backend) | Pattern 1〜4 | ir → generated.cs |
| 3d (Interpreter) | Pattern 1 | ir → tick実行結果 |
| 4 (統合) | Pattern 1〜5b 全て | 全スナップショットが green |

**Pattern 1 が全フェーズで最初に通るテスト。Pattern 5b が最後に通るテスト。**
