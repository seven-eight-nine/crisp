using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// セマンティック解析のオーケストレーター。
///
/// 名前解決 → 型推論 → 型検査 の3フェーズを順に実行し、
/// 統合された解析結果を返す。
///
/// <para>
/// 処理フロー:
/// <list type="number">
///   <item><description><see cref="NameResolver"/>: メンバー参照をコンテキスト型のシンボルと照合</description></item>
///   <item><description><see cref="TypeInferer"/>: ボトムアップで式の型を推論</description></item>
///   <item><description><see cref="TypeChecker"/>: トップダウンで型制約を検証</description></item>
/// </list>
/// </para>
///
/// Source Generator からは <see cref="Analyze"/> メソッドで呼び出し、
/// 返された <see cref="SemanticAnalysisResult"/> の診断情報をRoslyn 診断として報告する。
/// </summary>
public static class SemanticAnalyzer
{
    /// <summary>
    /// AST ツリーに対してセマンティック解析を実行する。
    ///
    /// コンテキスト型（<c>[BehaviorTree]</c> 属性が付与されたメソッドを含むクラス）を
    /// 基準として、DSL 内のメンバー参照を解決し、型の整合性を検証する。
    /// </summary>
    /// <param name="tree">解析対象の AST ツリー。</param>
    /// <param name="contextType">メンバー解決の基準となるコンテキスト型。</param>
    /// <returns>解析結果（セマンティックモデルと診断情報）。</returns>
    public static SemanticAnalysisResult Analyze(AstTree tree, INamedTypeSymbol contextType)
    {
        var model = new SemanticModel();
        var diagnostics = new DiagnosticBag();

        // フェーズ 1: 名前解決
        // DSL のメンバーパスをコンテキスト型の Roslyn シンボルと照合する。
        var resolver = new NameResolver(contextType, model, diagnostics);
        resolver.ResolveTree(tree);

        // フェーズ 2: 型推論（ボトムアップ）
        // リーフ式から親式へ向かって型を伝搬する。
        // 名前解決の結果（解決済みシンボルの型）を使用する。
        var inferer = new TypeInferer(model, diagnostics);
        inferer.InferTree(tree);

        // フェーズ 3: 型検査（トップダウン制約検証）
        // 各ノードが要求する型制約（check→bool, action→BtStatus 等）を検証する。
        // 型推論の結果を使用し、ErrorType によるカスケード抑制を行う。
        var checker = new TypeChecker(model, diagnostics);
        checker.CheckTree(tree);

        return new SemanticAnalysisResult(model, diagnostics);
    }
}

/// <summary>
/// セマンティック解析の結果を保持するレコード。
/// </summary>
/// <param name="Model">解決済みシンボルと推論型を含むセマンティックモデル。</param>
/// <param name="Diagnostics">解析中に検出された全診断メッセージ。</param>
public record SemanticAnalysisResult(SemanticModel Model, DiagnosticBag Diagnostics)
{
    /// <summary>エラーレベルの診断が存在するかどうか。</summary>
    public bool HasErrors => Diagnostics.HasErrors;
}
