# Crisp 設計要点まとめ（実装前チェック用）v2

本ドキュメントは **crisp-architecture-detailed.md** を踏まえ、
実装前に合意しておくべき「境界・判断・落とし穴」を整理したチェック用ドキュメントである。

---

## 1. ノード vs 式（Node / Expr）の判定ルール

### 基本方針

- **CST / Parser 段階では区別しない**
- **AST Lowering 時に文脈で判定する**
- Lowering関数に `ParseContext` enum（`NodePosition` / `ExprPosition`）を持たせる

### 判定規則（Lowering）

| 位置 | CST | AST変換結果 |
|---|---|---|
| Tree / Composite / Decorator の子 | `CstCall` | `AstActionCall` |
| `check` / 条件式内部 | `CstCall` | `AstCallExpr` |
| 式の引数 | `CstCall` | `AstCallExpr` |
| Node位置で括弧付き引数なし | `CstCall(args=[])` | `AstActionCall(Foo, args=[])` |
| Node位置で括弧なしMemberAccess | `CstMemberAccess` | `AstActionCall(Foo, args=[])` |

### 注意点

```
;; ノード位置 → ActionCall
(.Attack .Target)
(.Patrol)
.Patrol            ;; 括弧なしもActionCall

;; 式位置 → CallExpr
(check (.DistanceTo .Target))
(< (.DistanceTo .Target) 5.0)
```

**CSTの形が異なる2パス**（`CstCall(args=[])` と `CstMemberAccess`）が
どちらも `AstActionCall` になるため、Lowering側に両方のハンドリングが必要。

---

## 2. 負リテラルと単項マイナスの扱い

### 構文上のルール

| 表記 | 意味 | AST |
|---|---|---|
| `-3` | 負の数値リテラル | `AstLiteralExpr(-3)` |
| `(- x)` | 単項マイナス | `AstUnaryExpr(Negate, x)` |
| `(- 3 x)` | 二項引き算 | `AstBinaryExpr(Sub, 3, x)` |

### Lexer / Parser 方針

- `-3` は **Lexer段階で負の数値トークンに含める**
- `(` 直後の `-` は Parserが文脈判定:
  - 後続が2要素以上 → 二項演算 `(- a b)`
  - 後続が1要素 → 単項マイナス `(- a)`
- `-3` を `(- 3)` に desugar **しない**

### 理由

desugarすると以下が不自然になる:

- フォーマッタの出力
- エラーメッセージのスパン
- スナップショットテストの可読性

---

## 3. MemberAccess の設計（LSP最重要）

### CST

- `.Foo.Bar.Baz` は **単一 `CstMemberAccess`** ノード
- 内部に以下を保持:
  - `DotTokens: Token[]` — 各 `.` トークン（スパン付き）
  - `Segments: Token[]` — 各識別子トークン（スパン付き）
  - `DotTokens` と `Segments` は交互に並ぶ

### AST

- `AstMemberAccessExpr`
  - `Path: MemberPath` — `string[]` + `ResolvedSegments: ISymbol[]?`
  - `CstOrigin: CstMemberAccess`

### LSP観点の必須要件

1. **カーソル位置 → セグメントIndex逆引き**: ホバー・補完で「どのセグメントの上にいるか」を特定する必要がある
2. **各セグメントの個別TextSpan**: `CstMemberAccess.Segments[i].Span` で取得可能にする
3. **部分的な解決**: `.Foo.Bar.???` で `Bar` までは解決済み、`???` は補完候補を出す

### 実装上の注意

- Lexerが `.Foo.Bar.Baz` を単一トークンとして吐くか、`.` + `Foo` + `.` + `Bar` のように分割するかで
  Parserの実装が変わる
- **推奨**: Lexerは `.` 始まりのチェーンを **1トークン** `MemberAccess` として認識し、
  `CstMemberAccess` のコンストラクタ内でセグメント分割する

---

## 4. 型検査フェーズの責務分離

### Pass構成

```
Pass 1: 名前解決（Resolution）
  └ Roslyn ISymbol を AST の ResolvedSymbol に付与
  └ ケバブ→C#名変換はここで実行
  └ 見つからなければ BS0001

Pass 2: 型推論（Inference）
  └ Bottom-up で全 AstExpr に CrispType を付与
  └ エラー時は ErrorType を付与（以降のカスケード抑止）

Pass 3: 型検査（Check）
  └ ノード制約チェック（check→bool、action→BtStatus等）
  └ 引数の型互換チェック
  └ ErrorType を含む式はスキップ（二重エラー防止）
```

### ノード固有制約

| ノード | 制約 | 違反時のDiagnostic |
|---|---|---|
| `check` | exprがbool | BS0007 |
| `guard` | conditionがbool | BS0007 |
| `if` | conditionがbool | BS0007 |
| `while` | conditionがbool | BS0007 |
| `action_call` | 戻り値がBtStatus | BS0008 |
| `repeat` | 引数が正の整数リテラル | BS0013 |
| `timeout` / `cooldown` | 引数が正の数値リテラル | BS0014 |
| `select` / `seq` / `parallel` | 子が2つ以上 | BS0015 |

### ErrorTypeの伝播ルール

```
(< .UnknownMember 30)
     ↑ BS0001報告、ErrorType付与
(< ErrorType 30)
   ↑ ErrorTypeを含むので追加エラーなし、結果もErrorType
(check ErrorType)
   ↑ ErrorTypeなのでBS0007を報告しない
```

---

## 5. Diagnostics 設計指針

### 方針

- **1エラー = 1責務**: 1つのDiagnosticが1つの問題のみを指す
- **カスケード抑止**: ErrorTypeを含む式には追加エラーを出さない
- **ASTノード + CstOrigin紐付け**: 全DiagnosticにTextSpanが付く

### Diagnostic一覧（確定分）

#### 構文エラー (BS00xx)

| Code | Severity | メッセージ |
|---|---|---|
| BS0009 | Error | Parse error at {line}:{col}: {detail} |
| BS0016 | Error | Expected '{expected}', found '{actual}' |
| BS0017 | Error | Unterminated string literal |
| BS0018 | Error | Unmatched '(' |
| BS0019 | Error | Unexpected ')' |
| BS0020 | Warning | Unused tree '{name}' |

#### 名前解決エラー (BS01xx)

| Code | Severity | メッセージ |
|---|---|---|
| BS0001 | Error | Member '{name}' not found on type '{type}' |
| BS0011 | Error | External file '{path}' not found |
| BS0012 | Error | Overload resolution ambiguous for '{name}' with arguments ({types}) |
| BS0101 | Error | Context type '{type}' does not implement IBtContext |
| BS0102 | Error | Enum type '{name}' not found |
| BS0103 | Error | Enum member '{type}.{member}' not found |

#### 型エラー (BS02xx)

| Code | Severity | メッセージ |
|---|---|---|
| BS0002 | Error | Type mismatch: expected '{expected}', got '{actual}' |
| BS0003 | Error | Cannot compare '{typeA}' with '{typeB}' |
| BS0004 | Error | Arithmetic operator '{op}' not applicable to '{type}' |
| BS0005 | Error | Method '{name}' expects {expected} arguments, got {actual} |
| BS0006 | Error | Argument {index} of '{method}': expected '{expected}', got '{actual}' |
| BS0007 | Error | Expression in '{node}' must be bool, got '{type}' |
| BS0008 | Error | Action method '{name}' must return BtStatus |

#### 構造エラー (BS03xx)

| Code | Severity | メッセージ |
|---|---|---|
| BS0013 | Error | 'repeat' count must be a positive integer literal |
| BS0014 | Error | '{node}' duration must be a positive number literal |
| BS0015 | Error | '{node}' requires at least {n} children |
| BS0301 | Warning | Unreachable node after unconditional Success |
| BS0302 | Warning | 'if' without else branch always returns Failure on false |

#### その他 (BS09xx)

| Code | Severity | メッセージ |
|---|---|---|
| BS0010 | Warning | Member '{name}' is obsolete: {reason} |
| BS0901 | Error | Internal compiler error: {detail} |

---

## 6. IR設計の意図（Backendを楽にする）

### ポイント

- Roslyn Symbol は全て `Ref`（文字列ベース）に変換
- **暗黙型変換は IR で明示化**（`IrConvert` ノード挿入）
- バックエンドは型推論・型変換を一切考えなくてよい

### IrConvert の挿入例

```
;; DSL
(< .Health 30)

;; .Health: float, 30: int → intをfloatに暗黙変換

;; IR
(ir-binary-op :lt
  (ir-member-load ("Health") :type float)
  (ir-convert
    (ir-literal 30 :int)
    :to float))
```

### バックエンドごとの変換

| IR | C# Emitter | Interpreter |
|---|---|---|
| `IrConvert(expr, float)` | `(float)(expr)` | `Convert.ChangeType(val, typeof(float))` |
| `IrMemberLoad(chain)` | `this.chain[0].chain[1]...` | リフレクションで辿る |
| `IrAction(method, args)` | `this.Method(args)` | `MethodInfo.Invoke` |

---

## 7. Query DB 粒度の考え方

### 初期実装（file粒度）

```
source_text(file)  →  lex(file)  →  parse(file)  →  lower(file)
                                                        ↓
                                          resolve(file) ← context_type(file)
                                                        ↓
                                          type_check(file) ← compilation(file)
                                                        ↓
                                                    emit_ir(file)
```

### 増分の恩恵

| 消費者 | 恩恵 |
|---|---|
| Source Generator | 薄い（ビルド毎に新規DB） |
| LSP | 大きい（ファイル変更時に変更分のみ再計算） |

### 将来拡張余地

- expr単位のqueryに細分化（completion/hover最適化）
- 複数ファイル間のクロスリファレンスquery（サブツリー参照）

**最初はfile粒度で正解。困ってから細かくする。**

---

## 8. 既知の落とし穴

### 8.1 Source Generatorでの外部ファイルパス解決

`[BehaviorTree("EnemyCombat.crisp")]` の相対パスの基点が未定義。

| 選択肢 | 利点 | 欠点 |
|---|---|---|
| csprojからの相対パス | Unityの慣習に近い | ソースファイルの位置と離れる |
| ソースファイルからの相対パス | 直感的 | Source GeneratorでCallerFilePathが使えない |
| AdditionalFilesとのファイル名マッチ | 確実 | ユーザーがcsprojに登録する手間 |

→ **未決定事項U7として追加。初期実装はAdditionalFilesマッチが安全。**

### 8.2 Parallel内のReset伝播

```
(parallel :any
  (.LongRunningAction)    ;; Running
  (.QuickCheck))          ;; Success → Parallel全体がSuccess
```

この場合 `.LongRunningAction` は中断されるが:
- 次tickで再評価されるのか？
- Reset() が呼ばれるのか？
- 内部状態はどうなるのか？

→ **U4の一部。テストケースで挙動を固定する必要あり。**

### 8.3 ケバブケース変換の衝突

```csharp
public float Health { get; set; }   // PascalCase
private float _health;              // _camelCase
```

DSLで `.health` と書くと両方にマッチする。
優先順位（完全一致 > PascalCase > camelCase > _camelCase > snake_case）は定義済みだが:

- **複数マッチ時に警告を出すべきか？**
- Diagnostic候補: `BS0104 Warning: Ambiguous member resolution for '{name}': matched '{a}' and '{b}', using '{a}'`

→ **U6の一部。警告を出すのが安全。**

### 8.4 Action引数なし vs 引数ありプロパティ

```csharp
[BtAction]
public BtStatus Patrol() => ...;

public Func<BtStatus> PatrolFunc { get; set; }  // 呼び出し可能プロパティ
```

DSLで `(.Patrol)` と書いた場合、メソッド優先かプロパティ優先か。

→ **メソッドを優先する。プロパティが呼び出し可能であっても、DSLからの直接呼び出しは初期バージョンではサポートしない。**

### 8.5 null安全性

```
(check (= .Target null))
```

`.Target` がnon-nullable参照型の場合、この比較は:
- 常にfalseなので警告を出すべきか？
- C#のnullable annotation (#nullable enable) を尊重するか？

→ **初期バージョンでは nullable annotation を無視し、nullとの比較は常に許可する。将来的にnullable対応を追加（F13候補）。**

### 8.6 thisのメンバーと予約語の衝突

```csharp
public partial class AI : IBtContext
{
    public bool Select { get; set; }  // 予約語と同名

    [BehaviorTree(@"(tree T (check .Select))")]
    public partial BtNode Build();
}
```

`.Select` はメンバーアクセスなので予約語と衝突しない（ドットプレフィクスで区別）。
ただし以下は問題:

```
(tree T (select ...))     ;; 予約語のselect
```

→ **ドットなし = 予約語解釈、ドットあり = メンバーアクセス。衝突は発生しない。テストで確認。**

---

## 9. 未決定事項一覧（更新版）

| # | 項目 | 選択肢 | 優先度 |
|---|---|---|---|
| U1 | DeltaTimeの注入方法 | A: `Tick(TickContext)` / B: コンストラクタ注入 / C: static | **実装前に決定必須** |
| U2 | Running状態の記憶方式 | A: ノード内index / B: 外部Cursor / C: Zipper | Phase 3で決定可 |
| U3 | `IBtContext`の要否 | A: 必須 / B: 属性のみ / C: 不要 | Phase 2で決定可 |
| U4 | Parallel内Running子の扱いとReset | A: 全子毎tick / B: Running子のみ | **実装前に決定必須** |
| U5 | サブツリー参照の構文と型互換 | A: `(ref)` / B: `(include)` / C: 両方 | 将来拡張、初期はなし |
| U6 | ケバブ変換衝突時の警告 | A: エラー / B: 警告+優先順位採用 | Phase 2で決定可 |
| U7 | 外部.crispファイルパス解決 | A: csproj相対 / B: ソース相対 / C: AdditionalFiles | **実装前に決定必須** |

---

## 10. 実装開始前チェックリスト

### 設計決定（実装前に確定）

- [x] **U1**: `Tick` のシグネチャ確定 — `Tick(TickContext ctx)` record struct でアロケーションフリー
- [x] **U4**: Parallel + Reset のセマンティクス確定 — 確定時に全子 Reset（Running 含む）
- [x] **U7**: 外部ファイルパス解決方式確定 — AdditionalFiles のファイル名マッチ（初期実装）

### テスト基盤

- [x] スナップショットテスト用サンプルDSLの固定（最低5パターン） — 統合テスト Pattern 1-4 + 5b
- [x] Node / Expr 判定の境界テストケース作成 — Lowering テスト済み
- [x] 負リテラル vs 単項マイナスのテストケース作成 — Lexer/Parser テスト済み
- [x] ケバブケース変換のテストケース作成（衝突パターン含む） — NameConversion テスト済み
- [x] パーサーのエラー回復テストケース作成 — Parser エラーリカバリテスト済み

### 構造確認

- [x] MemberAccessのspan → segment逆引き設計確認 — CstMemberAccess.Segments で実装済み
- [x] ErrorTypeによるカスケード抑止の動作確認 — TypeChecker + 統合テスト検証済み
- [x] IRにIrConvertが正しく挿入されることの確認 — AstToIrLowering テスト検証済み
- [x] CSTラウンドトリップ（`source == cst.ToFullString()`）の確認 — PropertyBasedRoundTripTests で検証済み

---

## 11. 次のステップ

全項目の設計確定・テスト基盤構築・構造確認が完了。
Phase 1d（Formatter）および Phase 3（IR, C# Backend, Interpreter）の実装へ進む。
