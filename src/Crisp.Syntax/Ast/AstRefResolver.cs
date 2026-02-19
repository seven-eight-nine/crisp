using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Crisp.Syntax.Ast;

/// <summary>
/// F11: Multi-Tree のツリー参照解決パス。
///
/// AST 上の <see cref="AstRef"/> ノードについて以下の検査と解決を行う:
/// <list type="bullet">
///   <item><description>参照先ツリーの名前解決: 同一ファイル内（または import 先）のツリーを検索し、
///     <see cref="AstRef.ResolvedTree"/> を設定する</description></item>
///   <item><description>未定義ツリー参照の検出: 参照先が見つからない場合に BS0038 を報告する</description></item>
///   <item><description>循環参照の検出: ツリー間の参照グラフを走査し、循環があれば BS0037 を報告する</description></item>
/// </list>
///
/// 使用タイミング:
/// CstToAstLowering の後、AstToIrLowering の前に実行する。
/// 名前解決（NameResolver）とは独立して動作する。
/// </summary>
public static class AstRefResolver
{
    /// <summary>
    /// ツリー配列内の全 <see cref="AstRef"/> ノードを解決し、循環参照と未定義参照を検出する。
    /// </summary>
    /// <param name="trees">解決対象の AST ツリー群。</param>
    /// <param name="diagnostics">診断バッグ（エラー・警告の報告先）。</param>
    /// <returns>解決済みの AST ツリー群（AstRef.ResolvedTree が設定される）。</returns>
    public static ImmutableArray<AstTree> Resolve(
        ImmutableArray<AstTree> trees,
        DiagnosticBag diagnostics)
    {
        // ツリー名 → AstTree のマップを構築
        var treeMap = new Dictionary<string, AstTree>();
        foreach (var tree in trees)
        {
            treeMap[tree.Name] = tree;
        }

        // 各ツリー内の AstRef を解決
        foreach (var tree in trees)
        {
            ResolveRefsInNode(tree, treeMap, diagnostics);
        }

        // 循環参照の検出
        DetectCircularReferences(trees, diagnostics);

        return trees;
    }

    /// <summary>
    /// AST ノードを再帰的に走査し、<see cref="AstRef"/> の参照先を解決する。
    /// </summary>
    private static void ResolveRefsInNode(
        AstNode node,
        Dictionary<string, AstTree> treeMap,
        DiagnosticBag diagnostics)
    {
        if (node is AstRef astRef)
        {
            if (treeMap.TryGetValue(astRef.TreeName, out var resolved))
            {
                astRef.ResolvedTree = resolved;
            }
            else
            {
                // BS0038: 参照先のツリーが見つからない
                diagnostics.Report(
                    DiagnosticDescriptors.BS0038,
                    astRef.CstOrigin.Span,
                    null,
                    astRef.TreeName);
            }
            return;
        }

        // 子ノードを再帰的に走査
        foreach (var child in node.Children)
        {
            ResolveRefsInNode(child, treeMap, diagnostics);
        }
    }

    /// <summary>
    /// ツリー間の参照グラフを走査し、循環参照を検出する。
    /// DFS（深さ優先探索）で訪問済みスタックを追跡し、
    /// 再訪問を検出した時点で BS0037 を報告する。
    /// </summary>
    private static void DetectCircularReferences(
        ImmutableArray<AstTree> trees,
        DiagnosticBag diagnostics)
    {
        // ツリー名 → そのツリーが直接参照するツリー名の集合を構築
        var adjacency = new Dictionary<string, List<(string Target, TextSpan Span)>>();
        foreach (var tree in trees)
        {
            var refs = new List<(string Target, TextSpan Span)>();
            CollectRefs(tree, refs);
            adjacency[tree.Name] = refs;
        }

        // DFS で循環検出
        var visited = new HashSet<string>();       // 完全に処理済み
        var inStack = new HashSet<string>();        // 現在の DFS パス上
        var path = new List<string>();              // 現在のパス（循環報告用）

        foreach (var tree in trees)
        {
            if (!visited.Contains(tree.Name))
            {
                Dfs(tree.Name, adjacency, visited, inStack, path, diagnostics);
            }
        }
    }

    /// <summary>DFS で循環参照を検出する。</summary>
    private static void Dfs(
        string current,
        Dictionary<string, List<(string Target, TextSpan Span)>> adjacency,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        DiagnosticBag diagnostics)
    {
        visited.Add(current);
        inStack.Add(current);
        path.Add(current);

        if (adjacency.TryGetValue(current, out var refs))
        {
            foreach (var (target, span) in refs)
            {
                if (inStack.Contains(target))
                {
                    // 循環を検出: パス上の target の位置から現在のノードまでのサイクルを構築
                    var cycleStart = path.IndexOf(target);
                    var cycle = path.Skip(cycleStart).ToList();
                    cycle.Add(target); // 循環を閉じる
                    var cycleStr = string.Join(" → ", cycle);

                    diagnostics.Report(
                        DiagnosticDescriptors.BS0037,
                        span,
                        null,
                        cycleStr);
                }
                else if (!visited.Contains(target))
                {
                    Dfs(target, adjacency, visited, inStack, path, diagnostics);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(current);
    }

    /// <summary>ノード内の全 <see cref="AstRef"/> を収集する。</summary>
    private static void CollectRefs(AstNode node, List<(string Target, TextSpan Span)> refs)
    {
        if (node is AstRef astRef)
        {
            refs.Add((astRef.TreeName, astRef.CstOrigin.Span));
            return;
        }

        foreach (var child in node.Children)
        {
            CollectRefs(child, refs);
        }
    }
}
