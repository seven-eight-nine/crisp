using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// AST ノードに対してトップダウンの型制約検証を行う。
///
/// 型推論の結果（<see cref="SemanticModel"/>）を使用して、
/// 各ノードが要求する型制約を満たしているかを検査する。
///
/// <para>
/// 検査する制約:
/// <list type="bullet">
///   <item><description>check / guard / if / while: 条件式は <c>bool</c> であること（BS0007）</description></item>
///   <item><description>アクション呼び出し: メソッドの戻り値は <c>BtStatus</c> であること（BS0008）</description></item>
///   <item><description>メソッド引数: 引数の型がパラメータの型と互換であること（BS0006）</description></item>
/// </list>
/// </para>
///
/// <para>
/// ErrorType 伝搬（カスケード抑制）:
/// 式の推論型が <see cref="CrispType.Error"/> の場合、その式に対する型制約検査をスキップする。
/// これにより、名前解決や型推論で既に報告されたエラーに起因する二次的なエラーを抑制する。
/// </para>
/// </summary>
public class TypeChecker
{
    private readonly SemanticModel _model;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>
    /// <see cref="TypeChecker"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="model">型推論結果を含むセマンティックモデル。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    public TypeChecker(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ツリー全体を走査し、型制約の検査を実行する。
    /// </summary>
    /// <param name="tree">検査対象の AST ツリー。</param>
    public void CheckTree(AstTree tree)
    {
        foreach (var node in tree.Body)
        {
            CheckNode(node);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ノード走査
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ノードを再帰的に走査し、型制約を検査する。
    /// </summary>
    private void CheckNode(AstNode node)
    {
        switch (node)
        {
            case AstSelector sel:
                CheckChildrenReachability(sel.Children_, isSelector: true);
                foreach (var child in sel.Children_) CheckNode(child);
                break;

            case AstSequence seq:
                CheckChildrenReachability(seq.Children_, isSelector: false);
                foreach (var child in seq.Children_) CheckNode(child);
                break;

            case AstParallel par:
                foreach (var child in par.Children_) CheckNode(child);
                break;

            case AstCheck chk:
                CheckConditionIsBool(chk.Expr, "check");
                break;

            case AstGuard grd:
                CheckConditionIsBool(grd.Condition, "guard");
                CheckNode(grd.Body);
                break;

            case AstIf ifn:
                CheckConditionIsBool(ifn.Condition, "if");
                CheckNode(ifn.Then);
                if (ifn.Else != null)
                {
                    CheckNode(ifn.Else);
                }
                else
                {
                    // else 分岐がない場合、条件が false のときは常に Failure を返す。
                    // ユーザーが意図的かどうかを警告する（BS0302）。
                    _diagnostics.Report(
                        DiagnosticDescriptors.BS0302, ifn.CstOrigin.Span, null);
                }
                break;

            case AstInvert inv:
                CheckNode(inv.Child);
                break;

            case AstRepeat rep:
                CheckNode(rep.Child);
                break;

            case AstTimeout tmo:
                CheckNode(tmo.Child);
                break;

            case AstCooldown cld:
                CheckNode(cld.Child);
                break;

            case AstWhile whl:
                CheckConditionIsBool(whl.Condition, "while");
                CheckNode(whl.Body);
                break;

            case AstActionCall act:
                CheckActionReturnsBtStatus(act);
                CheckMethodArguments(act);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  型制約の検査
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 条件式が <c>bool</c> 型であることを検査する。
    ///
    /// check, guard, if, while ノードの条件式は bool でなければならない。
    /// 式の型が <see cref="CrispType.Error"/> の場合はカスケード抑制のためスキップする。
    /// </summary>
    /// <param name="condition">検査対象の条件式。</param>
    /// <param name="nodeName">報告用のノード名（"check", "guard", "if", "while"）。</param>
    private void CheckConditionIsBool(AstExpr condition, string nodeName)
    {
        var type = _model.GetType(condition);

        // ErrorType の場合はカスケード抑制（上流で既にエラー報告済み）
        if (type.IsError) return;

        // Unknown の場合もスキップ（型情報が不足している）
        if (type == CrispType.Unknown) return;

        if (type != CrispType.Bool)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.BS0007, condition.CstOrigin.Span, null,
                nodeName, type.Name);
        }
    }

    /// <summary>
    /// アクション呼び出しのメソッドが <c>BtStatus</c> を返すことを検査する。
    ///
    /// ビヘイビアツリーのノード位置にあるアクション呼び出しは、
    /// <c>BtStatus</c> を返すメソッドでなければならない。
    /// </summary>
    private void CheckActionReturnsBtStatus(AstActionCall act)
    {
        var symbol = _model.GetSymbol(act);
        if (symbol is not IMethodSymbol method) return; // 未解決の場合はスキップ

        var returnType = NameResolver.ToCrispType(method.ReturnType);

        if (returnType.IsError) return;

        if (returnType != CrispType.BtStatus && returnType != CrispType.BtNode)
        {
            var methodName = act.Member.Segments[^1];
            _diagnostics.Report(
                DiagnosticDescriptors.BS0008, act.CstOrigin.Span, null,
                methodName);
        }
    }

    /// <summary>
    /// メソッド呼び出しの引数の型が、パラメータの型と互換であることを検査する。
    ///
    /// 各引数の推論型とパラメータの型を比較し、互換性がない場合は BS0006 を報告する。
    /// 引数の型が <see cref="CrispType.Error"/> の場合はカスケード抑制のためスキップする。
    /// </summary>
    private void CheckMethodArguments(AstActionCall act)
    {
        var symbol = _model.GetSymbol(act);
        if (symbol is not IMethodSymbol method) return;

        var parameters = method.Parameters;
        for (var i = 0; i < Math.Min(act.Args.Length, parameters.Length); i++)
        {
            var argType = _model.GetType(act.Args[i]);
            if (argType.IsError || argType == CrispType.Unknown) continue;

            var paramType = NameResolver.ToCrispType(parameters[i].Type);
            if (paramType.IsError) continue;

            if (!IsAssignable(argType, paramType))
            {
                var methodName = act.Member.Segments[^1];
                _diagnostics.Report(
                    DiagnosticDescriptors.BS0006, act.Args[i].CstOrigin.Span, null,
                    i, methodName, paramType.Name, argType.Name);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  到達可能性チェック（BS0301）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 複合ノード（selector / sequence）の子ノードの到達可能性を検査する。
    ///
    /// selector 内で無条件に Success を返すノード（<c>(check true)</c> 等）がある場合、
    /// それ以降の兄弟ノードは到達不能となる。
    /// sequence 内で無条件に Failure を返すノード（<c>(check false)</c> 等）がある場合も同様。
    /// </summary>
    /// <param name="children">子ノードの配列。</param>
    /// <param name="isSelector">selector の場合 <c>true</c>、sequence の場合 <c>false</c>。</param>
    private void CheckChildrenReachability(
        System.Collections.Immutable.ImmutableArray<AstNode> children,
        bool isSelector)
    {
        for (var i = 0; i < children.Length - 1; i++)
        {
            var terminates = isSelector
                ? AlwaysSucceeds(children[i])
                : AlwaysFails(children[i]);

            if (terminates)
            {
                // i+1 番目以降の子ノードは到達不能
                _diagnostics.Report(
                    DiagnosticDescriptors.BS0301, children[i + 1].CstOrigin.Span, null);
                break;
            }
        }
    }

    /// <summary>
    /// ノードが無条件に Success を返すかどうかを静的に判定する。
    /// <c>(check true)</c> のように常に成功するノードを検出する。
    /// </summary>
    private static bool AlwaysSucceeds(AstNode node)
    {
        return node is AstCheck chk
               && chk.Expr is AstLiteralExpr { LiteralKind: LiteralKind.Bool, Value: true };
    }

    /// <summary>
    /// ノードが無条件に Failure を返すかどうかを静的に判定する。
    /// <c>(check false)</c> のように常に失敗するノードを検出する。
    /// </summary>
    private static bool AlwaysFails(AstNode node)
    {
        return node is AstCheck chk
               && chk.Expr is AstLiteralExpr { LiteralKind: LiteralKind.Bool, Value: false };
    }

    // ═══════════════════════════════════════════════════════════
    //  型互換性判定
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソース型がターゲット型に代入可能かどうかを判定する。
    ///
    /// 代入可能な場合:
    /// <list type="bullet">
    ///   <item><description>同一型同士</description></item>
    ///   <item><description>int → float（暗黙数値変換）</description></item>
    ///   <item><description>null → 参照型</description></item>
    /// </list>
    /// </summary>
    private static bool IsAssignable(CrispType source, CrispType target)
    {
        // 同一型
        if (source == target) return true;

        // int → float の暗黙変換
        if (source == CrispType.Int && target == CrispType.Float) return true;

        // null → 参照型（簡略化: string と非基本型）
        if (source == CrispType.Null
            && target != CrispType.Int
            && target != CrispType.Float
            && target != CrispType.Bool)
        {
            return true;
        }

        return false;
    }
}
