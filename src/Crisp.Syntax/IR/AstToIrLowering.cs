using System.Collections.Immutable;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

/// <summary>
/// AST（抽象構文木）から IR（中間表現）への変換を行う。
///
/// このクラスは Roslyn に依存しないスタンドアロンモードの lowering を提供する。
/// 型情報は AST のリテラル種別とメンバーパスから推論される。
///
/// 変換規則:
/// <list type="bullet">
///   <item><description>AstTree → IrTree</description></item>
///   <item><description>AstSelector → IrSelector</description></item>
///   <item><description>AstSequence → IrSequence</description></item>
///   <item><description>AstParallel → IrParallel</description></item>
///   <item><description>AstCheck → IrCondition</description></item>
///   <item><description>AstActionCall → IrAction</description></item>
///   <item><description>AstGuard → IrGuard</description></item>
///   <item><description>AstIf → IrIf</description></item>
///   <item><description>AstInvert → IrInvert</description></item>
///   <item><description>AstRepeat → IrRepeat</description></item>
///   <item><description>AstTimeout → IrTimeout</description></item>
///   <item><description>AstCooldown → IrCooldown</description></item>
///   <item><description>AstWhile → IrWhile</description></item>
/// </list>
///
/// 式の変換:
/// <list type="bullet">
///   <item><description>AstMemberAccessExpr → IrMemberLoad</description></item>
///   <item><description>AstLiteralExpr → IrLiteral</description></item>
///   <item><description>AstBinaryExpr → IrBinaryOp（int+float 混合時に IrConvert を挿入）</description></item>
///   <item><description>AstUnaryExpr → IrUnaryOp</description></item>
///   <item><description>AstLogicExpr → IrLogicOp</description></item>
///   <item><description>AstCallExpr → IrCall</description></item>
/// </list>
/// </summary>
public static class AstToIrLowering
{
    /// <summary>
    /// AST ツリーの配列を IR ツリーの配列に変換する。
    /// </summary>
    /// <param name="trees">lowering 対象の AST ツリー配列。</param>
    /// <returns>変換された IR ツリーの配列。</returns>
    public static ImmutableArray<IrTree> Lower(ImmutableArray<AstTree> trees)
    {
        return trees.Select(LowerTree).ToImmutableArray();
    }

    /// <summary>
    /// 単一の AST ツリーを IR ツリーに変換する。
    /// </summary>
    /// <param name="tree">lowering 対象の AST ツリー。</param>
    /// <returns>変換された IR ツリー。</returns>
    public static IrTree LowerTree(AstTree tree)
    {
        var body = tree.Body.Select(LowerNode).ToImmutableArray();
        return new IrTree(tree.Name, body) { TypeRef = TypeRef.BtStatus, Id = tree.CstOrigin.Id, Origin = tree.CstOrigin.Span };
    }

    // ──────────────────────────────────────────────
    //  ノード位置の lowering
    // ──────────────────────────────────────────────

    /// <summary>
    /// AST ノードを IR ノードに変換する。
    /// ノードの種別に応じて適切な IR ノードを生成する。
    /// </summary>
    /// <param name="node">変換対象の AST ノード。</param>
    /// <returns>変換された IR ノード。</returns>
    /// <exception cref="InvalidOperationException">未知の AST ノード型の場合。</exception>
    private static IrNode LowerNode(AstNode node) => node switch
    {
        AstSelector sel => LowerSelector(sel),
        AstSequence seq => LowerSequence(seq),
        AstParallel par => LowerParallel(par),
        AstCheck chk => LowerCheck(chk),
        AstActionCall act => LowerActionCall(act),
        AstGuard grd => LowerGuard(grd),
        AstIf ifn => LowerIf(ifn),
        AstInvert inv => LowerInvert(inv),
        AstRepeat rep => LowerRepeat(rep),
        AstTimeout tmo => LowerTimeout(tmo),
        AstCooldown cld => LowerCooldown(cld),
        AstWhile whl => LowerWhile(whl),
        AstReactive reactive => LowerReactive(reactive),
        AstReactiveSelect rsel => LowerReactiveSelect(rsel),
        AstRef astRef => LowerRef(astRef),
        _ => throw new InvalidOperationException(
            $"未対応の AST ノード型: {node.GetType().Name}"),
    };

    /// <summary>セレクターノードの lowering。全子ノードを再帰的に変換する。</summary>
    private static IrSelector LowerSelector(AstSelector sel)
    {
        var children = sel.Children_.Select(LowerNode).ToImmutableArray();
        return new IrSelector(children) { TypeRef = TypeRef.BtStatus, Id = sel.CstOrigin.Id, Origin = sel.CstOrigin.Span };
    }

    /// <summary>シーケンスノードの lowering。全子ノードを再帰的に変換する。</summary>
    private static IrSequence LowerSequence(AstSequence seq)
    {
        var children = seq.Children_.Select(LowerNode).ToImmutableArray();
        return new IrSequence(children) { TypeRef = TypeRef.BtStatus, Id = seq.CstOrigin.Id, Origin = seq.CstOrigin.Span };
    }

    /// <summary>パラレルノードの lowering。ポリシーを保持しつつ全子ノードを変換する。</summary>
    private static IrParallel LowerParallel(AstParallel par)
    {
        var children = par.Children_.Select(LowerNode).ToImmutableArray();
        return new IrParallel(par.Policy, children) { TypeRef = TypeRef.BtStatus, Id = par.CstOrigin.Id, Origin = par.CstOrigin.Span };
    }

    /// <summary>チェックノードの lowering。条件式を IrExpr に変換し、IrCondition を生成する。</summary>
    private static IrCondition LowerCheck(AstCheck chk)
    {
        var expr = LowerExpr(chk.Expr);
        return new IrCondition(expr) { TypeRef = TypeRef.BtStatus, Id = chk.CstOrigin.Id, Origin = chk.CstOrigin.Span };
    }

    /// <summary>
    /// アクション呼び出しの lowering。メンバーパスから MethodRef を構築する。
    ///
    /// メンバーパスの分解規則:
    /// <list type="bullet">
    ///   <item><description>単一セグメント（例: ["Patrol"]）→ 宣言型 "this"、メソッド名 "Patrol"</description></item>
    ///   <item><description>複数セグメント（例: ["Nav", "MoveTo"]）→ 宣言型 "Nav"（先頭〜末尾-1を結合）、メソッド名 "MoveTo"</description></item>
    /// </list>
    /// </summary>
    private static IrAction LowerActionCall(AstActionCall act)
    {
        var methodRef = MemberPathToMethodRef(act.Member, act.Args);
        var args = act.Args.Select(LowerExpr).ToImmutableArray();
        return new IrAction(methodRef, args) { TypeRef = TypeRef.BtStatus, Id = act.CstOrigin.Id, Origin = act.CstOrigin.Span };
    }

    /// <summary>ガードノードの lowering。条件式と本体ノードをそれぞれ変換する。</summary>
    private static IrGuard LowerGuard(AstGuard grd)
    {
        var condition = LowerExpr(grd.Condition);
        var body = LowerNode(grd.Body);
        return new IrGuard(condition, body) { TypeRef = TypeRef.BtStatus, Id = grd.CstOrigin.Id, Origin = grd.CstOrigin.Span };
    }

    /// <summary>if ノードの lowering。条件式・then 節・else 節（任意）を変換する。</summary>
    private static IrIf LowerIf(AstIf ifn)
    {
        var condition = LowerExpr(ifn.Condition);
        var then = LowerNode(ifn.Then);
        var @else = ifn.Else != null ? LowerNode(ifn.Else) : null;
        return new IrIf(condition, then, @else) { TypeRef = TypeRef.BtStatus, Id = ifn.CstOrigin.Id, Origin = ifn.CstOrigin.Span };
    }

    /// <summary>反転ノードの lowering。子ノードを変換する。</summary>
    private static IrInvert LowerInvert(AstInvert inv)
    {
        var child = LowerNode(inv.Child);
        return new IrInvert(child) { TypeRef = TypeRef.BtStatus, Id = inv.CstOrigin.Id, Origin = inv.CstOrigin.Span };
    }

    /// <summary>リピートノードの lowering。繰り返し回数を保持しつつ子ノードを変換する。</summary>
    private static IrRepeat LowerRepeat(AstRepeat rep)
    {
        var child = LowerNode(rep.Child);
        return new IrRepeat(rep.Count, child) { TypeRef = TypeRef.BtStatus, Id = rep.CstOrigin.Id, Origin = rep.CstOrigin.Span };
    }

    /// <summary>タイムアウトノードの lowering。秒数を保持しつつ子ノードを変換する。</summary>
    private static IrTimeout LowerTimeout(AstTimeout tmo)
    {
        var child = LowerNode(tmo.Child);
        return new IrTimeout(tmo.Seconds, child) { TypeRef = TypeRef.BtStatus, Id = tmo.CstOrigin.Id, Origin = tmo.CstOrigin.Span };
    }

    /// <summary>クールダウンノードの lowering。秒数を保持しつつ子ノードを変換する。</summary>
    private static IrCooldown LowerCooldown(AstCooldown cld)
    {
        var child = LowerNode(cld.Child);
        return new IrCooldown(cld.Seconds, child) { TypeRef = TypeRef.BtStatus, Id = cld.CstOrigin.Id, Origin = cld.CstOrigin.Span };
    }

    /// <summary>while ノードの lowering。条件式と本体ノードをそれぞれ変換する。</summary>
    private static IrWhile LowerWhile(AstWhile whl)
    {
        var condition = LowerExpr(whl.Condition);
        var body = LowerNode(whl.Body);
        return new IrWhile(condition, body) { TypeRef = TypeRef.BtStatus, Id = whl.CstOrigin.Id, Origin = whl.CstOrigin.Span };
    }

    // ──────────────────────────────────────────────
    //  リアクティブノード（F1）の lowering
    // ──────────────────────────────────────────────

    /// <summary>リアクティブノードの lowering。条件式と本体ノードをそれぞれ変換する。</summary>
    private static IrReactive LowerReactive(AstReactive reactive)
    {
        var condition = LowerExpr(reactive.Condition);
        var body = LowerNode(reactive.Body);
        return new IrReactive(condition, body) { TypeRef = TypeRef.BtStatus, Id = reactive.CstOrigin.Id, Origin = reactive.CstOrigin.Span };
    }

    /// <summary>リアクティブセレクターノードの lowering。全子ノードを再帰的に変換する。</summary>
    private static IrReactiveSelect LowerReactiveSelect(AstReactiveSelect rsel)
    {
        var children = rsel.Children_.Select(LowerNode).ToImmutableArray();
        return new IrReactiveSelect(children) { TypeRef = TypeRef.BtStatus, Id = rsel.CstOrigin.Id, Origin = rsel.CstOrigin.Span };
    }

    // ──────────────────────────────────────────────
    //  Multi-Tree ノード（F11）の lowering
    // ──────────────────────────────────────────────

    /// <summary>ツリー参照ノードの lowering。ツリー名を保持する IrTreeRef を生成する。</summary>
    private static IrTreeRef LowerRef(AstRef astRef)
    {
        return new IrTreeRef(astRef.TreeName) { TypeRef = TypeRef.BtStatus, Id = astRef.CstOrigin.Id, Origin = astRef.CstOrigin.Span };
    }

    // ──────────────────────────────────────────────
    //  サブツリーアノテーション（BtNode 戻りメソッドの識別）
    // ──────────────────────────────────────────────

    /// <summary>
    /// IR ツリー内の <see cref="IrAction"/> のうち、BtNode を返すメソッド呼び出しに
    /// <see cref="IrAction.IsSubtree"/> フラグを設定する。
    ///
    /// Roslyn 情報がない standalone lowering では全て IsSubtree=false で生成されるため、
    /// セマンティック解析後にこのメソッドでアノテーションを行う。
    /// </summary>
    /// <param name="trees">アノテーション対象の IR ツリー配列。</param>
    /// <param name="subtreeMethodNames">BtNode を返すメソッド名の集合。</param>
    /// <returns>IsSubtree フラグが設定された新しい IR ツリー配列。</returns>
    public static ImmutableArray<IrTree> AnnotateSubtrees(
        ImmutableArray<IrTree> trees, HashSet<string> subtreeMethodNames)
    {
        if (subtreeMethodNames.Count == 0) return trees;
        return trees.Select(t =>
        {
            var body = t.Body.Select(n => AnnotateNode(n, subtreeMethodNames)).ToImmutableArray();
            return new IrTree(t.Name, body) { TypeRef = t.TypeRef, Id = t.Id, Origin = t.Origin };
        }).ToImmutableArray();
    }

    private static IrNode AnnotateNode(IrNode node, HashSet<string> names) => node switch
    {
        IrAction act when names.Contains(act.Method.Name)
            => new IrAction(act.Method, act.Args, act.IsAsync, IsSubtree: true)
                { TypeRef = TypeRef.BtNode, Id = act.Id, Origin = act.Origin },
        IrSelector sel => new IrSelector(AnnotateChildren(sel.Children, names))
            { TypeRef = sel.TypeRef, Id = sel.Id, Origin = sel.Origin },
        IrSequence seq => new IrSequence(AnnotateChildren(seq.Children, names))
            { TypeRef = seq.TypeRef, Id = seq.Id, Origin = seq.Origin },
        IrParallel par => new IrParallel(par.Policy, AnnotateChildren(par.Children, names))
            { TypeRef = par.TypeRef, Id = par.Id, Origin = par.Origin },
        IrGuard grd => new IrGuard(grd.Condition, AnnotateNode(grd.Body, names))
            { TypeRef = grd.TypeRef, Id = grd.Id, Origin = grd.Origin },
        IrIf ifn => new IrIf(ifn.Condition, AnnotateNode(ifn.Then, names),
            ifn.Else != null ? AnnotateNode(ifn.Else, names) : null)
            { TypeRef = ifn.TypeRef, Id = ifn.Id, Origin = ifn.Origin },
        IrInvert inv => new IrInvert(AnnotateNode(inv.Child, names))
            { TypeRef = inv.TypeRef, Id = inv.Id, Origin = inv.Origin },
        IrRepeat rep => new IrRepeat(rep.Count, AnnotateNode(rep.Child, names))
            { TypeRef = rep.TypeRef, Id = rep.Id, Origin = rep.Origin },
        IrTimeout tmo => new IrTimeout(tmo.Seconds, AnnotateNode(tmo.Child, names))
            { TypeRef = tmo.TypeRef, Id = tmo.Id, Origin = tmo.Origin },
        IrCooldown cld => new IrCooldown(cld.Seconds, AnnotateNode(cld.Child, names))
            { TypeRef = cld.TypeRef, Id = cld.Id, Origin = cld.Origin },
        IrWhile whl => new IrWhile(whl.Condition, AnnotateNode(whl.Body, names))
            { TypeRef = whl.TypeRef, Id = whl.Id, Origin = whl.Origin },
        IrReactive rct => new IrReactive(rct.Condition, AnnotateNode(rct.Body, names))
            { TypeRef = rct.TypeRef, Id = rct.Id, Origin = rct.Origin },
        IrReactiveSelect rsel => new IrReactiveSelect(AnnotateChildren(rsel.Children, names))
            { TypeRef = rsel.TypeRef, Id = rsel.Id, Origin = rsel.Origin },
        _ => node,
    };

    private static ImmutableArray<IrNode> AnnotateChildren(
        ImmutableArray<IrNode> children, HashSet<string> names)
        => children.Select(n => AnnotateNode(n, names)).ToImmutableArray();

    // ──────────────────────────────────────────────
    //  式位置の lowering
    // ──────────────────────────────────────────────

    /// <summary>
    /// AST 式を IR 式に変換する。
    /// 式の種別に応じて適切な IR 式ノードを生成し、型情報を推論する。
    /// </summary>
    /// <param name="expr">変換対象の AST 式。</param>
    /// <returns>変換された IR 式。</returns>
    /// <exception cref="InvalidOperationException">未知の AST 式型の場合。</exception>
    private static IrExpr LowerExpr(AstExpr expr) => expr switch
    {
        AstMemberAccessExpr mem => LowerMemberAccess(mem),
        AstLiteralExpr lit => LowerLiteral(lit),
        AstBinaryExpr bin => LowerBinary(bin),
        AstUnaryExpr un => LowerUnary(un),
        AstLogicExpr log => LowerLogic(log),
        AstBlackboardAccessExpr bb => LowerBlackboardAccess(bb),
        AstCallExpr call => LowerCall(call),
        _ => throw new InvalidOperationException(
            $"未対応の AST 式型: {expr.GetType().Name}"),
    };

    /// <summary>
    /// ブラックボードアクセス式の lowering。MemberPath を MemberRef に変換し IrBlackboardLoad を生成する。
    /// </summary>
    private static IrBlackboardLoad LowerBlackboardAccess(AstBlackboardAccessExpr bb)
    {
        var memberRef = new MemberRef(bb.Path.Segments);
        return new IrBlackboardLoad(memberRef) { TypeRef = new TypeRef("unknown"), Id = bb.CstOrigin.Id, Origin = bb.CstOrigin.Span };
    }

    /// <summary>
    /// メンバーアクセス式の lowering。MemberPath を MemberRef に変換する。
    /// スタンドアロンモードでは型推論不可のため、TypeRef は汎用的な "unknown" を使用する。
    /// </summary>
    private static IrMemberLoad LowerMemberAccess(AstMemberAccessExpr mem)
    {
        var memberRef = new MemberRef(mem.Path.Segments);
        return new IrMemberLoad(memberRef) { TypeRef = new TypeRef("unknown"), Id = mem.CstOrigin.Id, Origin = mem.CstOrigin.Span };
    }

    /// <summary>
    /// リテラル式の lowering。LiteralKind から TypeRef を推論する。
    ///
    /// TypeRef マッピング:
    /// <list type="bullet">
    ///   <item><description>Int → TypeRef.Int</description></item>
    ///   <item><description>Float → TypeRef.Float</description></item>
    ///   <item><description>Bool → TypeRef.Bool</description></item>
    ///   <item><description>String → TypeRef.String</description></item>
    ///   <item><description>Null → TypeRef("null")</description></item>
    ///   <item><description>Enum → TypeRef(enumRef.TypeName)</description></item>
    /// </list>
    /// </summary>
    private static IrLiteral LowerLiteral(AstLiteralExpr lit)
    {
        var typeRef = LiteralKindToTypeRef(lit.LiteralKind, lit.Value);
        return new IrLiteral(lit.Value, lit.LiteralKind) { TypeRef = typeRef, Id = lit.CstOrigin.Id, Origin = lit.CstOrigin.Span };
    }

    /// <summary>
    /// 二項演算式の lowering。必要に応じて int → float の暗黙型変換（IrConvert）を挿入する。
    ///
    /// IrConvert 挿入規則:
    /// 算術演算（Add, Sub, Mul, Div, Mod）で int と float が混在する場合、
    /// int 側のオペランドを IrConvert で float に昇格させる。
    ///
    /// 比較演算（Lt, Gt, Le, Ge, Eq, Ne）でも同様に型昇格を行う。
    /// </summary>
    private static IrBinaryOp LowerBinary(AstBinaryExpr bin)
    {
        var lhs = LowerExpr(bin.Lhs);
        var rhs = LowerExpr(bin.Rhs);

        // int + float の混合演算の場合、int 側を IrConvert で float に昇格
        (lhs, rhs) = InsertNumericPromotions(lhs, rhs);

        // 結果型の決定: 比較演算子は bool、算術演算子はオペランドの型に従う
        var resultType = IsComparisonOp(bin.Op)
            ? TypeRef.Bool
            : DetermineArithmeticResultType(lhs.TypeRef, rhs.TypeRef);

        return new IrBinaryOp(bin.Op, lhs, rhs) { TypeRef = resultType, Id = bin.CstOrigin.Id, Origin = bin.CstOrigin.Span };
    }

    /// <summary>単項演算式の lowering。演算子種別に応じて結果型を決定する。</summary>
    private static IrUnaryOp LowerUnary(AstUnaryExpr un)
    {
        var operand = LowerExpr(un.Operand);
        var resultType = un.Op == UnaryOp.Not ? TypeRef.Bool : operand.TypeRef;
        return new IrUnaryOp(un.Op, operand) { TypeRef = resultType, Id = un.CstOrigin.Id, Origin = un.CstOrigin.Span };
    }

    /// <summary>論理演算式の lowering。全オペランドを変換し、結果型は常に bool。</summary>
    private static IrLogicOp LowerLogic(AstLogicExpr log)
    {
        var operands = log.Operands.Select(LowerExpr).ToImmutableArray();
        return new IrLogicOp(log.Op, operands) { TypeRef = TypeRef.Bool, Id = log.CstOrigin.Id, Origin = log.CstOrigin.Span };
    }

    /// <summary>
    /// メソッド呼び出し式（式位置）の lowering。MemberPath から MethodRef を構築する。
    /// スタンドアロンモードでは戻り値の型推論不可のため、TypeRef は "unknown" を使用する。
    /// </summary>
    private static IrCall LowerCall(AstCallExpr call)
    {
        var methodRef = MemberPathToMethodRef(call.Member, call.Args);
        var args = call.Args.Select(LowerExpr).ToImmutableArray();
        return new IrCall(methodRef, args) { TypeRef = new TypeRef("unknown"), Id = call.CstOrigin.Id, Origin = call.CstOrigin.Span };
    }

    // ──────────────────────────────────────────────
    //  ヘルパーメソッド
    // ──────────────────────────────────────────────

    /// <summary>
    /// MemberPath から MethodRef を構築する。
    ///
    /// セグメントの分解:
    /// <list type="bullet">
    ///   <item><description>1セグメント（例: ["Patrol"]）→ DeclaringType="this", Name="Patrol"</description></item>
    ///   <item><description>2+セグメント（例: ["Nav", "MoveTo"]）→ DeclaringType="Nav", Name="MoveTo"</description></item>
    /// </list>
    ///
    /// スタンドアロンモードではパラメータ型の推論不可のため、空の ParamTypes を使用する。
    /// </summary>
    private static MethodRef MemberPathToMethodRef(MemberPath member, ImmutableArray<AstExpr> args)
    {
        var segments = member.Segments;
        string declaringType;
        string methodName;

        if (segments.Length == 1)
        {
            // 単一セグメント: this のメソッド呼び出し
            declaringType = "this";
            methodName = segments[0];
        }
        else
        {
            // 複数セグメント: 先頭部分が宣言型、末尾がメソッド名
            methodName = segments[^1];
            declaringType = string.Join(".", segments.Take(segments.Length - 1));
        }

        // スタンドアロンモードではパラメータ型情報が不明なため空配列
        return new MethodRef(declaringType, methodName, ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// LiteralKind から対応する TypeRef を決定する。
    /// Enum リテラルの場合は EnumRef の型名から TypeRef を構築する。
    /// </summary>
    private static TypeRef LiteralKindToTypeRef(LiteralKind kind, object? value) => kind switch
    {
        LiteralKind.Int => TypeRef.Int,
        LiteralKind.Float => TypeRef.Float,
        LiteralKind.Bool => TypeRef.Bool,
        LiteralKind.String => TypeRef.String,
        LiteralKind.Null => new TypeRef("null"),
        LiteralKind.Enum when value is EnumRef enumRef => new TypeRef(enumRef.TypeName),
        _ => new TypeRef("unknown"),
    };

    /// <summary>
    /// 二項演算の左右オペランドに対して数値型昇格（int → float）を挿入する。
    /// 片方が int で他方が float の場合、int 側に IrConvert を挿入する。
    /// </summary>
    private static (IrExpr lhs, IrExpr rhs) InsertNumericPromotions(IrExpr lhs, IrExpr rhs)
    {
        if (lhs.TypeRef == TypeRef.Int && rhs.TypeRef == TypeRef.Float)
        {
            // 左辺が int、右辺が float → 左辺を float に昇格
            lhs = new IrConvert(lhs, TypeRef.Float) { TypeRef = TypeRef.Float };
        }
        else if (lhs.TypeRef == TypeRef.Float && rhs.TypeRef == TypeRef.Int)
        {
            // 左辺が float、右辺が int → 右辺を float に昇格
            rhs = new IrConvert(rhs, TypeRef.Float) { TypeRef = TypeRef.Float };
        }

        return (lhs, rhs);
    }

    /// <summary>
    /// 二項演算子が比較演算子かどうかを判定する。
    /// 比較演算子の場合、結果型は常に bool になる。
    /// </summary>
    private static bool IsComparisonOp(BinaryOp op) => op switch
    {
        BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le or BinaryOp.Ge
            or BinaryOp.Eq or BinaryOp.Ne => true,
        _ => false,
    };

    /// <summary>
    /// 算術演算の結果型を決定する。
    /// いずれかのオペランドが float なら float、両方 int なら int。
    /// それ以外の場合は左辺の型を使用する。
    /// </summary>
    private static TypeRef DetermineArithmeticResultType(TypeRef lhs, TypeRef rhs)
    {
        if (lhs == TypeRef.Float || rhs == TypeRef.Float) return TypeRef.Float;
        if (lhs == TypeRef.Int && rhs == TypeRef.Int) return TypeRef.Int;
        return lhs;
    }
}
