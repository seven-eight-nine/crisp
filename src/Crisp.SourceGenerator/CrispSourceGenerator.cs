using System.Collections.Immutable;
using System.Text;
using Crisp.Query;
using Crisp.Syntax.Emitter;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynDiagnosticDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Crisp.SourceGenerator;

/// <summary>
/// Crisp DSL のインクリメンタル Source Generator。
/// <c>[BehaviorTree]</c> 属性付きメソッドを検出し、DSL ソースから C# コードを生成する。
///
/// <para>
/// 処理フロー:
/// <list type="number">
///   <item><description><c>[BehaviorTree("...")]</c> 属性付き partial メソッドを Syntax Provider で検出</description></item>
///   <item><description>属性引数からソース文字列を取得（インライン S式 or .crisp ファイル名）</description></item>
///   <item><description>.crisp ファイル名の場合は AdditionalFiles から内容を取得</description></item>
///   <item><description><see cref="Crisp.Query.QueryDatabase"/> に入力を設定し、派生クエリ経由でコンパイルを実行</description></item>
///   <item><description>生成された C# コードを Source Output として登録</description></item>
/// </list>
/// </para>
///
/// <para>
/// QueryDatabase 統合（アーキテクチャ仕様 7.3）:
/// LSP サーバーと同一の <see cref="Crisp.Query.QueryDatabase"/> 派生クエリロジックを使用する。
/// これにより「LSP ではエラーが出るのにビルドでは出ない」という意味的な不整合が原理的に発生しない。
/// ビルドごとに新規 DB を作成するため、Source Generator 側でのキャッシュ恩恵は薄いが、
/// アーキテクチャの統一性を優先する。
/// </para>
///
/// <para>
/// 制約事項:
/// <list type="bullet">
///   <item><description>対象メソッドは <c>partial</c> かつ引数なしで <c>BtNode</c> を返す必要がある</description></item>
///   <item><description>外部 .crisp ファイルは MSBuild の <c>AdditionalFiles</c> に登録されている必要がある（U7 決定事項）</description></item>
///   <item><description>インクリメンタルビルドに対応し、未変更ファイルの再コンパイルを回避する</description></item>
/// </list>
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public class CrispSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// <c>[BehaviorTree]</c> 属性の完全修飾名。
    /// </summary>
    private const string BehaviorTreeAttributeName = "Crisp.Runtime.BehaviorTreeAttribute";

    // ═══════════════════════════════════════════════════════════
    //  Roslyn Diagnostic 定義
    // ═══════════════════════════════════════════════════════════

    /// <summary>Crisp DSL の構文・意味エラー。</summary>
    private static readonly RoslynDiagnosticDescriptor CompileError = new(
        id: "CRISP0001",
        title: "Crisp コンパイルエラー",
        messageFormat: "{0}",
        category: "Crisp",
        defaultSeverity: RoslynDiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>外部 .crisp ファイルが AdditionalFiles に見つからない。</summary>
    private static readonly RoslynDiagnosticDescriptor FileNotFound = new(
        id: "CRISP0011",
        title: "外部ファイルが見つかりません",
        messageFormat: "外部ファイル '{0}' が AdditionalFiles に見つかりません。csproj で <AdditionalFiles Include=\"{0}\" /> を追加してください。",
        category: "Crisp",
        defaultSeverity: RoslynDiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Crisp コンパイラの内部エラー。</summary>
    private static readonly RoslynDiagnosticDescriptor InternalError = new(
        id: "CRISP0900",
        title: "Crisp 内部エラー",
        messageFormat: "Crisp コンパイラで内部エラーが発生しました: {0}",
        category: "Crisp",
        defaultSeverity: RoslynDiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════
    //  初期化
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Generator の初期化。Syntax Provider / AdditionalTexts Provider / CompilationProvider の
    /// 登録とコード生成パイプラインの構築を行う。
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. [BehaviorTree] 属性付きメソッドを検出し、必要な情報を抽出
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
            BehaviorTreeAttributeName,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (ctx, ct) => ExtractMethodInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // 2. AdditionalFiles から .crisp ファイルを収集
        var crispFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".crisp", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, ct) => new CrispFile(
                Path.GetFileName(text.Path),
                text.GetText(ct)?.ToString() ?? ""));

        // 3. メソッド情報 + .crisp ファイル群 + Compilation を結合
        var combined = methods
            .Combine(crispFiles.Collect())
            .Combine(context.CompilationProvider);

        // 4. 各メソッドに対して BT コード生成を実行
        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var ((method, files), compilation) = pair;
            GenerateSource(spc, method, files, compilation);
        });

        // 5. F6: AOT アクセサテーブル生成（クラス単位で重複排除）
        //    同一クラスに複数 [BehaviorTree] メソッドがある場合でもアクセサテーブルは1回だけ生成する。
        var distinctTypes = methods
            .Collect()
            .SelectMany(static (allMethods, _) =>
            {
                var seen = new HashSet<string>();
                var result = ImmutableArray.CreateBuilder<MethodInfo>();
                foreach (var m in allMethods)
                {
                    if (seen.Add(m.FullTypeName))
                        result.Add(m);
                }
                return result.ToImmutable();
            });

        var typesWithCompilation = distinctTypes.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(typesWithCompilation, static (spc, pair) =>
        {
            var (method, compilation) = pair;
            var contextType = compilation.GetTypeByMetadataName(method.FullTypeName);
            if (contextType != null)
            {
                try
                {
                    var accessorSource = GenerateAccessorTable(
                        contextType, method.NamespaceName, method.ClassName);
                    spc.AddSource(
                        $"{method.ClassName}.Accessors.g.cs",
                        SourceText.From(accessorSource, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
                        InternalError, method.Location, ex.Message));
                }
            }
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  メソッド情報抽出
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Syntax Provider のコンテキストから、コード生成に必要なメソッド情報を抽出する。
    /// <c>[BehaviorTree]</c> 属性のコンストラクタ引数からソース文字列を取得し、
    /// メソッドが属するクラスの名前空間・クラス名・メソッド名・完全修飾型名を収集する。
    /// </summary>
    /// <returns>抽出されたメソッド情報。属性引数が不正な場合は null。</returns>
    private static MethodInfo? ExtractMethodInfo(
        GeneratorAttributeSyntaxContext ctx,
        System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not IMethodSymbol methodSymbol)
            return null;

        // [BehaviorTree("...")] のコンストラクタ引数からソース文字列を取得
        var attr = ctx.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == BehaviorTreeAttributeName);

        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;

        var source = attr.ConstructorArguments[0].Value as string;
        if (string.IsNullOrEmpty(source))
            return null;

        // 包含クラスの情報を取得
        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return null;

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? "";
        var className = containingType.Name;
        var methodName = methodSymbol.Name;

        // セマンティクス解析用の完全修飾型名を構築
        var fullTypeName = string.IsNullOrEmpty(namespaceName)
            ? className
            : $"{namespaceName}.{className}";

        // メソッドの Location を診断報告用に取得
        var location = ctx.TargetNode.GetLocation();

        return new MethodInfo(
            source!,
            namespaceName,
            className,
            methodName,
            fullTypeName,
            location);
    }

    // ═══════════════════════════════════════════════════════════
    //  コード生成
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// メソッド情報をもとに QueryDatabase 経由で Crisp コンパイルパイプラインを実行し、
    /// C# ソースコードを生成する。
    ///
    /// <para>
    /// アーキテクチャ仕様 7.3「LSP + Source Generator の Query DB 共有」に基づき、
    /// LSP サーバーと同一の <see cref="QueryDatabase"/> 派生クエリロジックを使用する。
    /// これにより「LSP ではエラーが出るのにビルドでは出ない」という不整合が原理的に発生しない。
    /// </para>
    ///
    /// 処理フロー:
    /// <list type="number">
    ///   <item><description>ソース文字列の解決（インライン S式 or 外部 .crisp ファイル）</description></item>
    ///   <item><description>QueryDatabase に入力を設定（SourceText, ContextType, RoslynCompilation）</description></item>
    ///   <item><description>AllDiagnostics クエリで構文 + セマンティック診断を一括取得</description></item>
    ///   <item><description>EmitIR クエリで IR ツリーを取得</description></item>
    ///   <item><description>CSharpEmitter で IR → C# ソースコードを生成</description></item>
    /// </list>
    ///
    /// 診断にエラーが含まれる場合は、Roslyn 診断として報告して C# 生成をスキップする。
    /// </summary>
    private static void GenerateSource(
        SourceProductionContext spc,
        MethodInfo method,
        ImmutableArray<CrispFile> crispFiles,
        Compilation compilation)
    {
        // ソース文字列の解決（インライン or 外部ファイル）
        var crispSource = ResolveSource(spc, method, crispFiles);
        if (crispSource is null)
            return;

        try
        {
            // QueryDatabase を構築し、入力を設定する
            var db = new QueryDatabase();
            var fileId = FileId.ForInline(method.FullTypeName, method.MethodName);

            db.SetSourceText(fileId, crispSource);
            db.SetContextType(fileId, compilation.GetTypeByMetadataName(method.FullTypeName));
            db.SetRoslynCompilation(fileId, compilation);

            // 全診断（構文 + セマンティック）を一括取得する
            var diagnostics = db.AllDiagnostics(fileId);
            if (diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error))
            {
                foreach (var diag in diagnostics)
                {
                    spc.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
                        CompileError,
                        method.Location,
                        diag.Message));
                }
                return;
            }

            // IR ツリーを取得する
            var irTrees = db.EmitIR(fileId);
            if (irTrees.Length == 0)
                return;

            // C# Emit（IR → C# ソースコード）
            var csharpSource = CSharpEmitter.Emit(
                irTrees[0],
                method.NamespaceName,
                method.ClassName,
                method.MethodName);

            // ソース出力を登録
            var hintName = $"{method.ClassName}.{method.MethodName}.g.cs";
            spc.AddSource(hintName, SourceText.From(csharpSource, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            // コンパイラ内部エラー
            spc.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
                InternalError,
                method.Location,
                ex.Message));
        }
    }

    /// <summary>
    /// <c>[BehaviorTree]</c> 属性のソース引数を解決する。
    /// <c>.crisp</c> 拡張子の場合は AdditionalFiles からファイル内容を検索する。
    /// それ以外の場合はインライン S式としてそのまま返す。
    /// </summary>
    /// <returns>Crisp DSL ソース文字列。解決できない場合は null（診断済み）。</returns>
    private static string? ResolveSource(
        SourceProductionContext spc,
        MethodInfo method,
        ImmutableArray<CrispFile> crispFiles)
    {
        if (!method.Source.EndsWith(".crisp", StringComparison.OrdinalIgnoreCase))
        {
            // インライン S式
            return method.Source;
        }

        // 外部 .crisp ファイル — ファイル名マッチ（U7 決定事項）
        var fileName = method.Source;
        foreach (var file in crispFiles)
        {
            if (string.Equals(file.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                return file.Content;
        }

        // ファイルが見つからない
        spc.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            FileNotFound,
            method.Location,
            fileName));

        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  F6: AOT アクセサテーブル生成
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// コンテキスト型の全メンバーに対する AOT 安全なアクセサテーブルを生成する。
    ///
    /// 生成内容:
    /// <list type="bullet">
    ///   <item><description><c>_memberAccessors</c>: プロパティ・フィールドの値取得デリゲート</description></item>
    ///   <item><description><c>_methodInvokers</c>: メソッド呼び出しデリゲート</description></item>
    ///   <item><description><c>GetAccessorTable()</c>: <see cref="Crisp.Runtime.IAccessorTable{T}"/> を返す静的メソッド</description></item>
    /// </list>
    /// </summary>
    private static string GenerateAccessorTable(
        INamedTypeSymbol contextType,
        string namespaceName,
        string className)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Crisp.Runtime;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        sb.AppendLine($"    partial class {className}");
        sb.AppendLine("    {");

        // メンバーアクセサ辞書を生成
        sb.AppendLine($"        private static readonly Dictionary<string, Func<{className}, object?>> _crispMemberAccessors = new()");
        sb.AppendLine("        {");

        foreach (var member in contextType.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol prop when !prop.IsStatic && !prop.IsIndexer
                    && prop.DeclaredAccessibility != Accessibility.Private:
                    sb.AppendLine($"            [\"{prop.Name}\"] = ctx => ctx.{prop.Name},");
                    break;

                case IFieldSymbol field when !field.IsStatic && !field.IsImplicitlyDeclared
                    && field.DeclaredAccessibility != Accessibility.Private:
                    sb.AppendLine($"            [\"{field.Name}\"] = ctx => ctx.{field.Name},");
                    break;
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine();

        // メソッドインボーカ辞書を生成
        sb.AppendLine($"        private static readonly Dictionary<string, Func<{className}, object?[], object?>> _crispMethodInvokers = new()");
        sb.AppendLine("        {");

        foreach (var member in contextType.GetMembers())
        {
            if (member is IMethodSymbol method
                && !method.IsStatic
                && method.MethodKind == MethodKind.Ordinary
                && method.DeclaredAccessibility != Accessibility.Private
                && !method.IsImplicitlyDeclared)
            {
                // 引数リストのキャスト式を生成
                var paramCasts = new StringBuilder();
                for (var i = 0; i < method.Parameters.Length; i++)
                {
                    if (i > 0) paramCasts.Append(", ");
                    var paramType = method.Parameters[i].Type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat);
                    paramCasts.Append($"({paramType})args[{i}]");
                }

                if (method.ReturnsVoid)
                {
                    sb.AppendLine($"            [\"{method.Name}\"] = (ctx, args) => {{ ctx.{method.Name}({paramCasts}); return null; }},");
                }
                else
                {
                    sb.AppendLine($"            [\"{method.Name}\"] = (ctx, args) => ctx.{method.Name}({paramCasts}),");
                }
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine();

        // GetAccessorTable メソッドを生成
        sb.AppendLine($"        /// <summary>AOT 安全なアクセサテーブルを返す（F6 Source Generator 生成）。</summary>");
        sb.AppendLine($"        public static IAccessorTable<{className}> GetAccessorTable()");
        sb.AppendLine($"            => new AccessorTable<{className}>(_crispMemberAccessors, _crispMethodInvokers);");

        sb.AppendLine("    }");

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //  データ型（インクリメンタルキャッシュ用の equatable レコード）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// コード生成に必要なメソッド情報。
    /// インクリメンタル Source Generator のキャッシュに対応するため、
    /// equatable なレコード型として定義する。
    /// </summary>
    /// <param name="Source">DSL ソース文字列（インライン S式 or .crisp ファイル名）。</param>
    /// <param name="NamespaceName">メソッドを含むクラスの名前空間。</param>
    /// <param name="ClassName">メソッドを含むクラスの名前。</param>
    /// <param name="MethodName">BtNode を返す partial メソッドの名前。</param>
    /// <param name="FullTypeName">セマンティクス解析用の完全修飾型名（例: "TestApp.SimpleAI"）。</param>
    /// <param name="Location">診断報告用のソース位置。</param>
    private sealed record MethodInfo(
        string Source,
        string NamespaceName,
        string ClassName,
        string MethodName,
        string FullTypeName,
        Location Location);

    /// <summary>
    /// AdditionalFiles から読み込んだ .crisp ファイルの情報。
    /// </summary>
    /// <param name="FileName">ファイル名（パスを含まない）。</param>
    /// <param name="Content">ファイルの内容。</param>
    private sealed record CrispFile(
        string FileName,
        string Content);
}
