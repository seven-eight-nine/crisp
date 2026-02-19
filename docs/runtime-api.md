# ランタイム API リファレンス

`Crisp.Runtime` パッケージはビヘイビアツリーの実行に必要な全ての型を提供します。Source Generator が生成するコードはこれらの型を使用します。

## 目次

- [BtStatus](#btstatus)
- [BtNode](#btnode)
- [TickContext](#tickcontext)
- [コンポジットノード](#コンポジットノード)
- [デコレータノード](#デコレータノード)
- [リアクティブノード](#リアクティブノード)
- [リーフノード](#リーフノード)
- [非同期ノード](#非同期ノード)
- [デバッグノード](#デバッグノード)
- [ParallelPolicy](#parallelpolicy)
- [Blackboard](#blackboard)
- [非同期操作 (IAsyncOperation)](#非同期操作-iasyncoperation)
- [デバッガ (IDebugSink)](#デバッガ-idebugsink)
- [ランタイムデバッガ (BtDebugger)](#ランタイムデバッガ-btdebugger)
- [AOT サポート](#aot-サポート)
- [属性](#属性)
- [インタプリタ (Hot Reload)](#インタプリタ-hot-reload)

## BtStatus

ノードの実行結果を表す列挙型です。

```csharp
namespace Crisp.Runtime;

public enum BtStatus
{
    Success,    // ノードの処理が成功した
    Failure,    // ノードの処理が失敗した
    Running,    // ノードの処理が継続中（次の tick で再評価が必要）
}
```

| 値 | 説明 | 用途 |
|---|---|---|
| `Success` | ノードの処理が正常に完了した | アクション完了、条件が真 |
| `Failure` | ノードの処理が失敗した | アクション失敗、条件が偽 |
| `Running` | 処理が進行中で、次のフレームで継続する | 移動中、アニメーション再生中 |

`Running` はビヘイビアツリーの重要な概念です。1 フレームで完了しない処理（例: 目的地への移動）では `Running` を返し、次のフレームの `Tick` で処理を継続します。

## BtNode

全てのビヘイビアツリーノードの抽象基底クラスです。

```csharp
namespace Crisp.Runtime;

public abstract class BtNode
{
    public abstract BtStatus Tick(TickContext ctx);
    public virtual void Reset() { }
    public virtual void Abort() => Reset();

    // デバッグ用プロパティ
    public BtStatus? LastStatus { get; protected set; }
    public virtual IReadOnlyList<BtNode> DebugChildren => Array.Empty<BtNode>();
    public virtual string DebugNodeType => GetType().Name.Replace("Node", "").ToLowerInvariant();
    public virtual string? DebugLabel => null;
}
```

### Tick メソッド

ノードを 1 回評価し、結果を返します。ゲームループの各フレームで呼び出します。

```csharp
var tree = ai.BuildTree();
var ctx = new TickContext(DeltaTime: Time.deltaTime);
BtStatus result = tree.Tick(ctx);
```

### Reset メソッド

ノードの内部状態をクリアし、次の `Tick` が初期状態から開始されるようにします。

以下の場面で使用されます:
- ツリーを最初から再評価する場合
- `Parallel` が結果を確定し、子ノードの状態をクリアする場合
- デコレータが子を再実行する場合

```csharp
tree.Reset();   // 全ノードの状態をクリア
tree.Tick(ctx); // 最初から評価し直す
```

### Abort メソッド

外部要因によるノードの強制中断を処理します。以下の場面で呼び出されます:

- `reactive` ノードの条件が `false` になった場合
- `reactive-select` で上位の子が復活した場合
- 非同期アクション（`AsyncActionNode`）のキャンセル時
- `timeout` ノードの時間切れ時

デフォルト実装は `Reset()` に委譲します。キャンセル固有のクリーンアップ（アニメーション停止等）が必要な場合はオーバーライドしてください。

### デバッグプロパティ

`BtNode` は実行時のデバッグ・インスペクションのための 4 つのプロパティを提供します。`BtDebugger` によるスナップショット取得や、独自のデバッグツール構築に使用できます。

| プロパティ | 型 | 説明 |
|---|---|---|
| `LastStatus` | `BtStatus?` | 最後の `Tick` の結果。未評価なら `null`。`Reset()` で `null` にクリアされる |
| `DebugChildren` | `IReadOnlyList<BtNode>` | デバッグ用の子ノード一覧。リーフノードは空リストを返す |
| `DebugNodeType` | `string` | ノード種別名（`"selector"`, `"sequence"`, `"action"` 等） |
| `DebugLabel` | `string?` | 人間可読なラベル（`"Patrol()"`, `".Health < 30"` 等）。ラベルなしなら `null` |

**`LastStatus` のライフサイクル:**

```csharp
var node = new ActionNode(() => BtStatus.Success);

// 未評価 → null
Console.WriteLine(node.LastStatus); // null

// Tick 後 → 結果が記録される
node.Tick(ctx);
Console.WriteLine(node.LastStatus); // Success

// Reset 後 → null にクリア
node.Reset();
Console.WriteLine(node.LastStatus); // null
```

**各ノード型の `DebugChildren`:**

| ノード型 | DebugChildren |
|---|---|
| SelectorNode, SequenceNode, ParallelNode | 全子ノード |
| ReactiveSelectorNode | 全子ノード |
| GuardNode, InvertNode, RepeatNode, TimeoutNode, CooldownNode, WhileNode, ReactiveNode | 子ノード 1 つ |
| IfNode | then + else（else がある場合） |
| ConditionNode, ActionNode, AsyncActionNode | 空 |
| DebugProxyNode | 内部ノードの DebugChildren を透過的に返す |

## TickContext

ノードの `Tick` メソッドに渡されるコンテキスト情報です。`record struct` のため、ヒープアロケーションは発生しません。

```csharp
namespace Crisp.Runtime;

public readonly record struct TickContext(
    float DeltaTime,
    int FrameIndex = 0,
    IDebugSink? Debug = null);
```

| パラメータ | 型 | 説明 |
|---|---|---|
| `DeltaTime` | `float` | 前回の Tick からの経過時間（秒）。`Timeout` や `Cooldown` ノードの時間計測に使用 |
| `FrameIndex` | `int` | フレーム番号（オプション）。デバッグやログ出力に使用。デフォルト値は `0` |
| `Debug` | `IDebugSink?` | デバッグシンク。`null` なら通常実行、非 `null` ならデバッグモード |

```csharp
// 通常の使用
var ctx = new TickContext(DeltaTime: 0.016f);

// Unity の場合
var ctx = new TickContext(DeltaTime: Time.deltaTime, FrameIndex: Time.frameCount);

// デバッグモード
var ctx = new TickContext(DeltaTime: 0.016f, Debug: myDebugSink);
```

## コンポジットノード

### SelectorNode

子ノードを順に評価し、最初に Success または Running を返した子の結果を返します。Running の子を記憶し、次の Tick でその子から評価を再開します。

```csharp
public class SelectorNode : BtNode
{
    public SelectorNode(params BtNode[] children);
}
```

### SequenceNode

子ノードを順に評価し、全てが Success を返す場合に Success を返します。Running の子を記憶し、次の Tick でその子から評価を再開します。

```csharp
public class SequenceNode : BtNode
{
    public SequenceNode(params BtNode[] children);
}
```

### ParallelNode

全ての子ノードを毎 Tick 評価し、ポリシーに基づいて結果を決定します。結果確定時に全子ノード（Running の子を含む）を `Reset()` します。

```csharp
public class ParallelNode : BtNode
{
    public ParallelNode(ParallelPolicy policy, params BtNode[] children);
}
```

## デコレータノード

### GuardNode

条件が真の場合のみ子ノードを評価します。

```csharp
public class GuardNode : BtNode
{
    public GuardNode(Func<bool> condition, BtNode child);
}
```

### IfNode

条件に基づいて 2 つのノードを切り替えます。

```csharp
public class IfNode : BtNode
{
    public IfNode(Func<bool> condition, BtNode then, BtNode? @else = null);
}
```

### InvertNode

子ノードの結果を反転します。Success ↔ Failure。Running は変更なし。

```csharp
public class InvertNode : BtNode
{
    public InvertNode(BtNode child);
}
```

### RepeatNode

子ノードを指定回数繰り返します。

```csharp
public class RepeatNode : BtNode
{
    public RepeatNode(int count, BtNode child);
}
```

### TimeoutNode

子ノードの実行に時間制限を設けます。`TickContext.DeltaTime` を積算して時間を計測します。

```csharp
public class TimeoutNode : BtNode
{
    public TimeoutNode(float seconds, BtNode child);
}
```

### CooldownNode

子ノードが Success を返した後、指定時間は再実行を抑制します。

```csharp
public class CooldownNode : BtNode
{
    public CooldownNode(float seconds, BtNode child);
}
```

### WhileNode

条件が真の間、子ノードを繰り返し評価します。

```csharp
public class WhileNode : BtNode
{
    public WhileNode(Func<bool> condition, BtNode body);
}
```

## リアクティブノード

### ReactiveNode

毎 Tick 条件を再評価し、`false` になった場合は Running 中の子を `Abort()` して Failure を返します。

```csharp
public class ReactiveNode : BtNode
{
    public ReactiveNode(Func<bool> condition, BtNode body);
}
```

**Abort 動作:**
- 条件が `false` かつ子が Running → 子に `Abort()` を呼んで Failure を返す
- 条件が `true` → 子を Tick して結果を返す
- 子が完了（Success/Failure）→ 子を `Reset()` する

### ReactiveSelectorNode

毎 Tick 先頭から全子を再評価し、上位の子が復活したら下位の Running 中の子を `Abort()` します。

```csharp
public class ReactiveSelectorNode : BtNode
{
    public ReactiveSelectorNode(params BtNode[] children);
}
```

**通常の SelectorNode との違い:**
- `SelectorNode`: Running の子を記憶し、次 Tick でその子から再開
- `ReactiveSelectorNode`: 毎 Tick 先頭から評価。Running 子の集合を追跡し、上位の復活時に下位を `Abort()`

## リーフノード

### ConditionNode

関数を評価し、`true` → Success、`false` → Failure を返します。DSL の `check` はこのノードに変換されます。

```csharp
public class ConditionNode : BtNode
{
    public ConditionNode(Func<bool> condition, string? debugLabel = null);
}
```

`debugLabel` は `DebugLabel` プロパティとして公開され、デバッガのスナップショットに含まれます。Source Generator は条件式のテキスト表現（例: `".Health < 30"`）を自動的に設定します。

### ActionNode

関数を実行し、`BtStatus` をそのまま返します。DSL のアクション呼び出しで `BtStatus` を返すメソッドはこのノードに変換されます。

```csharp
public class ActionNode : BtNode
{
    public ActionNode(Func<BtStatus> action, string? debugLabel = null);
}
```

`debugLabel` は `DebugLabel` プロパティとして公開されます。Source Generator はメソッド名と引数のテキスト表現（例: `"Patrol()"`, `"Attack(.Target)"`）を自動的に設定します。

### サブツリー埋め込み（BtNode を返すメソッド）

DSL のアクション位置で `BtNode` を返すメソッドを呼び出すと、`ActionNode` でラップされず、ツリー構築時に 1 回だけ呼び出されて返された `BtNode` が直接埋め込まれます。ロジックの再利用やコードによるサブツリー構築に使用します。

```csharp
public partial class EnemyAI
{
    // BtNode を返す → サブツリーとして埋め込み
    public BtNode BuildCombatSubtree()
    {
        return new SequenceNode(
            new ConditionNode(() => this.IsEnemyVisible),
            new ActionNode(() => this.Attack()));
    }

    // BtStatus を返す → 毎 tick 実行されるアクション
    public BtStatus Patrol() => BtStatus.Success;
}
```

```lisp
(tree MainAI
  (select
    (.BuildCombatSubtree)   ; BtNode を返す → サブツリー埋め込み
    (.Patrol)))              ; BtStatus を返す → アクション
```

生成コード:
```csharp
// BtNode メソッド → this.BuildCombatSubtree()（直接呼び出し）
// BtStatus メソッド → new ActionNode(() => this.Patrol(), "Patrol()")
return new SelectorNode(
    this.BuildCombatSubtree(),
    new ActionNode(() => this.Patrol(), "Patrol()"));
```

## 非同期ノード

### AsyncActionNode

C# の async/await をビヘイビアツリーの tick 駆動モデルに橋渡しします。

```csharp
public class AsyncActionNode : BtNode
{
    public AsyncActionNode(Func<CancellationToken, IAsyncOperation> factory, string? debugLabel = null);
}
```

**動作:**
1. 最初の Tick: `factory` から `IAsyncOperation` を作成
2. 毎 Tick: 完了状態をポーリング
   - 未完了 → Running を返す
   - 完了 → 結果（Success/Failure）を返し、リソースをクリーンアップ
3. Abort: `CancellationToken` と `IAsyncOperation.Cancel()` の両方でキャンセル

```csharp
// 使用例
new AsyncActionNode(ct =>
{
    var task = ai.MoveToAsync(target, ct);
    return new ValueTaskAdapter(task);
})
```

## デバッグノード

### DebugProxyNode

実ノードをラップし、`IDebugSink` にイベントを通知するプロキシノードです。Source Generator がデバッグモード時に各ノードをラップします。

```csharp
public class DebugProxyNode : BtNode
{
    public DebugProxyNode(BtNode inner, int nodeId, IDebugSink sink);
}
```

**動作:**
1. `OnBeforeTick` を通知
2. `ShouldBreak` でブレークポイントを確認 → `WaitForContinue` で一時停止
3. 内部ノードを Tick
4. `OnAfterTick` を通知

`nodeId` は `IrNode.Id`（= `CstNode.Id`）と一致し、エディタ上の位置にマッピングできます。

## ParallelPolicy

`ParallelNode` の完了条件を定義する抽象レコード型です。

```csharp
public abstract record ParallelPolicy
{
    public sealed record Any : ParallelPolicy;
    public sealed record All : ParallelPolicy;
    public sealed record N(int Count) : ParallelPolicy;
}
```

| ポリシー | 説明 |
|---|---|
| `Any` | いずれか 1 つの子が Success → 全体 Success |
| `All` | 全ての子が Success → 全体 Success |
| `N(count)` | N 個以上の子が Success → 全体 Success |

## Blackboard

### IBtBlackboard

Blackboard（共有データストア）型のマーカーインターフェースです。

```csharp
namespace Crisp.Runtime;

public interface IBtBlackboard { }
```

DSL で `:blackboard` を宣言する型はこのインターフェースを実装する必要があります。

```csharp
public class WorldState : IBtBlackboard
{
    public bool IsAlarmTriggered { get; set; }
    public List<Entity> AllEnemies { get; set; }
}
```

## 非同期操作 (IAsyncOperation)

具体的な非同期ライブラリ（ValueTask, Task, UniTask 等）に依存しない抽象インターフェースです。

```csharp
namespace Crisp.Runtime;

public interface IAsyncOperation
{
    bool IsCompleted { get; }
    BtStatus GetResult();
    void Cancel();
}
```

| メンバー | 説明 |
|---|---|
| `IsCompleted` | 非同期操作が完了したかどうか。毎 Tick ポーリングされる |
| `GetResult()` | 完了後の結果を取得。`IsCompleted == true` の場合のみ呼び出し可能 |
| `Cancel()` | 実行中の操作をキャンセル。Abort 時に呼び出される |

**アダプタ実装例:**

```csharp
// ValueTask<BtStatus> アダプタ
public class ValueTaskAdapter : IAsyncOperation
{
    private readonly ValueTask<BtStatus> _task;
    public bool IsCompleted => _task.IsCompleted;
    public BtStatus GetResult() => _task.Result;
    public void Cancel() { /* CancellationToken 経由でキャンセル */ }
}
```

## デバッガ (IDebugSink)

ビヘイビアツリーのデバッグイベントを受信するインターフェースです。`TickContext.Debug` に設定して使用します。VSCode 拡張のビジュアルエディタではリアルタイムのデバッグ可視化に利用されます（詳細は [ビジュアルエディタ](visual-editor.md) を参照）。

```csharp
namespace Crisp.Runtime;

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

| メソッド | 説明 |
|---|---|
| `OnBeforeTick` | ノード評価開始前に呼び出される |
| `OnAfterTick` | ノード評価完了後に呼び出される |
| `OnExprEvaluated` | 式の評価値をキャプチャ（インスペクション用） |
| `OnAbort` | ノードが Abort された時に呼び出される |
| `ShouldBreak` | 指定ノードにブレークポイントが設定されているか確認 |
| `WaitForContinue` | ブレークポイントで一時停止し、続行指示を待つ |

`nodeId` は `IrNode.Id`（= `CstNode.Id`）と一致します。`IrNode.Origin`（TextSpan）を経由して、エディタ上の行・列に変換できます。

**使用例:**

```csharp
// デバッグモードでツリーを実行
var debugSink = new MyDebugSink();
var ctx = new TickContext(DeltaTime: 0.016f, Debug: debugSink);
tree.Tick(ctx);
```

## ランタイムデバッガ (BtDebugger)

`BtDebugger` はビヘイビアツリーの構造・実行状態・Blackboard 値のスナップショットを非破壊的に取得するデバッガです。ゲームコードから簡単にツリーの現在状態を確認できます。

### BtNodeSnapshot

ツリー内の 1 ノードの状態を表すスナップショットです。

```csharp
namespace Crisp.Runtime.Debug;

public sealed class BtNodeSnapshot
{
    public string NodeType { get; }
    public string? Label { get; }
    public BtStatus? LastStatus { get; }
    public IReadOnlyList<BtNodeSnapshot> Children { get; }
}
```

| プロパティ | 説明 |
|---|---|
| `NodeType` | ノード種別名（`BtNode.DebugNodeType` から取得） |
| `Label` | 人間可読なラベル（`BtNode.DebugLabel` から取得） |
| `LastStatus` | 最後の Tick 結果。未評価なら `null` |
| `Children` | 子ノードのスナップショット（`BtNode.DebugChildren` を再帰走査） |

### BtTreeSnapshot

ツリー全体と Blackboard の状態を表すスナップショットです。

```csharp
namespace Crisp.Runtime.Debug;

public sealed class BtTreeSnapshot
{
    public BtNodeSnapshot Root { get; }
    public IReadOnlyDictionary<string, object?>? BlackboardValues { get; }
}
```

| プロパティ | 説明 |
|---|---|
| `Root` | ルートノードのスナップショット |
| `BlackboardValues` | Blackboard の全パブリックプロパティのキー・値ペア。Blackboard 未設定の場合は `null` |

### BtDebugger

スナップショットを取得するデバッガ本体です。

```csharp
namespace Crisp.Runtime.Debug;

public sealed class BtDebugger
{
    public BtDebugger(BtNode root, object? blackboard = null);
    public BtTreeSnapshot Capture();
}
```

**コンストラクタ引数:**

| パラメータ | 説明 |
|---|---|
| `root` | デバッグ対象のルートノード |
| `blackboard` | Blackboard オブジェクト（オプション）。指定するとパブリックプロパティがスナップショットに含まれる |

`Capture()` は呼び出し時点のツリー状態を非破壊的にキャプチャします。`BtNode.DebugChildren` を再帰的に走査し、各ノードの `DebugNodeType`・`DebugLabel`・`LastStatus` をコピーします。Blackboard が指定されている場合はリフレクションでパブリックプロパティを読み取ります。

**使用例:**

```csharp
using Crisp.Runtime.Debug;

// ツリーを構築・実行
var ai = new EnemyAI { Health = 20 };
var tree = ai.BuildTree();
tree.Tick(new TickContext(DeltaTime: 0.016f));

// スナップショット取得
var debugger = new BtDebugger(tree);
var snapshot = debugger.Capture();

// ノード状態を確認
Console.WriteLine(snapshot.Root.NodeType);    // "selector"
Console.WriteLine(snapshot.Root.LastStatus);  // Success
Console.WriteLine(snapshot.Root.Children[0].Children[0].Label); // ".Health < 30"
```

**Blackboard 付きの使用例:**

```csharp
var blackboard = new WorldState { IsAlarmTriggered = true, GlobalThreatLevel = 0.8f };
var debugger = new BtDebugger(tree, blackboard);
var snapshot = debugger.Capture();

// Blackboard 値を確認
foreach (var (key, value) in snapshot.BlackboardValues!)
{
    Console.WriteLine($"  {key} = {value}");
}
// IsAlarmTriggered = True
// GlobalThreatLevel = 0.8
```

### BtDebugFormatter

スナップショットを人間可読なテキスト形式にフォーマットするユーティリティです。

```csharp
namespace Crisp.Runtime.Debug;

public static class BtDebugFormatter
{
    public static string Format(BtTreeSnapshot snapshot);
    public static string Format(BtNodeSnapshot node);
}
```

**出力例:**

```
selector [Success]
+-- sequence [Success]
|   +-- check ".Health < 30" [Success]
|   \-- action "Flee()" [Success]
+-- sequence [-]
|   +-- check ".IsEnemyVisible" [-]
|   \-- action "Attack()" [-]
\-- action "Patrol()" [-]

Blackboard (WorldState):
  IsAlarmTriggered = True
  GlobalThreatLevel = 0.8
```

**フォーマット規則:**

| 要素 | 表示 |
|---|---|
| ノード種別 | `selector`, `sequence`, `action` 等 |
| ラベル | `"ラベル文字列"`（ダブルクォート囲み）。ラベルなしなら省略 |
| 実行状態 | `[Success]`, `[Failure]`, `[Running]`, `[-]`（未評価） |
| ツリー構造 | `+--`（中間子）、`\--`（末尾子）、`|`（継続線） |
| Blackboard | `Blackboard (型名):` ヘッダーの後にプロパティ一覧 |

**使用シーン:**

- ゲーム実行中のコンソール出力で AI のデバッグ
- テスト時のアサーション用にツリー状態をキャプチャ
- ログファイルへの AI 状態出力
- カスタムデバッグ UI の構築（`BtNodeSnapshot` を直接走査）

## AOT サポート

NativeAOT / IL2CPP 環境でリフレクション不要のメンバーアクセスを提供します。

### IAccessorTable\<T\>

メンバーアクセサとメソッドインボーカのテーブルインターフェースです。

```csharp
namespace Crisp.Runtime;

public interface IAccessorTable<T>
{
    IReadOnlyDictionary<string, Func<T, object?>> MemberAccessors { get; }
    IReadOnlyDictionary<string, Func<T, object?[], object?>> MethodInvokers { get; }
    bool HasMember(string memberName);
    bool HasMethod(string methodName);
    object? GetMember(T instance, string memberName);
    object? InvokeMethod(T instance, string methodName, params object?[] args);
}
```

### AccessorTable\<T\>

`IAccessorTable<T>` の標準実装。Source Generator が生成するコードから使用されます。

```csharp
public sealed class AccessorTable<T> : IAccessorTable<T>
{
    public AccessorTable(
        Dictionary<string, Func<T, object?>> memberAccessors,
        Dictionary<string, Func<T, object?[], object?>> methodInvokers);
}
```

### CrispAccessibleAttribute

Source Generator にアクセサテーブルの生成を指示する属性です。`[BehaviorTree]` を持つクラスには自動適用されます。チェーンアクセス先の型など、別アセンブリの型に対して明示的に指定できます。

```csharp
[CrispAccessible]
public class Position
{
    public float X { get; set; }
    public float Y { get; set; }
}
```

### 生成されるコード

Source Generator は以下のようなコードを自動生成します:

```csharp
partial class EnemyAI
{
    private static readonly Dictionary<string, Func<EnemyAI, object?>> _crispMemberAccessors = new()
    {
        ["Health"] = ctx => ctx.Health,
        ["Target"] = ctx => ctx.Target,
    };

    private static readonly Dictionary<string, Func<EnemyAI, object?[], object?>> _crispMethodInvokers = new()
    {
        ["Attack"] = (ctx, args) => ctx.Attack(),
        ["Flee"] = (ctx, args) => { ctx.Flee(); return null; },
    };

    public static IAccessorTable<EnemyAI> GetAccessorTable()
        => new AccessorTable<EnemyAI>(_crispMemberAccessors, _crispMethodInvokers);
}
```

## 属性

### BehaviorTreeAttribute

メソッドにビヘイビアツリーの DSL ソースを関連付けます。

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class BehaviorTreeAttribute : Attribute
{
    public string Source { get; }
    public BehaviorTreeAttribute(string source);
}
```

**制約:**
- メソッドは `partial` でなければならない
- 戻り値は `BtNode` でなければならない
- メソッドは引数を取れない（Blackboard 使用時を除く）
- クラスも `partial` でなければならない

**使用方法:**

```csharp
// インライン S 式
[BehaviorTree("(tree T (.Patrol))")]
public partial BtNode BuildTree();

// 外部ファイル参照
[BehaviorTree("Combat.crisp")]
public partial BtNode BuildCombat();
```

### BtActionAttribute

メソッドをビヘイビアツリーのアクションとしてマークします（ドキュメンテーション目的、必須ではない）。

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class BtActionAttribute : Attribute { }
```

## インタプリタ (Hot Reload)

実行中に `.crisp` を変更し、ゲームを再起動せずに AI を調整するための API です。

### CrispRuntime

```csharp
namespace Crisp.Syntax.Interpreter;

public static class CrispRuntime
{
    /// <summary>DSL ソースをコンパイルし IR ツリーを返す。</summary>
    public static ImmutableArray<IrTree> Compile(string source);

    /// <summary>DSL ソースをコンパイルし、Evaluator とペアで返す。</summary>
    public static (IrTree Tree, Evaluator Evaluator) CompileAndBuild(
        string source, object context, object? blackboard = null);
}
```

**使用例:**

```csharp
// コンパイルとツリー構築
var (tree, evaluator) = CrispRuntime.CompileAndBuild(source, myContext);

// 毎フレーム Tick
var ctx = new TickContext(DeltaTime: 0.016f);
var status = evaluator.Tick(tree, ctx);

// ホットリロード: ソースが変更されたら再コンパイル
var (newTree, newEvaluator) = CrispRuntime.CompileAndBuild(newSource, myContext);
```

**注意:**
- Hot Reload 時、Running 中のノードの状態は**引き継がない**。新しいツリーは初期状態から実行される
- コンパイルエラーがある場合は `CrispCompilationException` がスローされる
- インタプリタはリフレクションを使用するため、AOT 環境では Source Generator バックエンドを使用すること
