using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Resolution;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// AST ツリー内のメンバー参照を Roslyn <c>INamedTypeSymbol</c> を使って解決する。
///
/// DSL のメンバーパス（<c>.Health</c>、<c>.Target.Position</c> 等）を
/// コンテキスト型のメンバー（プロパティ、フィールド、メソッド）と照合し、
/// 解決結果を <see cref="SemanticModel"/> に格納する。
///
/// 名前の照合には <see cref="NameConversion"/> の候補生成を使用し、
/// ケバブケース → PascalCase 等の変換を含む柔軟なマッチングを行う。
///
/// <para>
/// 処理対象のノード:
/// <list type="bullet">
///   <item><description><see cref="AstMemberAccessExpr"/>: プロパティ・フィールド参照</description></item>
///   <item><description><see cref="AstActionCall"/>: メソッド呼び出し（ノード位置）</description></item>
///   <item><description><see cref="AstCallExpr"/>: メソッド呼び出し（式位置）</description></item>
/// </list>
/// </para>
///
/// <para>
/// 報告する診断:
/// <list type="bullet">
///   <item><description>BS0001: メンバーが見つからない</description></item>
///   <item><description>BS0005: メソッドの引数の数が一致しない</description></item>
///   <item><description>BS0012: オーバーロード解決があいまい</description></item>
/// </list>
/// </para>
/// </summary>
public class NameResolver
{
    private readonly INamedTypeSymbol _contextType;
    private readonly SemanticModel _model;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>
    /// <see cref="NameResolver"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="contextType">DSL メンバーの解決先となるコンテキスト型。</param>
    /// <param name="model">解決結果を格納するセマンティックモデル。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    public NameResolver(INamedTypeSymbol contextType, SemanticModel model, DiagnosticBag diagnostics)
    {
        _contextType = contextType;
        _model = model;
        _diagnostics = diagnostics;
    }

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ツリー全体を走査し、全てのメンバー参照を解決する。
    /// </summary>
    /// <param name="tree">解決対象の AST ツリー。</param>
    public void ResolveTree(AstTree tree)
    {
        foreach (var node in tree.Body)
        {
            ResolveNode(node);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ノード走査
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ノードを再帰的に走査し、含まれるメンバー参照を解決する。
    /// </summary>
    private void ResolveNode(AstNode node)
    {
        switch (node)
        {
            case AstSelector sel:
                foreach (var child in sel.Children_) ResolveNode(child);
                break;

            case AstSequence seq:
                foreach (var child in seq.Children_) ResolveNode(child);
                break;

            case AstParallel par:
                foreach (var child in par.Children_) ResolveNode(child);
                break;

            case AstCheck chk:
                ResolveExpr(chk.Expr);
                break;

            case AstGuard grd:
                ResolveExpr(grd.Condition);
                ResolveNode(grd.Body);
                break;

            case AstIf ifn:
                ResolveExpr(ifn.Condition);
                ResolveNode(ifn.Then);
                if (ifn.Else != null) ResolveNode(ifn.Else);
                break;

            case AstInvert inv:
                ResolveNode(inv.Child);
                break;

            case AstRepeat rep:
                ResolveNode(rep.Child);
                break;

            case AstTimeout tmo:
                ResolveNode(tmo.Child);
                break;

            case AstCooldown cld:
                ResolveNode(cld.Child);
                break;

            case AstWhile whl:
                ResolveExpr(whl.Condition);
                ResolveNode(whl.Body);
                break;

            case AstActionCall act:
                ResolveActionCall(act);
                break;
        }
    }

    /// <summary>
    /// AST 式を再帰的に走査し、含まれるメンバー参照を解決する。
    /// </summary>
    private void ResolveExpr(AstExpr expr)
    {
        switch (expr)
        {
            case AstMemberAccessExpr mem:
                ResolveMemberAccess(mem);
                break;

            case AstBinaryExpr bin:
                ResolveExpr(bin.Lhs);
                ResolveExpr(bin.Rhs);
                break;

            case AstUnaryExpr un:
                ResolveExpr(un.Operand);
                break;

            case AstLogicExpr log:
                foreach (var operand in log.Operands) ResolveExpr(operand);
                break;

            case AstCallExpr call:
                ResolveCallExpr(call);
                break;

            case AstLiteralExpr:
                // リテラルには解決するメンバーがない
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  メンバーアクセスの解決
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// メンバーアクセス式 (<c>.Foo</c>, <c>.Foo.Bar</c>) を解決する。
    ///
    /// メンバーパスの各セグメントを順にコンテキスト型のメンバーと照合し、
    /// 最終セグメントの解決結果を <see cref="SemanticModel"/> に格納する。
    /// チェーンメンバー（<c>.Foo.Bar</c>）の場合、各セグメントの型を辿って解決する。
    /// </summary>
    private void ResolveMemberAccess(AstMemberAccessExpr mem)
    {
        var symbol = ResolveMemberPath(mem.Path, mem.CstOrigin.Span);
        if (symbol != null)
        {
            _model.SetSymbol(mem, symbol);
            CheckObsolete(symbol, mem.CstOrigin.Span);
        }
    }

    /// <summary>
    /// メンバーパスをコンテキスト型に対して解決する。
    ///
    /// 各セグメントについて:
    /// <list type="number">
    ///   <item><description><see cref="NameConversion.GenerateCandidates"/> で C# 名候補を生成</description></item>
    ///   <item><description>現在の型のメンバーと大文字小文字を無視して照合</description></item>
    ///   <item><description>見つかった場合、そのメンバーの型を次のセグメントの解決に使用</description></item>
    ///   <item><description>見つからない場合、BS0001 を報告して <c>null</c> を返す</description></item>
    /// </list>
    /// </summary>
    /// <returns>最後のセグメントに対応するシンボル。解決失敗時は <c>null</c>。</returns>
    private ISymbol? ResolveMemberPath(MemberPath path, TextSpan span)
    {
        ITypeSymbol currentType = _contextType;
        ISymbol? lastSymbol = null;

        foreach (var segment in path.Segments)
        {
            lastSymbol = FindMemberOnType(currentType, segment, span);
            if (lastSymbol == null)
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.BS0001, span, null,
                    segment, currentType.ToDisplayString());
                return null;
            }

            // 次のセグメントの解決のために、メンバーの型を取得
            currentType = GetMemberType(lastSymbol);
            if (currentType == null!)
            {
                return lastSymbol;
            }
        }

        return lastSymbol;
    }

    // ═══════════════════════════════════════════════════════════
    //  アクション呼び出しの解決
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// アクション呼び出し（ノード位置）を解決する。
    /// メソッド名をコンテキスト型から検索し、引数の数を検証する。
    /// </summary>
    private void ResolveActionCall(AstActionCall act)
    {
        // 引数の式も解決する
        foreach (var arg in act.Args)
        {
            ResolveExpr(arg);
        }

        var methodName = act.Member.Segments[^1];
        var span = act.CstOrigin.Span;

        // メンバーパスが複数セグメントの場合、先頭セグメントのメンバー型を辿る
        ITypeSymbol targetType = _contextType;
        if (act.Member.Segments.Length > 1)
        {
            for (var i = 0; i < act.Member.Segments.Length - 1; i++)
            {
                var navSymbol = FindMemberOnType(targetType, act.Member.Segments[i], span);
                if (navSymbol == null)
                {
                    _diagnostics.Report(
                        DiagnosticDescriptors.BS0001, span, null,
                        act.Member.Segments[i], targetType.ToDisplayString());
                    return;
                }
                targetType = GetMemberType(navSymbol);
                if (targetType == null!) return;
            }
        }

        var resolved = ResolveMethod(targetType, methodName, act.Args.Length, span);
        if (resolved != null)
        {
            _model.SetSymbol(act, resolved);
            CheckObsolete(resolved, span);
        }
    }

    /// <summary>
    /// メソッド呼び出し式（式位置）を解決する。
    /// </summary>
    private void ResolveCallExpr(AstCallExpr call)
    {
        foreach (var arg in call.Args)
        {
            ResolveExpr(arg);
        }

        var methodName = call.Member.Segments[^1];
        var span = call.CstOrigin.Span;

        ITypeSymbol targetType = _contextType;
        if (call.Member.Segments.Length > 1)
        {
            for (var i = 0; i < call.Member.Segments.Length - 1; i++)
            {
                var navSymbol = FindMemberOnType(targetType, call.Member.Segments[i], span);
                if (navSymbol == null)
                {
                    _diagnostics.Report(
                        DiagnosticDescriptors.BS0001, span, null,
                        call.Member.Segments[i], targetType.ToDisplayString());
                    return;
                }
                targetType = GetMemberType(navSymbol);
                if (targetType == null!) return;
            }
        }

        var resolved = ResolveMethod(targetType, methodName, call.Args.Length, span);
        if (resolved != null)
        {
            _model.SetSymbol(call, resolved);
            CheckObsolete(resolved, span);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Obsolete チェック（BS0010）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 解決されたシンボルに <c>[System.Obsolete]</c> 属性が付与されている場合、
    /// BS0010 警告を報告する。
    /// </summary>
    /// <param name="symbol">解決されたシンボル。</param>
    /// <param name="span">DSL 上のソース位置。</param>
    private void CheckObsolete(ISymbol symbol, TextSpan span)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")
            {
                var reason = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value?.ToString() ?? ""
                    : "";
                _diagnostics.Report(
                    DiagnosticDescriptors.BS0010, span, null,
                    symbol.Name, reason);
                return;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  メソッド解決
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 指定された型からメソッドを解決する。
    ///
    /// <list type="number">
    ///   <item><description>名前候補で全メソッドを検索</description></item>
    ///   <item><description>見つからなければ BS0001 を報告</description></item>
    ///   <item><description>引数の数でフィルタリング</description></item>
    ///   <item><description>一致なしなら BS0005、複数一致なら BS0012 を報告</description></item>
    /// </list>
    /// </summary>
    /// <returns>解決されたメソッドシンボル。失敗時は <c>null</c>。</returns>
    private IMethodSymbol? ResolveMethod(ITypeSymbol type, string dslName, int argCount, TextSpan span)
    {
        // 名前でメソッドを検索
        var allMethods = FindMethodsByName(type, dslName, span);
        if (allMethods.Count == 0)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.BS0001, span, null,
                dslName, type.ToDisplayString());
            return null;
        }

        // 引数の数でフィルタリング
        var matched = allMethods.Where(m => m.Parameters.Length == argCount).ToList();
        if (matched.Count == 0)
        {
            // メソッドは見つかったが引数の数が合わない
            var expectedCount = allMethods[0].Parameters.Length;
            _diagnostics.Report(
                DiagnosticDescriptors.BS0005, span, null,
                dslName, expectedCount, argCount);
            return null;
        }

        if (matched.Count > 1)
        {
            // 複数候補が見つかった（オーバーロード解決あいまい）
            var paramTypes = string.Join(", ", matched[0].Parameters.Select(p => p.Type.ToDisplayString()));
            _diagnostics.Report(
                DiagnosticDescriptors.BS0012, span, null,
                dslName, paramTypes);
            // あいまいでも最初の候補を返す（後続の解析を続行するため）
            return matched[0];
        }

        return matched[0];
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 型のメンバーから DSL 名にマッチするメンバー（プロパティ・フィールド）を検索する。
    /// <see cref="NameConversion.GenerateCandidates"/> で生成した候補を
    /// 大文字小文字無視で照合する。
    ///
    /// 複数の異なる候補がそれぞれ異なるメンバーにマッチした場合、
    /// 優先順位の高い候補を採用し、BS0104 警告を報告する。
    /// </summary>
    /// <param name="type">検索対象の型。</param>
    /// <param name="dslName">DSL で記述されたメンバー名。</param>
    /// <param name="span">DSL 上のソース位置（BS0104 報告用）。</param>
    /// <returns>最初にマッチしたシンボル。見つからない場合は <c>null</c>。</returns>
    private ISymbol? FindMemberOnType(ITypeSymbol type, string dslName, TextSpan span)
    {
        ISymbol? firstMatch = null;

        foreach (var candidate in NameConversion.GenerateCandidates(dslName))
        {
            foreach (var member in type.GetMembers())
            {
                // メソッドは FindMethodsByName で別途検索するため、ここではスキップ
                if (member is IMethodSymbol) continue;

                if (string.Equals(member.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    if (firstMatch == null)
                    {
                        firstMatch = member;
                    }
                    else if (!string.Equals(firstMatch.Name, member.Name, StringComparison.Ordinal))
                    {
                        // 異なるメンバーが同じ候補または異なる候補にマッチ → あいまいな名前解決
                        _diagnostics.Report(
                            DiagnosticDescriptors.BS0104, span, null,
                            dslName, firstMatch.Name, member.Name);
                        return firstMatch;
                    }
                    // break しない: 同じ候補で別のメンバー（大文字小文字違い）が
                    // マッチする可能性があるため、全メンバーを走査し続ける
                }
            }
        }

        return firstMatch;
    }

    /// <summary>
    /// 型のメンバーから DSL 名にマッチする全メソッドを検索する。
    ///
    /// 複数の異なる候補がそれぞれ異なる名前のメソッドにマッチした場合、
    /// 優先順位の高い候補のメソッドを採用し、BS0104 警告を報告する。
    /// 同じ候補名内のオーバーロード（同名・異なるパラメータ）は全て含める。
    /// </summary>
    /// <param name="type">検索対象の型。</param>
    /// <param name="dslName">DSL で記述されたメソッド名。</param>
    /// <param name="span">DSL 上のソース位置（BS0104 報告用）。</param>
    /// <returns>マッチしたメソッドシンボルのリスト。</returns>
    private List<IMethodSymbol> FindMethodsByName(ITypeSymbol type, string dslName, TextSpan span)
    {
        var results = new List<IMethodSymbol>();
        string? resolvedName = null;

        foreach (var candidate in NameConversion.GenerateCandidates(dslName))
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IMethodSymbol method
                    && !method.IsImplicitlyDeclared
                    && string.Equals(method.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    if (resolvedName == null)
                    {
                        resolvedName = method.Name;
                    }

                    if (string.Equals(resolvedName, method.Name, StringComparison.Ordinal))
                    {
                        // 同名メソッド（オーバーロード含む）を追加
                        if (!results.Contains(method))
                        {
                            results.Add(method);
                        }
                    }
                    else
                    {
                        // 異なる候補が異なる名前のメソッドにマッチ → あいまいな名前解決
                        _diagnostics.Report(
                            DiagnosticDescriptors.BS0104, span, null,
                            dslName, resolvedName, method.Name);
                        return results;
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// シンボルの型（プロパティの型、フィールドの型、メソッドの戻り値型）を取得する。
    /// </summary>
    private static ITypeSymbol GetMemberType(ISymbol symbol) => symbol switch
    {
        IPropertySymbol prop => prop.Type,
        IFieldSymbol field => field.Type,
        IMethodSymbol method => method.ReturnType,
        _ => null!,
    };

    // ═══════════════════════════════════════════════════════════
    //  Roslyn 型 → CrispType 変換（TypeInferer で使用）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Roslyn の <c>ITypeSymbol</c> を Crisp の型システムに変換する。
    ///
    /// <c>SpecialType</c> で基本型を識別し、<c>Crisp.Runtime.BtStatus</c> は
    /// 完全修飾名で照合する。未知の型はカスタム <see cref="CrispType"/> インスタンスとなる。
    /// </summary>
    /// <param name="symbol">変換対象の Roslyn 型シンボル。</param>
    /// <returns>対応する Crisp 型。</returns>
    public static CrispType ToCrispType(ITypeSymbol symbol)
    {
        return symbol.SpecialType switch
        {
            SpecialType.System_Int32 => CrispType.Int,
            SpecialType.System_Single => CrispType.Float,
            SpecialType.System_Boolean => CrispType.Bool,
            SpecialType.System_String => CrispType.String,
            SpecialType.System_Void => CrispType.Void,
            _ when symbol.ToDisplayString() == "Crisp.Runtime.BtStatus" => CrispType.BtStatus,
            _ when symbol.ToDisplayString() == "Crisp.Runtime.BtNode" => CrispType.BtNode,
            _ => new CrispType(symbol.ToDisplayString()),
        };
    }
}
