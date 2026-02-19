using Crisp.Semantics;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crisp.Semantics.Tests;

/// <summary>
/// セマンティクス解析テスト用のヘルパークラス。
///
/// テスト用の C# コンテキスト型を Roslyn Compilation で定義し、
/// Crisp DSL の AST と組み合わせてセマンティクス解析を実行するための
/// ユーティリティメソッドを提供する。
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// テスト用の C# ソースコードから Roslyn Compilation を生成し、
    /// 指定された完全修飾型名の <c>INamedTypeSymbol</c> を取得する。
    /// </summary>
    /// <param name="csSource">C# ソースコード。</param>
    /// <param name="fullTypeName">取得する型の完全修飾名（例: "Test.AI"）。</param>
    /// <returns>型シンボル。型が見つからない場合は <c>null</c>。</returns>
    public static INamedTypeSymbol? GetTypeSymbol(string csSource, string fullTypeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csSource);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BtStatus).Assembly.Location),
        };

        // System.Runtime の参照を追加（netcoreapp 必須）
        var systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName(fullTypeName);
    }

    /// <summary>
    /// テスト用の C# ソースコードから Roslyn Compilation を生成する。
    /// ジェネリック型の構築（Construct）等に Compilation が必要な場合に使用する。
    /// </summary>
    /// <param name="csSource">C# ソースコード。</param>
    /// <returns>Roslyn Compilation インスタンス。</returns>
    public static CSharpCompilation GetCompilation(string csSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csSource);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BtStatus).Assembly.Location),
        };

        var systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Crisp DSL ソースから AST ツリーを生成する。
    /// </summary>
    /// <param name="dslSource">Crisp DSL のソーステキスト。</param>
    /// <returns>AST ツリーの配列。</returns>
    public static AstTree[] ParseToAst(string dslSource)
    {
        var (program, _) = CompilationPipeline.Parse(dslSource);
        var astTrees = CompilationPipeline.LowerToAst(program);
        return [.. astTrees];
    }

    /// <summary>
    /// Crisp DSL ソースとコンテキスト型の C# 定義からセマンティクス解析を実行する。
    /// テストの簡略化のため、型シンボル取得と DSL パースを一括で行う。
    /// </summary>
    /// <param name="dslSource">Crisp DSL のソーステキスト。</param>
    /// <param name="csSource">コンテキスト型を含む C# ソースコード。</param>
    /// <param name="fullTypeName">コンテキスト型の完全修飾名。</param>
    /// <returns>セマンティクス解析結果。</returns>
    public static SemanticAnalysisResult Analyze(string dslSource, string csSource, string fullTypeName)
    {
        var contextType = GetTypeSymbol(csSource, fullTypeName)!;
        var astTrees = ParseToAst(dslSource);
        return SemanticAnalyzer.Analyze(astTrees[0], contextType);
    }
}
