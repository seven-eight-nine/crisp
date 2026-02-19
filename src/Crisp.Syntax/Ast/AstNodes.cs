using System.Collections.Immutable;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Ast;

// ═══════════════════════════════════════════════════════════
//  列挙型
// ═══════════════════════════════════════════════════════════

/// <summary>二項演算子の種別。</summary>
public enum BinaryOp
{
    Add, Sub, Mul, Div, Mod,
    Lt, Gt, Le, Ge, Eq, Ne,
}

/// <summary>単項演算子の種別。</summary>
public enum UnaryOp
{
    Not, Negate,
}

/// <summary>論理演算子の種別。</summary>
public enum LogicOp
{
    And, Or,
}

/// <summary>リテラルの種別。</summary>
public enum LiteralKind
{
    Int, Float, Bool, String, Null, Enum,
}

/// <summary>
/// Parallel ノードのポリシー。
/// </summary>
public abstract record ParallelPolicy
{
    /// <summary>いずれかの子が Success なら全体 Success。</summary>
    public sealed record Any : ParallelPolicy;

    /// <summary>全ての子が Success なら全体 Success。</summary>
    public sealed record All : ParallelPolicy;

    /// <summary>N 個の子が Success なら全体 Success。</summary>
    public sealed record N(int Count) : ParallelPolicy;
}

/// <summary>
/// メンバーパス。ドットで連鎖するメンバーアクセスを表す。
/// 例: <c>.Target.Position.x</c> → <c>Segments = ["Target", "Position", "x"]</c>。
/// </summary>
public record MemberPath(ImmutableArray<string> Segments)
{
    public override string ToString() => string.Join(".", Segments);
}

/// <summary>
/// enum リテラルの参照。型名とメンバー名を保持する。
/// </summary>
public record EnumRef(string TypeName, string MemberName);

// ═══════════════════════════════════════════════════════════
//  ツリー・複合ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー定義 <c>(tree Name body...)</c> の AST ノード。
/// </summary>
public sealed class AstTree : AstNode
{
    public string Name { get; }

    /// <summary>:blackboard 属性で指定されたブラックボード型名（F3）。未指定の場合は null。</summary>
    public string? BlackboardType { get; }

    public ImmutableArray<AstNode> Body { get; }

    public AstTree(CstNode origin, string name, ImmutableArray<AstNode> body, string? blackboardType = null) : base(origin)
    {
        Name = name;
        BlackboardType = blackboardType;
        Body = body;
    }

    public override IEnumerable<AstNode> Children => Body;
    protected override string DumpSelf() => BlackboardType != null
        ? $"AstTree \"{Name}\" :blackboard {BlackboardType}"
        : $"AstTree \"{Name}\"";
}

/// <summary>
/// セレクターノード。子を順に評価し、最初に成功/継続を返すものを採用する。
/// </summary>
public sealed class AstSelector : AstNode
{
    public ImmutableArray<AstNode> Children_ { get; }

    public AstSelector(CstNode origin, ImmutableArray<AstNode> children) : base(origin)
        => Children_ = children;

    public override IEnumerable<AstNode> Children => Children_;
}

/// <summary>
/// シーケンスノード。子を順に評価し、全て成功なら成功を返す。
/// </summary>
public sealed class AstSequence : AstNode
{
    public ImmutableArray<AstNode> Children_ { get; }

    public AstSequence(CstNode origin, ImmutableArray<AstNode> children) : base(origin)
        => Children_ = children;

    public override IEnumerable<AstNode> Children => Children_;
}

/// <summary>
/// パラレルノード。全子を同時に評価し、ポリシーに応じて結果を返す。
/// </summary>
public sealed class AstParallel : AstNode
{
    public ParallelPolicy Policy { get; }
    public ImmutableArray<AstNode> Children_ { get; }

    public AstParallel(CstNode origin, ParallelPolicy policy, ImmutableArray<AstNode> children) : base(origin)
    {
        Policy = policy;
        Children_ = children;
    }

    public override IEnumerable<AstNode> Children => Children_;

    protected override string DumpSelf()
    {
        var policyStr = Policy switch
        {
            ParallelPolicy.Any => ":any",
            ParallelPolicy.All => ":all",
            ParallelPolicy.N n => $":n {n.Count}",
            _ => Policy.ToString()
        };
        return $"AstParallel {policyStr}";
    }
}

// ═══════════════════════════════════════════════════════════
//  デコレータ・条件ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// チェックノード。式を評価し、true なら Success、false なら Failure。
/// </summary>
public sealed class AstCheck : AstNode
{
    public AstExpr Expr { get; }

    public AstCheck(CstNode origin, AstExpr expr) : base(origin) => Expr = expr;

    public override IEnumerable<AstNode> Children => [Expr];
    protected override string DumpSelf() => "AstCheck";
}

/// <summary>
/// ガードノード。条件が true なら body を評価、false なら Failure。
/// </summary>
public sealed class AstGuard : AstNode
{
    public AstExpr Condition { get; }
    public AstNode Body { get; }

    public AstGuard(CstNode origin, AstExpr condition, AstNode body) : base(origin)
    {
        Condition = condition;
        Body = body;
    }

    public override IEnumerable<AstNode> Children => [Condition, Body];
}

/// <summary>
/// if ノード。条件が true なら Then を、false なら Else（または Failure）を返す。
/// </summary>
public sealed class AstIf : AstNode
{
    public AstExpr Condition { get; }
    public AstNode Then { get; }
    public AstNode? Else { get; }

    public AstIf(CstNode origin, AstExpr condition, AstNode then, AstNode? @else) : base(origin)
    {
        Condition = condition;
        Then = then;
        Else = @else;
    }

    public override IEnumerable<AstNode> Children
    {
        get
        {
            yield return Condition;
            yield return Then;
            if (Else != null) yield return Else;
        }
    }
}

/// <summary>
/// 反転ノード。Success ↔ Failure を反転する。Running はそのまま。
/// </summary>
public sealed class AstInvert : AstNode
{
    public AstNode Child { get; }

    public AstInvert(CstNode origin, AstNode child) : base(origin) => Child = child;

    public override IEnumerable<AstNode> Children => [Child];
}

/// <summary>
/// リピートノード。子ノードを指定回数繰り返す。
/// </summary>
public sealed class AstRepeat : AstNode
{
    public int Count { get; }
    public AstNode Child { get; }

    public AstRepeat(CstNode origin, int count, AstNode child) : base(origin)
    {
        Count = count;
        Child = child;
    }

    public override IEnumerable<AstNode> Children => [Child];
    protected override string DumpSelf() => $"AstRepeat {Count}";
}

/// <summary>
/// タイムアウトノード。指定秒数以内に子が完了しなければ Failure。
/// </summary>
public sealed class AstTimeout : AstNode
{
    public float Seconds { get; }
    public AstNode Child { get; }

    public AstTimeout(CstNode origin, float seconds, AstNode child) : base(origin)
    {
        Seconds = seconds;
        Child = child;
    }

    public override IEnumerable<AstNode> Children => [Child];
    protected override string DumpSelf() => $"AstTimeout {Seconds}";
}

/// <summary>
/// クールダウンノード。子が Success を返した後、指定秒数が経過するまで Failure。
/// </summary>
public sealed class AstCooldown : AstNode
{
    public float Seconds { get; }
    public AstNode Child { get; }

    public AstCooldown(CstNode origin, float seconds, AstNode child) : base(origin)
    {
        Seconds = seconds;
        Child = child;
    }

    public override IEnumerable<AstNode> Children => [Child];
    protected override string DumpSelf() => $"AstCooldown {Seconds}";
}

/// <summary>
/// while ノード。条件が true の間、body を毎 tick 評価する。
/// </summary>
public sealed class AstWhile : AstNode
{
    public AstExpr Condition { get; }
    public AstNode Body { get; }

    public AstWhile(CstNode origin, AstExpr condition, AstNode body) : base(origin)
    {
        Condition = condition;
        Body = body;
    }

    public override IEnumerable<AstNode> Children => [Condition, Body];
}

// ═══════════════════════════════════════════════════════════
//  リアクティブノード（F1: Reactive Reevaluation）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// リアクティブノード。毎 tick 条件を再評価し、false になったら Running 中の子を中断する。
/// </summary>
public sealed class AstReactive : AstNode
{
    /// <summary>毎 tick 再評価される条件式。bool 制約。</summary>
    public AstExpr Condition { get; }

    /// <summary>条件が true の間 tick される子ノード。</summary>
    public AstNode Body { get; }

    public AstReactive(CstNode origin, AstExpr condition, AstNode body) : base(origin)
    {
        Condition = condition;
        Body = body;
    }

    public override IEnumerable<AstNode> Children => [Condition, Body];
}

/// <summary>
/// リアクティブセレクターノード。毎 tick 先頭から再評価し、上位が復活したら下位を中断する。
/// </summary>
public sealed class AstReactiveSelect : AstNode
{
    public ImmutableArray<AstNode> Children_ { get; }

    public AstReactiveSelect(CstNode origin, ImmutableArray<AstNode> children) : base(origin)
        => Children_ = children;

    public override IEnumerable<AstNode> Children => Children_;
}

// ═══════════════════════════════════════════════════════════
//  Multi-Tree（F11: Multi-Tree & Cross-Reference）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー参照ノード。別のツリー定義を参照し、メソッド呼び出しとして展開される。
/// </summary>
public sealed class AstRef : AstNode
{
    /// <summary>参照先のツリー名。</summary>
    public string TreeName { get; }

    /// <summary>名前解決後に設定される、参照先の解決済みツリー（null の場合は未解決）。</summary>
    public AstTree? ResolvedTree { get; set; }

    public AstRef(CstNode origin, string treeName) : base(origin) => TreeName = treeName;

    public override IEnumerable<AstNode> Children => [];
    protected override string DumpSelf() => $"AstRef \"{TreeName}\"";
}

// ═══════════════════════════════════════════════════════════
//  User-Defined Decorators（F2: defdec）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// デコレータ定義ノード。DSL 側でカスタムデコレータを定義する。
/// </summary>
public sealed class AstDefdec : AstNode
{
    public string Name { get; }
    public ImmutableArray<string> Parameters { get; }
    public AstNode Body { get; }

    public AstDefdec(CstNode origin, string name, ImmutableArray<string> parameters, AstNode body) : base(origin)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
    }

    public override IEnumerable<AstNode> Children => [Body];
    protected override string DumpSelf() => $"AstDefdec \"{Name}\" ({string.Join(", ", Parameters)})";
}

/// <summary>
/// <c>&lt;body&gt;</c> プレースホルダ。defdec 展開時に実引数のノードに置換される。
/// </summary>
public sealed class AstBodyPlaceholder : AstNode
{
    public AstBodyPlaceholder(CstNode origin) : base(origin) { }

    public override IEnumerable<AstNode> Children => [];
    protected override string DumpSelf() => "AstBodyPlaceholder";
}

/// <summary>
/// defdec 呼び出しノード（F2）。展開パスによって実際のノードツリーに置換される。
/// <c>(safe-timed-action 10.0 2.0 (.Attack .Target))</c> のように、
/// defdec 名 + パラメータ式引数 + body 子ノード の形式で呼び出す。
/// </summary>
public sealed class AstDefdecCall : AstNode
{
    /// <summary>呼び出す defdec の名前。</summary>
    public string Name { get; }

    /// <summary>パラメータに渡す式引数（リテラルや式）。</summary>
    public ImmutableArray<AstExpr> Args { get; }

    /// <summary><c>&lt;body&gt;</c> プレースホルダに代入される子ノード。</summary>
    public AstNode Body { get; }

    public AstDefdecCall(CstNode origin, string name, ImmutableArray<AstExpr> args, AstNode body) : base(origin)
    {
        Name = name;
        Args = args;
        Body = body;
    }

    public override IEnumerable<AstNode> Children
    {
        get
        {
            foreach (var arg in Args) yield return arg;
            yield return Body;
        }
    }

    protected override string DumpSelf() => $"AstDefdecCall \"{Name}\"";
}

// ═══════════════════════════════════════════════════════════
//  Macro System（F10: defmacro）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// マクロ定義ノード（F10）。DSL 側でマクロを定義する。
/// body は生の CST ノードとして保持され、マクロ展開時にパラメータ置換して構文木を生成する。
/// マクロは AST レベルで展開されるため、展開前の body をそのまま保持する。
/// </summary>
public sealed class AstDefmacro : AstNode
{
    /// <summary>マクロ名。</summary>
    public string Name { get; }

    /// <summary>マクロパラメータ名のリスト。</summary>
    public ImmutableArray<string> Parameters { get; }

    /// <summary>
    /// マクロテンプレートの本体。展開時にパラメータを実引数で置換する。
    /// マクロは構文レベルで操作するため、生の CST ノードとして保持する。
    /// </summary>
    public CstNode BodyCst { get; }

    public AstDefmacro(CstNode origin, string name, ImmutableArray<string> parameters, CstNode bodyCst) : base(origin)
    {
        Name = name;
        Parameters = parameters;
        BodyCst = bodyCst;
    }

    public override IEnumerable<AstNode> Children => [];
    protected override string DumpSelf() => $"AstDefmacro \"{Name}\" ({string.Join(", ", Parameters)})";
}

// ═══════════════════════════════════════════════════════════
//  アクション呼び出し
// ═══════════════════════════════════════════════════════════

/// <summary>
/// アクション呼び出しノード（ノード位置）。
/// メソッドを呼び出し、BtStatus を返すことが期待される。
/// </summary>
public sealed class AstActionCall : AstNode
{
    public MemberPath Member { get; }
    public ImmutableArray<AstExpr> Args { get; }

    public AstActionCall(CstNode origin, MemberPath member, ImmutableArray<AstExpr> args) : base(origin)
    {
        Member = member;
        Args = args;
    }

    public override IEnumerable<AstNode> Children => Args;
    protected override string DumpSelf() => $"AstActionCall .{Member}";
}

// ═══════════════════════════════════════════════════════════
//  式ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// AST 式ノードの基底クラス。全ての式は値を持つ。
/// </summary>
public abstract class AstExpr : AstNode
{
    protected AstExpr(CstNode cstOrigin) : base(cstOrigin) { }
}

/// <summary>
/// メンバーアクセス式。<c>.Foo.Bar.Baz</c> 等。
/// </summary>
public sealed class AstMemberAccessExpr : AstExpr
{
    public MemberPath Path { get; }

    public AstMemberAccessExpr(CstNode origin, MemberPath path) : base(origin) => Path = path;

    public override IEnumerable<AstNode> Children => [];
    protected override string DumpSelf() => $"AstMemberAccessExpr .{Path}";
}

/// <summary>
/// リテラル式。int, float, bool, string, null, enum。
/// </summary>
public sealed class AstLiteralExpr : AstExpr
{
    /// <summary>リテラル値（ボックス化される）。null リテラルの場合は <c>null</c>。</summary>
    public object? Value { get; }

    /// <summary>リテラルの種別。</summary>
    public LiteralKind LiteralKind { get; }

    public AstLiteralExpr(CstNode origin, object? value, LiteralKind kind) : base(origin)
    {
        Value = value;
        LiteralKind = kind;
    }

    public override IEnumerable<AstNode> Children => [];

    protected override string DumpSelf()
    {
        var valueStr = Value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            EnumRef e => $"::{e.TypeName}.{e.MemberName}",
            _ => Value.ToString() ?? "null"
        };
        return $"AstLiteralExpr {valueStr} ({LiteralKind})";
    }
}

/// <summary>
/// 二項式。算術演算・比較演算。
/// </summary>
public sealed class AstBinaryExpr : AstExpr
{
    public BinaryOp Op { get; }
    public AstExpr Lhs { get; }
    public AstExpr Rhs { get; }

    public AstBinaryExpr(CstNode origin, BinaryOp op, AstExpr lhs, AstExpr rhs) : base(origin)
    {
        Op = op;
        Lhs = lhs;
        Rhs = rhs;
    }

    public override IEnumerable<AstNode> Children => [Lhs, Rhs];
    protected override string DumpSelf() => $"AstBinaryExpr {Op}";
}

/// <summary>
/// 単項式。<c>(not expr)</c> や <c>(- expr)</c>（単項マイナス）。
/// </summary>
public sealed class AstUnaryExpr : AstExpr
{
    public UnaryOp Op { get; }
    public AstExpr Operand { get; }

    public AstUnaryExpr(CstNode origin, UnaryOp op, AstExpr operand) : base(origin)
    {
        Op = op;
        Operand = operand;
    }

    public override IEnumerable<AstNode> Children => [Operand];
    protected override string DumpSelf() => $"AstUnaryExpr {Op}";
}

/// <summary>
/// 論理式。<c>(and expr expr+)</c> / <c>(or expr expr+)</c>。
/// </summary>
public sealed class AstLogicExpr : AstExpr
{
    public LogicOp Op { get; }
    public ImmutableArray<AstExpr> Operands { get; }

    public AstLogicExpr(CstNode origin, LogicOp op, ImmutableArray<AstExpr> operands) : base(origin)
    {
        Op = op;
        Operands = operands;
    }

    public override IEnumerable<AstNode> Children => Operands;
    protected override string DumpSelf() => $"AstLogicExpr {Op}";
}

/// <summary>
/// ブラックボードアクセス式（F3）。<c>$.Foo.Bar</c> 等。
/// <c>$</c> プレフィクスで始まるメンバーチェーンは blackboard オブジェクトへのアクセスとなる。
/// </summary>
public sealed class AstBlackboardAccessExpr : AstExpr
{
    /// <summary>ブラックボードメンバーへのパス。</summary>
    public MemberPath Path { get; }

    public AstBlackboardAccessExpr(CstNode origin, MemberPath path) : base(origin) => Path = path;

    public override IEnumerable<AstNode> Children => [];
    protected override string DumpSelf() => $"AstBlackboardAccessExpr ${Path}";
}

/// <summary>
/// メソッド呼び出し式（式位置）。式コンテキスト内の <c>(.Method arg1 arg2)</c>。
/// </summary>
public sealed class AstCallExpr : AstExpr
{
    public MemberPath Member { get; }
    public ImmutableArray<AstExpr> Args { get; }

    public AstCallExpr(CstNode origin, MemberPath member, ImmutableArray<AstExpr> args) : base(origin)
    {
        Member = member;
        Args = args;
    }

    public override IEnumerable<AstNode> Children => Args;
    protected override string DumpSelf() => $"AstCallExpr .{Member}";
}
