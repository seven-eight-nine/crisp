using System.Collections.Immutable;
using System.Globalization;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Ast;

/// <summary>
/// CST（具象構文木）から AST（抽象構文木）への変換を行う。
///
/// 設計上の要点:
/// <list type="bullet">
///   <item><description>Trivia は全て破棄される</description></item>
///   <item><description><see cref="CstCall"/> は文脈（ノード位置 / 式位置）に応じて
///     <see cref="AstActionCall"/> または <see cref="AstCallExpr"/> に変換される</description></item>
///   <item><description>括弧なし <see cref="CstMemberAccess"/> がノード位置にある場合は
///     引数なし <see cref="AstActionCall"/> に変換される</description></item>
///   <item><description><see cref="CstMissing"/> / <see cref="CstError"/> は無視される
///     （Diagnostic はパーサーが既に生成済み）</description></item>
/// </list>
/// </summary>
public static class CstToAstLowering
{
    /// <summary>
    /// CST プログラムから AST ツリーの配列に変換する。
    /// </summary>
    /// <param name="program">パース済みの CST プログラム。</param>
    /// <returns>lowering された AST ツリーの配列。</returns>
    public static ImmutableArray<AstTree> Lower(CstProgram program)
    {
        return program.Trees.Select(LowerTree).ToImmutableArray();
    }

    /// <summary>
    /// CST プログラムから AST ツリーの配列と defdec 定義の配列を返す。
    /// F2: defdec 展開パスで使用する。
    /// </summary>
    /// <param name="program">パース済みの CST プログラム。</param>
    /// <returns>lowering された AST ツリーの配列と defdec 定義の配列のタプル。</returns>
    public static (ImmutableArray<AstTree> Trees, ImmutableArray<AstDefdec> Defdecs) LowerWithDefdecs(CstProgram program)
    {
        var trees = program.Trees.Select(LowerTree).ToImmutableArray();
        var defdecs = program.Defdecs.Select(LowerDefdec).ToImmutableArray();
        return (trees, defdecs);
    }

    /// <summary>
    /// CST プログラムから AST ツリーの配列、defdec 定義、および defmacro 定義を返す。
    /// F10: マクロ展開パスで使用する。
    /// </summary>
    /// <param name="program">パース済みの CST プログラム。</param>
    /// <returns>lowering された AST ツリー、defdec 定義、defmacro 定義のタプル。</returns>
    public static (ImmutableArray<AstTree> Trees, ImmutableArray<AstDefdec> Defdecs, ImmutableArray<AstDefmacro> Defmacros) LowerWithDefdecsAndDefmacros(CstProgram program)
    {
        var trees = program.Trees.Select(LowerTree).ToImmutableArray();
        var defdecs = program.Defdecs.Select(LowerDefdec).ToImmutableArray();
        var defmacros = program.Defmacros.Select(LowerDefmacro).ToImmutableArray();
        return (trees, defdecs, defmacros);
    }

    /// <summary>defdec 定義を lowering する。</summary>
    private static AstDefdec LowerDefdec(CstDefdec defdec)
    {
        var name = defdec.Name.Text;
        var parameters = defdec.Parameters.Select(p => p.Text).ToImmutableArray();
        var body = LowerNode(defdec.Body)!;
        return new AstDefdec(defdec, name, parameters, body);
    }

    /// <summary>
    /// defmacro 定義を lowering する（F10）。
    /// マクロは構文レベルで操作するため、body を生の CST ノードとして保持する。
    /// </summary>
    private static AstDefmacro LowerDefmacro(CstDefmacro defmacro)
    {
        var name = defmacro.Name.Text;
        var parameters = defmacro.Parameters.Select(p => p.Text).ToImmutableArray();
        return new AstDefmacro(defmacro, name, parameters, defmacro.Body);
    }

    /// <summary>ツリー定義を lowering する。</summary>
    private static AstTree LowerTree(CstTree tree)
    {
        var body = tree.Body
            .Select(LowerNode)
            .Where(n => n != null)
            .Cast<AstNode>()
            .ToImmutableArray();
        return new AstTree(tree, tree.Name.Text, body, tree.BlackboardType?.Text);
    }

    // ──────────────────────────────────────────────
    //  ノード位置の lowering
    // ──────────────────────────────────────────────

    /// <summary>
    /// ノード位置の CST ノードを AST ノードに変換する。
    /// CstMissing / CstError の場合は <c>null</c> を返す。
    /// </summary>
    private static AstNode? LowerNode(CstNode node)
    {
        return node switch
        {
            CstSelect select => LowerSelect(select),
            CstSequence seq => LowerSequence(seq),
            CstParallel parallel => LowerParallel(parallel),
            CstCheck check => LowerCheck(check),
            CstGuard guard => LowerGuard(guard),
            CstIf ifNode => LowerIf(ifNode),
            CstInvert invert => LowerInvert(invert),
            CstRepeat repeat => LowerRepeat(repeat),
            CstTimeout timeout => LowerTimeout(timeout),
            CstCooldown cooldown => LowerCooldown(cooldown),
            CstWhile whileNode => LowerWhile(whileNode),
            CstReactive reactive => LowerReactive(reactive),
            CstReactiveSelect reactiveSelect => LowerReactiveSelect(reactiveSelect),
            CstRef cstRef => LowerRef(cstRef),
            CstDefdecCall defdecCall => LowerDefdecCall(defdecCall),
            CstBodyPlaceholder bp => new AstBodyPlaceholder(bp),
            CstCall call => LowerCallAsNode(call),
            CstMemberAccess member => LowerMemberAccessAsNode(member),
            CstMissing or CstError => null, // エラーノードはスキップ
            _ => null,
        };
    }

    private static AstSelector LowerSelect(CstSelect select)
    {
        var children = select.Children_
            .Select(LowerNode)
            .Where(n => n != null)
            .Cast<AstNode>()
            .ToImmutableArray();
        return new AstSelector(select, children);
    }

    private static AstSequence LowerSequence(CstSequence seq)
    {
        var children = seq.Children_
            .Select(LowerNode)
            .Where(n => n != null)
            .Cast<AstNode>()
            .ToImmutableArray();
        return new AstSequence(seq, children);
    }

    private static AstParallel LowerParallel(CstParallel parallel)
    {
        var policy = ParseParallelPolicy(parallel);
        var children = parallel.Children_
            .Select(LowerNode)
            .Where(n => n != null)
            .Cast<AstNode>()
            .ToImmutableArray();
        return new AstParallel(parallel, policy, children);
    }

    private static ParallelPolicy ParseParallelPolicy(CstParallel parallel)
    {
        return parallel.Policy.Text switch
        {
            ":any" => new ParallelPolicy.Any(),
            ":all" => new ParallelPolicy.All(),
            ":n" when parallel.PolicyArg.HasValue
                && int.TryParse(parallel.PolicyArg.Value.Text, out var n)
                => new ParallelPolicy.N(n),
            _ => new ParallelPolicy.All(), // デフォルト
        };
    }

    private static AstCheck LowerCheck(CstCheck check)
    {
        var expr = LowerExpr(check.Expr);
        return new AstCheck(check, expr);
    }

    private static AstGuard LowerGuard(CstGuard guard)
    {
        var condition = LowerExpr(guard.Condition);
        var body = LowerNode(guard.Body)!;
        return new AstGuard(guard, condition, body);
    }

    private static AstIf LowerIf(CstIf ifNode)
    {
        var condition = LowerExpr(ifNode.Condition);
        var then = LowerNode(ifNode.Then)!;
        var @else = ifNode.Else != null ? LowerNode(ifNode.Else) : null;
        return new AstIf(ifNode, condition, then, @else);
    }

    private static AstInvert LowerInvert(CstInvert invert)
    {
        var child = LowerNode(invert.Child)!;
        return new AstInvert(invert, child);
    }

    private static AstRepeat LowerRepeat(CstRepeat repeat)
    {
        var count = int.TryParse(repeat.Count.Text, out var n) ? n : 0;
        var child = LowerNode(repeat.Child)!;
        return new AstRepeat(repeat, count, child);
    }

    private static AstTimeout LowerTimeout(CstTimeout timeout)
    {
        var seconds = ParseFloat(timeout.Seconds.Text);
        var child = LowerNode(timeout.Child)!;
        return new AstTimeout(timeout, seconds, child);
    }

    private static AstCooldown LowerCooldown(CstCooldown cooldown)
    {
        var seconds = ParseFloat(cooldown.Seconds.Text);
        var child = LowerNode(cooldown.Child)!;
        return new AstCooldown(cooldown, seconds, child);
    }

    private static AstWhile LowerWhile(CstWhile whileNode)
    {
        var condition = LowerExpr(whileNode.Condition);
        var body = LowerNode(whileNode.Body)!;
        return new AstWhile(whileNode, condition, body);
    }

    // ──────────────────────────────────────────────
    //  リアクティブノード（F1）の lowering
    // ──────────────────────────────────────────────

    private static AstReactive LowerReactive(CstReactive reactive)
    {
        var condition = LowerExpr(reactive.Condition);
        var body = LowerNode(reactive.Body)!;
        return new AstReactive(reactive, condition, body);
    }

    private static AstReactiveSelect LowerReactiveSelect(CstReactiveSelect reactiveSelect)
    {
        var children = reactiveSelect.Children_
            .Select(LowerNode)
            .Where(n => n != null)
            .Cast<AstNode>()
            .ToImmutableArray();
        return new AstReactiveSelect(reactiveSelect, children);
    }

    // ──────────────────────────────────────────────
    //  Multi-Tree ノード（F11）の lowering
    // ──────────────────────────────────────────────

    private static AstRef LowerRef(CstRef cstRef)
    {
        return new AstRef(cstRef, cstRef.Name.Text);
    }

    // ──────────────────────────────────────────────
    //  User-Defined Decorators（F2）の lowering
    // ──────────────────────────────────────────────

    /// <summary>defdec 呼び出しを AstDefdecCall に lowering する。</summary>
    private static AstDefdecCall LowerDefdecCall(CstDefdecCall call)
    {
        var name = call.Name.Text;
        var args = call.Args.Select(LowerExpr).ToImmutableArray();
        var body = LowerNode(call.Body)!;
        return new AstDefdecCall(call, name, args, body);
    }

    /// <summary>
    /// ノード位置の CstCall を AstActionCall に変換する。
    /// </summary>
    private static AstActionCall LowerCallAsNode(CstCall call)
    {
        var member = new MemberPath(call.Member.Segments);
        var args = call.Args.Select(LowerExpr).ToImmutableArray();
        return new AstActionCall(call, member, args);
    }

    /// <summary>
    /// ノード位置の括弧なし CstMemberAccess を引数なし AstActionCall に変換する。
    /// <c>.Patrol</c> → <c>AstActionCall(["Patrol"], args=[])</c>
    /// </summary>
    private static AstActionCall LowerMemberAccessAsNode(CstMemberAccess member)
    {
        var path = new MemberPath(member.Segments);
        return new AstActionCall(member, path, ImmutableArray<AstExpr>.Empty);
    }

    // ──────────────────────────────────────────────
    //  式位置の lowering
    // ──────────────────────────────────────────────

    /// <summary>
    /// 式位置の CST ノードを AST 式に変換する。
    /// </summary>
    private static AstExpr LowerExpr(CstNode node)
    {
        return node switch
        {
            CstMemberAccess member => LowerMemberAccessAsExpr(member),
            CstIntLiteral lit => new AstLiteralExpr(lit, ParseInt(lit.Token.Text), LiteralKind.Int),
            CstFloatLiteral lit => new AstLiteralExpr(lit, ParseFloat(lit.Token.Text), LiteralKind.Float),
            CstBoolLiteral lit => new AstLiteralExpr(lit, lit.Token.Kind == TokenKind.BoolTrue, LiteralKind.Bool),
            CstStringLiteral lit => new AstLiteralExpr(lit, ParseString(lit.Token.Text), LiteralKind.String),
            CstNullLiteral lit => new AstLiteralExpr(lit, null, LiteralKind.Null),
            CstEnumLiteral lit => new AstLiteralExpr(lit, new EnumRef(lit.TypeName, lit.MemberName), LiteralKind.Enum),
            CstBinaryExpr bin => LowerBinaryExpr(bin),
            CstUnaryExpr un => LowerUnaryExpr(un),
            CstLogicExpr logic => LowerLogicExpr(logic),
            CstBlackboardAccess bb => new AstBlackboardAccessExpr(bb, new MemberPath(bb.Segments)),
            CstCall call => LowerCallAsExpr(call),
            // エラーノードはダミーのリテラル式として返す
            _ => new AstLiteralExpr(node, null, LiteralKind.Null),
        };
    }

    private static AstMemberAccessExpr LowerMemberAccessAsExpr(CstMemberAccess member)
    {
        return new AstMemberAccessExpr(member, new MemberPath(member.Segments));
    }

    private static AstBinaryExpr LowerBinaryExpr(CstBinaryExpr bin)
    {
        var op = ParseBinaryOp(bin.Operator.Text, bin.Operator.Kind);
        var lhs = LowerExpr(bin.Lhs);
        var rhs = LowerExpr(bin.Rhs);
        return new AstBinaryExpr(bin, op, lhs, rhs);
    }

    private static AstUnaryExpr LowerUnaryExpr(CstUnaryExpr un)
    {
        var op = un.Operator.Text == "not" ? UnaryOp.Not : UnaryOp.Negate;
        var operand = LowerExpr(un.Operand);
        return new AstUnaryExpr(un, op, operand);
    }

    private static AstLogicExpr LowerLogicExpr(CstLogicExpr logic)
    {
        var op = logic.Operator.Text == "and" ? LogicOp.And : LogicOp.Or;
        var operands = logic.Operands.Select(LowerExpr).ToImmutableArray();
        return new AstLogicExpr(logic, op, operands);
    }

    /// <summary>
    /// 式位置の CstCall を AstCallExpr に変換する。
    /// </summary>
    private static AstCallExpr LowerCallAsExpr(CstCall call)
    {
        var member = new MemberPath(call.Member.Segments);
        var args = call.Args.Select(LowerExpr).ToImmutableArray();
        return new AstCallExpr(call, member, args);
    }

    // ──────────────────────────────────────────────
    //  パースヘルパー
    // ──────────────────────────────────────────────

    private static BinaryOp ParseBinaryOp(string text, TokenKind kind) => kind switch
    {
        TokenKind.Plus => BinaryOp.Add,
        TokenKind.Minus => BinaryOp.Sub,
        TokenKind.Star => BinaryOp.Mul,
        TokenKind.Slash => BinaryOp.Div,
        TokenKind.Percent => BinaryOp.Mod,
        TokenKind.LessThan => BinaryOp.Lt,
        TokenKind.GreaterThan => BinaryOp.Gt,
        TokenKind.LessEqual => BinaryOp.Le,
        TokenKind.GreaterEqual => BinaryOp.Ge,
        TokenKind.Equal => BinaryOp.Eq,
        TokenKind.NotEqual => BinaryOp.Ne,
        _ => text switch
        {
            "+" => BinaryOp.Add,
            "-" => BinaryOp.Sub,
            "*" => BinaryOp.Mul,
            "/" => BinaryOp.Div,
            "%" => BinaryOp.Mod,
            "<" => BinaryOp.Lt,
            ">" => BinaryOp.Gt,
            "<=" => BinaryOp.Le,
            ">=" => BinaryOp.Ge,
            "=" => BinaryOp.Eq,
            "!=" => BinaryOp.Ne,
            _ => BinaryOp.Eq,
        },
    };

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static float ParseFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

    /// <summary>
    /// 文字列リテラルの引用符とエスケープシーケンスを処理する。
    /// <c>"he\\\"llo"</c> → <c>he"llo</c>
    /// </summary>
    private static string ParseString(string text)
    {
        if (text.Length < 2) return text;
        var inner = text[1..^1]; // 引用符を除去
        return inner
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\r", "\r");
    }
}
