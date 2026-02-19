using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

/// <summary>
/// IR ノードを S 式テキストに直列化するシリアライザ。
///
/// 用途:
/// <list type="bullet">
///   <item><description>テストのスナップショット比較</description></item>
///   <item><description>デバッグ時の中間出力</description></item>
///   <item><description>将来的な IR レベル最適化パスの入出力</description></item>
/// </list>
///
/// 出力形式の例:
/// <code>
/// (ir-selector
///   (ir-sequence
///     (ir-condition
///       (ir-binary-op :lt
///         (ir-member-load ("Health") :type "unknown")
///         (ir-literal 30 :int)))
///     (ir-action "this" "Flee" () :type "Crisp.Runtime.BtStatus"))
///   (ir-action "this" "Patrol" () :type "Crisp.Runtime.BtStatus"))
/// </code>
/// </summary>
public static class IrSerializer
{
    /// <summary>
    /// IR ノードを S 式テキストに直列化する。
    /// </summary>
    /// <param name="node">直列化対象の IR ノード。</param>
    /// <returns>インデント付き S 式テキスト。</returns>
    public static string Serialize(IrNode node)
    {
        var sb = new StringBuilder();
        SerializeNode(sb, node, 0);
        return sb.ToString();
    }

    /// <summary>
    /// 複数の IR ツリーを S 式テキストに直列化する。
    /// 各ツリーの間に空行を挿入する。
    /// </summary>
    /// <param name="trees">直列化対象の IR ツリー配列。</param>
    /// <returns>インデント付き S 式テキスト。</returns>
    public static string Serialize(ImmutableArray<IrTree> trees)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < trees.Length; i++)
        {
            if (i > 0) sb.AppendLine();
            SerializeNode(sb, trees[i], 0);
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  ノードの直列化
    // ──────────────────────────────────────────────

    /// <summary>
    /// IR ノードを種別に応じて直列化する。
    /// </summary>
    private static void SerializeNode(StringBuilder sb, IrNode node, int indent)
    {
        switch (node)
        {
            case IrTree tree:
                SerializeTree(sb, tree, indent);
                break;
            case IrSelector sel:
                SerializeChildren(sb, "ir-selector", sel.Children, indent);
                break;
            case IrSequence seq:
                SerializeChildren(sb, "ir-sequence", seq.Children, indent);
                break;
            case IrParallel par:
                SerializeParallel(sb, par, indent);
                break;
            case IrCondition cond:
                SerializeCondition(sb, cond, indent);
                break;
            case IrAction act:
                SerializeAction(sb, act, indent);
                break;
            case IrGuard grd:
                SerializeGuard(sb, grd, indent);
                break;
            case IrIf ifn:
                SerializeIf(sb, ifn, indent);
                break;
            case IrInvert inv:
                SerializeSingleChild(sb, "ir-invert", inv.Child, indent);
                break;
            case IrRepeat rep:
                SerializeRepeat(sb, rep, indent);
                break;
            case IrTimeout tmo:
                SerializeFloatChild(sb, "ir-timeout", tmo.Seconds, tmo.Child, indent);
                break;
            case IrCooldown cld:
                SerializeFloatChild(sb, "ir-cooldown", cld.Seconds, cld.Child, indent);
                break;
            case IrWhile whl:
                SerializeWhile(sb, whl, indent);
                break;
            case IrReactive rct:
                SerializeReactive(sb, rct, indent);
                break;
            case IrReactiveSelect rsel:
                SerializeChildren(sb, "ir-reactive-select", rsel.Children, indent);
                break;
            case IrTreeRef tref:
                Indent(sb, indent);
                sb.Append($"(ir-tree-ref \"{tref.TreeName}\")");
                break;
            case IrExpr expr:
                SerializeExpr(sb, expr, indent);
                break;
            default:
                Indent(sb, indent);
                sb.Append("(ir-unknown)");
                break;
        }
    }

    /// <summary>ツリー定義の直列化。</summary>
    private static void SerializeTree(StringBuilder sb, IrTree tree, int indent)
    {
        Indent(sb, indent);
        sb.Append($"(ir-tree \"{tree.Name}\"");

        if (tree.Body.IsEmpty)
        {
            sb.Append(')');
            return;
        }

        foreach (var child in tree.Body)
        {
            sb.AppendLine();
            SerializeNode(sb, child, indent + 2);
        }
        sb.Append(')');
    }

    /// <summary>子ノード配列を持つノードの共通直列化（selector, sequence）。</summary>
    private static void SerializeChildren(StringBuilder sb, string tag, ImmutableArray<IrNode> children, int indent)
    {
        Indent(sb, indent);
        sb.Append($"({tag}");

        if (children.IsEmpty)
        {
            sb.Append(')');
            return;
        }

        foreach (var child in children)
        {
            sb.AppendLine();
            SerializeNode(sb, child, indent + 2);
        }
        sb.Append(')');
    }

    /// <summary>パラレルノードの直列化。ポリシーを含む。</summary>
    private static void SerializeParallel(StringBuilder sb, IrParallel par, int indent)
    {
        Indent(sb, indent);
        var policyStr = par.Policy switch
        {
            ParallelPolicy.Any => ":any",
            ParallelPolicy.All => ":all",
            ParallelPolicy.N n => $":n {n.Count}",
            _ => ":unknown",
        };
        sb.Append($"(ir-parallel {policyStr}");

        foreach (var child in par.Children)
        {
            sb.AppendLine();
            SerializeNode(sb, child, indent + 2);
        }
        sb.Append(')');
    }

    /// <summary>条件ノードの直列化。</summary>
    private static void SerializeCondition(StringBuilder sb, IrCondition cond, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine("(ir-condition");
        SerializeExpr(sb, cond.Expr, indent + 2);
        sb.Append(')');
    }

    /// <summary>アクション呼び出しの直列化。</summary>
    private static void SerializeAction(StringBuilder sb, IrAction act, int indent)
    {
        Indent(sb, indent);
        sb.Append($"(ir-action \"{act.Method.DeclaringType}\" \"{act.Method.Name}\" (");

        // 引数の直列化
        for (var i = 0; i < act.Args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            SerializeExprInline(sb, act.Args[i]);
        }

        sb.Append(')');

        // 型情報
        if (act.TypeRef != null)
            sb.Append($" :type \"{act.TypeRef.FullName}\"");

        sb.Append(')');
    }

    /// <summary>ガードノードの直列化。</summary>
    private static void SerializeGuard(StringBuilder sb, IrGuard grd, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine("(ir-guard");
        SerializeExpr(sb, grd.Condition, indent + 2);
        sb.AppendLine();
        SerializeNode(sb, grd.Body, indent + 2);
        sb.Append(')');
    }

    /// <summary>if ノードの直列化。</summary>
    private static void SerializeIf(StringBuilder sb, IrIf ifn, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine("(ir-if");
        SerializeExpr(sb, ifn.Condition, indent + 2);
        sb.AppendLine();
        SerializeNode(sb, ifn.Then, indent + 2);
        if (ifn.Else != null)
        {
            sb.AppendLine();
            SerializeNode(sb, ifn.Else, indent + 2);
        }
        sb.Append(')');
    }

    /// <summary>単一子ノードを持つノードの共通直列化（invert）。</summary>
    private static void SerializeSingleChild(StringBuilder sb, string tag, IrNode child, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine($"({tag}");
        SerializeNode(sb, child, indent + 2);
        sb.Append(')');
    }

    /// <summary>リピートノードの直列化。</summary>
    private static void SerializeRepeat(StringBuilder sb, IrRepeat rep, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine($"(ir-repeat {rep.Count}");
        SerializeNode(sb, rep.Child, indent + 2);
        sb.Append(')');
    }

    /// <summary>float 値と子ノードを持つノードの共通直列化（timeout, cooldown）。</summary>
    private static void SerializeFloatChild(StringBuilder sb, string tag, float value, IrNode child, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine($"({tag} {FormatFloat(value)}");
        SerializeNode(sb, child, indent + 2);
        sb.Append(')');
    }

    /// <summary>while ノードの直列化。</summary>
    private static void SerializeWhile(StringBuilder sb, IrWhile whl, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine("(ir-while");
        SerializeExpr(sb, whl.Condition, indent + 2);
        sb.AppendLine();
        SerializeNode(sb, whl.Body, indent + 2);
        sb.Append(')');
    }

    /// <summary>リアクティブノードの直列化。条件式とボディを含む。</summary>
    private static void SerializeReactive(StringBuilder sb, IrReactive rct, int indent)
    {
        Indent(sb, indent);
        sb.AppendLine("(ir-reactive");
        SerializeExpr(sb, rct.Condition, indent + 2);
        sb.AppendLine();
        SerializeNode(sb, rct.Body, indent + 2);
        sb.Append(')');
    }

    // ──────────────────────────────────────────────
    //  式の直列化
    // ──────────────────────────────────────────────

    /// <summary>
    /// IR 式をインデント付きで直列化する。
    /// </summary>
    private static void SerializeExpr(StringBuilder sb, IrExpr expr, int indent)
    {
        Indent(sb, indent);
        SerializeExprInline(sb, expr);
    }

    /// <summary>
    /// IR 式をインラインで（インデントなしで）直列化する。
    /// 引数リスト内の式など、改行なしで出力する場合に使用する。
    /// </summary>
    private static void SerializeExprInline(StringBuilder sb, IrExpr expr)
    {
        switch (expr)
        {
            case IrBlackboardLoad bb:
                SerializeBlackboardLoadInline(sb, bb);
                break;
            case IrMemberLoad mem:
                SerializeMemberLoadInline(sb, mem);
                break;
            case IrLiteral lit:
                SerializeLiteralInline(sb, lit);
                break;
            case IrBinaryOp bin:
                SerializeBinaryOpInline(sb, bin);
                break;
            case IrUnaryOp un:
                SerializeUnaryOpInline(sb, un);
                break;
            case IrLogicOp log:
                SerializeLogicOpInline(sb, log);
                break;
            case IrCall call:
                SerializeCallInline(sb, call);
                break;
            case IrConvert conv:
                SerializeConvertInline(sb, conv);
                break;
            default:
                sb.Append("(ir-unknown-expr)");
                break;
        }
    }

    /// <summary>ブラックボードロード式の直列化。</summary>
    private static void SerializeBlackboardLoadInline(StringBuilder sb, IrBlackboardLoad bb)
    {
        sb.Append("(ir-blackboard-load (");
        for (var i = 0; i < bb.Ref.Chain.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append($"\"{bb.Ref.Chain[i]}\"");
        }
        sb.Append($") :type \"{bb.TypeRef.FullName}\")");
    }

    /// <summary>メンバーロード式の直列化。</summary>
    private static void SerializeMemberLoadInline(StringBuilder sb, IrMemberLoad mem)
    {
        sb.Append("(ir-member-load (");
        for (var i = 0; i < mem.Ref.Chain.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append($"\"{mem.Ref.Chain[i]}\"");
        }
        sb.Append($") :type \"{mem.TypeRef.FullName}\")");
    }

    /// <summary>リテラル式の直列化。値と型種別を出力する。</summary>
    private static void SerializeLiteralInline(StringBuilder sb, IrLiteral lit)
    {
        var valueStr = FormatLiteralValue(lit.Value, lit.Kind);
        var kindStr = lit.Kind.ToString().ToLowerInvariant();
        sb.Append($"(ir-literal {valueStr} :{kindStr})");
    }

    /// <summary>二項演算式の直列化。</summary>
    private static void SerializeBinaryOpInline(StringBuilder sb, IrBinaryOp bin)
    {
        var opStr = FormatBinaryOp(bin.Op);
        sb.Append($"(ir-binary-op :{opStr} ");
        SerializeExprInline(sb, bin.Lhs);
        sb.Append(' ');
        SerializeExprInline(sb, bin.Rhs);
        sb.Append(')');
    }

    /// <summary>単項演算式の直列化。</summary>
    private static void SerializeUnaryOpInline(StringBuilder sb, IrUnaryOp un)
    {
        var opStr = un.Op.ToString().ToLowerInvariant();
        sb.Append($"(ir-unary-op :{opStr} ");
        SerializeExprInline(sb, un.Operand);
        sb.Append(')');
    }

    /// <summary>論理演算式の直列化。</summary>
    private static void SerializeLogicOpInline(StringBuilder sb, IrLogicOp log)
    {
        var opStr = log.Op.ToString().ToLowerInvariant();
        sb.Append($"(ir-logic-op :{opStr}");
        foreach (var operand in log.Operands)
        {
            sb.Append(' ');
            SerializeExprInline(sb, operand);
        }
        sb.Append(')');
    }

    /// <summary>メソッド呼び出し式の直列化。</summary>
    private static void SerializeCallInline(StringBuilder sb, IrCall call)
    {
        sb.Append($"(ir-call \"{call.Method.DeclaringType}\" \"{call.Method.Name}\" (");
        for (var i = 0; i < call.Args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            SerializeExprInline(sb, call.Args[i]);
        }
        sb.Append($") :type \"{call.TypeRef.FullName}\")");
    }

    /// <summary>暗黙型変換式の直列化。</summary>
    private static void SerializeConvertInline(StringBuilder sb, IrConvert conv)
    {
        sb.Append("(ir-convert ");
        SerializeExprInline(sb, conv.Operand);
        sb.Append($" :to \"{conv.TargetType.FullName}\")");
    }

    // ──────────────────────────────────────────────
    //  フォーマットヘルパー
    // ──────────────────────────────────────────────

    /// <summary>インデントを出力する。</summary>
    private static void Indent(StringBuilder sb, int indent)
    {
        sb.Append(new string(' ', indent));
    }

    /// <summary>リテラル値を文字列にフォーマットする。</summary>
    private static string FormatLiteralValue(object? value, LiteralKind kind) => kind switch
    {
        LiteralKind.Int => value?.ToString() ?? "0",
        LiteralKind.Float => FormatFloat(value is float f ? f : 0f),
        LiteralKind.Bool => value is true ? "true" : "false",
        LiteralKind.String => $"\"{value}\"",
        LiteralKind.Null => "null",
        LiteralKind.Enum when value is EnumRef er => $"::{er.TypeName}.{er.MemberName}",
        _ => value?.ToString() ?? "null",
    };

    /// <summary>float 値を文化非依存のフォーマットで出力する。小数点以下が 0 でも .0 を付与する。</summary>
    private static string FormatFloat(float value)
    {
        var str = value.ToString("G", CultureInfo.InvariantCulture);
        if (!str.Contains('.') && !str.Contains('E') && !str.Contains('e'))
            str += ".0";
        return str;
    }

    /// <summary>二項演算子をシリアライズ用の小文字文字列に変換する。</summary>
    private static string FormatBinaryOp(BinaryOp op) => op switch
    {
        BinaryOp.Add => "add",
        BinaryOp.Sub => "sub",
        BinaryOp.Mul => "mul",
        BinaryOp.Div => "div",
        BinaryOp.Mod => "mod",
        BinaryOp.Lt => "lt",
        BinaryOp.Gt => "gt",
        BinaryOp.Le => "le",
        BinaryOp.Ge => "ge",
        BinaryOp.Eq => "eq",
        BinaryOp.Ne => "ne",
        _ => op.ToString().ToLowerInvariant(),
    };
}
