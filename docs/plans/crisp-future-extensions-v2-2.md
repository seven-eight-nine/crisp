# Crisp 将来拡張仕様書 v2

> **Version**: 0.2.0-draft
> **Status**: Planning (Review Incorporated)
> **Prerequisites**: Crisp Core (Phase 1〜4) が完成していること

本ドキュメントは、Crisp の将来拡張候補 F1〜F13 の全仕様を定義する。
各拡張は独立して実装可能であるが、依存関係がある場合は明示する。

---

## 目次

0. [拡張共通原則](#0-拡張共通原則)
1. [F1: Reactive Reevaluation](#f1-reactive-reevaluation)
2. [F2: User-Defined Decorators](#f2-user-defined-decorators)
3. [F3: Typed Blackboard](#f3-typed-blackboard)
4. [F4: Visual Editor](#f4-visual-editor)
5. [F5: IR Optimization Passes](#f5-ir-optimization-passes)
6. [F6: AOT Support](#f6-aot-support)
7. [F7: Debugger](#f7-debugger)
8. [F8: Async/Await Integration](#f8-asyncawait-integration)
9. [F9: Hot Reload](#f9-hot-reload)
10. [F10: Macro System](#f10-macro-system)
11. [F11: Multi-Tree & Cross-Reference](#f11-multi-tree--cross-reference)
12. [F12: Generics](#f12-generics)
13. [F13: Nullable Support](#f13-nullable-support)
14. [Core側に用意すべきフック](#core側に用意すべきフック)
15. [依存関係グラフ](#依存関係グラフ)
16. [推奨実装順序](#推奨実装順序)

---

## 0. 拡張共通原則

### 0.1 Coreと拡張の境界

拡張は以下の原則に従い、Coreを肥大化させない:

| 区分 | Core側で用意するもの | 拡張側で閉じるもの |
|---|---|---|
| BtNode基底 | `Tick()`, `Reset()`, `Abort()` | 具体的なノード型（ReactiveNode等） |
| IrNode | `Id`, `Origin` フィールド | 拡張固有のIRノード型（IrReactive等） |
| Query DB | クエリ追加のインターフェース | 拡張固有のクエリ実装 |
| CST/AST | ビジター基盤 | 拡張固有のノード型 |
| Diagnostics | DiagnosticBag, Descriptor基盤 | 拡張固有のDiagnostic ID |

**原則: Coreに入れるのは「フック」と「基盤」のみ。具体的な挙動は拡張側。**

### 0.2 Query DB 拡張ルール

- 拡張は **新しいクエリを追加する** ことで機能を実現する。既存クエリを変更しない
- 既存クエリの出力型を変更する必要がある場合は、ラッパークエリを作る
- 例外: AST展開系（F2/F10）は lowering と resolve の間にパスを挿入する

### 0.3 AST展開系の実行順序

マクロ・defdec等のAST変換を含む拡張は、以下の順序を遵守する:

```
Parse → CST
  ↓
Lower → AST（マクロ/defdec 定義・呼び出しを含む）
  ↓
★ Expand → AST（マクロ/defdec が全て展開された AST）
  ↓
Resolve → 名前解決
  ↓
TypeCheck → 型検査
  ↓
EmitIR → IR
```

**展開は名前解決の前**。これにより展開結果が通常のASTと同様に型チェックされる。

### 0.4 Reset と Abort の責務分離

Core の `BtNode` 基底に `Reset()` と `Abort()` の2つの状態操作を設ける。
両者の責務は明確に異なる:

| 操作 | 意味 | トリガー | 呼び出し元 |
|---|---|---|---|
| `Reset()` | **正常終了後の状態初期化** | Parallel確定(U4)、ツリー再起動 | Core (Parallel, Sequence等) |
| `Abort()` | **外的要因による強制キャンセル** | Reactive条件false、Async中断 | 拡張 (F1, F8等) |

```csharp
public abstract class BtNode
{
    public abstract BtStatus Tick(TickContext ctx);

    /// 正常終了後の状態初期化。
    /// Parallel確定時、ツリー再起動時に呼ばれる。
    /// サブクラスは内部状態をクリアし、次回Tick時に初期状態から実行されるようにする。
    public virtual void Reset() { }

    /// 外的要因による強制キャンセル。
    /// Reactive条件のfalse化、Async中断、タイムアウト等で呼ばれる。
    /// デフォルトはReset()と同じだが、サブクラスでオーバーライドして
    /// 中断固有の後処理（アニメーション停止等）を実装できる。
    public virtual void Abort() => Reset();
}
```

`Abort()` のデフォルト実装は `Reset()` への委譲。
拡張がなければ `Abort()` は使われず、Coreの挙動に影響しない。

### 0.5 IrNode の Stable Id と Origin

全ての `IrNode` は以下のフィールドを持つ:

```csharp
public abstract class IrNode
{
    /// ノードの一意識別子。
    /// CST → AST → IR を通じて安定的に付与される。
    /// Debugger, Visual Editor, 実行トレースで共通に使用。
    public int Id { get; }

    /// このIRノードの元となったCSTの位置情報。
    /// エラー報告、LSPホバー、ブレークポイント設定に使用。
    public TextSpan Origin { get; }

    public TypeRef? TypeRef { get; }
}
```

Id の付与ルール:
- CST パース時に各 CstNode に連番 Id を付与
- AST lowering 時に CstNode.Id → AstNode.Id を引き継ぐ
- IR lowering 時に AstNode.Id → IrNode.Id を引き継ぐ
- IR最適化パス(F5)でノードが新規生成された場合は新しいIdを採番

これにより:
- F7 (Debugger): nodeId でブレークポイント設定・実行トレース
- F4 (Visual Editor): nodeId でCST位置 ↔ ビジュアルノード対応
- F9 (Hot Reload): 旧ツリーと新ツリーの差分をId単位で追跡可能

---

## F1: Reactive Reevaluation

### 概要

上位ノードの条件が変化したとき、下位で Running 中のサブツリーを
中断・破棄して上位から再評価する仕組み。

従来の BT は「Running の子を記憶して次 tick で続行」が基本だが、
ゲームAIでは「敵が消えたら攻撃を中断してパトロールに戻る」等の
リアクティブな振る舞いが頻繁に必要になる。

### 動機

```crisp
;; 従来: Running中は guard 条件を再評価しない
(guard .IsEnemyVisible
  (seq
    (.Approach .Target)    ;; Running を返し続ける
    (.Attack .Target)))

;; 問題: IsEnemyVisible が false になっても
;;        Approach が Running を返し続ける限り中断されない
```

### DSL 構文

```crisp
;; reactive ノード: 毎 tick 条件を再評価し、
;; false になったら Running 中の子を中断する
(reactive .IsEnemyVisible
  (seq
    (.Approach .Target)
    (.Attack .Target)))

;; reactive-select: 上位の子が Success/Running になったら
;; 下位の Running 中の子を中断して上位を優先する
(reactive-select
  (seq
    (check (< .Health 20))
    (.Flee))                     ;; 優先度高: HPが減ったら即中断
  (seq
    (guard .IsEnemyVisible
      (.Attack .Target)))        ;; 優先度低: 攻撃中でもFleeが割り込む
  (.Patrol))
```

### セマンティクス

#### `reactive`

```
(reactive condition body)

Tick:
  1. condition を評価
  2. condition が false:
     a. body 内の Running 中ノードに Abort() を呼ぶ
     b. Failure を返す
  3. condition が true:
     a. body を Tick する
     b. body の結果をそのまま返す
```

#### `reactive-select`

```
(reactive-select child1 child2 ... childN)

Tick:
  1. child1 から順に評価
  2. childI が Success または Running を返した場合:
     a. childI+1 〜 childN のうち Running 中のもの全てに Abort() を呼ぶ
     b. childI の結果を返す
  3. childI が Failure の場合:
     a. 次の子へ進む
  4. 全て Failure → Failure を返す

通常の select との違い:
  - select: Running の子を記憶し、次 tick ではその子から再開
  - reactive-select: 毎 tick 先頭から再評価。上位が復活したら下位を中断
```

### Running子の追跡

`reactive-select` は **複数の子が同時にRunning状態になり得る**（例: parallel内のサブツリー等）。
そのため、単一の `lastRunningIndex` ではなく **Running集合を追跡** する。

```csharp
public class ReactiveSelectorNode : BtNode
{
    private readonly BtNode[] _children;
    private readonly HashSet<int> _runningIndices = new();

    public override BtStatus Tick(TickContext ctx)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            var status = _children[i].Tick(ctx);
            if (status != BtStatus.Failure)
            {
                // 今回選ばれなかったRunning子を全てAbort
                foreach (var ri in _runningIndices)
                {
                    if (ri != i)
                        _children[ri].Abort();
                }
                _runningIndices.Clear();

                if (status == BtStatus.Running)
                    _runningIndices.Add(i);

                return status;
            }
        }

        // 全 Failure: 残っている Running を全て Abort
        foreach (var ri in _runningIndices)
            _children[ri].Abort();
        _runningIndices.Clear();

        return BtStatus.Failure;
    }

    public override void Abort()
    {
        foreach (var ri in _runningIndices)
            _children[ri].Abort();
        _runningIndices.Clear();
    }

    public override void Reset()
    {
        foreach (var child in _children)
            child.Reset();
        _runningIndices.Clear();
    }
}
```

### ReactiveNode

```csharp
public class ReactiveNode : BtNode
{
    private readonly Func<bool> _condition;
    private readonly BtNode _body;
    private bool _bodyRunning;

    public override BtStatus Tick(TickContext ctx)
    {
        if (!_condition())
        {
            if (_bodyRunning)
            {
                _body.Abort();
                _bodyRunning = false;
            }
            return BtStatus.Failure;
        }

        var status = _body.Tick(ctx);
        _bodyRunning = status == BtStatus.Running;
        if (status != BtStatus.Running)
            _body.Reset();
        return status;
    }

    public override void Abort()
    {
        if (_bodyRunning)
            _body.Abort();
        _bodyRunning = false;
    }

    public override void Reset()
    {
        _body.Reset();
        _bodyRunning = false;
    }
}
```

### CST / AST / IR

```
CST:
  CstReactive        (reactive condition body)
  CstReactiveSelect  (reactive-select children...)

AST:
  AstReactive
    Condition: AstExpr    // bool制約
    Body: AstNode
  AstReactiveSelect
    Children: AstNode[]

IR:
  IrReactive
    Condition: IrExpr
    Body: IrNode
  IrReactiveSelect
    Children: IrNode[]
```

### 型制約

| ノード | 制約 |
|---|---|
| `reactive` | condition は bool |
| `reactive-select` | 子が2つ以上 |

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0021 | Expression in 'reactive' must be bool, got '{type}' |

### 依存

- Core の `Abort()` フック（セクション0.4）

---

## F2: User-Defined Decorators

### 概要

DSL側でカスタムデコレータを定義し、ツリー内で再利用する仕組み。
C#側にノードクラスを追加することなく、DSLの組み合わせで新しいデコレータを作れる。

### 動機

```crisp
;; 毎回これを書くのは冗長
(guard .IsAlive
  (timeout 10.0
    (cooldown 2.0
      (.Attack .Target))))

;; デコレータとして抽象化したい
(defdec safe-timed-action (duration cd)
  (guard .IsAlive
    (timeout duration
      (cooldown cd
        <body>))))

;; 使用
(safe-timed-action 10.0 2.0
  (.Attack .Target))
```

### DSL 構文

```crisp
;; デコレータ定義
(defdec name (param1 param2 ...)
  body-with-<body>-placeholder)

;; 使用
(name arg1 arg2 ...
  child-node)
```

#### 文法追加

```ebnf
program     = (tree | defdec)* EOF ;

defdec      = "(" "defdec" identifier "(" param* ")" node ")" ;
param       = identifier ;

defdec_call = "(" identifier expr* node ")" ;
```

### `<body>` プレースホルダの設計

**`<body>` は専用の CSTノード型として定義する。** 通常の識別子として扱うと、
名前解決時やフォーマッタで事故る。

```
CST:
  CstBodyPlaceholder    <body> を表す専用ノード

AST:
  AstBodyPlaceholder    展開時に実引数のノードに置換される
```

レクサーは `<body>` を単一トークン（`TokenKind.BodyPlaceholder`）として認識する。
これにより:
- パーサーが `<body>` を式やメンバーアクセスとして誤解しない
- フォーマッタが `<body>` を正しくインデントできる
- LSPのセマンティックトークンで専用の色付けが可能

### パラメータの型推論

defdec のパラメータは型アノテーションを持たない。
使用箇所から型を推論する:

```crisp
(defdec safe-timed-action (duration cd)
  (guard .IsAlive
    (timeout duration      ;; timeout は float を期待 → duration: float
      (cooldown cd         ;; cooldown は float を期待 → cd: float
        <body>))))

;; 使用時に型チェック
(safe-timed-action 10.0 2.0 (.Attack .Target))  ;; OK
(safe-timed-action "x" 2.0 (.Attack .Target))   ;; BS0022
```

### 展開 (Expansion)

defdec は **AST → AST のマクロ展開** として実装する。
セクション0.3の順序に従い、名前解決の前に展開される。

```
展開前 AST:
  AstDefdecCall("safe-timed-action", [10.0, 2.0], body=AstActionCall("Attack"))

展開後 AST:
  AstGuard(
    condition=AstMemberAccessExpr("IsAlive"),
    body=AstTimeout(
      seconds=10.0,
      child=AstCooldown(
        seconds=2.0,
        child=AstActionCall("Attack"))))
```

### Query DB への影響

```
新しいクエリ:
  defdecs(file_id) → Map<string, AstDefdec>    // depends: lower
  expand(file_id) → AstTree                     // depends: lower, defdecs
  resolve(file_id) → ResolvedTree               // depends: expand（lowerではなく）
```

### 制約

- defdec は再帰呼び出し不可（無限展開防止）
- defdec 内での defdec 定義不可（ネスト禁止）
- `<body>` プレースホルダーは defdec の body 内に正確に1回出現すること
- パラメータはリテラルまたは式（ノードは渡せない。ノードは `<body>` のみ）

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0022 | Defdec parameter '{name}' inferred as '{expected}', got '{actual}' |
| BS0023 | Defdec '{name}' not found |
| BS0024 | Defdec '{name}' expects {n} parameters, got {m} |
| BS0025 | Recursive defdec call detected: '{name}' |
| BS0026 | Missing `<body>` placeholder in defdec '{name}' |
| BS0027 | Multiple `<body>` placeholders in defdec '{name}' |

### 依存

- F10 (Macro System) はこの拡張の上位互換。F2を先に実装し、F10でより汎用的な仕組みに拡張する

---

## F3: Typed Blackboard

### 概要

Context (this) 以外のデータストアを、DSL から型安全にアクセスする仕組み。
複数の AI エージェント間でデータを共有する場合や、
Context に含めたくないグローバルな状態にアクセスする場合に使用する。

### 動機

```csharp
// 現在: 全データを Context に詰め込む必要がある
public partial class EnemyAI : IBtContext
{
    public float Health { get; set; }
    // これは本来 AI 個別の状態ではなくワールドの状態
    public bool IsAlarmTriggered { get; set; }
    public List<Entity> AllEnemies { get; set; }
}
```

### DSL 構文

```crisp
;; tree 定義に blackboard 型を宣言
(tree CombatAI
  :context EnemyAI
  :blackboard WorldState

  (select
    ;; $ プレフィクスで blackboard にアクセス
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

| プレフィクス | 解決先 | 例 |
|---|---|---|
| `.` | context (this) のメンバー | `.Health` → `this.Health` |
| `$` | blackboard のメンバー | `$.IsAlarmTriggered` → `blackboard.IsAlarmTriggered` |
| `$.` チェーン | blackboard のメンバーチェーン | `$.AllEnemies.Count` |

### アクセス修飾子の方針

Blackboard のメンバーは **Context と同様にアクセス修飾子を無視する（private含めて全てアクセス可能）**。

理由: 生成コードは blackboard オブジェクトを引数として受け取るため、
実行時のアクセスは呼び出し元のスコープに依存する。
ただし Source Generator が生成するコードは blackboard のメンバーに直接アクセスする形式となるため、
blackboard 型が partial であるか、internal/public なメンバーを提供する必要がある。

実装上の注意:
- Blackboard 型が別アセンブリにある場合は internal メンバーにはアクセスできない
- この制約は Diagnostic BS0031 として報告する

### C# 側の定義

```csharp
public class WorldState : IBtBlackboard
{
    public bool IsAlarmTriggered { get; set; }
    public List<Entity> AllEnemies { get; set; }
    public float GlobalThreatLevel { get; set; }
}

public interface IBtBlackboard { }
```

### 生成コード

```csharp
partial class EnemyAI
{
    public partial BtNode BuildCombatTree(WorldState blackboard)
    {
        return new SelectorNode(
            new SequenceNode(
                new ConditionNode(() => blackboard.IsAlarmTriggered),
                new ActionNode(() => this.AlertAllies())),
            new SequenceNode(
                new ConditionNode(() => this.Health < 30f),
                new ActionNode(() => this.Flee())),
            new ActionNode(() => this.Patrol()));
    }
}
```

### CST / AST / IR

```
CST:
  CstTree に :blackboard 属性を追加
  CstBlackboardAccess  $.Foo.Bar  ($ プレフィクスの MemberAccess)

AST:
  AstTree に BlackboardType: string を追加
  AstBlackboardAccessExpr
    Path: MemberPath

IR:
  IrBlackboardLoad
    Ref: MemberRef
```

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0028 | Blackboard access '$' used but no :blackboard declared in tree '{name}' |
| BS0029 | Blackboard member '{name}' not found on type '{type}' |
| BS0030 | Blackboard type '{type}' does not implement IBtBlackboard |
| BS0031 | Blackboard member '{name}' is inaccessible from generated code (different assembly) |

### 依存

- なし（Core のみ）

---

## F4: Visual Editor

### 概要

LSP + WebView を組み合わせて、.crisp ファイルの
ビジュアルなツリーエディタを提供する。
テキスト編集とビジュアル編集の双方向同期を実現する。

### アーキテクチャ

```
┌─────────────────────────────────────────┐
│  Editor (VSCode / Rider)                │
│                                         │
│  ┌──────────────┐  ┌────────────────┐   │
│  │ Text Editor  │  │ WebView Panel  │   │
│  │ (.crisp)   │  │ (Tree Visual)  │   │
│  └──────┬───────┘  └───────┬────────┘   │
│         │                  │            │
│         │    LSP Protocol  │  PostMessage│
│         ▼                  ▼            │
│  ┌──────────────────────────────────┐   │
│  │       Crisp LSP Server           │   │
│  │  ┌──────────┐ ┌──────────────┐   │   │
│  │  │ Query DB │ │ Tree Layout  │   │   │
│  │  │          │ │ Engine       │   │   │
│  │  └──────────┘ └──────────────┘   │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

### 双方向同期

```
テキスト → ビジュアル:
  1. テキスト変更 → LSP didChange
  2. Query DB 更新 → CST/AST 再計算
  3. AST → TreeLayout JSON 変換（IrNode.Id でノード識別）
  4. LSP custom notification → WebView に送信
  5. WebView がツリーを再描画

ビジュアル → テキスト:
  1. WebView でノードをドラッグ&ドロップ / 追加 / 削除
  2. WebView → LSP custom request (操作コマンド + nodeId)
  3. LSP が nodeId → CstNode.Origin で CST 上の位置を特定
  4. CST を編集 → フォーマッタ経由でテキスト生成
  5. LSP workspace/applyEdit → テキストエディタに反映
```

### Tree Layout JSON

```json
{
  "tree": "EnemyCombat",
  "root": {
    "id": 1,
    "type": "selector",
    "origin": { "start": 24, "length": 156 },
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
            "returnType": "BtStatus"
          }
        ]
      }
    ]
  },
  "diagnostics": []
}
```

ノードの `id` フィールドは `IrNode.Id`（= `CstNode.Id`）と一致する。
これにより:
- ビジュアルノードクリック → テキスト上の位置にジャンプ
- テキスト上のカーソル位置 → 対応するビジュアルノードをハイライト

### LSP カスタムメソッド

| メソッド | 方向 | 内容 |
|---|---|---|
| `crisp/treeLayout` | Server → Client | ツリーレイアウト JSON の送信 |
| `crisp/addNode` | Client → Server | ノード追加 (parentId, position, nodeType) |
| `crisp/removeNode` | Client → Server | ノード削除 (nodeId) |
| `crisp/moveNode` | Client → Server | ノード移動 (nodeId, newParentId, newPosition) |
| `crisp/wrapNode` | Client → Server | ノードをデコレータで包む (nodeId, decoratorType) |
| `crisp/unwrapNode` | Client → Server | デコレータを解除 (nodeId) |

### WebView 技術選定

- **React + D3.js** でツリー描画
- WebView は VSCode の Webview API / Rider の Tool Window で提供
- LSP サーバーとは WebSocket または postMessage で通信

### 依存

- Crisp LSP (Phase 2) が前提
- フォーマッタ (Phase 1c) が双方向同期に必要
- Core の Stable Id（セクション0.5）

---

## F5: IR Optimization Passes

### 概要

IR レベルで最適化を行うパスを追加する。
正当性を保ったまま実行時のオーバーヘッドを削減する。

### パス一覧

#### Pass 1: 定数畳み込み (Constant Folding)

```
最適化前:
  (ir-binary-op :add (ir-literal 10 :int) (ir-literal 20 :int))

最適化後:
  (ir-literal 30 :int)
```

適用条件: 両オペランドがリテラル、演算が純粋

#### Pass 2: 到達不能ノード除去 (Dead Node Elimination)

```
最適化前:
  (ir-sequence
    (ir-condition (ir-literal false :bool))   ;; 常に Failure
    (ir-action "Attack" ()))                  ;; 到達不能

最適化後:
  (ir-condition (ir-literal false :bool))     ;; Sequence は Failure 確定
```

#### Pass 3: 単一子ノードの畳み込み (Single-Child Collapse)

```
  (ir-selector (ir-action "Patrol" ()))
→ (ir-action "Patrol" ())

  (ir-invert (ir-invert child))
→ child
```

#### Pass 4: 共通部分式の巻き上げ (Common Subexpression Hoisting)

```
最適化前:
  (ir-selector
    (ir-sequence
      (ir-condition (ir-member-load ("Health")))
      (ir-action "Attack" ()))
    (ir-sequence
      (ir-condition (ir-member-load ("Health")))
      (ir-action "Approach" ())))

最適化後:
  (ir-let "tmp_0" (ir-member-load ("Health"))
    (ir-selector
      (ir-sequence
        (ir-condition (ir-ref "tmp_0"))
        (ir-action "Attack" ()))
      (ir-sequence
        (ir-condition (ir-ref "tmp_0"))
        (ir-action "Approach" ()))))
```

**初期はプロパティアクセスのみを対象**。メソッド呼び出しの純粋性は保証できないため対象外。
CSE で `IrLet / IrRef` を導入すると Emitter / Interpreter の責務が増えるため、
初期は Pass 1〜3 のみ実装し、Pass 4 は需要を見て追加する。

#### Pass 5: 型変換の統合 (Convert Fusion)

```
  (ir-convert (ir-convert expr :to float) :to float)
→ (ir-convert expr :to float)

  (ir-convert (ir-literal 42 :int) :to float)
→ (ir-literal 42.0 :float)
```

### IR 追加ノード (Pass 4 実装時)

```
IrLet     Name: string, Value: IrExpr, Body: IrNode
IrRef     Name: string
```

### パスマネージャ

```csharp
public class IrPassManager
{
    private readonly List<IIrPass> _passes = new();

    public IrPassManager()
    {
        _passes.Add(new ConstantFoldingPass());
        _passes.Add(new DeadNodeEliminationPass());
        _passes.Add(new SingleChildCollapsePass());
        _passes.Add(new ConvertFusionPass());
        // CSE は将来追加
    }

    public IrNode Optimize(IrNode root)
    {
        var current = root;
        foreach (var pass in _passes)
            current = pass.Run(current);
        return current;
    }
}

public interface IIrPass
{
    IrNode Run(IrNode root);
}
```

### Query DB への影響

```
新しいクエリ:
  optimize_ir(file_id) → IrTree    // depends: emit_ir
```

### テスト方針

- 最適化前 IR → 最適化後 IR のスナップショットテスト
- 最適化後 IR の実行結果が最適化前と同一であることのプロパティテスト

### 依存

- なし（Core のみ）

---

## F6: AOT Support

### 概要

NativeAOT / IL2CPP 環境でインタープリタバックエンドが動作するよう、
リフレクション依存を排除する。

### 解決策: Source-Generated Accessor

Source Generator がコンテキスト型の全メンバーに対する
アクセサデリゲートを事前生成する。

```csharp
// Source Generator が生成
partial class EnemyAI
{
    private static readonly Dictionary<string, Func<EnemyAI, object?>> _memberAccessors = new()
    {
        ["Health"] = ctx => ctx.Health,
        ["Target"] = ctx => ctx.Target,
    };

    private static readonly Dictionary<string, Func<EnemyAI, object?[], object?>> _methodInvokers = new()
    {
        ["Attack"] = (ctx, args) => ctx.Attack((Entity)args[0]),
        ["Flee"] = (ctx, args) => ctx.Flee(),
    };

    public static IAccessorTable<EnemyAI> GetAccessorTable()
        => new AccessorTable<EnemyAI>(_memberAccessors, _methodInvokers);
}
```

### チェーンアクセスの制限

Context のメンバー（第1段）は AOT 安全。
チェーン先（`.Target.Position.x`）の `.Position.x` 部分は:

- Source Generator がアクセサを追加生成可能な型は AOT 安全
- 未知の型はリフレクションにフォールバック（AOT 非安全）
- 完全 AOT 対応が必要なら、`[CrispAccessible]` 属性を対象型にも付与

### 依存

- なし（Core のみ。Source Generator の拡張）

---

## F7: Debugger

### 概要

ツリーの実行をステップ実行し、各ノードの評価結果・式の値・
Running 状態をリアルタイムで観察できるデバッガ。

### アーキテクチャ

```
┌──────────────────────┐     ┌────────────────────────┐
│  Game Runtime         │     │  Debug UI (Editor)     │
│                       │     │                        │
│  BtNode.Tick()        │     │  Tree View + Inspector │
│    │                  │     │    ▲                   │
│    ▼                  │     │    │                   │
│  DebugProxy           │────→│  Debug Protocol        │
│  (イベント送信)        │     │  (WebSocket)           │
│                       │     │                        │
└──────────────────────┘     └────────────────────────┘
```

### Debug Proxy

IrNode.Id（= CstNode.Id）を使ってノードを一意に識別する。
Source Generator がデバッグモード時に DebugProxy を挿入する生成コードを出す。

```csharp
public class DebugProxyNode : BtNode
{
    private readonly BtNode _inner;
    private readonly int _nodeId;       // IrNode.Id と一致
    private readonly IDebugSink _sink;

    public override BtStatus Tick(TickContext ctx)
    {
        _sink.OnBeforeTick(_nodeId, ctx);

        if (_sink.ShouldBreak(_nodeId))
            _sink.WaitForContinue();

        var status = _inner.Tick(ctx);

        _sink.OnAfterTick(_nodeId, status, ctx);
        return status;
    }
}
```

### IDebugSink

```csharp
public interface IDebugSink
{
    void OnBeforeTick(int nodeId, TickContext ctx);
    void OnAfterTick(int nodeId, BtStatus status, TickContext ctx);
    void OnExprEvaluated(int nodeId, string expr, object? value);
    void OnAbort(int nodeId);
    bool ShouldBreak(int nodeId);
    void WaitForContinue();
}
```

### TickContext への統合

```csharp
public readonly record struct TickContext(
    float DeltaTime,
    int FrameIndex = 0,
    IDebugSink? Debug = null);   // F7 で追加
```

`Debug` が null なら通常実行。非 null ならデバッグモード。
IDebugSink を TickContext に含めることで、Coreの BtNode シグネチャを変更せずに
デバッグ機能を注入できる。

### nodeId ↔ エディタ位置の対応

nodeId は IrNode.Id であり、IrNode.Origin（TextSpan）を経由して
エディタ上の行・列に変換可能。これにより:

- エディタ上でクリックした行 → nodeId → ブレークポイント設定
- 実行トレースの nodeId → エディタ上でハイライト

### Debug Protocol メッセージ

| メッセージ | 方向 | 内容 |
|---|---|---|
| `tick_start` | Runtime → UI | Tick 開始 |
| `node_enter` | Runtime → UI | ノード評価開始 (nodeId, type) |
| `node_exit` | Runtime → UI | ノード評価終了 (nodeId, status) |
| `expr_value` | Runtime → UI | 式の評価値 (nodeId, expr, value) |
| `abort` | Runtime → UI | ノードが Abort された (nodeId) |
| `tree_state` | Runtime → UI | 全ノードの状態スナップショット |
| `set_breakpoint` | UI → Runtime | ブレークポイント設定 (nodeId) |
| `continue` | UI → Runtime | 実行続行 |
| `step_over` | UI → Runtime | 次ノードまで実行 |
| `step_into` | UI → Runtime | 子ノードに入る |

### 依存

- Core の Stable Id（セクション0.5）
- F4 (Visual Editor) と組み合わせるとツリー上でブレークポイントを視覚的に設定できる
- F7 単体でも CLI / ログベースのデバッガとして動作可能

---

## F8: Async/Await Integration

### 概要

C# の async/await をアクションとして使えるようにする。
tick 駆動と async の橋渡しを行う。

### 動機

```csharp
// 現在: Running を手動管理
[BtAction]
public BtStatus MoveTo(Vector3 target)
{
    if (Vector3.Distance(transform.position, target) < 0.1f)
        return BtStatus.Success;
    transform.position = Vector3.MoveTowards(...);
    return BtStatus.Running;
}

// 理想: async で書きたい
[BtAction]
public async ValueTask<BtStatus> MoveTo(Vector3 target, CancellationToken ct)
{
    while (Vector3.Distance(transform.position, target) > 0.1f)
    {
        ct.ThrowIfCancellationRequested();
        transform.position = Vector3.MoveTowards(...);
        await YieldFrame(ct);
    }
    return BtStatus.Success;
}
```

### DSL 構文

DSL 側に変更はない。C# 側のメソッドシグネチャで判定する。

### 非同期アクションの抽象モデル

具体的な非同期ライブラリ（UniTask, ValueTask, Task 等）に依存しない抽象モデル:

**非同期アクションは内部に未完了の非同期操作を保持し、
各 tick で完了状態をポーリングし、完了時に BtStatus を返す。**

```csharp
/// 非同期操作の抽象インターフェース
public interface IAsyncOperation
{
    bool IsCompleted { get; }
    BtStatus GetResult();
    void Cancel();
}
```

### 型検査の拡張

アクションメソッドの戻り値チェック (BS0008) を拡張:

```
許可される戻り値型:
  - BtStatus                          (同期)
  - IAsyncOperation を返す任意の型     (非同期)

具体的にサポートする型は設定可能:
  - ValueTask<BtStatus>    (.NET標準)
  - Task<BtStatus>         (.NET標準)
  - UniTask<BtStatus>      (Unity, アダプタ経由)
```

### ランタイム: AsyncActionNode

```csharp
public class AsyncActionNode : BtNode
{
    private readonly Func<CancellationToken, IAsyncOperation> _factory;
    private IAsyncOperation? _current;
    private CancellationTokenSource? _cts;

    public override BtStatus Tick(TickContext ctx)
    {
        if (_current == null)
        {
            _cts = new CancellationTokenSource();
            _current = _factory(_cts.Token);
        }

        if (_current.IsCompleted)
        {
            var result = _current.GetResult();
            _current = null;
            _cts?.Dispose();
            _cts = null;
            return result;
        }

        return BtStatus.Running;
    }

    public override void Abort()
    {
        _cts?.Cancel();
        _current?.Cancel();
        _current = null;
        _cts?.Dispose();
        _cts = null;
    }

    public override void Reset()
    {
        if (_current != null)
        {
            _cts?.Cancel();
            _current?.Cancel();
        }
        _current = null;
        _cts?.Dispose();
        _cts = null;
    }
}
```

### アダプタ

具体的な非同期ライブラリへのアダプタを提供:

```csharp
// ValueTask アダプタ
public class ValueTaskAdapter : IAsyncOperation
{
    private readonly ValueTask<BtStatus> _task;
    public bool IsCompleted => _task.IsCompleted;
    public BtStatus GetResult() => _task.Result;
    public void Cancel() { /* CancellationToken経由 */ }
}

// UniTask アダプタ（Unity パッケージとして別配布）
public class UniTaskAdapter : IAsyncOperation { ... }
```

### IR への影響

```
IrAction に IsAsync フラグを追加:
  IrAction
    Method: MethodRef
    Args: IrExpr[]
    IsAsync: bool
```

C# Emitter は IsAsync に応じて `ActionNode` か `AsyncActionNode` を生成。

### 依存

- F1 (Reactive Reevaluation) の Abort() メカニズムと連携
- IAsyncOperation の具体実装はライブラリ依存（UniTask等）

---

## F9: Hot Reload

### 概要

実行中に .crisp ファイルを変更し、ツリーを即座に再構築する。
ゲームを再起動せずに AI の挙動を調整できる。

### ランタイム API

```csharp
public class BtRunner : MonoBehaviour
{
    [SerializeField] private TextAsset _bscriptFile;

    private BtNode _tree;
    private IBtContext _context;

    void Start()
    {
        _context = GetComponent<IBtContext>();
        _tree = CrispRuntime.Compile(_bscriptFile.text, _context);

        #if UNITY_EDITOR
        CrispHotReload.Watch(
            AssetDatabase.GetAssetPath(_bscriptFile),
            newSource =>
            {
                try
                {
                    _tree = CrispRuntime.Compile(newSource, _context);
                    Debug.Log("[Crisp] Hot reloaded");
                }
                catch (CrispCompilationException ex)
                {
                    Debug.LogError($"[Crisp] Hot reload failed: {ex.Message}");
                }
            });
        #endif
    }

    void Update()
    {
        _tree.Tick(new TickContext(Time.deltaTime));
    }
}
```

### CrispRuntime (ランタイムコンパイラ)

```csharp
public static class CrispRuntime
{
    public static BtNode Compile(string source, IBtContext context)
    {
        var db = new Database();
        var fileId = new FileId("runtime://hot-reload");

        db.SetSourceText(fileId, source);
        db.SetContextType(fileId, context.GetType());

        var diagnostics = db.AllDiagnostics(fileId);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new CrispCompilationException(diagnostics);

        var ir = db.EmitIR(fileId);
        return Interpreter.BuildTree(ir, context);
    }
}
```

### 状態の引き継ぎ

Hot Reload 時、Running 中のノードの状態は**引き継がない**。
新しいツリーは初期状態から実行される。

理由:
- ツリー構造が変わった場合、ノードの対応関係が取れない
- Running 状態の引き継ぎは安全でない
- 開発時のイテレーションでは初期状態からの実行で十分

### 依存

- インタープリタバックエンド (Phase 3d) が前提
- F6 (AOT Support) のランタイムコンパイラ部分と共通基盤

---

## F10: Macro System

### 概要

DSL 内でマクロを定義・展開する、Lisp 的なメタプログラミング機能。
F2 (defdec) の上位互換として、より汎用的な構文変換を提供する。

### DSL 構文

```crisp
(defmacro name (params...)
  body)

;; body 内で使える特殊形式:
;;   (quote expr)                展開しない
;;   (unquote expr)              パラメータの値に置換
;;   (splice exprs)              リストの中身を展開
;;   (gensym prefix)             一意なシンボル生成
;;   (if-expand cond then else)  展開時の条件分岐
```

### 例

#### 優先度付きアクション列

```crisp
(defmacro priority-actions (actions...)
  (select (splice
    (map actions (fn (a)
      (seq (check (quote (. (concat a "-ready?"))))
           (quote a)))))))

(priority-actions .Attack .Defend .Patrol)

;; 展開結果
(select
  (seq (check .Attack-ready?) .Attack)
  (seq (check .Defend-ready?) .Defend)
  (seq (check .Patrol-ready?) .Patrol))
```

#### ステートマシン風

```crisp
(defmacro state-machine (states...)
  (select (splice
    (map states (fn (s)
      (guard (= .CurrentState (quote (:: State (. (car s)))))
        (unquote (cdr s))))))))

(state-machine
  (Idle (.Idle))
  (Combat (seq (.Approach .Target) (.Attack .Target)))
  (Flee (.Flee)))
```

### マクロ展開器の内部言語

```
利用可能な関数:
  quote, unquote, splice     構文操作
  map, filter, fold          リスト操作
  car, cdr, cons, list       リスト基本操作
  concat, symbol             シンボル操作
  gensym                     一意シンボル生成
  if-expand, cond-expand     条件分岐
  length, nth                リストアクセス
  =, <, >, not, and, or      比較・論理
  +, -, *, /                 算術
```

### 安全性

- 展開の最大深度を制限（デフォルト: 100）
- 相互再帰を検出してエラー
- 展開結果が有効な AST であることをバリデーション

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0031 | Macro '{name}' not found |
| BS0032 | Macro '{name}' expects {n} arguments, got {m} |
| BS0033 | Macro expansion exceeded depth limit ({n}) |
| BS0034 | Recursive macro detected: {cycle} |
| BS0035 | Macro expansion produced invalid syntax: {detail} |

### 依存

- F2 (defdec) を先に実装し、F10 で上位互換化

---

## F11: Multi-Tree & Cross-Reference

### 概要

1ファイルに複数のツリーを定義し、ツリー間で相互参照する機能。

### DSL 構文

```crisp
(tree MainBehavior
  (select
    (ref CombatBehavior)
    (ref PatrolBehavior)
    (.Idle)))

(tree CombatBehavior
  (seq
    (check .IsEnemyVisible)
    (ref AttackSequence)
    (.Disengage)))

(tree AttackSequence
  (seq
    (.Approach .Target)
    (.Attack .Target)))
```

### 別ファイル参照

```crisp
;; SharedBehaviors.crisp
(tree FleeSequence
  :context IFleeCapable
  (seq
    (.FindSafePoint)
    (.RunTo .SafePoint)))
```

```crisp
;; EnemyAI.crisp
(import "SharedBehaviors.crisp")

(tree EnemyMain
  (select
    (seq
      (check (< .Health 20))
      (ref FleeSequence))
    (.Patrol)))
```

### Context 型の互換性チェック

サブツリーが `:context` を宣言している場合、呼び出し元の Context 型が
そのインターフェースを実装しているかをコンパイル時にチェック。

### ref の展開

`ref` はインライン展開ではなく、**別メソッド呼び出し**として生成:

```csharp
partial class EnemyAI
{
    public partial BtNode BuildMainBehavior()
    {
        return new SelectorNode(
            BuildCombatBehavior(),
            BuildPatrolBehavior(),
            new ActionNode(() => this.Idle()));
    }
}
```

### 循環参照の検出

```
(tree A (ref B))
(tree B (ref A))   ;; BS0037: Circular tree reference: A → B → A
```

### CST / AST / IR

```
CST:
  CstRef        (ref TreeName)
  CstImport     (import "path")

AST:
  AstRef
    TreeName: string
    ResolvedTree: AstTree?

IR:
  IrRef
    TreeName: string
```

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0036 | Context type '{type}' does not satisfy constraint '{interface}' required by tree '{tree}' |
| BS0037 | Circular tree reference detected: {cycle} |
| BS0038 | Tree '{name}' not found (in current file or imports) |
| BS0039 | Ambiguous tree '{name}': found in multiple imported files |
| BS0040 | Import file '{path}' not found |

### 依存

- なし（Core のみ）

---

## F12: Generics

### 概要

Context 型のジェネリック型引数に対応する。

### DSL 構文

```crisp
(tree GenericFSM
  :context StateMachineAI<EnemyState>

  (select
    (guard (= .CurrentState ::EnemyState.Combat)
      (.Attack .Target))
    (.TransitionTo ::EnemyState.Patrol)))
```

### Source Generator の処理

1. `[BehaviorTree]` 属性の付いた partial メソッドを検出
2. ContainingType がジェネリックかチェック
3. `:context` から具体型引数を取得
4. ConstructedFrom で open generic type を取得、型引数を適用して closed type を構築
5. closed type のメンバーに対して名前解決

### 制約 (where) の尊重

```csharp
public partial class AI<T> : IBtContext where T : IComparable<T>
```

```crisp
(tree Test :context AI<int>)      ;; OK
(tree Test :context AI<Entity>)   ;; BS0041 (制約違反)
```

### Diagnostics

| Code | メッセージ |
|---|---|
| BS0041 | Type argument '{type}' does not satisfy constraint '{constraint}' on '{genericType}' |
| BS0042 | Generic type '{type}' requires {n} type arguments, got {m} |
| BS0043 | Cannot use open generic type '{type}' as context |

### 依存

- なし（Core のみ。Source Generator と型検査の拡張）

---

## F13: Nullable Support

### 概要

C# の nullable annotation (#nullable enable) を尊重し、
null 安全性をコンパイル時に検証する。

### Nullable 解析

Source Generator が Roslyn の `NullableAnnotation` を読み取る:

```csharp
IPropertySymbol.NullableAnnotation == NullableAnnotation.Annotated     // T?
IPropertySymbol.NullableAnnotation == NullableAnnotation.NotAnnotated   // T
```

### フロー解析（Null Narrowing）

```crisp
(seq
  (guard (!= .Target null)
    (seq
      (check (> .Target.Health 0))    ;; OK: null チェック済み
      (.Attack .Target))))            ;; OK

(.Attack .Target)                     ;; Warning: .Target may be null
```

フロー解析ルール:

```
guard (!= .X null) body  → body 内で .X は NotNull
if (!= .X null) then else → then 内で NotNull, else 内で MaybeNull
check (!= .X null)       → 後続 seq 内で NotNull（Failure なら seq 中断のため）
```

### Diagnostics

| Code | Severity | メッセージ |
|---|---|---|
| BS0044 | Warning | Dereference of possibly null member '{name}' |
| BS0045 | Warning | Comparison with null is always true: '{name}' is non-nullable |
| BS0046 | Warning | Comparison with null is always false: '{name}' is non-nullable |
| BS0047 | Info | Null check on non-nullable member '{name}' is unnecessary |

全て Warning / Info。Error ではない。

### 設定

```csharp
[BehaviorTree("Combat.crisp", NullableAnalysis = true)]
public partial BtNode BuildTree();
```

デフォルトは、コンテキスト型のプロジェクトが `#nullable enable` なら有効。

### 依存

- なし（Core のみ。型検査の拡張）

---

## Core側に用意すべきフック

レビューの結果、**Core完成時点で以下のフックを用意する**ことで
将来拡張の実装コストを下げ、全体の一貫性を保つ:

### フック一覧

| フック | Core での実装 | 使用する拡張 |
|---|---|---|
| `BtNode.Abort()` | `virtual void Abort() => Reset();` | F1, F8 |
| `IrNode.Id` | 全IrNodeに連番Idを付与 | F4, F7, F9 |
| `IrNode.Origin` | CstNode.Span を引き継ぎ | F4, F7 |
| `TickContext.Debug` | `IDebugSink? Debug = null` | F7 |
| `CstNode.Id` | パース時に連番付与 | F4, F7（Origin経由） |

### 実装コスト

- `Abort()`: BtNode に 1メソッド追加。デフォルトは Reset() 委譲。影響ゼロ
- `Id / Origin`: 既存のノードに int / TextSpan フィールド追加。パフォーマンス影響は微小
- `TickContext.Debug`: record struct にオプショナルフィールド追加。null なら通常パス

**全てデフォルト値で既存動作に影響なし。** フックの存在コストは実質ゼロ。

---

## 依存関係グラフ

```
Core hooks:
  Abort()     ←── F1, F8
  Id/Origin   ←── F4, F7, F9
  Debug sink  ←── F7

機能間依存:
  F2 (defdec) ──→ F10 (macro, 上位互換)
  F1 (reactive) ──→ F8 (async, Abort連携)
  Phase 2 (LSP) ──→ F4 (visual editor)
  Phase 3d (interpreter) ──→ F9 (hot reload)
  F4 ←──→ F7 (相互に恩恵)

独立:
  F3 (blackboard)
  F5 (optimization)
  F6 (AOT)
  F11 (multi-tree)
  F12 (generics)
  F13 (nullable)
```

---

## 推奨実装順序

### Tier 1: 高インパクト・低コスト（Core 直後に実装）

| 順序 | 拡張 | 理由 |
|---|---|---|
| 1 | **F11: Multi-Tree** | 実用的な AI に不可欠。パーサーと Query DB の小規模拡張 |
| 2 | **F1: Reactive** | ゲーム AI の最頻出パターン。Abort() 機構の活用 |
| 3 | **F5: IR Optimization** | Pass 1〜3 だけでも価値がある。段階的追加可能 |

### Tier 2: 高インパクト・中コスト

| 順序 | 拡張 | 理由 |
|---|---|---|
| 4 | **F8: Async/Await** | 非同期アクションの需要。F1 の Abort が前提 |
| 5 | **F3: Blackboard** | マルチエージェントで必要 |
| 6 | **F13: Nullable** | モダン C# への適合 |
| 7 | **F2: Defdec** | DSL の表現力向上。マクロの基礎 |

### Tier 3: 開発体験向上

| 順序 | 拡張 | 理由 |
|---|---|---|
| 8 | **F7: Debugger** | AI デバッグの劇的改善 |
| 9 | **F9: Hot Reload** | 開発イテレーション高速化 |
| 10 | **F4: Visual Editor** | 大きな実装コストだがインパクトも大 |

### Tier 4: 上級機能

| 順序 | 拡張 | 理由 |
|---|---|---|
| 11 | **F6: AOT** | IL2CPP 対応が必要になったら |
| 12 | **F12: Generics** | ジェネリック Context の需要が出たら |
| 13 | **F10: Macro** | F2 で足りなくなったら |

### 実装工数の目安

| 拡張 | 推定工数 | 主な作業 |
|---|---|---|
| F1 | 中 (1-2週) | ReactiveNode, ReactiveSelectorNode, Running集合追跡 |
| F2 | 中 (1-2週) | defdec構文, CstBodyPlaceholder, AST展開パス |
| F3 | 中 (1-2週) | $ プレフィクス構文, 名前解決拡張 |
| F4 | 大 (4-6週) | WebView UI, 双方向同期, Tree Layout |
| F5 | 中 (2-3週) | Pass 1-3 (初期), Pass 4-5 (追加) |
| F6 | 中 (1-2週) | Source-Generated Accessor |
| F7 | 大 (3-4週) | DebugProxy, Debug Protocol, Debug UI |
| F8 | 中 (2-3週) | AsyncActionNode, IAsyncOperation, アダプタ |
| F9 | 中 (1-2週) | FileWatcher, CrispRuntime |
| F10 | 大 (3-4週) | マクロインタープリタ, quote/unquote |
| F11 | 小 (1週) | ref/import構文, 循環検出 |
| F12 | 中 (2-3週) | ジェネリック型解決, 制約チェック |
| F13 | 中 (2-3週) | Nullable解析, フロー解析 |
