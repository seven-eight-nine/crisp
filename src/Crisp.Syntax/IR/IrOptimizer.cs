using System.Collections.Immutable;
using System.Globalization;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

/// <summary>
/// IR 最適化パスのインターフェース。
/// 各最適化パスは IR ノードツリーを受け取り、最適化後のツリーを返す。
/// 最適化は正当性を保ちつつ、実行時のオーバーヘッドを削減する。
/// </summary>
public interface IIrPass
{
    /// <summary>
    /// IR ノードツリーに最適化パスを適用する。
    /// </summary>
    /// <param name="root">最適化対象の IR ノード。</param>
    /// <returns>最適化後の IR ノード。変更がない場合は入力をそのまま返す。</returns>
    IrNode Run(IrNode root);
}

/// <summary>
/// IR 最適化パスマネージャ。
/// 複数の最適化パスを順に適用し、IR ツリーを最適化する。
///
/// 現在実装されているパス:
/// <list type="bullet">
///   <item><description>Pass 1: 定数畳み込み（Constant Folding）— 両オペランドがリテラルの純粋演算をコンパイル時に評価</description></item>
///   <item><description>Pass 2: 到達不能ノード除去（Dead Node Elimination）— 常に Failure/Success になるパスを検出・除去</description></item>
///   <item><description>Pass 3: 単一子ノード畳み込み（Single-Child Collapse）— 子が1つの Selector/Sequence を除去</description></item>
///   <item><description>Pass 5: 型変換統合（Convert Fusion）— 冗長な型変換やリテラルの型変換をインライン化</description></item>
/// </list>
/// </summary>
public class IrPassManager
{
    private readonly List<IIrPass> _passes = new();

    /// <summary>
    /// デフォルトの最適化パスを順に登録した <see cref="IrPassManager"/> を生成する。
    /// </summary>
    public IrPassManager()
    {
        _passes.Add(new ConstantFoldingPass());
        _passes.Add(new DeadNodeEliminationPass());
        _passes.Add(new SingleChildCollapsePass());
        _passes.Add(new ConvertFusionPass());
    }

    /// <summary>
    /// 全ての最適化パスを順に適用する。
    /// </summary>
    /// <param name="root">最適化対象の IR ノード。</param>
    /// <returns>全パス適用後の IR ノード。</returns>
    public IrNode Optimize(IrNode root)
    {
        var current = root;
        foreach (var pass in _passes)
            current = pass.Run(current);
        return current;
    }

    /// <summary>
    /// IR ツリーを最適化する。ツリー内の全ノードに最適化パスを適用する。
    /// </summary>
    /// <param name="tree">最適化対象の IR ツリー。</param>
    /// <returns>最適化後の IR ツリー。</returns>
    public IrTree OptimizeTree(IrTree tree)
    {
        var optimizedBody = tree.Body.Select(n => Optimize(n)).ToImmutableArray();
        return new IrTree(tree.Name, optimizedBody)
        {
            TypeRef = tree.TypeRef,
            Id = tree.Id,
            Origin = tree.Origin
        };
    }
}

// ═══════════════════════════════════════════════════════════
//  Pass 1: 定数畳み込み（Constant Folding）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 定数畳み込みパス。
/// 両オペランドがリテラルである純粋な演算をコンパイル時に評価し、
/// 結果をリテラルノードに置き換える。
///
/// 適用例:
/// <code>
/// (ir-binary-op :add (ir-literal 10 :int) (ir-literal 20 :int))
/// → (ir-literal 30 :int)
/// </code>
/// </summary>
public class ConstantFoldingPass : IIrPass
{
    /// <inheritdoc/>
    public IrNode Run(IrNode root) => Fold(root);

    /// <summary>ノードを再帰的に走査し、定数畳み込みを適用する。</summary>
    private IrNode Fold(IrNode node) => node switch
    {
        IrTree tree => new IrTree(tree.Name, FoldChildren(tree.Body))
            { TypeRef = tree.TypeRef, Id = tree.Id, Origin = tree.Origin },
        IrSelector sel => new IrSelector(FoldChildren(sel.Children))
            { TypeRef = sel.TypeRef, Id = sel.Id, Origin = sel.Origin },
        IrSequence seq => new IrSequence(FoldChildren(seq.Children))
            { TypeRef = seq.TypeRef, Id = seq.Id, Origin = seq.Origin },
        IrParallel par => new IrParallel(par.Policy, FoldChildren(par.Children))
            { TypeRef = par.TypeRef, Id = par.Id, Origin = par.Origin },
        IrCondition cond => new IrCondition(FoldExpr(cond.Expr))
            { TypeRef = cond.TypeRef, Id = cond.Id, Origin = cond.Origin },
        IrGuard grd => new IrGuard(FoldExpr(grd.Condition), Fold(grd.Body))
            { TypeRef = grd.TypeRef, Id = grd.Id, Origin = grd.Origin },
        IrIf ifn => new IrIf(FoldExpr(ifn.Condition), Fold(ifn.Then), ifn.Else != null ? Fold(ifn.Else) : null)
            { TypeRef = ifn.TypeRef, Id = ifn.Id, Origin = ifn.Origin },
        IrInvert inv => new IrInvert(Fold(inv.Child))
            { TypeRef = inv.TypeRef, Id = inv.Id, Origin = inv.Origin },
        IrRepeat rep => new IrRepeat(rep.Count, Fold(rep.Child))
            { TypeRef = rep.TypeRef, Id = rep.Id, Origin = rep.Origin },
        IrTimeout tmo => new IrTimeout(tmo.Seconds, Fold(tmo.Child))
            { TypeRef = tmo.TypeRef, Id = tmo.Id, Origin = tmo.Origin },
        IrCooldown cld => new IrCooldown(cld.Seconds, Fold(cld.Child))
            { TypeRef = cld.TypeRef, Id = cld.Id, Origin = cld.Origin },
        IrWhile whl => new IrWhile(FoldExpr(whl.Condition), Fold(whl.Body))
            { TypeRef = whl.TypeRef, Id = whl.Id, Origin = whl.Origin },
        IrReactive rct => new IrReactive(FoldExpr(rct.Condition), Fold(rct.Body))
            { TypeRef = rct.TypeRef, Id = rct.Id, Origin = rct.Origin },
        IrReactiveSelect rsel => new IrReactiveSelect(FoldChildren(rsel.Children))
            { TypeRef = rsel.TypeRef, Id = rsel.Id, Origin = rsel.Origin },
        IrAction act => new IrAction(act.Method, FoldExprs(act.Args))
            { TypeRef = act.TypeRef, Id = act.Id, Origin = act.Origin },
        IrExpr expr => FoldExpr(expr),
        _ => node,
    };

    /// <summary>子ノード配列を再帰的に畳み込む。</summary>
    private ImmutableArray<IrNode> FoldChildren(ImmutableArray<IrNode> children)
        => children.Select(Fold).ToImmutableArray();

    /// <summary>式配列を再帰的に畳み込む。</summary>
    private ImmutableArray<IrExpr> FoldExprs(ImmutableArray<IrExpr> exprs)
        => exprs.Select(FoldExpr).ToImmutableArray();

    /// <summary>
    /// 式を再帰的に走査し、定数畳み込みを適用する。
    /// 両オペランドがリテラルの場合にコンパイル時評価を行う。
    /// </summary>
    private IrExpr FoldExpr(IrExpr expr)
    {
        switch (expr)
        {
            case IrBinaryOp bin:
            {
                var lhs = FoldExpr(bin.Lhs);
                var rhs = FoldExpr(bin.Rhs);
                if (lhs is IrLiteral litL && rhs is IrLiteral litR)
                {
                    var result = TryFoldBinary(bin.Op, litL, litR);
                    if (result != null) return result;
                }
                return new IrBinaryOp(bin.Op, lhs, rhs)
                    { TypeRef = bin.TypeRef, Id = bin.Id, Origin = bin.Origin };
            }

            case IrUnaryOp un:
            {
                var operand = FoldExpr(un.Operand);
                if (operand is IrLiteral lit)
                {
                    var result = TryFoldUnary(un.Op, lit);
                    if (result != null) return result;
                }
                return new IrUnaryOp(un.Op, operand)
                    { TypeRef = un.TypeRef, Id = un.Id, Origin = un.Origin };
            }

            case IrLogicOp log:
            {
                var operands = log.Operands.Select(FoldExpr).ToImmutableArray();
                return new IrLogicOp(log.Op, operands)
                    { TypeRef = log.TypeRef, Id = log.Id, Origin = log.Origin };
            }

            case IrConvert conv:
            {
                var operand = FoldExpr(conv.Operand);
                return new IrConvert(operand, conv.TargetType)
                    { TypeRef = conv.TypeRef, Id = conv.Id, Origin = conv.Origin };
            }

            case IrCall call:
            {
                var args = call.Args.Select(FoldExpr).ToImmutableArray();
                return new IrCall(call.Method, args)
                    { TypeRef = call.TypeRef, Id = call.Id, Origin = call.Origin };
            }

            default:
                return expr;
        }
    }

    /// <summary>
    /// 二項演算のリテラル畳み込みを試行する。
    /// 演算が純粋で両オペランドがリテラルの場合、結果のリテラルを返す。
    /// </summary>
    private static IrLiteral? TryFoldBinary(BinaryOp op, IrLiteral lhs, IrLiteral rhs)
    {
        // int 同士の演算
        if (lhs.Kind == LiteralKind.Int && rhs.Kind == LiteralKind.Int
            && lhs.Value is int li && rhs.Value is int ri)
        {
            return op switch
            {
                BinaryOp.Add => new IrLiteral(li + ri, LiteralKind.Int) { TypeRef = TypeRef.Int },
                BinaryOp.Sub => new IrLiteral(li - ri, LiteralKind.Int) { TypeRef = TypeRef.Int },
                BinaryOp.Mul => new IrLiteral(li * ri, LiteralKind.Int) { TypeRef = TypeRef.Int },
                BinaryOp.Div when ri != 0 => new IrLiteral(li / ri, LiteralKind.Int) { TypeRef = TypeRef.Int },
                BinaryOp.Mod when ri != 0 => new IrLiteral(li % ri, LiteralKind.Int) { TypeRef = TypeRef.Int },
                BinaryOp.Lt => new IrLiteral(li < ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Gt => new IrLiteral(li > ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Le => new IrLiteral(li <= ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Ge => new IrLiteral(li >= ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Eq => new IrLiteral(li == ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Ne => new IrLiteral(li != ri, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                _ => null,
            };
        }

        // float 同士の演算
        if (lhs.Kind == LiteralKind.Float && rhs.Kind == LiteralKind.Float
            && lhs.Value is float lf && rhs.Value is float rf)
        {
            return op switch
            {
                BinaryOp.Add => new IrLiteral(lf + rf, LiteralKind.Float) { TypeRef = TypeRef.Float },
                BinaryOp.Sub => new IrLiteral(lf - rf, LiteralKind.Float) { TypeRef = TypeRef.Float },
                BinaryOp.Mul => new IrLiteral(lf * rf, LiteralKind.Float) { TypeRef = TypeRef.Float },
                BinaryOp.Div when rf != 0f => new IrLiteral(lf / rf, LiteralKind.Float) { TypeRef = TypeRef.Float },
                BinaryOp.Mod when rf != 0f => new IrLiteral(lf % rf, LiteralKind.Float) { TypeRef = TypeRef.Float },
                BinaryOp.Lt => new IrLiteral(lf < rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Gt => new IrLiteral(lf > rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Le => new IrLiteral(lf <= rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Ge => new IrLiteral(lf >= rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Eq => new IrLiteral(lf == rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Ne => new IrLiteral(lf != rf, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                _ => null,
            };
        }

        // bool 同士の等値演算
        if (lhs.Kind == LiteralKind.Bool && rhs.Kind == LiteralKind.Bool
            && lhs.Value is bool lb && rhs.Value is bool rb)
        {
            return op switch
            {
                BinaryOp.Eq => new IrLiteral(lb == rb, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                BinaryOp.Ne => new IrLiteral(lb != rb, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// 単項演算のリテラル畳み込みを試行する。
    /// </summary>
    private static IrLiteral? TryFoldUnary(UnaryOp op, IrLiteral operand)
    {
        return (op, operand.Value, operand.Kind) switch
        {
            (UnaryOp.Not, bool b, LiteralKind.Bool) =>
                new IrLiteral(!b, LiteralKind.Bool) { TypeRef = TypeRef.Bool },
            (UnaryOp.Negate, int i, LiteralKind.Int) =>
                new IrLiteral(-i, LiteralKind.Int) { TypeRef = TypeRef.Int },
            (UnaryOp.Negate, float f, LiteralKind.Float) =>
                new IrLiteral(-f, LiteralKind.Float) { TypeRef = TypeRef.Float },
            _ => null,
        };
    }
}

// ═══════════════════════════════════════════════════════════
//  Pass 2: 到達不能ノード除去（Dead Node Elimination）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 到達不能ノード除去パス。
/// 常に Failure/Success が確定するパスを検出し、到達不能なノードを除去する。
///
/// 適用例:
/// <code>
/// ;; Sequence の先頭で常に Failure → 後続は到達不能
/// (ir-sequence
///   (ir-condition (ir-literal false :bool))
///   (ir-action "Attack" ()))
/// → (ir-condition (ir-literal false :bool))
/// </code>
/// </summary>
public class DeadNodeEliminationPass : IIrPass
{
    /// <inheritdoc/>
    public IrNode Run(IrNode root) => Eliminate(root);

    /// <summary>ノードを再帰的に走査し、到達不能ノードを除去する。</summary>
    private IrNode Eliminate(IrNode node)
    {
        switch (node)
        {
            case IrTree tree:
                return new IrTree(tree.Name, EliminateChildren(tree.Body))
                    { TypeRef = tree.TypeRef, Id = tree.Id, Origin = tree.Origin };

            case IrSequence seq:
            {
                var children = new List<IrNode>();
                foreach (var child in seq.Children)
                {
                    var optimized = Eliminate(child);
                    children.Add(optimized);
                    // 常に Failure を返すノードの後は到達不能
                    if (IsAlwaysFailure(optimized))
                        break;
                }
                if (children.Count == 1) return children[0];
                return new IrSequence(children.ToImmutableArray())
                    { TypeRef = seq.TypeRef, Id = seq.Id, Origin = seq.Origin };
            }

            case IrSelector sel:
            {
                var children = new List<IrNode>();
                foreach (var child in sel.Children)
                {
                    var optimized = Eliminate(child);
                    children.Add(optimized);
                    // 常に Success を返すノードの後は到達不能
                    if (IsAlwaysSuccess(optimized))
                        break;
                }
                if (children.Count == 1) return children[0];
                return new IrSelector(children.ToImmutableArray())
                    { TypeRef = sel.TypeRef, Id = sel.Id, Origin = sel.Origin };
            }

            case IrParallel par:
                return new IrParallel(par.Policy, EliminateChildren(par.Children))
                    { TypeRef = par.TypeRef, Id = par.Id, Origin = par.Origin };

            case IrGuard grd:
                return new IrGuard(grd.Condition, Eliminate(grd.Body))
                    { TypeRef = grd.TypeRef, Id = grd.Id, Origin = grd.Origin };

            case IrIf ifn:
                return new IrIf(ifn.Condition, Eliminate(ifn.Then), ifn.Else != null ? Eliminate(ifn.Else) : null)
                    { TypeRef = ifn.TypeRef, Id = ifn.Id, Origin = ifn.Origin };

            case IrInvert inv:
                return new IrInvert(Eliminate(inv.Child))
                    { TypeRef = inv.TypeRef, Id = inv.Id, Origin = inv.Origin };

            case IrRepeat rep:
                return new IrRepeat(rep.Count, Eliminate(rep.Child))
                    { TypeRef = rep.TypeRef, Id = rep.Id, Origin = rep.Origin };

            case IrTimeout tmo:
                return new IrTimeout(tmo.Seconds, Eliminate(tmo.Child))
                    { TypeRef = tmo.TypeRef, Id = tmo.Id, Origin = tmo.Origin };

            case IrCooldown cld:
                return new IrCooldown(cld.Seconds, Eliminate(cld.Child))
                    { TypeRef = cld.TypeRef, Id = cld.Id, Origin = cld.Origin };

            case IrWhile whl:
                return new IrWhile(whl.Condition, Eliminate(whl.Body))
                    { TypeRef = whl.TypeRef, Id = whl.Id, Origin = whl.Origin };

            case IrReactive rct:
                return new IrReactive(rct.Condition, Eliminate(rct.Body))
                    { TypeRef = rct.TypeRef, Id = rct.Id, Origin = rct.Origin };

            case IrReactiveSelect rsel:
                return new IrReactiveSelect(EliminateChildren(rsel.Children))
                    { TypeRef = rsel.TypeRef, Id = rsel.Id, Origin = rsel.Origin };

            default:
                return node;
        }
    }

    /// <summary>子ノード配列を再帰的に走査する。</summary>
    private ImmutableArray<IrNode> EliminateChildren(ImmutableArray<IrNode> children)
        => children.Select(Eliminate).ToImmutableArray();

    /// <summary>ノードが常に Failure を返すか判定する。</summary>
    private static bool IsAlwaysFailure(IrNode node) => node switch
    {
        IrCondition { Expr: IrLiteral { Kind: LiteralKind.Bool, Value: false } } => true,
        _ => false,
    };

    /// <summary>ノードが常に Success を返すか判定する。</summary>
    private static bool IsAlwaysSuccess(IrNode node) => node switch
    {
        IrCondition { Expr: IrLiteral { Kind: LiteralKind.Bool, Value: true } } => true,
        _ => false,
    };
}

// ═══════════════════════════════════════════════════════════
//  Pass 3: 単一子ノード畳み込み（Single-Child Collapse）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 単一子ノード畳み込みパス。
/// 子が1つしかない Selector/Sequence を除去し、子ノードに置き換える。
/// また、二重反転（invert(invert(x))）を除去する。
///
/// 適用例:
/// <code>
/// (ir-selector (ir-action "Patrol" ()))
/// → (ir-action "Patrol" ())
///
/// (ir-invert (ir-invert child))
/// → child
/// </code>
/// </summary>
public class SingleChildCollapsePass : IIrPass
{
    /// <inheritdoc/>
    public IrNode Run(IrNode root) => Collapse(root);

    /// <summary>ノードを再帰的に走査し、単一子ノードを畳み込む。</summary>
    private IrNode Collapse(IrNode node)
    {
        switch (node)
        {
            case IrTree tree:
                return new IrTree(tree.Name, CollapseChildren(tree.Body))
                    { TypeRef = tree.TypeRef, Id = tree.Id, Origin = tree.Origin };

            case IrSelector sel:
            {
                var children = CollapseChildren(sel.Children);
                return children.Length == 1 ? children[0] : new IrSelector(children)
                    { TypeRef = sel.TypeRef, Id = sel.Id, Origin = sel.Origin };
            }

            case IrSequence seq:
            {
                var children = CollapseChildren(seq.Children);
                return children.Length == 1 ? children[0] : new IrSequence(children)
                    { TypeRef = seq.TypeRef, Id = seq.Id, Origin = seq.Origin };
            }

            case IrParallel par:
                return new IrParallel(par.Policy, CollapseChildren(par.Children))
                    { TypeRef = par.TypeRef, Id = par.Id, Origin = par.Origin };

            case IrInvert inv:
            {
                var child = Collapse(inv.Child);
                // 二重反転の除去: invert(invert(x)) → x
                if (child is IrInvert inner)
                    return inner.Child;
                return new IrInvert(child)
                    { TypeRef = inv.TypeRef, Id = inv.Id, Origin = inv.Origin };
            }

            case IrGuard grd:
                return new IrGuard(grd.Condition, Collapse(grd.Body))
                    { TypeRef = grd.TypeRef, Id = grd.Id, Origin = grd.Origin };

            case IrIf ifn:
                return new IrIf(ifn.Condition, Collapse(ifn.Then), ifn.Else != null ? Collapse(ifn.Else) : null)
                    { TypeRef = ifn.TypeRef, Id = ifn.Id, Origin = ifn.Origin };

            case IrRepeat rep:
                return new IrRepeat(rep.Count, Collapse(rep.Child))
                    { TypeRef = rep.TypeRef, Id = rep.Id, Origin = rep.Origin };

            case IrTimeout tmo:
                return new IrTimeout(tmo.Seconds, Collapse(tmo.Child))
                    { TypeRef = tmo.TypeRef, Id = tmo.Id, Origin = tmo.Origin };

            case IrCooldown cld:
                return new IrCooldown(cld.Seconds, Collapse(cld.Child))
                    { TypeRef = cld.TypeRef, Id = cld.Id, Origin = cld.Origin };

            case IrWhile whl:
                return new IrWhile(whl.Condition, Collapse(whl.Body))
                    { TypeRef = whl.TypeRef, Id = whl.Id, Origin = whl.Origin };

            case IrReactive rct:
                return new IrReactive(rct.Condition, Collapse(rct.Body))
                    { TypeRef = rct.TypeRef, Id = rct.Id, Origin = rct.Origin };

            case IrReactiveSelect rsel:
                return new IrReactiveSelect(CollapseChildren(rsel.Children))
                    { TypeRef = rsel.TypeRef, Id = rsel.Id, Origin = rsel.Origin };

            default:
                return node;
        }
    }

    /// <summary>子ノード配列を再帰的に畳み込む。</summary>
    private ImmutableArray<IrNode> CollapseChildren(ImmutableArray<IrNode> children)
        => children.Select(Collapse).ToImmutableArray();
}

// ═══════════════════════════════════════════════════════════
//  Pass 5: 型変換統合（Convert Fusion）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 型変換統合パス。
/// 冗長な型変換を除去・統合する。
///
/// 適用例:
/// <code>
/// ;; 二重変換の統合
/// (ir-convert (ir-convert expr :to float) :to float)
/// → (ir-convert expr :to float)
///
/// ;; リテラルへの型変換のインライン化
/// (ir-convert (ir-literal 42 :int) :to float)
/// → (ir-literal 42.0 :float)
/// </code>
/// </summary>
public class ConvertFusionPass : IIrPass
{
    /// <inheritdoc/>
    public IrNode Run(IrNode root) => Fuse(root);

    /// <summary>ノードを再帰的に走査し、型変換を統合する。</summary>
    private IrNode Fuse(IrNode node) => node switch
    {
        IrTree tree => new IrTree(tree.Name, FuseChildren(tree.Body))
            { TypeRef = tree.TypeRef, Id = tree.Id, Origin = tree.Origin },
        IrSelector sel => new IrSelector(FuseChildren(sel.Children))
            { TypeRef = sel.TypeRef, Id = sel.Id, Origin = sel.Origin },
        IrSequence seq => new IrSequence(FuseChildren(seq.Children))
            { TypeRef = seq.TypeRef, Id = seq.Id, Origin = seq.Origin },
        IrParallel par => new IrParallel(par.Policy, FuseChildren(par.Children))
            { TypeRef = par.TypeRef, Id = par.Id, Origin = par.Origin },
        IrCondition cond => new IrCondition(FuseExpr(cond.Expr))
            { TypeRef = cond.TypeRef, Id = cond.Id, Origin = cond.Origin },
        IrGuard grd => new IrGuard(FuseExpr(grd.Condition), Fuse(grd.Body))
            { TypeRef = grd.TypeRef, Id = grd.Id, Origin = grd.Origin },
        IrIf ifn => new IrIf(FuseExpr(ifn.Condition), Fuse(ifn.Then), ifn.Else != null ? Fuse(ifn.Else) : null)
            { TypeRef = ifn.TypeRef, Id = ifn.Id, Origin = ifn.Origin },
        IrInvert inv => new IrInvert(Fuse(inv.Child))
            { TypeRef = inv.TypeRef, Id = inv.Id, Origin = inv.Origin },
        IrRepeat rep => new IrRepeat(rep.Count, Fuse(rep.Child))
            { TypeRef = rep.TypeRef, Id = rep.Id, Origin = rep.Origin },
        IrTimeout tmo => new IrTimeout(tmo.Seconds, Fuse(tmo.Child))
            { TypeRef = tmo.TypeRef, Id = tmo.Id, Origin = tmo.Origin },
        IrCooldown cld => new IrCooldown(cld.Seconds, Fuse(cld.Child))
            { TypeRef = cld.TypeRef, Id = cld.Id, Origin = cld.Origin },
        IrWhile whl => new IrWhile(FuseExpr(whl.Condition), Fuse(whl.Body))
            { TypeRef = whl.TypeRef, Id = whl.Id, Origin = whl.Origin },
        IrReactive rct => new IrReactive(FuseExpr(rct.Condition), Fuse(rct.Body))
            { TypeRef = rct.TypeRef, Id = rct.Id, Origin = rct.Origin },
        IrReactiveSelect rsel => new IrReactiveSelect(FuseChildren(rsel.Children))
            { TypeRef = rsel.TypeRef, Id = rsel.Id, Origin = rsel.Origin },
        IrAction act => new IrAction(act.Method, FuseExprs(act.Args))
            { TypeRef = act.TypeRef, Id = act.Id, Origin = act.Origin },
        IrExpr expr => FuseExpr(expr),
        _ => node,
    };

    /// <summary>子ノード配列を再帰的に走査する。</summary>
    private ImmutableArray<IrNode> FuseChildren(ImmutableArray<IrNode> children)
        => children.Select(Fuse).ToImmutableArray();

    /// <summary>式配列を再帰的に走査する。</summary>
    private ImmutableArray<IrExpr> FuseExprs(ImmutableArray<IrExpr> exprs)
        => exprs.Select(FuseExpr).ToImmutableArray();

    /// <summary>
    /// 式を再帰的に走査し、型変換を統合する。
    /// </summary>
    private IrExpr FuseExpr(IrExpr expr)
    {
        switch (expr)
        {
            case IrConvert conv:
            {
                var operand = FuseExpr(conv.Operand);

                // 二重変換の統合: convert(convert(e, T), T) → convert(e, T)
                if (operand is IrConvert inner && inner.TargetType == conv.TargetType)
                    return new IrConvert(inner.Operand, conv.TargetType) { TypeRef = conv.TypeRef };

                // リテラルの型変換インライン化: convert(literal 42 int, float) → literal 42.0 float
                if (operand is IrLiteral lit && conv.TargetType == TypeRef.Float && lit.Kind == LiteralKind.Int)
                {
                    var floatVal = Convert.ToSingle(lit.Value, CultureInfo.InvariantCulture);
                    return new IrLiteral(floatVal, LiteralKind.Float) { TypeRef = TypeRef.Float };
                }

                return new IrConvert(operand, conv.TargetType)
                    { TypeRef = conv.TypeRef, Id = conv.Id, Origin = conv.Origin };
            }

            case IrBinaryOp bin:
                return new IrBinaryOp(bin.Op, FuseExpr(bin.Lhs), FuseExpr(bin.Rhs))
                    { TypeRef = bin.TypeRef, Id = bin.Id, Origin = bin.Origin };

            case IrUnaryOp un:
                return new IrUnaryOp(un.Op, FuseExpr(un.Operand))
                    { TypeRef = un.TypeRef, Id = un.Id, Origin = un.Origin };

            case IrLogicOp log:
                return new IrLogicOp(log.Op, log.Operands.Select(FuseExpr).ToImmutableArray())
                    { TypeRef = log.TypeRef, Id = log.Id, Origin = log.Origin };

            case IrCall call:
                return new IrCall(call.Method, call.Args.Select(FuseExpr).ToImmutableArray())
                    { TypeRef = call.TypeRef, Id = call.Id, Origin = call.Origin };

            default:
                return expr;
        }
    }
}
