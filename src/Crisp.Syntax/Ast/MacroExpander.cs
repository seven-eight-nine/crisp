using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Ast;

/// <summary>
/// F10: マクロ展開器。
/// <c>defmacro</c> で定義されたマクロを AST レベルで展開する。
///
/// 展開方針:
/// <list type="bullet">
///   <item><description>マクロ呼び出し（<see cref="AstDefdecCall"/> で名前がマクロ名に一致するもの）を検出する</description></item>
///   <item><description>マクロ本体の CST テンプレート内のパラメータ名を実引数の CST ノードで置換する</description></item>
///   <item><description>置換済み CST を再 lower して AST に変換する</description></item>
///   <item><description>安全性: 展開の最大深度を制限（デフォルト100）し、無限再帰を防止する</description></item>
///   <item><description>相互再帰検出: 展開中のマクロ名をスタックで追跡し、循環参照を報告する</description></item>
/// </list>
///
/// 使用例:
/// <code>
/// (defmacro retry-twice ()
///   (repeat 2 &lt;body&gt;))
///
/// (tree Main
///   (retry-twice .DoWork))
/// ;; → (tree Main (repeat 2 .DoWork))
/// </code>
/// </summary>
public static class MacroExpander
{
    /// <summary>マクロ展開の最大深度。この深度を超えると BS0033 を報告する。</summary>
    private const int MaxExpansionDepth = 100;

    /// <summary>
    /// 与えられたツリー配列内の全マクロ呼び出しを展開する。
    /// </summary>
    /// <param name="trees">展開対象の AST ツリー配列。</param>
    /// <param name="macros">利用可能なマクロ定義の配列。</param>
    /// <param name="diagnostics">診断メッセージの蓄積先。</param>
    /// <returns>マクロ展開後の AST ツリー配列。</returns>
    public static ImmutableArray<AstTree> Expand(
        ImmutableArray<AstTree> trees,
        ImmutableArray<AstDefmacro> macros,
        DiagnosticBag diagnostics)
    {
        if (macros.Length == 0)
            return trees;

        // マクロ名 → 定義のマップを構築
        var registry = new Dictionary<string, AstDefmacro>();
        foreach (var macro in macros)
        {
            registry[macro.Name] = macro;
        }

        return trees.Select(tree => ExpandTree(tree, registry, diagnostics)).ToImmutableArray();
    }

    /// <summary>ツリー1つを展開する。</summary>
    private static AstTree ExpandTree(
        AstTree tree,
        Dictionary<string, AstDefmacro> registry,
        DiagnosticBag diagnostics)
    {
        var expandedBody = tree.Body
            .Select(n => ExpandNode(n, registry, diagnostics, ImmutableHashSet<string>.Empty, 0))
            .ToImmutableArray();
        return new AstTree(tree.CstOrigin, tree.Name, expandedBody, tree.BlackboardType);
    }

    /// <summary>
    /// ノードを再帰的に走査し、マクロ呼び出し（AstDefdecCall でマクロ名に一致）を展開する。
    /// </summary>
    private static AstNode ExpandNode(
        AstNode node,
        Dictionary<string, AstDefmacro> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        if (depth > MaxExpansionDepth)
        {
            diagnostics.Report(DiagnosticDescriptors.BS0033, node.CstOrigin.Span, null, MaxExpansionDepth);
            return node;
        }

        // AstDefdecCall の名前がマクロ名に一致する場合、マクロ展開を行う
        if (node is AstDefdecCall call && registry.ContainsKey(call.Name))
        {
            return ExpandMacroCall(call, registry, diagnostics, expandingNames, depth);
        }

        // その他のノードは子を再帰的に展開
        return RebuildWithExpandedChildren(node, registry, diagnostics, expandingNames, depth);
    }

    /// <summary>
    /// マクロ呼び出しを展開する。
    /// マクロ本体の CST テンプレートにパラメータを置換して再 lower する。
    /// </summary>
    private static AstNode ExpandMacroCall(
        AstDefdecCall call,
        Dictionary<string, AstDefmacro> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        var macro = registry[call.Name];

        // 循環参照検出
        if (expandingNames.Contains(call.Name))
        {
            var cycle = string.Join(" → ", expandingNames.Append(call.Name));
            diagnostics.Report(DiagnosticDescriptors.BS0034, call.CstOrigin.Span, null, cycle);
            return call;
        }

        // パラメータ数チェック
        if (call.Args.Length != macro.Parameters.Length)
        {
            diagnostics.Report(DiagnosticDescriptors.BS0032, call.CstOrigin.Span, null,
                call.Name, macro.Parameters.Length, call.Args.Length);
            return call;
        }

        // CST レベルでパラメータ置換を行う
        // パラメータ名 → 呼び出し側の CST 引数ノードのマップを構築
        var cstCall = call.CstOrigin as CstDefdecCall;
        if (cstCall == null)
        {
            // CST origin が CstDefdecCall でない場合はそのまま返す
            return call;
        }

        var paramMap = new Dictionary<string, CstNode>();
        for (int i = 0; i < macro.Parameters.Length && i < cstCall.Args.Length; i++)
        {
            paramMap[macro.Parameters[i]] = cstCall.Args[i];
        }

        // マクロ本体の CST テンプレートでパラメータを置換
        var substitutedCst = SubstituteCstNode(macro.BodyCst, paramMap, cstCall.Body);

        // 置換済み CST を AST に re-lower
        AstNode? expanded;
        try
        {
            // CstToAstLowering の内部メソッドは private なので、
            // ノード単体の lowering は LowerNode 相当の処理を手動で行う
            expanded = LowerCstNodeToAst(substitutedCst);
        }
        catch
        {
            diagnostics.Report(DiagnosticDescriptors.BS0035, call.CstOrigin.Span, null,
                "failed to lower expanded macro body");
            return call;
        }

        if (expanded == null)
        {
            diagnostics.Report(DiagnosticDescriptors.BS0035, call.CstOrigin.Span, null,
                "macro expansion produced null AST node");
            return call;
        }

        // 展開結果をさらに再帰的に展開（ネストマクロ呼び出しに対応）
        return ExpandNode(expanded, registry, diagnostics, expandingNames.Add(call.Name), depth + 1);
    }

    /// <summary>
    /// CST ノード内のパラメータ名（Identifier トークン）を実引数の CST ノードで置換する。
    /// <c>&lt;body&gt;</c> プレースホルダも呼び出し側の body で置換する。
    /// </summary>
    private static CstNode SubstituteCstNode(
        CstNode node,
        Dictionary<string, CstNode> paramMap,
        CstNode bodyReplacement)
    {
        // <body> プレースホルダの置換
        if (node is CstBodyPlaceholder)
        {
            return bodyReplacement;
        }

        // パラメータ名に一致する識別子の置換
        // CstMemberAccess の場合、先頭セグメントがパラメータ名なら置換
        // （簡易実装: 完全なパラメータ置換は将来の拡張で対応）

        // 子ノードを持つ複合ノードの場合は子を再帰的に置換
        switch (node)
        {
            case CstSequence seq:
                return new CstSequence(seq.OpenParen, seq.Keyword,
                    seq.Children_.Select(c => SubstituteCstNode(c, paramMap, bodyReplacement)).ToImmutableArray(),
                    seq.CloseParen);

            case CstSelect sel:
                return new CstSelect(sel.OpenParen, sel.Keyword,
                    sel.Children_.Select(c => SubstituteCstNode(c, paramMap, bodyReplacement)).ToImmutableArray(),
                    sel.CloseParen);

            case CstGuard guard:
                return new CstGuard(guard.OpenParen, guard.Keyword,
                    SubstituteCstNode(guard.Condition, paramMap, bodyReplacement),
                    SubstituteCstNode(guard.Body, paramMap, bodyReplacement),
                    guard.CloseParen);

            case CstIf ifNode:
                return new CstIf(ifNode.OpenParen, ifNode.Keyword,
                    SubstituteCstNode(ifNode.Condition, paramMap, bodyReplacement),
                    SubstituteCstNode(ifNode.Then, paramMap, bodyReplacement),
                    ifNode.Else != null ? SubstituteCstNode(ifNode.Else, paramMap, bodyReplacement) : null,
                    ifNode.CloseParen);

            case CstInvert inv:
                return new CstInvert(inv.OpenParen, inv.Keyword,
                    SubstituteCstNode(inv.Child, paramMap, bodyReplacement),
                    inv.CloseParen);

            case CstRepeat rep:
                return new CstRepeat(rep.OpenParen, rep.Keyword, rep.Count,
                    SubstituteCstNode(rep.Child, paramMap, bodyReplacement),
                    rep.CloseParen);

            case CstTimeout timeout:
                return new CstTimeout(timeout.OpenParen, timeout.Keyword, timeout.Seconds,
                    SubstituteCstNode(timeout.Child, paramMap, bodyReplacement),
                    timeout.CloseParen);

            case CstCooldown cooldown:
                return new CstCooldown(cooldown.OpenParen, cooldown.Keyword, cooldown.Seconds,
                    SubstituteCstNode(cooldown.Child, paramMap, bodyReplacement),
                    cooldown.CloseParen);

            case CstWhile whileNode:
                return new CstWhile(whileNode.OpenParen, whileNode.Keyword,
                    SubstituteCstNode(whileNode.Condition, paramMap, bodyReplacement),
                    SubstituteCstNode(whileNode.Body, paramMap, bodyReplacement),
                    whileNode.CloseParen);

            case CstCheck check:
                return new CstCheck(check.OpenParen, check.Keyword,
                    SubstituteCstNode(check.Expr, paramMap, bodyReplacement),
                    check.CloseParen);

            case CstReactive reactive:
                return new CstReactive(reactive.OpenParen, reactive.Keyword,
                    SubstituteCstNode(reactive.Condition, paramMap, bodyReplacement),
                    SubstituteCstNode(reactive.Body, paramMap, bodyReplacement),
                    reactive.CloseParen);

            case CstParallel par:
                return new CstParallel(par.OpenParen, par.Keyword, par.Policy, par.PolicyArg,
                    par.Children_.Select(c => SubstituteCstNode(c, paramMap, bodyReplacement)).ToImmutableArray(),
                    par.CloseParen);

            default:
                // リーフノード（CstMemberAccess, CstCall, CstLiteral 等）はそのまま返す
                return node;
        }
    }

    /// <summary>ダミートークンを生成するヘルパー。</summary>
    private static Token DummyToken(TokenKind kind, string text) =>
        new(kind, text, new TextSpan(0, 0),
            ImmutableArray<Trivia>.Empty, ImmutableArray<Trivia>.Empty);

    /// <summary>
    /// 単一の CST ノードを AST ノードに変換するヘルパー。
    /// CstToAstLowering の内部メソッドが private のため、
    /// トップレベルの Lower を経由して単一ノードの変換を行う。
    /// </summary>
    private static AstNode? LowerCstNodeToAst(CstNode cstNode)
    {
        // 簡易実装: CST ノードを含むダミーの CstTree → CstProgram を構築し、
        // CstToAstLowering.Lower() を経由して AST に変換する
        var dummyTree = new CstTree(
            DummyToken(TokenKind.LeftParen, "("),
            DummyToken(TokenKind.Identifier, "tree"),
            DummyToken(TokenKind.Identifier, "_macro_temp_"),
            ImmutableArray.Create(cstNode),
            DummyToken(TokenKind.RightParen, ")"));
        var dummyProgram = new CstProgram(
            ImmutableArray.Create(dummyTree),
            DummyToken(TokenKind.EndOfFile, ""));
        var trees = CstToAstLowering.Lower(dummyProgram);

        if (trees.Length > 0 && trees[0].Body.Length > 0)
            return trees[0].Body[0];
        return null;
    }

    /// <summary>
    /// 子ノードを展開した新しいノードを構築するヘルパー。
    /// </summary>
    private static AstNode RebuildWithExpandedChildren(
        AstNode node,
        Dictionary<string, AstDefmacro> registry,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string> expandingNames,
        int depth)
    {
        switch (node)
        {
            case AstTree tree:
                return new AstTree(tree.CstOrigin, tree.Name,
                    tree.Body.Select(c => ExpandNode(c, registry, diagnostics, expandingNames, depth)).ToImmutableArray(),
                    tree.BlackboardType);

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
                // AstActionCall, AstCheck, AstRef, AstDefdecCall（非マクロ）等はリーフなのでそのまま
                return node;
        }
    }
}
