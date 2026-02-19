using System.Text;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.IR;
using Crisp.Syntax.Pipeline;

namespace Crisp.Snapshot.Tests;

/// <summary>
/// スナップショットテスト用のヘルパークラス。
///
/// コンパイルパイプラインの各ステージの出力をテキスト化して返す。
/// Verify.Xunit のスナップショット比較に適した文字列表現を生成する。
/// </summary>
public static class SnapshotHelper
{
    /// <summary>
    /// ソーステキストを全ステージでコンパイルし、スナップショット用のテキスト群を返す。
    ///
    /// 構文エラーがある場合は IR / C# は生成しない（空文字列）。
    /// </summary>
    /// <param name="source">Crisp DSL ソーステキスト。</param>
    /// <param name="namespaceName">C# 生成時の名前空間名。</param>
    /// <param name="className">C# 生成時のクラス名。</param>
    /// <param name="methodName">C# 生成時のメソッド名。</param>
    /// <returns>各ステージの出力をまとめた結果。</returns>
    public static SnapshotResult CompileAndSnapshot(
        string source,
        string namespaceName = "Test",
        string className = "AI",
        string methodName = "Build")
    {
        // ステージ 1: Parse（CST 生成）
        var (program, diagnostics) = CompilationPipeline.Parse(source);
        var cstDump = program.DumpTree();

        // ステージ 2: AST Lowering
        var astTrees = CompilationPipeline.LowerToAst(program);
        var astDump = DumpAstTrees(astTrees);

        // ステージ 3: 診断テキスト
        var diagnosticsText = FormatDiagnostics(diagnostics);

        // エラーがある場合は IR / C# を生成しない
        if (diagnostics.HasErrors)
        {
            return new SnapshotResult(cstDump, astDump, diagnosticsText, null, null);
        }

        // ステージ 4: IR Lowering + シリアライズ
        var irTrees = CompilationPipeline.LowerToIr(astTrees);
        var irText = IrSerializer.Serialize(irTrees);

        // ステージ 5: C# Emit
        var csharpText = irTrees.Length > 0
            ? CSharpEmitter.Emit(irTrees[0], namespaceName, className, methodName)
            : "";

        return new SnapshotResult(cstDump, astDump, diagnosticsText, irText, csharpText);
    }

    /// <summary>
    /// AST ツリー配列をダンプテキストに変換する。
    /// </summary>
    private static string DumpAstTrees(System.Collections.Immutable.ImmutableArray<AstTree> astTrees)
    {
        var sb = new StringBuilder();
        foreach (var tree in astTrees)
        {
            sb.Append(tree.DumpTree());
        }
        return sb.ToString();
    }

    /// <summary>
    /// 診断情報をスナップショット用のテキストに変換する。
    /// 各診断は <c>[Code] Message</c> の形式で出力する。
    /// </summary>
    public static string FormatDiagnostics(DiagnosticBag diagnostics)
    {
        if (diagnostics.Diagnostics.Count == 0)
            return "(empty)";

        var sb = new StringBuilder();
        foreach (var diag in diagnostics.Diagnostics)
        {
            sb.AppendLine($"[{diag.Id}] {diag.Message}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// スナップショットテストの結果を保持するレコード。
/// </summary>
/// <param name="Cst">CST ダンプテキスト。</param>
/// <param name="Ast">AST ダンプテキスト。</param>
/// <param name="Diagnostics">診断テキスト。エラーなしの場合は "(empty)"。</param>
/// <param name="Ir">IR S式テキスト。エラー時は null。</param>
/// <param name="CSharp">生成 C# テキスト。エラー時は null。</param>
public record SnapshotResult(
    string Cst,
    string Ast,
    string Diagnostics,
    string? Ir,
    string? CSharp);
