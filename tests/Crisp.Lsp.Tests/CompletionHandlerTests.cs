using Crisp.Lsp.Handlers;
using Crisp.Query;
using Crisp.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Tests;

/// <summary>
/// CompletionHandler の統合テスト。
///
/// 実際の <see cref="QueryDatabase"/> を使用して、補完候補の生成ロジックを検証する。
/// ハンドラを直接インスタンス化し、<see cref="CompletionParams"/> を構築して呼び出す。
///
/// <para>
/// テスト範囲:
/// <list type="number">
///   <item><description>基本的なメンバー補完（プロパティ・メソッド）</description></item>
///   <item><description>全アクセス修飾子のメンバーが補完候補に含まれること</description></item>
///   <item><description>ケバブケース候補の生成</description></item>
///   <item><description>BtStatus メソッドと非 BtStatus メソッドの分類</description></item>
///   <item><description>コンテキスト型未設定時の空リスト</description></item>
/// </list>
/// </para>
///
/// t-wada 式 TDD: 最小テストから段階的に複雑さを増す。
/// </summary>
public class CompletionHandlerTests
{
    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    /// <summary>テスト用のファイルパス。UriToFileId で使われる。</summary>
    private const string TestFilePath = "/tmp/test.crisp";

    /// <summary>
    /// テスト用の QueryDatabase を構築する。
    /// </summary>
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
    /// CompletionHandler を呼び出して補完リストを取得する。
    /// </summary>
    private static async Task<CompletionList> GetCompletions(QueryDatabase db)
    {
        var handler = new CompletionHandler(db);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(new Uri($"file://{TestFilePath}")),
            Position = new Position(0, 0),
        };
        return await handler.Handle(request, CancellationToken.None);
    }

    // ═══════════════════════════════════════════════════════════
    //  1. 基本テスト — 最小のプロパティ補完
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task プロパティが補完候補に含まれる()
    {
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

        var db = SetupDatabase("(tree T (check .Health))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        Assert.Contains(result.Items, item => item.Label == ".Health");
    }

    [Fact]
    public async Task メソッドが補完候補に含まれる()
    {
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

        var db = SetupDatabase("(tree T (.Patrol))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        Assert.Contains(result.Items, item => item.Label == ".Patrol");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. アクセス修飾子 — 全修飾子が補完される
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task privateメンバーが補完候補に含まれる()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    private int _health;
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase("(tree T (.Patrol))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        // private フィールドも partial class 内からアクセス可能なので補完候補に含まれる
        Assert.Contains(result.Items, item => item.Label == "._health");
    }

    [Fact]
    public async Task protectedメンバーが補完候補に含まれる()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    protected bool IsAlive { get; set; }
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase("(tree T (.Patrol))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        Assert.Contains(result.Items, item => item.Label == ".IsAlive");
    }

    // ═══════════════════════════════════════════════════════════
    //  3. ケバブケース候補
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PascalCaseメンバーにケバブケース候補が追加される()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsEnemyVisible { get; set; }
                }
            }
            """;

        var db = SetupDatabase("(tree T (check .IsEnemyVisible))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        // PascalCase と kebab-case の両方が存在
        Assert.Contains(result.Items, item => item.Label == ".IsEnemyVisible");
        Assert.Contains(result.Items, item => item.Label == ".is-enemy-visible");
    }

    [Fact]
    public async Task 単一単語にはケバブケース候補が追加されない()
    {
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

        var db = SetupDatabase("(tree T (check .Health))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        // "health" は "Health" と同じ（case-insensitive）なのでケバブケース候補なし
        var healthItems = result.Items.Where(i => i.Label!.Contains("health", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(healthItems);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. メソッド分類（BtStatus vs 非 BtStatus）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task BtStatus戻りのメソッドはMethodとして分類される()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Attack() => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase("(tree T (.Attack))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        var attackItem = result.Items.First(i => i.Label == ".Attack");
        Assert.Equal(CompletionItemKind.Method, attackItem.Kind);
    }

    [Fact]
    public async Task 非BtStatus戻りのメソッドはFunctionとして分類される()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public float GetDistance() => 0f;
                }
            }
            """;

        var db = SetupDatabase("(tree T (check .GetDistance))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        var getDistItem = result.Items.First(i => i.Label == ".GetDistance");
        Assert.Equal(CompletionItemKind.Function, getDistItem.Kind);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. エッジケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task コンテキスト型未設定で空リストを返す()
    {
        var db = new QueryDatabase();
        var fileId = FileId.ForFile(TestFilePath);
        db.SetSourceText(fileId, "(tree T (.Patrol))");
        // ContextType を設定しない

        var handler = new CompletionHandler(db);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(new Uri($"file://{TestFilePath}")),
            Position = new Position(0, 0),
        };
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task 引数つきメソッドに省略記号が付く()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Attack(string target) => BtStatus.Success;
                }
            }
            """;

        var db = SetupDatabase("(tree T (.Attack .Target))", contextCSharp, "Test.AI");
        var result = await GetCompletions(db);

        var attackItem = result.Items.First(i => i.Label == ".Attack(...)");
        Assert.NotNull(attackItem);
        Assert.Equal(CompletionItemKind.Method, attackItem.Kind);
    }
}
