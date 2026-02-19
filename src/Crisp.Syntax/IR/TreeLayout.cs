using System.Collections.Immutable;
using System.Text;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

// ═══════════════════════════════════════════════════════════
//  F4: Visual Editor — Tree Layout モデル
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ビジュアルエディタ用のツリーレイアウト情報（F4: Visual Editor）。
///
/// IrTree を JSON 風の構造に変換し、WebView 上でのツリー描画と
/// テキスト ↔ ビジュアルの双方向同期を実現する。
///
/// <para>
/// 各ノードの <c>Id</c> は <c>IrNode.Id</c>（= <c>CstNode.Id</c>）と一致する。
/// これにより:
/// <list type="bullet">
///   <item><description>ビジュアルノードクリック → テキスト上の位置にジャンプ</description></item>
///   <item><description>テキスト上のカーソル位置 → 対応するビジュアルノードをハイライト</description></item>
/// </list>
/// </para>
/// </summary>
/// <param name="TreeName">ツリー名。</param>
/// <param name="Root">ルートのレイアウトノード。</param>
/// <param name="Diagnostics">ツリーに関連する診断情報。</param>
public sealed record TreeLayout(
    string TreeName,
    TreeLayoutNode Root,
    ImmutableArray<TreeLayoutDiagnostic> Diagnostics);

/// <summary>
/// ツリーレイアウトの個々のノード。
///
/// <c>IrNode</c> を WebView 描画に必要な最小限の情報に射影する。
/// <c>Id</c> はソース位置の逆引きに使用され、
/// <c>Origin</c> はテキストエディタ上でのハイライト範囲を指定する。
/// </summary>
/// <param name="Id">ノード Id（IrNode.Id / CstNode.Id と一致）。</param>
/// <param name="NodeType">ノードの種別（"selector", "sequence", "action" 等）。</param>
/// <param name="Label">表示用ラベル（アクション名、条件式等）。null の場合は NodeType をラベルとして使用。</param>
/// <param name="Origin">ソース上の位置情報。テキストジャンプに使用。</param>
/// <param name="ResolvedType">型推論結果（"bool", "BtStatus" 等）。不明な場合は null。</param>
/// <param name="Children">子ノードのレイアウト情報。</param>
public sealed record TreeLayoutNode(
    int Id,
    string NodeType,
    string? Label,
    TreeLayoutOrigin Origin,
    string? ResolvedType,
    ImmutableArray<TreeLayoutNode> Children);

/// <summary>
/// ソース位置情報。テキストエディタでのジャンプ・ハイライトに使用する。
/// </summary>
/// <param name="Start">開始オフセット。</param>
/// <param name="Length">長さ。</param>
public sealed record TreeLayoutOrigin(int Start, int Length);

/// <summary>
/// ツリーレイアウトに付随する診断情報。
/// </summary>
/// <param name="NodeId">対象ノードの Id。ノード特定不能の場合は -1。</param>
/// <param name="Severity">診断の深刻度（"error", "warning", "info"）。</param>
/// <param name="Message">診断メッセージ。</param>
public sealed record TreeLayoutDiagnostic(int NodeId, string Severity, string Message);

/// <summary>
/// <see cref="IrTree"/> から <see cref="TreeLayout"/> への変換を行うビルダー（F4: Visual Editor）。
///
/// IR ツリーを再帰的に走査し、各ノードの種別・ラベル・位置情報・型情報を
/// <see cref="TreeLayoutNode"/> に射影する。
///
/// <para>
/// 使用例:
/// <code>
/// var irTree = AstToIrLowering.Lower(astTrees)[0];
/// var layout = TreeLayoutBuilder.Build(irTree);
/// // layout.Root.Id == irTree.Body[0].Id
/// // layout.Root.NodeType == "selector" etc.
/// </code>
/// </para>
/// </summary>
public static class TreeLayoutBuilder
{
    /// <summary>
    /// IR ツリーからツリーレイアウトを構築する。
    /// </summary>
    /// <param name="irTree">変換元の IR ツリー。</param>
    /// <param name="diagnostics">ツリーに関連する診断情報。省略時は空配列。</param>
    /// <returns>ツリーレイアウト情報。</returns>
    public static TreeLayout Build(IrTree irTree, ImmutableArray<TreeLayoutDiagnostic>? diagnostics = null)
    {
        var rootChildren = irTree.Body.Select(ConvertNode).ToImmutableArray();
        var root = new TreeLayoutNode(
            irTree.Id,
            "tree",
            irTree.Name,
            new TreeLayoutOrigin(irTree.Origin.Start, irTree.Origin.Length),
            irTree.TypeRef?.FullName,
            rootChildren);

        return new TreeLayout(
            irTree.Name,
            root,
            diagnostics ?? ImmutableArray<TreeLayoutDiagnostic>.Empty);
    }

    /// <summary>
    /// IR ノードをレイアウトノードに再帰変換する。
    /// ノードの種別に応じてラベル・子ノードを構築する。
    /// </summary>
    private static TreeLayoutNode ConvertNode(IrNode node)
    {
        return node switch
        {
            IrSelector sel => MakeNode(sel, "selector", null, sel.Children),
            IrSequence seq => MakeNode(seq, "sequence", null, seq.Children),
            IrParallel par => MakeNode(par, "parallel", $":{par.Policy.ToString().ToLowerInvariant()}", par.Children),
            IrCondition cond => MakeNode(cond, "check", FormatExpr(cond.Expr), ImmutableArray<IrNode>.Empty),
            IrAction act => MakeNode(act, "action", FormatAction(act), ImmutableArray<IrNode>.Empty),
            IrGuard grd => MakeNode(grd, "guard", FormatExpr(grd.Condition), ImmutableArray.Create(grd.Body)),
            IrIf ifn => MakeIfNode(ifn),
            IrInvert inv => MakeNode(inv, "invert", null, ImmutableArray.Create(inv.Child)),
            IrRepeat rep => MakeNode(rep, "repeat", rep.Count.ToString(), ImmutableArray.Create(rep.Child)),
            IrTimeout tmo => MakeNode(tmo, "timeout", $"{tmo.Seconds}s", ImmutableArray.Create(tmo.Child)),
            IrCooldown cld => MakeNode(cld, "cooldown", $"{cld.Seconds}s", ImmutableArray.Create(cld.Child)),
            IrWhile whl => MakeNode(whl, "while", FormatExpr(whl.Condition), ImmutableArray.Create(whl.Body)),
            IrReactive rct => MakeNode(rct, "reactive", FormatExpr(rct.Condition), ImmutableArray.Create(rct.Body)),
            IrReactiveSelect rsel => MakeNode(rsel, "reactive-select", null, rsel.Children),
            IrTreeRef tref => MakeNode(tref, "tree-ref", tref.TreeName, ImmutableArray<IrNode>.Empty),
            _ => MakeNode(node, "unknown", null, ImmutableArray<IrNode>.Empty),
        };
    }

    /// <summary>
    /// レイアウトノードを構築するヘルパー。
    /// </summary>
    private static TreeLayoutNode MakeNode(
        IrNode node,
        string nodeType,
        string? label,
        ImmutableArray<IrNode> children)
    {
        var layoutChildren = children.Select(ConvertNode).ToImmutableArray();
        var resolvedType = node.TypeRef?.FullName;
        return new TreeLayoutNode(
            node.Id,
            nodeType,
            label,
            new TreeLayoutOrigin(node.Origin.Start, node.Origin.Length),
            resolvedType,
            layoutChildren);
    }

    /// <summary>
    /// if ノード用の特殊変換。Then と Else の両方を子として含める。
    /// </summary>
    private static TreeLayoutNode MakeIfNode(IrIf ifn)
    {
        var children = ifn.Else != null
            ? ImmutableArray.Create(ifn.Then, ifn.Else)
            : ImmutableArray.Create(ifn.Then);
        return MakeNode(ifn, "if", FormatExpr(ifn.Condition), children);
    }

    /// <summary>
    /// アクションノードのラベルを生成する。
    /// メソッド名と引数を「Method(arg1, arg2)」形式で表示する。
    /// </summary>
    private static string FormatAction(IrAction act)
    {
        var sb = new StringBuilder();
        sb.Append(act.Method.Name);
        sb.Append('(');
        for (var i = 0; i < act.Args.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(FormatExpr(act.Args[i]));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// 式ノードを人間可読なラベル文字列に変換する。
    /// ビジュアルエディタのノードラベル表示に使用する。
    /// </summary>
    internal static string FormatExpr(IrExpr expr)
    {
        return expr switch
        {
            IrMemberLoad mem => FormatMemberRef(mem.Ref),
            IrBlackboardLoad bb => "$" + FormatMemberRef(bb.Ref),
            IrLiteral lit => FormatLiteral(lit),
            IrBinaryOp bin => $"{FormatExpr(bin.Lhs)} {FormatBinaryOp(bin.Op)} {FormatExpr(bin.Rhs)}",
            IrUnaryOp un => $"{FormatUnaryOp(un.Op)}{FormatExpr(un.Operand)}",
            IrLogicOp log => string.Join(
                log.Op == LogicOp.And ? " and " : " or ",
                log.Operands.Select(FormatExpr)),
            IrCall call => $"{call.Method.Name}({string.Join(", ", call.Args.Select(FormatExpr))})",
            _ => "?",
        };
    }

    /// <summary>メンバー参照チェーンをドット区切り文字列に変換する。</summary>
    private static string FormatMemberRef(MemberRef memberRef) =>
        "." + string.Join(".", memberRef.Chain);

    /// <summary>リテラル値を表示用文字列に変換する。</summary>
    private static string FormatLiteral(IrLiteral lit) =>
        lit.Kind switch
        {
            LiteralKind.Null => "null",
            LiteralKind.Bool => lit.Value is true ? "true" : "false",
            LiteralKind.String => $"\"{lit.Value}\"",
            _ => lit.Value?.ToString() ?? "null",
        };

    /// <summary>二項演算子を表示用文字列に変換する。</summary>
    private static string FormatBinaryOp(BinaryOp op) =>
        op switch
        {
            BinaryOp.Add => "+",
            BinaryOp.Sub => "-",
            BinaryOp.Mul => "*",
            BinaryOp.Div => "/",
            BinaryOp.Mod => "%",
            BinaryOp.Lt => "<",
            BinaryOp.Gt => ">",
            BinaryOp.Le => "<=",
            BinaryOp.Ge => ">=",
            BinaryOp.Eq => "=",
            BinaryOp.Ne => "!=",
            _ => "?",
        };

    /// <summary>単項演算子を表示用文字列に変換する。</summary>
    private static string FormatUnaryOp(UnaryOp op) =>
        op switch
        {
            UnaryOp.Not => "not ",
            UnaryOp.Negate => "-",
            _ => "?",
        };
}
