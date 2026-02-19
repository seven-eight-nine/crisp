using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;
using Crisp.Syntax.Pipeline;

namespace Crisp.Syntax.Interpreter;

/// <summary>
/// Crisp のランタイムコンパイラ（F9: Hot Reload）。
/// ソーステキストを即座にコンパイルし、インタープリタベースの BtNode ツリーを構築する。
///
/// 主な用途:
/// <list type="bullet">
///   <item><description>Hot Reload: 実行中に .crisp ファイルを変更し、ツリーを即座に再構築</description></item>
///   <item><description>プロトタイピング: ゲームを再起動せずに AI の挙動を調整</description></item>
///   <item><description>テスト: 単一の API 呼び出しでソースから実行可能なツリーを生成</description></item>
/// </list>
///
/// コンパイルパイプライン:
/// <code>
/// ソーステキスト → Lexer → Parser → CstToAstLowering → AstToIrLowering → Evaluator
/// </code>
///
/// 注意事項:
/// <list type="bullet">
///   <item><description>Hot Reload 時、Running 中のノードの状態は引き継がない（新ツリーは初期状態から実行）</description></item>
///   <item><description>ツリー構造が変わった場合のノード対応関係は保証しない</description></item>
///   <item><description>エラーが含まれる場合は <see cref="CrispCompilationException"/> をスロー</description></item>
/// </list>
/// </summary>
public static class CrispRuntime
{
    /// <summary>
    /// ソーステキストをコンパイルし、IR ツリーの配列を返す。
    /// </summary>
    /// <param name="source">Crisp DSL ソーステキスト。</param>
    /// <returns>コンパイルされた IR ツリーの配列。</returns>
    /// <exception cref="CrispCompilationException">コンパイルエラーが含まれる場合。</exception>
    public static ImmutableArray<IrTree> Compile(string source)
    {
        // 1. パース
        var (program, diagnostics) = Parser.Parse(source);

        // 2. CST → AST lowering（defdec/macro 展開 + ref 解決を含む）
        var astTrees = CompilationPipeline.LowerToAst(program, diagnostics);

        // 3. エラー診断チェック
        var errors = diagnostics.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new CrispCompilationException(errors);

        // 4. AST → IR lowering
        var irTrees = AstToIrLowering.Lower(astTrees);

        return irTrees;
    }

    /// <summary>
    /// ソーステキストをコンパイルし、最初のツリーの Evaluator を返す。
    /// </summary>
    /// <param name="source">Crisp DSL ソーステキスト。</param>
    /// <param name="context">コンテキストオブジェクト（this に相当）。</param>
    /// <param name="blackboard">ブラックボードオブジェクト（$ アクセスの対象、省略可）。</param>
    /// <returns>最初のツリーの IR と Evaluator のタプル。</returns>
    /// <exception cref="CrispCompilationException">コンパイルエラーが含まれる場合。</exception>
    /// <exception cref="InvalidOperationException">ツリーが定義されていない場合。</exception>
    public static (IrTree Tree, Evaluator Evaluator) CompileAndBuild(
        string source, object context, object? blackboard = null)
    {
        var trees = Compile(source);
        if (trees.IsEmpty)
            throw new InvalidOperationException("ソースにツリー定義が含まれていません。");

        var evaluator = new Evaluator(context, blackboard);
        return (trees[0], evaluator);
    }
}

/// <summary>
/// Crisp コンパイルエラーの例外。
/// ソースに含まれるエラー診断のリストを保持する。
/// </summary>
public class CrispCompilationException : Exception
{
    /// <summary>コンパイル時に検出されたエラー診断のリスト。</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// 新しい <see cref="CrispCompilationException"/> を生成する。
    /// </summary>
    /// <param name="diagnostics">エラー診断のリスト。</param>
    public CrispCompilationException(IReadOnlyList<Diagnostic> diagnostics)
        : base(FormatMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    /// <summary>診断リストからエラーメッセージを構築する。</summary>
    private static string FormatMessage(IReadOnlyList<Diagnostic> diagnostics)
    {
        var messages = diagnostics.Select(d => $"[{d.Id}] {d.Message}");
        return $"Crisp コンパイルエラー ({diagnostics.Count} 件):\n{string.Join("\n", messages)}";
    }
}
