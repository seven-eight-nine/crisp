# 診断メッセージ一覧

Crisp コンパイラは、構文エラー・名前解決エラー・型エラー・構造エラー・警告の 5 カテゴリで合計 47 種類の診断メッセージを報告します。

## 目次

- [概要](#概要)
- [構文エラー (BS0009, BS0016-BS0020)](#構文エラー)
- [名前解決エラー (BS0001, BS0011-BS0012, BS0101-BS0104)](#名前解決エラー)
- [型エラー (BS0002-BS0008, BS0021-BS0022)](#型エラー)
- [構造エラー (BS0013-BS0015)](#構造エラー)
- [Defdec エラー (BS0023-BS0027)](#defdec-エラー)
- [Blackboard エラー (BS0028-BS0031)](#blackboard-エラー)
- [マクロエラー (BS0031-BS0035)](#マクロエラー)
- [マルチツリーエラー (BS0036-BS0040)](#マルチツリーエラー)
- [ジェネリクスエラー (BS0041-BS0043)](#ジェネリクスエラー)
- [Nullable 警告 (BS0044-BS0047)](#nullable-警告)
- [構造警告 (BS0301-BS0302)](#構造警告)
- [その他 (BS0010, BS0901)](#その他)
- [カスケード抑制](#カスケード抑制)

## 概要

### 重大度

| 重大度 | 説明 | コード生成 |
|---|---|---|
| Error | 致命的な問題。修正が必要 | 阻止される |
| Warning | 潜在的な問題。修正を推奨 | 阻止されない |
| Info | 情報提供。改善のヒント | 阻止されない |

---

## 構文エラー

### BS0009: Parse error

**重大度:** Error
**メッセージ:** `Parse error at {行}:{列}: {詳細}`

パーサーが予期しない構文を検出しました。

```lisp
(tree T (select .Patrol)
;; → BS0009: Parse error
```

**対処法:** 括弧の対応を確認し、構文を修正してください。

### BS0016: Unexpected token

**重大度:** Error
**メッセージ:** `Expected '{期待トークン}', found '{実際のトークン}'`

特定のトークンが期待される位置に異なるトークンが出現しました。

```lisp
(tree (select .Patrol))
;; → BS0016: Expected 'identifier', found '('
```

**対処法:** 期待されるトークンをメッセージに従って追加してください。

### BS0017: Unterminated string

**重大度:** Error
**メッセージ:** `Unterminated string literal`

文字列リテラルが閉じクォートなしで終端しました。

```lisp
(.Say "hello)
;; → BS0017: Unterminated string literal
```

**対処法:** 文字列リテラルの末尾に `"` を追加してください。

### BS0018: Unmatched paren

**重大度:** Error
**メッセージ:** `Unmatched '('`

開き括弧に対応する閉じ括弧がありません。

**対処法:** 括弧の対応を確認し、不足している `)` を追加してください。

### BS0019: Unexpected close paren

**重大度:** Error
**メッセージ:** `Unexpected ')'`

対応する開き括弧のない閉じ括弧が出現しました。

**対処法:** 余分な `)` を削除してください。

### BS0020: Unused tree

**重大度:** Warning
**メッセージ:** `Unused tree '{ツリー名}'`

ファイル内に定義されたツリーが `ref` で参照されておらず、Source Generator でも使用されていません。

**対処法:** 不要なツリー定義を削除するか、`ref` で参照してください。

---

## 名前解決エラー

### BS0001: Member not found

**重大度:** Error
**メッセージ:** `Member '{メンバー名}' not found on type '{型名}'`

DSL で参照されたメンバーがコンテキスト型に存在しません。

```lisp
(check .Unknown)
;; → BS0001: Member 'Unknown' not found on type 'Game.AI.EnemyAI'
```

**対処法:**
- メンバー名のスペルを確認してください
- ケバブケースを使用している場合、変換先の C# メンバーが存在するか確認してください

### BS0011: External file not found

**重大度:** Error
**メッセージ:** `External file '{ファイル名}' not found`

`[BehaviorTree]` で指定された外部ファイルが見つかりません。

**対処法:** `.csproj` の `<AdditionalFiles>` にファイルを登録してください。

### BS0012: Ambiguous overload

**重大度:** Error
**メッセージ:** `Overload resolution ambiguous for '{メソッド名}' with arguments ({引数型})`

同じ引数の数を持つ複数のオーバーロードが見つかり、どれを使うか決定できません。

**対処法:** オーバーロードを削除するか、引数の型で区別できるようにしてください。

### BS0101: Missing IBtContext

**重大度:** Error
**メッセージ:** `Context type '{型名}' does not implement IBtContext`

コンテキスト型が要求されるインターフェースを実装していません。

### BS0102: Enum type not found

**重大度:** Error
**メッセージ:** `Enum type '{型名}' not found`

列挙型リテラルで指定された型が見つかりません。

```lisp
(check (= .State ::AIStatus.Idle))
;; → BS0102: Enum type 'AIStatus' not found
```

### BS0103: Enum member not found

**重大度:** Error
**メッセージ:** `Enum member '{型名}.{メンバー名}' not found`

列挙型は見つかりましたが、指定されたメンバーが存在しません。

### BS0104: Ambiguous member name

**重大度:** Warning
**メッセージ:** `Ambiguous member resolution for '{DSL名}': matched '{候補A}' and '{候補B}', using '{候補A}'`

DSL の名前が複数の異なる C# メンバーにマッチしました。優先順位の高い候補が使用されます。

**対処法:** あいまいさを避けるため、DSL で正確な名前を使用してください。

---

## 型エラー

### BS0002: Type mismatch

**重大度:** Error
**メッセージ:** `Type mismatch: expected '{期待型}', got '{実際の型}'`

式の型が期待される型と一致しません。

### BS0003: Cannot compare

**重大度:** Error
**メッセージ:** `Cannot compare '{型A}' with '{型B}'`

比較演算子の左右の型が比較できません。

```lisp
(check (< .Name 42))
;; → BS0003: Cannot compare 'string' with 'int'
```

### BS0004: Invalid arithmetic

**重大度:** Error
**メッセージ:** `Arithmetic operator '{演算子}' not applicable to '{型}'`

算術演算子が適用できない型に対して使用されました。算術演算子は `int` と `float` にのみ使用できます。

### BS0005: Argument count mismatch

**重大度:** Error
**メッセージ:** `Method '{メソッド名}' expects {期待数} arguments, got {実際の数}`

メソッド呼び出しの引数の数が一致しません。

### BS0006: Argument type mismatch

**重大度:** Error
**メッセージ:** `Argument {番号} of '{メソッド名}': expected '{期待型}', got '{実際の型}'`

メソッド呼び出しの引数の型が一致しません。

### BS0007: Bool required

**重大度:** Error
**メッセージ:** `Expression in '{コンテキスト}' must be bool, got '{実際の型}'`

`bool` 型が要求される位置（`check`, `guard`, `if`, `while`）に異なる型の式があります。

```lisp
(check .Health)
;; → BS0007: Expression in 'check' must be bool, got 'int'
```

**対処法:** 比較演算子を使用するか、`bool` 型のプロパティを参照してください。

### BS0008: BtStatus or BtNode required

**重大度:** Error
**メッセージ:** `Action method '{メソッド名}' must return BtStatus or BtNode`

アクション位置で呼び出されたメソッドが `BtStatus` も `BtNode` も返しません。アクション位置のメソッドは `BtStatus`（毎 tick 実行されるアクション）または `BtNode`（ビルド時にサブツリーとして埋め込み）のいずれかを返す必要があります。式位置（`check` 内等）では任意の戻り値型を使用できます。

**対処法:**
- アクションメソッドの戻り値型を `BtStatus` または `BtNode` に変更してください
- 式としてメソッドを使用したい場合は、`check` 内など式位置で呼び出してください

### BS0021: Reactive condition type

**重大度:** Error
**メッセージ:** `Expression in 'reactive' must be bool, got '{型}'`

`reactive` ノードの条件式が `bool` 型ではありません。

### BS0022: Defdec parameter type mismatch

**重大度:** Error
**メッセージ:** `Defdec parameter '{名前}' inferred as '{期待型}', got '{実際の型}'`

ユーザー定義デコレータのパラメータ型が使用箇所と一致しません。

---

## 構造エラー

### BS0013: Invalid repeat count

**重大度:** Error
**メッセージ:** `'repeat' count must be a positive integer literal`

`repeat` の回数に正の整数リテラル以外が指定されました。変数や式は使用できません。

### BS0014: Invalid duration

**重大度:** Error
**メッセージ:** `'{ノード名}' duration must be a positive number literal`

`timeout` または `cooldown` の時間に正の数値リテラル以外が指定されました。

### BS0015: Insufficient children

**重大度:** Error
**メッセージ:** `'{ノード名}' requires at least {数} children`

コンポジットノードに必要な数の子ノードがありません。

---

## Defdec エラー

### BS0023: Defdec not found

**重大度:** Error
**メッセージ:** `Defdec '{名前}' not found`

呼び出されたユーザー定義デコレータが定義されていません。

### BS0024: Defdec parameter count mismatch

**重大度:** Error
**メッセージ:** `Defdec '{名前}' expects {期待数} parameters, got {実際の数}`

ユーザー定義デコレータの呼び出し時の引数の数が定義と一致しません。

### BS0025: Recursive defdec

**重大度:** Error
**メッセージ:** `Recursive defdec call detected: '{名前}'`

ユーザー定義デコレータが直接的または間接的に自身を呼び出しています。再帰呼び出しは無限展開になるため禁止されています。

### BS0026: Missing body placeholder

**重大度:** Warning
**メッセージ:** `Missing <body> placeholder in defdec '{名前}'`

ユーザー定義デコレータのボディに `<body>` プレースホルダーがありません。

### BS0027: Multiple body placeholders

**重大度:** Error
**メッセージ:** `Multiple <body> placeholders in defdec '{名前}'`

ユーザー定義デコレータのボディに `<body>` が複数回出現しています。`<body>` は正確に 1 回のみ使用できます。

---

## Blackboard エラー

### BS0028: Blackboard access without declaration

**重大度:** Error
**メッセージ:** `Blackboard access '$' used but no :blackboard declared in tree '{ツリー名}'`

`$` プレフィックスで Blackboard にアクセスしていますが、ツリー定義に `:blackboard` が宣言されていません。

```lisp
(tree T (check $.IsAlarm))
;; → BS0028: Blackboard access '$' used but no :blackboard declared in tree 'T'
```

**対処法:** ツリー定義に `:blackboard 型名` を追加してください。

### BS0029: Blackboard member not found

**重大度:** Error
**メッセージ:** `Blackboard member '{メンバー名}' not found on type '{型名}'`

Blackboard 型に指定されたメンバーが存在しません。

### BS0030: Missing IBtBlackboard

**重大度:** Error
**メッセージ:** `Blackboard type '{型名}' does not implement IBtBlackboard`

Blackboard 型が `IBtBlackboard` インターフェースを実装していません。

### BS0031: Inaccessible blackboard member

**重大度:** Error
**メッセージ:** `Blackboard member '{メンバー名}' is inaccessible from generated code (different assembly)`

Blackboard 型が別アセンブリにある場合、`internal` メンバーにはアクセスできません。

---

## マクロエラー

### BS0031: Macro not found

**重大度:** Error
**メッセージ:** `Macro '{名前}' not found`

呼び出されたマクロが定義されていません。

### BS0032: Macro argument count mismatch

**重大度:** Error
**メッセージ:** `Macro '{名前}' expects {期待数} arguments, got {実際の数}`

マクロ呼び出しの引数の数が定義と一致しません。

### BS0033: Macro expansion depth exceeded

**重大度:** Error
**メッセージ:** `Macro expansion exceeded depth limit ({深度})`

マクロ展開が最大深度（デフォルト: 100）を超えました。マクロ間の相互再帰が原因の可能性があります。

### BS0034: Recursive macro detected

**重大度:** Error
**メッセージ:** `Recursive macro detected: {サイクル}`

マクロが直接的または間接的に自身を呼び出す循環が検出されました。

### BS0035: Invalid macro expansion

**重大度:** Error
**メッセージ:** `Macro expansion produced invalid syntax: {詳細}`

マクロ展開の結果が有効な構文ではありません。

---

## マルチツリーエラー

### BS0036: Context type constraint mismatch

**重大度:** Error
**メッセージ:** `Context type '{型名}' does not satisfy constraint '{インターフェース}' required by tree '{ツリー名}'`

参照先ツリーが `:context` で要求するインターフェースを、呼び出し元の Context 型が実装していません。

### BS0037: Circular tree reference

**重大度:** Error
**メッセージ:** `Circular tree reference detected: {サイクル}`

ツリー間の参照が循環しています。

```lisp
(tree A (ref B))
(tree B (ref A))
;; → BS0037: Circular tree reference detected: A → B → A
```

### BS0038: Tree not found

**重大度:** Error
**メッセージ:** `Tree '{名前}' not found (in current file or imports)`

`ref` で参照されたツリーが現在のファイルにもインポートファイルにも見つかりません。

### BS0039: Ambiguous tree reference

**重大度:** Warning
**メッセージ:** `Ambiguous tree '{名前}': found in multiple imported files`

複数のインポートファイルに同名のツリーが存在します。

### BS0040: Import file not found

**重大度:** Error
**メッセージ:** `Import file '{パス}' not found`

`import` で指定されたファイルが見つかりません。

---

## ジェネリクスエラー

### BS0041: Type argument constraint violation

**重大度:** Error
**メッセージ:** `Type argument '{型}' does not satisfy constraint '{制約}' on type parameter '{パラメータ}' of '{ジェネリック型}'`

ジェネリック型引数が `where` 制約を満たしていません。

```csharp
public partial class AI<T> where T : IComparable<T> { }
```

```lisp
(tree Test :context AI<Entity>)
;; → BS0041: Entity が IComparable<Entity> を実装していない場合
```

### BS0042: Wrong number of type arguments

**重大度:** Error
**メッセージ:** `Generic type '{型名}' requires {期待数} type argument(s), but {実際の数} were provided`

ジェネリック型に渡す型引数の数が一致しません。

### BS0043: Open generic type

**重大度:** Error
**メッセージ:** `Open generic type '{型名}' cannot be used as a context type; provide type arguments`

具体的な型引数を指定せずにジェネリック型をコンテキストとして使用しています。

---

## Nullable 警告

全て Warning または Info です。Error ではありません。

### BS0044: Dereference of possibly null member

**重大度:** Warning
**メッセージ:** `Member '{メンバー名}' may be null at this point`

null チェックなしで nullable なメンバーにアクセスしています。

```lisp
;; Target が string? の場合
(.Attack .Target)
;; → BS0044: Member 'Target' may be null at this point
```

**対処法:** `guard (!= .Target null)` や `check (!= .Target null)` で null チェックしてください。

### BS0045: Comparison with null always true

**重大度:** Warning
**メッセージ:** `Comparison of '{メンバー名}' with null is always true`

常に null でないメンバーを `!= null` で比較しています。

### BS0046: Comparison with null always false

**重大度:** Warning
**メッセージ:** `Comparison of '{メンバー名}' with null is always false`

常に null でないメンバーを `= null` で比較しています。

### BS0047: Unnecessary null check

**重大度:** Info
**メッセージ:** `Null check on '{メンバー名}' is unnecessary because it is non-nullable`

非 nullable なメンバーへの null チェックは不要です。

---

## 構造警告

### BS0301: Unreachable node

**重大度:** Warning
**メッセージ:** `Unreachable node after unconditional Success`

コンポジットノード内で、あるノードの後に到達不可能なノードがあります。

```lisp
(select
  (check true)    ; 常に Success
  (.Patrol))      ; ← 到達不可能
;; → BS0301: Unreachable node after unconditional Success
```

### BS0302: If without else

**重大度:** Warning
**メッセージ:** `'if' without else branch always returns Failure on false`

`if` ノードに `else` 節がないため、条件が偽のとき常に Failure を返します。

---

## その他

### BS0010: Obsolete member

**重大度:** Warning
**メッセージ:** `Member '{メンバー名}' is obsolete: {理由}`

`[Obsolete]` 属性が付いたメンバーが参照されました。

**対処法:** 非推奨のメンバーを新しいものに置き換えてください。

### BS0901: Internal compiler error

**重大度:** Error
**メッセージ:** `Internal compiler error: {詳細}`

コンパイラ内部で予期しないエラーが発生しました。これはコンパイラのバグの可能性があります。

**対処法:** 再現手順と共にイシューを報告してください。

---

## カスケード抑制

Crisp コンパイラはエラーのカスケード（連鎖的な大量エラー）を抑制します。名前解決に失敗した式は特殊な「ErrorType」として扱われ、その式に対する後続の型エラーは報告されません。

```lisp
;; .Unknown が見つからない → BS0001 のみ報告
;; (< ErrorType 30) の BS0003 は抑制される
;; check の BS0007 も抑制される
(check (< .Unknown 30))
```

この仕組みにより、1 つの原因エラーにつき 1 つの診断メッセージのみが報告され、開発者は根本原因に集中できます。
