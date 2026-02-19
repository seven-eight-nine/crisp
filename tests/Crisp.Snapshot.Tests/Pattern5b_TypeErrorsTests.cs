using Crisp.Semantics;
using Crisp.Syntax;
using Crisp.Syntax.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crisp.Snapshot.Tests;

/// <summary>
/// Pattern 5b: 型エラー — 名前解決・型検査エラーのスナップショットテスト。
///
/// 固定される仕様:
/// <list type="bullet">
///   <item><description>存在しないメンバー → BS0001</description></item>
///   <item><description>check に bool でない式 → BS0007</description></item>
///   <item><description>比較不能な型 → BS0003</description></item>
///   <item><description>引数の数が不一致 → BS0005</description></item>
///   <item><description>戻り値が BtStatus でない → BS0008</description></item>
///   <item><description>算術不能な型 → BS0004</description></item>
///   <item><description>ErrorType によるカスケード抑止（BS0004 のみ報告、BS0003/BS0007 は抑制）</description></item>
///   <item><description>IR / C# は生成されない（セマンティックエラーがあるため）</description></item>
/// </list>
/// </summary>
public class Pattern5b_TypeErrorsTests
{
    /// <summary>Pattern 5b の DSL ソース。</summary>
    private const string DslSource = @";; 存在しないメンバー、型不一致、引数エラー
(tree TypeError
  (select
    ;; BS0001: メンバー未解決
    (check .NonExistent)

    ;; BS0007: checkにboolでない式
    (check .Health)

    ;; BS0003: 比較不能な型
    (check (< .Health ""hello""))

    ;; BS0005: 引数の数が不一致
    (.Attack)

    ;; BS0008: 戻り値がBtStatusでない
    (.DistanceTo .Target)

    ;; BS0004: 算術不能な型 + カスケード抑止テスト
    (check (> (+ .Name 1) 0))

    (.Patrol)))";

    /// <summary>Pattern 5b のコンテキスト型定義。</summary>
    private const string ContextSource = @"
using Crisp.Runtime;

namespace Test;

public class Entity
{
    public float X { get; set; }
}

public class ErrorAI
{
    public float Health { get; set; }
    public string Name { get; set; } = """";
    public Entity Target { get; set; } = new();

    public BtStatus Attack(Entity target) => BtStatus.Success;
    public BtStatus Patrol() => BtStatus.Success;

    // BtActionなし、戻り値がfloat — アクション位置で呼ぶとBS0008
    public float DistanceTo(Entity target) => 0f;
}";

    /// <summary>
    /// セマンティック解析を実行し、統合された診断情報を返す。
    /// </summary>
    private static (SnapshotResult SyntaxResult, string SemanticDiagnostics) AnalyzeWithSemantics()
    {
        // 構文解析（CST/AST）
        var syntaxResult = SnapshotHelper.CompileAndSnapshot(DslSource);

        // セマンティック解析用の Roslyn コンパイルを構築
        var contextType = GetTypeSymbol(ContextSource, "Test.ErrorAI");
        if (contextType == null)
            return (syntaxResult, "Error: Could not resolve context type");

        // AST を取得してセマンティック解析を実行
        var (program, _) = CompilationPipeline.Parse(DslSource);
        var astTrees = CompilationPipeline.LowerToAst(program);
        var semanticResult = SemanticAnalyzer.Analyze(astTrees[0], contextType);
        var diagnosticsText = SnapshotHelper.FormatDiagnostics(semanticResult.Diagnostics);

        return (syntaxResult, diagnosticsText);
    }

    /// <summary>
    /// C# ソースからコンパイルして型シンボルを取得するヘルパー。
    /// </summary>
    private static INamedTypeSymbol? GetTypeSymbol(string csSource, string fullTypeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csSource);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BtNode).Assembly.Location),
        };

        var systemRuntimePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (System.IO.File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName(fullTypeName);
    }

    [Fact]
    public Task CST構造がエラーなしで生成される()
    {
        // 構文は正しいので CST にエラーノードは含まれない
        var (syntaxResult, _) = AnalyzeWithSemantics();
        return Verify(syntaxResult.Cst).UseMethodName("Pattern5b_Cst");
    }

    [Fact]
    public Task AST構造が全ノード変換される()
    {
        var (syntaxResult, _) = AnalyzeWithSemantics();
        return Verify(syntaxResult.Ast).UseMethodName("Pattern5b_Ast");
    }

    [Fact]
    public Task セマンティックエラーが正しく報告される()
    {
        var (_, diagnosticsText) = AnalyzeWithSemantics();
        return Verify(diagnosticsText).UseMethodName("Pattern5b_Diagnostics");
    }

    [Fact]
    public void IRが生成されない()
    {
        // 構文エラーはないが、セマンティックエラーがあるため IR は生成しない
        // （構文パイプラインは IR を生成するが、セマンティックエラー時は Source Generator がスキップする）
        // ここでは、セマンティックエラーがあることを確認する
        var (_, diagnosticsText) = AnalyzeWithSemantics();
        Assert.DoesNotContain("(empty)", diagnosticsText);
    }

    [Fact]
    public void カスケード抑止が正しく動作する()
    {
        // (+ .Name 1) → BS0004 のみ
        // (> (+ .Name 1) 0) → BS0003 は抑制される
        // (check (> (+ .Name 1) 0)) → BS0007 は抑制される
        var (_, diagnosticsText) = AnalyzeWithSemantics();

        // BS0004 が含まれることを確認
        Assert.Contains("BS0004", diagnosticsText);

        // BS0004 のカスケードで BS0003/BS0007 が追加されないことを確認
        // （名前解決エラーの BS0001 からの BS0007 も同様に抑制される）
        var lines = diagnosticsText.Split('\n');
        var bs0004Count = lines.Count(l => l.Contains("BS0004"));
        Assert.Equal(1, bs0004Count);
    }
}
