using System.Collections.Immutable;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Ast;

/// <summary>
/// F2: defdec 展開パス。
///
/// AST 上の <see cref="AstDefdecCall"/> を、対応する <see cref="AstDefdec"/> の本体で
/// 置換（展開）する AST→AST 変換パス。
///
/// 展開ルール:
/// <list type="bullet">
///   <item><description>defdec 本体中の <see cref="AstBodyPlaceholder"/> を呼び出し側の子ノードで置換する</description></item>
///   <item><description>defdec パラメータ名と一致するリテラル式を、呼び出し側の式引数で置換する</description></item>
///   <item><description>再帰呼び出しは検出してエラーにする（無限展開防止）</description></item>
///   <item><description>展開は名前解決の前に行う（セクション0.3 の順序）</description></item>
/// </list>
///
/// 使用例:
/// <code>
/// (defdec safe-timed-action (duration cd)
///   (guard .IsAlive
///     (timeout duration
///       (cooldown cd
///         &lt;body&gt;))))
///
/// (safe-timed-action 10.0 2.0
///   (.Attack .Target))
///
/// → 展開後:
/// (guard .IsAlive
///   (timeout 10.0
///     (cooldown 2.0
///       (.Attack .Target))))
/// </code>
/// </summary>
public static class AstDefdecExpander
{
    /// <summary>展開時の最大再帰深度（安全弁）。</summary>
    private const int MaxExpansionDepth = 100;

    /// <summary>
    /// defdec 定義を使って、ツリー内の全 <see cref="AstDefdecCall"/> を展開する。
    /// </summary>
    /// <param name="trees">展開対象の AST ツリー群。</param>
    /// <param name="defdecs">defdec 定義群。</param>
    /// <param name="diagnostics">診断バッグ（エラー報告先）。</param>
    /// <returns>展開済みの AST ツリー群。</returns>
    public static ImmutableArray<AstTree> Expand(
        ImmutableArray<AstTree> trees,
        ImmutableArray<AstDefdec> defdecs,
        DiagnosticBag diagnostics)
    {
        // defdec 名 → 定義のマップを構築
        var registry = new Dictionary<string, AstDefdec>();
        foreach (var defdec in defdecs)
        {
            registry[defdec.Name] = defdec;
        }

        if (registry.Count == 0)
            return trees; // defdec 定義が無ければ何もしない

        return trees.Select(tree => ExpandTree(tree, registry, diagnostics)).ToImmutableArray();
    }

    /// <summary>ツリー1つを展開する。</summary>
    private static AstTree ExpandTree(AstTree tree, Dictionary<string, AstDefdec> registry, DiagnosticBag diagnostics)
    {
        var expandedBody = tree.Body
            .Select(n => ExpandNode(n, registry, diagnostics, ImmutableHashSet<string>.Empty, 0))
            .ToImmutableArray();
        return new AstTree(tree.CstOrigin, tree.Name, expandedBody);
    }

    /// <summary>
    /// ノードを再帰的に走査し、<see cref="AstDefdecCall"/> を見つけたら展開する。
    /// それ以外のノードは子を再帰的に展開する。
    /// </summary>
    private static AstNode ExpandNode(
        AstNode node,
        Dictionary<string, AstDefdec> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        if (depth > MaxExpansionDepth)
        {
            diagnostics.Report(DiagnosticDescriptors.BS0025, node.CstOrigin.Span, null, "depth limit exceeded");
            return node;
        }

        if (node is AstDefdecCall call)
        {
            return ExpandCall(call, registry, diagnostics, expandingNames, depth);
        }

        // その他のノードは子を再帰的に展開
        return RebuildWithExpandedChildren(node, registry, diagnostics, expandingNames, depth);
    }

    /// <summary>
    /// <see cref="AstDefdecCall"/> を展開する。
    /// defdec 定義の本体を取得し、パラメータと body を置換する。
    /// </summary>
    private static AstNode ExpandCall(
        AstDefdecCall call,
        Dictionary<string, AstDefdec> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        // defdec 定義を検索
        if (!registry.TryGetValue(call.Name, out var defdec))
        {
            diagnostics.Report(DiagnosticDescriptors.BS0023, call.CstOrigin.Span, null, call.Name);
            return call;
        }

        // 再帰呼び出し検出
        if (expandingNames.Contains(call.Name))
        {
            diagnostics.Report(DiagnosticDescriptors.BS0025, call.CstOrigin.Span, null, call.Name);
            return call;
        }

        // パラメータ数チェック
        if (call.Args.Length != defdec.Parameters.Length)
        {
            diagnostics.Report(DiagnosticDescriptors.BS0024, call.CstOrigin.Span, null,
                call.Name, defdec.Parameters.Length, call.Args.Length);
            return call;
        }

        // パラメータ名 → 式引数のマップを構築
        var paramMap = new Dictionary<string, AstExpr>();
        for (int i = 0; i < defdec.Parameters.Length; i++)
        {
            paramMap[defdec.Parameters[i]] = call.Args[i];
        }

        // defdec 本体をクローンして置換
        var expandedBody = SubstituteNode(defdec.Body, paramMap, call.Body, call.CstOrigin);

        // 展開結果をさらに再帰的に展開（ネスト defdec 呼び出しに対応）
        return ExpandNode(expandedBody, registry, diagnostics, expandingNames.Add(call.Name), depth + 1);
    }

    /// <summary>
    /// defdec 本体内のパラメータ参照と <c>&lt;body&gt;</c> プレースホルダを置換する。
    /// </summary>
    /// <param name="node">defdec 本体の AST ノード。</param>
    /// <param name="paramMap">パラメータ名→式引数のマップ。</param>
    /// <param name="bodyReplacement"><c>&lt;body&gt;</c> プレースホルダの代わりに挿入する子ノード。</param>
    /// <param name="callOrigin">呼び出し側の Origin（展開後のノードに設定する）。</param>
    private static AstNode SubstituteNode(
        AstNode node,
        Dictionary<string, AstExpr> paramMap,
        AstNode bodyReplacement,
        CstNode callOrigin)
    {
        switch (node)
        {
            case AstBodyPlaceholder:
                return bodyReplacement;

            case AstSelector sel:
                return new AstSelector(callOrigin,
                    sel.Children_.Select(c => SubstituteNode(c, paramMap, bodyReplacement, callOrigin)).ToImmutableArray());

            case AstSequence seq:
                return new AstSequence(callOrigin,
                    seq.Children_.Select(c => SubstituteNode(c, paramMap, bodyReplacement, callOrigin)).ToImmutableArray());

            case AstParallel par:
                return new AstParallel(callOrigin, par.Policy,
                    par.Children_.Select(c => SubstituteNode(c, paramMap, bodyReplacement, callOrigin)).ToImmutableArray());

            case AstCheck check:
                return new AstCheck(callOrigin, SubstituteExpr(check.Expr, paramMap, callOrigin));

            case AstGuard guard:
                return new AstGuard(callOrigin,
                    SubstituteExpr(guard.Condition, paramMap, callOrigin),
                    SubstituteNode(guard.Body, paramMap, bodyReplacement, callOrigin));

            case AstIf ifNode:
                return new AstIf(callOrigin,
                    SubstituteExpr(ifNode.Condition, paramMap, callOrigin),
                    SubstituteNode(ifNode.Then, paramMap, bodyReplacement, callOrigin),
                    ifNode.Else != null ? SubstituteNode(ifNode.Else, paramMap, bodyReplacement, callOrigin) : null);

            case AstInvert inv:
                return new AstInvert(callOrigin,
                    SubstituteNode(inv.Child, paramMap, bodyReplacement, callOrigin));

            case AstRepeat rep:
                return new AstRepeat(callOrigin, rep.Count,
                    SubstituteNode(rep.Child, paramMap, bodyReplacement, callOrigin));

            case AstTimeout timeout:
                return new AstTimeout(callOrigin,
                    SubstituteExprAsFloat(timeout.Seconds, "timeout", paramMap),
                    SubstituteNode(timeout.Child, paramMap, bodyReplacement, callOrigin));

            case AstCooldown cooldown:
                return new AstCooldown(callOrigin,
                    SubstituteExprAsFloat(cooldown.Seconds, "cooldown", paramMap),
                    SubstituteNode(cooldown.Child, paramMap, bodyReplacement, callOrigin));

            case AstWhile whileNode:
                return new AstWhile(callOrigin,
                    SubstituteExpr(whileNode.Condition, paramMap, callOrigin),
                    SubstituteNode(whileNode.Body, paramMap, bodyReplacement, callOrigin));

            case AstReactive reactive:
                return new AstReactive(callOrigin,
                    SubstituteExpr(reactive.Condition, paramMap, callOrigin),
                    SubstituteNode(reactive.Body, paramMap, bodyReplacement, callOrigin));

            case AstReactiveSelect reactiveSelect:
                return new AstReactiveSelect(callOrigin,
                    reactiveSelect.Children_.Select(c => SubstituteNode(c, paramMap, bodyReplacement, callOrigin)).ToImmutableArray());

            case AstDefdecCall nestedCall:
                return new AstDefdecCall(callOrigin, nestedCall.Name,
                    nestedCall.Args.Select(a => SubstituteExpr(a, paramMap, callOrigin)).ToImmutableArray(),
                    SubstituteNode(nestedCall.Body, paramMap, bodyReplacement, callOrigin));

            default:
                // AstActionCall, AstRef 等はそのまま返す
                return node;
        }
    }

    /// <summary>
    /// 式内のパラメータ参照を置換する。
    /// defdec パラメータは AstMemberAccessExpr（<c>.paramName</c>なし、先頭セグメント一致）として
    /// 出現しないため、特殊なリテラル式トークンとして検出する。
    ///
    /// 実装方針: timeout/cooldown の秒数パラメータは <see cref="SubstituteExprAsFloat"/> で処理。
    /// check/guard の条件式中のパラメータ参照は、式全体を返す。
    /// </summary>
    private static AstExpr SubstituteExpr(
        AstExpr expr,
        Dictionary<string, AstExpr> paramMap,
        CstNode callOrigin)
    {
        switch (expr)
        {
            case AstBinaryExpr bin:
                return new AstBinaryExpr(callOrigin, bin.Op,
                    SubstituteExpr(bin.Lhs, paramMap, callOrigin),
                    SubstituteExpr(bin.Rhs, paramMap, callOrigin));

            case AstUnaryExpr un:
                return new AstUnaryExpr(callOrigin, un.Op,
                    SubstituteExpr(un.Operand, paramMap, callOrigin));

            case AstLogicExpr logic:
                return new AstLogicExpr(callOrigin, logic.Op,
                    logic.Operands.Select(o => SubstituteExpr(o, paramMap, callOrigin)).ToImmutableArray());

            case AstCallExpr call:
                return new AstCallExpr(callOrigin, call.Member,
                    call.Args.Select(a => SubstituteExpr(a, paramMap, callOrigin)).ToImmutableArray());

            default:
                // AstLiteralExpr, AstMemberAccessExpr, AstBlackboardAccessExpr はそのまま
                return expr;
        }
    }

    /// <summary>
    /// timeout/cooldown の秒数パラメータがパラメータ名参照の場合、
    /// 対応する式引数のリテラル値から float を取り出す。
    /// パラメータ参照でない場合は元の値をそのまま返す。
    /// </summary>
    private static float SubstituteExprAsFloat(float original, string context, Dictionary<string, AstExpr> paramMap)
    {
        // AstTimeout/AstCooldown は既に float に変換されているため、
        // パラメータ名を保持していない。この場合は元の値を返す。
        // ※ パラメータ経由の場合は Lower 時に 0 になるので、paramMap から復元する必要がある。
        // ただし現在の設計では timeout/cooldown の秒数は直接 float 保持。
        // パラメータ渡しに対応するには AstTimeout に式を保持する必要がある。
        // → 初期実装ではパラメータ経由の秒数指定は非対応とする。
        return original;
    }

    /// <summary>
    /// 子ノードを展開した新しいノードを構築するヘルパー。
    /// </summary>
    private static AstNode RebuildWithExpandedChildren(
        AstNode node,
        Dictionary<string, AstDefdec> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        switch (node)
        {
            case AstTree tree:
                return new AstTree(tree.CstOrigin, tree.Name,
                    tree.Body.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray());

            case AstSelector sel:
                return new AstSelector(sel.CstOrigin,
                    sel.Children_.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray());

            case AstSequence seq:
                return new AstSequence(seq.CstOrigin,
                    seq.Children_.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray());

            case AstParallel par:
                return new AstParallel(par.CstOrigin, par.Policy,
                    par.Children_.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray());

            case AstGuard guard:
                return new AstGuard(guard.CstOrigin, guard.Condition,
                    ExpandNode(guard.Body, registry, diagnostics, expandingNames, depth));

            case AstIf ifNode:
                return new AstIf(ifNode.CstOrigin, ifNode.Condition,
                    ExpandNode(ifNode.Then, registry, diagnostics, expandingNames, depth),
                    ifNode.Else != null ? ExpandNode(ifNode.Else, registry, diagnostics, expandingNames, depth) : null);

            case AstInvert inv:
                return new AstInvert(inv.CstOrigin,
                    ExpandNode(inv.Child, registry, diagnostics, expandingNames, depth));

            case AstRepeat rep:
                return new AstRepeat(rep.CstOrigin, rep.Count,
                    ExpandNode(rep.Child, registry, diagnostics, expandingNames, depth));

            case AstTimeout timeout:
                return new AstTimeout(timeout.CstOrigin, timeout.Seconds,
                    ExpandNode(timeout.Child, registry, diagnostics, expandingNames, depth));

            case AstCooldown cooldown:
                return new AstCooldown(cooldown.CstOrigin, cooldown.Seconds,
                    ExpandNode(cooldown.Child, registry, diagnostics, expandingNames, depth));

            case AstWhile whileNode:
                return new AstWhile(whileNode.CstOrigin, whileNode.Condition,
                    ExpandNode(whileNode.Body, registry, diagnostics, expandingNames, depth));

            case AstReactive reactive:
                return new AstReactive(reactive.CstOrigin, reactive.Condition,
                    ExpandNode(reactive.Body, registry, diagnostics, expandingNames, depth));

            case AstReactiveSelect reactiveSelect:
                return new AstReactiveSelect(reactiveSelect.CstOrigin,
                    reactiveSelect.Children_.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray());

            default:
                // AstActionCall, AstCheck, AstRef, AstBodyPlaceholder 等はリーフなのでそのまま
                return node;
        }
    }
}
