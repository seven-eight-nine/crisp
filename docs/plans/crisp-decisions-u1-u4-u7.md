# Crisp 未決定事項 決定記録（U1 / U4 / U7）

> **Status**: 合意済み・確定
> **Date**: 2026-02-16

本ドキュメントは **crisp-design-checklist-v2.md** で「実装前に決定必須」とされた
3つの未決定事項の決定内容を記録するものである。

---

## U1: Tick / DeltaTime の注入方法

### 決定

```csharp
BtStatus Tick(TickContext ctx)
```

### TickContext の定義

```csharp
public readonly record struct TickContext(
    float DeltaTime,
    int FrameIndex = 0);
    // 将来拡張:
    // IRandom Random
    // IDebugSink Debug
```

`record struct` とすることでアロケーションフリー。

### 責務

| フィールド | 用途 | 必須 |
|---|---|---|
| `DeltaTime` | TimeoutNode, CooldownNode 等の時間計測 | ✓ |
| `FrameIndex` | デバッグ・ログ用 | 任意 |

### 理由

- ランタイム・Interpreter・Generated Code で **完全に同一API**
- Unity依存を持ち込まない（`Time.deltaTime` をユーザーが渡す）
- 将来パラメータ追加時に破壊的変更にならない（record struct のフィールド追加）

### 却下した案

| 案 | 却下理由 |
|---|---|
| `Tick()` 引数なし | DeltaTime注入が拡張不能 |
| static DeltaTime | テスト不能・並列実行不可 |
| コンストラクタ注入 | フレーム毎に値が変化するものに不向き |

### ランタイムへの影響

全 BtNode のシグネチャが `Tick(TickContext ctx)` になる:

```csharp
public abstract class BtNode
{
    public abstract BtStatus Tick(TickContext ctx);
    public virtual void Reset() { }
}
```

ユーザーの使用例:

```csharp
// Unity MonoBehaviour
void Update()
{
    var ctx = new TickContext(Time.deltaTime, Time.frameCount);
    _tree.Tick(ctx);
}
```

---

## U4: Parallel 内の Running 子ノードと Reset セマンティクス

### 決定

- Parallel 自身が **Running を返す**場合: 子ノードの状態を **維持して続行**
- Parallel 自身が **Success / Failure を返す**場合: **全子を Reset（Running 含む）**

### 再突入問題の解決

Parallel が Success/Failure で確定した後、親（selector等）が次 tick で
同じ Parallel を再評価する場合:

```
Tick #1:
  (parallel :any A B)
    A → Running
    B → Success
    Parallel → Success
    → 全子 Reset（A も B も）

Tick #2（親が再評価）:
    A → 初期状態から評価（前回の Running は引き継がない）
    B → 初期状態から評価
```

**「Parallel が確定した = このサブツリーの仕事が終わった」** という解釈。
状態が漏れない。

### 各ポリシーの確定・Reset マトリクス

| 子の結果 | :any | :all |
|---|---|---|
| Running + Success | **Success → 全子Reset** | Running（継続） |
| Running + Failure | Running（継続） | **Failure → 全子Reset** |
| Running + Running | Running（継続） | Running（継続） |
| Success + Success | **Success → 全子Reset** | **Success → 全子Reset** |
| Failure + Failure | **Failure → 全子Reset** | **Failure → 全子Reset** |
| Success + Failure | **Success → 全子Reset** | **Failure → 全子Reset** |

`:n` ポリシー:
- Success数 >= N → **Success → 全子Reset**
- Failure数 > (子の数 - N) → **Failure → 全子Reset**（成功の可能性が消滅）
- それ以外 → Running（継続）

### 理由

- 長時間 Action（移動・アニメーション等）が不要になったら確実に中断される
- 再突入時の状態漏れが原理的に起きない
- 仕様が単純で、テストで固定しやすい

### 実装への影響

```csharp
public class ParallelNode : BtNode
{
    public override BtStatus Tick(TickContext ctx)
    {
        // ... 子を全て評価 ...

        var result = EvaluatePolicy();

        // 確定時は全子Reset
        if (result != BtStatus.Running)
        {
            foreach (var child in _children)
                child.Reset();
        }

        return result;
    }
}
```

---

## U7: 外部 `.crisp` ファイルの解決方法

### 決定（初期実装）

**MSBuild AdditionalFiles に含まれる `.crisp` ファイルとのファイル名マッチ**

### マッチングルール

```
[BehaviorTree("EnemyCombat.crisp")]

1. Source Generator が AdditionalFiles を列挙
2. ファイル名（パス末尾）が "EnemyCombat.crisp" と一致するものを探す
3. 一致が1件 → そのファイルの内容を使用
4. 一致が0件 → BS0011: External file 'EnemyCombat.crisp' not found
5. 一致が2件以上 → BS0011: Ambiguous match for 'EnemyCombat.crisp'
```

### ユーザーの設定方法

csproj に以下を追加:

```xml
<ItemGroup>
  <AdditionalFiles Include="Assets/BehaviorTrees/**/*.crisp" />
</ItemGroup>
```

### 理由

- Source Generator で最も安全・確実なファイルアクセス方法
- Unity / .NET SDK 両対応
- ビルド環境依存の差異が出ない（CI/CDでも同じ動作）
- CallerFilePath 等のハックが不要

### 将来拡張候補（非初期）

| 方式 | 状況 |
|---|---|
| ソースファイルからの相対パス | ユーザー要望があれば |
| csproj からの相対パス | Unity のアセットパス慣習に合わせたい場合 |
| 明示的な絶対パス指定 | 特殊なビルド構成向け |

---

## 残りの未決定事項（Phase 2以降で決定可）

以下はまだ決定していないが、Phase 1の実装には影響しない:

| # | 項目 | 決定タイミング |
|---|---|---|
| U2 | Running状態の記憶方式 | Phase 3（ランタイム実装時） |
| U3 | `IBtContext` の要否 | Phase 2（セマンティクス実装時） |
| U5 | サブツリー参照の構文 | 将来拡張（初期はなし） |
| U6 | ケバブ変換衝突時の警告 | Phase 2（名前解決実装時） |
