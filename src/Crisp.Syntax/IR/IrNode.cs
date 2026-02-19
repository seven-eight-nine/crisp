using System.Collections.Immutable;
using Crisp.Syntax;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

// ═══════════════════════════════════════════════════════════
//  IR ノード基底クラス
// ═══════════════════════════════════════════════════════════

/// <summary>
/// IR（中間表現）ノードの抽象基底クラス。
/// AST からの lowering により生成され、バックエンド（C# エミッタ、インタープリタ）の入力となる。
///
/// 設計方針:
/// <list type="bullet">
///   <item><description>Roslyn シンボルに依存しない — 全ての参照は文字列ベースの Ref 型</description></item>
///   <item><description>型情報付き — 各ノードに TypeRef が付与される</description></item>
///   <item><description>S式で直列化可能 — デバッグ・テスト・中間出力として使用</description></item>
///   <item><description>安定 Id — CST → AST → IR を通じて一意のノード識別子を保持する（F4, F7, F9 で使用）</description></item>
///   <item><description>Origin — 元の CST 位置情報を保持し、エラー報告・LSP連携に使用する</description></item>
/// </list>
/// </summary>
public abstract record IrNode
{
    /// <summary>
    /// ノードの結果型。ステートメント系ノード（IrSelector 等）では null の場合がある。
    /// 式ノード（IrExpr 派生）では非 null が保証される。
    /// </summary>
    public TypeRef? TypeRef { get; init; }

    /// <summary>
    /// ノードの一意識別子。
    /// CST → AST → IR を通じて安定的に付与される。
    /// Debugger（F7）、Visual Editor（F4）、Hot Reload（F9）の実行トレースで共通に使用する。
    /// IR最適化パス（F5）でノードが新規生成された場合は新しいIdを採番する。
    /// デフォルト値 -1 は未割り当てを示す。
    /// </summary>
    public int Id { get; init; } = -1;

    /// <summary>
    /// このIRノードの元となったCSTの位置情報。
    /// エラー報告、LSPホバー、ブレークポイント設定に使用する。
    /// </summary>
    public TextSpan Origin { get; init; }
}

// ═══════════════════════════════════════════════════════════
//  ツリー定義ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー定義の IR ノード。AST の <c>AstTree</c> に対応する。
/// ビヘイビアツリー全体のエントリポイントとなる。
/// </summary>
/// <param name="Name">ツリー名。</param>
/// <param name="Body">ツリー本体のノード配列。</param>
public sealed record IrTree(string Name, ImmutableArray<IrNode> Body) : IrNode;

// ═══════════════════════════════════════════════════════════
//  複合ノード（子ノードを持つ制御フローノード）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// セレクターノード。子を順に評価し、最初に成功または継続を返すものを採用する。
/// 全ての子が失敗した場合のみ失敗を返す。
/// </summary>
/// <param name="Children">子ノードの配列。</param>
public sealed record IrSelector(ImmutableArray<IrNode> Children) : IrNode;

/// <summary>
/// シーケンスノード。子を順に評価し、全て成功なら成功を返す。
/// いずれかの子が失敗した時点で失敗を返す。
/// </summary>
/// <param name="Children">子ノードの配列。</param>
public sealed record IrSequence(ImmutableArray<IrNode> Children) : IrNode;

/// <summary>
/// パラレルノード。全子を同時に評価し、ポリシーに応じて結果を返す。
/// ポリシーには Any（いずれか成功）、All（全て成功）、N（N個成功）がある。
/// </summary>
/// <param name="Policy">並列実行のポリシー。</param>
/// <param name="Children">子ノードの配列。</param>
public sealed record IrParallel(ParallelPolicy Policy, ImmutableArray<IrNode> Children) : IrNode;

// ═══════════════════════════════════════════════════════════
//  条件・ガード・制御フローノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 条件ノード。式を評価し、true なら Success、false なら Failure を返す。
/// AST の <c>AstCheck</c> に対応する。
/// </summary>
/// <param name="Expr">評価する条件式。</param>
public sealed record IrCondition(IrExpr Expr) : IrNode;

/// <summary>
/// アクション呼び出しノード。メソッドを呼び出し、BtStatus を返す。
/// AST の <c>AstActionCall</c> に対応する。
/// </summary>
/// <param name="Method">呼び出すメソッドへの参照。</param>
/// <param name="Args">メソッド引数の式配列。</param>
/// <param name="IsAsync">非同期アクションかどうか（F8: Async/Await）。true の場合、AsyncActionNode として生成される。</param>
/// <param name="IsSubtree">サブツリー埋め込みかどうか。true の場合、メソッドは BtNode を返し、直接ノードとして組み込まれる。</param>
public sealed record IrAction(MethodRef Method, ImmutableArray<IrExpr> Args, bool IsAsync = false, bool IsSubtree = false) : IrNode;

/// <summary>
/// ガードノード。条件が true なら body を評価、false なら Failure を返す。
/// 条件式が毎 tick 評価される点が IrIf との違い。
/// </summary>
/// <param name="Condition">ガード条件式。</param>
/// <param name="Body">条件が true の場合に評価するノード。</param>
public sealed record IrGuard(IrExpr Condition, IrNode Body) : IrNode;

/// <summary>
/// if ノード。条件が true なら Then を、false なら Else（または Failure）を返す。
/// </summary>
/// <param name="Condition">分岐条件式。</param>
/// <param name="Then">条件が true の場合に評価するノード。</param>
/// <param name="Else">条件が false の場合に評価するノード。null の場合は Failure。</param>
public sealed record IrIf(IrExpr Condition, IrNode Then, IrNode? Else) : IrNode;

/// <summary>
/// 反転ノード。子ノードの Success と Failure を反転する。Running はそのまま通す。
/// </summary>
/// <param name="Child">反転対象の子ノード。</param>
public sealed record IrInvert(IrNode Child) : IrNode;

/// <summary>
/// リピートノード。子ノードを指定回数繰り返す。
/// 子が Failure を返した場合はその時点で Failure を返す。
/// </summary>
/// <param name="Count">繰り返し回数。</param>
/// <param name="Child">繰り返し対象の子ノード。</param>
public sealed record IrRepeat(int Count, IrNode Child) : IrNode;

/// <summary>
/// タイムアウトノード。指定秒数以内に子が完了しなければ Failure を返す。
/// </summary>
/// <param name="Seconds">タイムアウト秒数。</param>
/// <param name="Child">タイムアウト対象の子ノード。</param>
public sealed record IrTimeout(float Seconds, IrNode Child) : IrNode;

/// <summary>
/// クールダウンノード。子が Success を返した後、指定秒数が経過するまで Failure を返す。
/// </summary>
/// <param name="Seconds">クールダウン秒数。</param>
/// <param name="Child">クールダウン対象の子ノード。</param>
public sealed record IrCooldown(float Seconds, IrNode Child) : IrNode;

/// <summary>
/// while ノード。条件が true の間、body を毎 tick 評価する。
/// 条件が false になった時点で Success を返す。
/// </summary>
/// <param name="Condition">ループ条件式。</param>
/// <param name="Body">ループ本体のノード。</param>
public sealed record IrWhile(IrExpr Condition, IrNode Body) : IrNode;

// ═══════════════════════════════════════════════════════════
//  リアクティブノード（F1: Reactive Reevaluation）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// リアクティブノード。毎 tick 条件を再評価し、false になったら Running 中の body を中断する。
/// Coreの guard との違い: guard は Running 子を記憶して続行するが、reactive は毎 tick 再評価する。
/// </summary>
/// <param name="Condition">毎 tick 再評価される条件式（bool 制約）。</param>
/// <param name="Body">条件が true の間 tick される子ノード。</param>
public sealed record IrReactive(IrExpr Condition, IrNode Body) : IrNode;

/// <summary>
/// リアクティブセレクターノード。毎 tick 先頭から子を再評価し、
/// 上位が Success/Running を返したら下位の Running 中の子を全て中断する。
/// </summary>
/// <param name="Children">優先度順の子ノード配列。先頭が最も優先度が高い。</param>
public sealed record IrReactiveSelect(ImmutableArray<IrNode> Children) : IrNode;

// ═══════════════════════════════════════════════════════════
//  Multi-Tree ノード（F11: Multi-Tree & Cross-Reference）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー参照ノード。別ツリーへの参照を表し、生成コードではメソッド呼び出しに変換される。
/// </summary>
/// <param name="TreeName">参照先のツリー名。</param>
public sealed record IrTreeRef(string TreeName) : IrNode;

// ═══════════════════════════════════════════════════════════
//  Blackboard ノード（F3: Typed Blackboard）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ブラックボードロード式。<c>$</c> プレフィクスのメンバーチェーンを通じて
/// ブラックボードオブジェクトから値を読み込む。
/// AST の <c>AstBlackboardAccessExpr</c> に対応する。
///
/// 例: <c>$.IsAlarmTriggered</c> → <c>IrBlackboardLoad(MemberRef(["IsAlarmTriggered"]))</c>
/// </summary>
/// <param name="Ref">読み込むブラックボードメンバーへの参照チェーン。</param>
public sealed record IrBlackboardLoad(MemberRef Ref) : IrExpr;

// ═══════════════════════════════════════════════════════════
//  IR 式ノード基底クラス
// ═══════════════════════════════════════════════════════════

/// <summary>
/// IR 式ノードの抽象基底クラス。全ての式は型情報（TypeRef）を持つ。
/// IrNode と異なり、TypeRef は非 null が保証される。
/// </summary>
public abstract record IrExpr : IrNode
{
    /// <summary>
    /// 式の型参照。式ノードでは常に非 null。
    /// new キーワードで IrNode.TypeRef を隠蔽し、非 null 型として再定義する。
    /// </summary>
    public new required TypeRef TypeRef
    {
        get => base.TypeRef!;
        init => base.TypeRef = value;
    }
}

// ═══════════════════════════════════════════════════════════
//  式ノード: メンバーアクセス・リテラル
// ═══════════════════════════════════════════════════════════

/// <summary>
/// メンバーロード式。メンバーチェーンを通じて値を読み込む。
/// AST の <c>AstMemberAccessExpr</c> に対応する。
///
/// 例: <c>.Target.Position.x</c> → <c>IrMemberLoad(MemberRef(["Target", "Position", "x"]))</c>
/// </summary>
/// <param name="Ref">読み込むメンバーへの参照チェーン。</param>
public sealed record IrMemberLoad(MemberRef Ref) : IrExpr;

/// <summary>
/// リテラル式。コンパイル時に値が確定する定数値。
/// AST の <c>AstLiteralExpr</c> に対応する。
///
/// 値のボクシング:
/// <list type="bullet">
///   <item><description>Int → <c>int</c></description></item>
///   <item><description>Float → <c>float</c></description></item>
///   <item><description>Bool → <c>bool</c></description></item>
///   <item><description>String → <c>string</c></description></item>
///   <item><description>Null → <c>null</c></description></item>
///   <item><description>Enum → <c>EnumRef</c></description></item>
/// </list>
/// </summary>
/// <param name="Value">リテラル値。null リテラルの場合は <c>null</c>。</param>
/// <param name="Kind">リテラルの種別。</param>
public sealed record IrLiteral(object? Value, LiteralKind Kind) : IrExpr;

// ═══════════════════════════════════════════════════════════
//  式ノード: 演算子
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 二項演算式。算術演算（+, -, *, /, %）および比較演算（&lt;, &gt;, &lt;=, &gt;=, =, !=）。
/// lowering 時に int+float の混合演算では IrConvert が挿入される。
/// </summary>
/// <param name="Op">二項演算子の種別。</param>
/// <param name="Lhs">左辺の式。</param>
/// <param name="Rhs">右辺の式。</param>
public sealed record IrBinaryOp(BinaryOp Op, IrExpr Lhs, IrExpr Rhs) : IrExpr;

/// <summary>
/// 単項演算式。論理否定（not）および算術否定（-）。
/// </summary>
/// <param name="Op">単項演算子の種別。</param>
/// <param name="Operand">オペランドの式。</param>
public sealed record IrUnaryOp(UnaryOp Op, IrExpr Operand) : IrExpr;

/// <summary>
/// 論理演算式。短絡評価を行う and / or 演算。
/// 2つ以上のオペランドを持つ可変長演算。
/// </summary>
/// <param name="Op">論理演算子の種別（And / Or）。</param>
/// <param name="Operands">オペランドの式配列。</param>
public sealed record IrLogicOp(LogicOp Op, ImmutableArray<IrExpr> Operands) : IrExpr;

// ═══════════════════════════════════════════════════════════
//  式ノード: 呼び出し・型変換
// ═══════════════════════════════════════════════════════════

/// <summary>
/// メソッド呼び出し式（式位置）。式コンテキスト内でメソッドを呼び出し、戻り値を返す。
/// AST の <c>AstCallExpr</c> に対応する。
/// </summary>
/// <param name="Method">呼び出すメソッドへの参照。</param>
/// <param name="Args">メソッド引数の式配列。</param>
public sealed record IrCall(MethodRef Method, ImmutableArray<IrExpr> Args) : IrExpr;

/// <summary>
/// 暗黙型変換式。AST → IR lowering 時に自動挿入される。
/// バックエンドは型変換を意識する必要がなくなる。
///
/// 主な使用場面:
/// <list type="bullet">
///   <item><description>int → float の暗黙昇格（二項算術演算で int と float が混合する場合）</description></item>
/// </list>
///
/// 例: <c>(+ .IntValue 1.0)</c> で <c>.IntValue</c> が int の場合、
/// <c>IrConvert(IrMemberLoad(...), TypeRef.Float)</c> が挿入される。
/// </summary>
/// <param name="Operand">変換元の式。</param>
/// <param name="TargetType">変換先の型。</param>
public sealed record IrConvert(IrExpr Operand, TypeRef TargetType) : IrExpr;
