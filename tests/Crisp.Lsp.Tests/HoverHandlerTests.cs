using Crisp.Lsp.Handlers;
using Crisp.Query;
using Crisp.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Tests;

/// <summary>
/// HoverHandler の統合テスト。
///
/// 実際の <see cref="QueryDatabase"/> を使用して、ホバー情報の生成ロジックを検証する。
///
/// <para>
/// テスト範囲:
/// <list type="number">
///   <item><description>プロパティ上のホバーで型情報が表示される</description></item>
///   <item><description>メソッド上のホバーでシグネチャが表示される</description></item>
///   <item><description>キーワード上のホバーで説明が表示される</description></item>
///   <item><description>リテラル上のホバーで型と値が表示される</description></item>
///   <item><description>未登録ファイルの場合 null を返す</description></item>
/// </list>
/// </para>
///
/// t-wada 式 TDD: 最小テストから段階的に複雑さを増す。
/// </summary>
public class HoverHandlerTests
{
    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    private const string TestFilePath = "/tmp/hover-test.crisp";

    private static QueryDatabase SetupDatabase(string crispSource, string contextCSharp, string typeName)
    {
        var db = new QueryDatabase();
        var fileId = FileId.ForFile(TestFilePath);

        var syntaxTree = CSharpSyntaxTree.ParseText(contextCSharp);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(BtNode).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(BtStatus).Assembly.Location),
        };

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

        var contextType = compilation.GetTypeByMetadataName(typeName)!;

        db.SetSourceText(fileId, crispSource);
        db.SetContextType(fileId, contextType);
        db.SetRoslynCompilation(fileId, compilation);

        return db;
    }

    /// <summary>
    /// 指定した行・列位置のホバー結果を取得する。
    /// </summary>
    private static async Task<Hover?> GetHover(QueryDatabase db, int line, int character)
    {
        var handler = new HoverHandler(db);
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(new Uri($"file://{TestFilePath}")),
            Position = new Position(line, character),
        };
        return await handler.Handle(request, CancellationToken.None);
    }

    /// <summary>ホバー結果の Markdown テキストを取得する。</summary>
    private static string? GetHoverText(Hover? hover)
    {
        if (hover?.Contents.HasMarkupContent == true)
        {
            return hover.Contents.MarkupContent!.Value;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  1. キーワードホバー — 最小テスト
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task selectキーワード上のホバーで説明が表示される()
    {
        const string source = "(tree T (select (.Patrol)))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase(source, contextCSharp, "Test.AI");

        // "select" は offset 9-15 (0-indexed)、行0の9文字目から
        var hover = await GetHover(db, 0, 9);
        var text = GetHoverText(hover);

        Assert.NotNull(text);
        Assert.Contains("select", text!);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. メンバーアクセスホバー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task プロパティ上のホバーで型情報が表示される()
    {
        const string source = "(tree T (check .Health))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public int Health { get; set; }
                }
            }
            """;

        var db = SetupDatabase(source, contextCSharp, "Test.AI");

        // ".Health" は offset 15-22、行0の15文字目から
        var hover = await GetHover(db, 0, 15);
        var text = GetHoverText(hover);

        Assert.NotNull(text);
        Assert.Contains("Health", text!);
    }

    [Fact]
    public async Task メソッド上のホバーでシグネチャが表示される()
    {
        const string source = "(tree T (.Attack .Target))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public string Target { get; set; }
                    public BtStatus Attack(string target) => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase(source, contextCSharp, "Test.AI");

        // ".Attack" は offset 9-16、行0の9文字目から
        var hover = await GetHover(db, 0, 9);
        var text = GetHoverText(hover);

        Assert.NotNull(text);
        Assert.Contains("Attack", text!);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. リテラルホバー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task 整数リテラル上のホバーで型と値が表示される()
    {
        const string source = "(tree T (check (< .Health 30)))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public int Health { get; set; }
                }
            }
            """;

        var db = SetupDatabase(source, contextCSharp, "Test.AI");

        // "30" は offset 26-28
        var hover = await GetHover(db, 0, 26);
        var text = GetHoverText(hover);

        Assert.NotNull(text);
        Assert.Contains("int", text!);
        Assert.Contains("30", text!);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. エッジケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task 未登録ファイルでnullを返す()
    {
        var db = new QueryDatabase();
        // ファイルを登録しない

        var handler = new HoverHandler(db);
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(
                new Uri("file:///tmp/unknown.crisp")),
            Position = new Position(0, 0),
        };
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Null(result);
    }
}
