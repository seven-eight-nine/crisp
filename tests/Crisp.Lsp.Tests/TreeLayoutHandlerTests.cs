using Crisp.Lsp.Handlers;
using Crisp.Query;
using Crisp.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Crisp.Lsp.Tests;

/// <summary>
/// F4: Visual Editor — <see cref="TreeLayoutHandler"/> の統合テスト。
///
/// 実際の <see cref="QueryDatabase"/> を使用して、ツリーレイアウト生成ロジックを検証する。
///
/// <para>
/// テスト範囲:
/// <list type="number">
///   <item><description>基本的なツリーからレイアウト JSON が生成される</description></item>
///   <item><description>ノード Id とソース位置が正しく伝播される</description></item>
///   <item><description>ノード種別が正しくマッピングされる</description></item>
///   <item><description>ラベルが式やアクション名を正しく表示する</description></item>
///   <item><description>複雑なネスト構造が再帰的に変換される</description></item>
///   <item><description>未登録ファイルでは空のレスポンスが返る</description></item>
///   <item><description>構文エラーでは空のレスポンスが返る</description></item>
/// </list>
/// </para>
///
/// t-wada 式 TDD: 最小テストから段階的に複雑さを増す。
/// </summary>
public class TreeLayoutHandlerTests
{
    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    private const string TestFilePath = "/tmp/tree-layout-test.crisp";

    /// <summary>テスト用の QueryDatabase を構築する。</summary>
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

    /// <summary>ハンドラーを実行してレスポンスを取得する。</summary>
    private static async Task<TreeLayoutResponse> GetTreeLayout(QueryDatabase db)
    {
        var handler = new TreeLayoutHandler(db);
        var request = new TreeLayoutParams
        {
            Uri = new Uri($"file://{TestFilePath}"),
        };
        return await handler.Handle(request, CancellationToken.None);
    }

    /// <summary>テスト用のコンテキスト C# コード。</summary>
    private const string ContextCs = @"
namespace TestApp
{
    public class SimpleAI
    {
        public bool IsAlive { get; set; } = true;
        public bool IsReady { get; set; } = true;
        public float Health { get; set; } = 100f;

        public Crisp.Runtime.BtStatus Patrol() => Crisp.Runtime.BtStatus.Success;
        public Crisp.Runtime.BtStatus Attack() => Crisp.Runtime.BtStatus.Success;
        public Crisp.Runtime.BtStatus Flee() => Crisp.Runtime.BtStatus.Running;
    }
}";

    // ═══════════════════════════════════════════════════════════
    //  1. 基本: 最小のツリー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task 最小のツリーからレイアウトが生成される()
    {
        var db = SetupDatabase("(tree T (.Patrol))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);

        Assert.Single(response.Trees);
        Assert.Equal("T", response.Trees[0].Tree);
        Assert.NotNull(response.Trees[0].Root);
        Assert.Equal("tree", response.Trees[0].Root!.Type);
    }

    [Fact]
    public async Task アクションノードが正しく変換される()
    {
        var db = SetupDatabase("(tree T (.Patrol))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var root = response.Trees[0].Root!;

        Assert.Single(root.Children);
        var action = root.Children[0];
        Assert.Equal("action", action.Type);
        Assert.Equal("Patrol()", action.Label);
    }

    [Fact]
    public async Task ノードIdが正の値で設定される()
    {
        var db = SetupDatabase("(tree T (.Patrol))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var root = response.Trees[0].Root!;

        // Id は CstNode から伝播された値（0以上）
        Assert.True(root.Id >= 0, $"Root Id should be >= 0, got {root.Id}");
        Assert.True(root.Children[0].Id >= 0, $"Action Id should be >= 0, got {root.Children[0].Id}");
    }

    [Fact]
    public async Task Originにソース位置が設定される()
    {
        var db = SetupDatabase("(tree T (.Patrol))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var root = response.Trees[0].Root!;

        // Origin の length が 0 より大きい
        Assert.True(root.Origin!.Length > 0, "Root origin length should be > 0");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 複合ノード
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task セレクターノードが正しくマッピングされる()
    {
        var db = SetupDatabase("(tree T (select (.Patrol) (.Attack)))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var root = response.Trees[0].Root!;
        var selector = root.Children[0];

        Assert.Equal("selector", selector.Type);
        Assert.Equal(2, selector.Children.Count);
    }

    [Fact]
    public async Task シーケンスノードが正しくマッピングされる()
    {
        var db = SetupDatabase("(tree T (seq (.Patrol) (.Attack)))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var selector = response.Trees[0].Root!.Children[0];

        Assert.Equal("sequence", selector.Type);
        Assert.Equal(2, selector.Children.Count);
    }

    [Fact]
    public async Task ガードノードが正しくマッピングされる()
    {
        var db = SetupDatabase("(tree T (guard .IsAlive (.Patrol)))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var guard = response.Trees[0].Root!.Children[0];

        Assert.Equal("guard", guard.Type);
        Assert.Equal(".IsAlive", guard.Label);
        Assert.Single(guard.Children);
    }

    [Fact]
    public async Task 条件ノードのラベルに式が表示される()
    {
        var db = SetupDatabase("(tree T (check (< .Health 30.0)))", ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var check = response.Trees[0].Root!.Children[0];

        Assert.Equal("check", check.Type);
        Assert.Contains(".Health", check.Label);
        Assert.Contains("<", check.Label);
        Assert.Contains("30", check.Label);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. ネスト構造
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ネストしたツリーが再帰的に変換される()
    {
        var source = @"(tree Combat
            (select
                (seq (check (< .Health 30.0)) (.Flee))
                (.Patrol)))";
        var db = SetupDatabase(source, ContextCs, "TestApp.SimpleAI");

        var response = await GetTreeLayout(db);
        var root = response.Trees[0].Root!;

        // tree > selector > [sequence, action]
        var selector = root.Children[0];
        Assert.Equal("selector", selector.Type);
        Assert.Equal(2, selector.Children.Count);

        var sequence = selector.Children[0];
        Assert.Equal("sequence", sequence.Type);
        Assert.Equal(2, sequence.Children.Count);

        Assert.Equal("check", sequence.Children[0].Type);
        Assert.Equal("action", sequence.Children[1].Type);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. エラーケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task 未登録ファイルでは空のレスポンスが返る()
    {
        var db = new QueryDatabase();
        var handler = new TreeLayoutHandler(db);
        var request = new TreeLayoutParams
        {
            Uri = new Uri("file:///nonexistent.crisp"),
        };

        var response = await handler.Handle(request, CancellationToken.None);

        Assert.Empty(response.Trees);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. JSON 変換
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToJsonでTreeLayoutが正しく変換される()
    {
        var layout = new Crisp.Syntax.IR.TreeLayout(
            "Test",
            new Crisp.Syntax.IR.TreeLayoutNode(
                0, "tree", "Test",
                new Crisp.Syntax.IR.TreeLayoutOrigin(0, 10),
                "BtStatus",
                System.Collections.Immutable.ImmutableArray.Create(
                    new Crisp.Syntax.IR.TreeLayoutNode(
                        1, "action", "Patrol()",
                        new Crisp.Syntax.IR.TreeLayoutOrigin(5, 5),
                        "BtStatus",
                        System.Collections.Immutable.ImmutableArray<Crisp.Syntax.IR.TreeLayoutNode>.Empty))),
            System.Collections.Immutable.ImmutableArray<Crisp.Syntax.IR.TreeLayoutDiagnostic>.Empty);

        var json = TreeLayoutHandler.ConvertToJson(layout);

        Assert.Equal("Test", json.Tree);
        Assert.NotNull(json.Root);
        Assert.Equal(0, json.Root!.Id);
        Assert.Equal("tree", json.Root.Type);
        Assert.Single(json.Root.Children);
        Assert.Equal(1, json.Root.Children[0].Id);
        Assert.Equal("action", json.Root.Children[0].Type);
        Assert.Equal("Patrol()", json.Root.Children[0].Label);
    }

    [Fact]
    public void ConvertToJsonで診断情報が変換される()
    {
        var layout = new Crisp.Syntax.IR.TreeLayout(
            "T",
            new Crisp.Syntax.IR.TreeLayoutNode(
                0, "tree", "T",
                new Crisp.Syntax.IR.TreeLayoutOrigin(0, 5),
                null,
                System.Collections.Immutable.ImmutableArray<Crisp.Syntax.IR.TreeLayoutNode>.Empty),
            System.Collections.Immutable.ImmutableArray.Create(
                new Crisp.Syntax.IR.TreeLayoutDiagnostic(1, "error", "エラーメッセージ")));

        var json = TreeLayoutHandler.ConvertToJson(layout);

        Assert.Single(json.Diagnostics);
        Assert.Equal(1, json.Diagnostics[0].NodeId);
        Assert.Equal("error", json.Diagnostics[0].Severity);
        Assert.Equal("エラーメッセージ", json.Diagnostics[0].Message);
    }
}
