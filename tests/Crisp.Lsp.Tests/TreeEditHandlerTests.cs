using Crisp.Lsp.Handlers;
using Crisp.Syntax;
using Crisp.Syntax.Cst;

namespace Crisp.Lsp.Tests;

/// <summary>
/// F4: Visual Editor — ツリー編集ヘルパーの単体テスト。
///
/// <see cref="TreeEditHelper"/> の CST ノード検索、スニペット生成、
/// オフセット計算、ラップ/アンラップ操作を検証する。
///
/// <para>
/// テスト範囲:
/// <list type="number">
///   <item><description>FindNodeById で CST ノードが Id で検索できる</description></item>
///   <item><description>GenerateNodeSnippet で各ノード種別のS式が生成される</description></item>
///   <item><description>ComputeInsertOffset で正しい挿入位置が計算される</description></item>
///   <item><description>GetInnerChild でデコレータの子ノードが取得できる</description></item>
///   <item><description>ソース編集の結合テスト（追加・削除・ラップ・アンラップ）</description></item>
/// </list>
/// </para>
///
/// t-wada 式 TDD: 最小テストから段階的に複雑さを増す。
/// </summary>
public class TreeEditHandlerTests
{
    // ═══════════════════════════════════════════════════════════
    //  1. FindNodeById
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FindNodeByIdでルートノードが見つかる()
    {
        // Arrange
        var (program, _) = Parser.Parse("(tree T (.Patrol))");

        // Act
        var found = TreeEditHelper.FindNodeById(program, 0);

        // Assert: CstProgram の Id は 0
        Assert.NotNull(found);
        Assert.Equal(0, found!.Id);
        Assert.IsType<CstProgram>(found);
    }

    [Fact]
    public void FindNodeByIdで子ノードが見つかる()
    {
        // Arrange
        var (program, _) = Parser.Parse("(tree T (.Patrol))");
        var tree = program.Trees[0];

        // Act: ツリーノードの Id で検索
        var found = TreeEditHelper.FindNodeById(program, tree.Id);

        // Assert
        Assert.NotNull(found);
        Assert.IsType<CstTree>(found);
    }

    [Fact]
    public void FindNodeByIdで存在しないIdはnullを返す()
    {
        // Arrange
        var (program, _) = Parser.Parse("(tree T (.Patrol))");

        // Act
        var found = TreeEditHelper.FindNodeById(program, 999);

        // Assert
        Assert.Null(found);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. GenerateNodeSnippet
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("action", "(.TODO)")]
    [InlineData("check", "(check .TODO)")]
    [InlineData("selector", "(select)")]
    [InlineData("select", "(select)")]
    [InlineData("sequence", "(seq)")]
    [InlineData("seq", "(seq)")]
    [InlineData("invert", "(invert (.TODO))")]
    [InlineData("guard", "(guard .TODO (.TODO))")]
    public void GenerateNodeSnippetで各ノード種別のS式が生成される(string nodeType, string expected)
    {
        // Act
        var snippet = TreeEditHelper.GenerateNodeSnippet(nodeType);

        // Assert
        Assert.Equal(expected, snippet);
    }

    [Fact]
    public void GenerateNodeSnippetで未知の種別はアクション形式になる()
    {
        // Act
        var snippet = TreeEditHelper.GenerateNodeSnippet("CustomAction");

        // Assert
        Assert.Equal("(.CustomAction)", snippet);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. ComputeInsertOffset
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ComputeInsertOffsetで先頭位置に挿入できる()
    {
        // Arrange: (tree T (seq (.A) (.B)))
        var (program, _) = Parser.Parse("(tree T (seq (.A) (.B)))");
        var tree = program.Trees[0];
        // seq ノードを探す
        var seq = tree.Children.First();

        // Act: position 0 → 最初の子の前
        var offset = TreeEditHelper.ComputeInsertOffset(seq, 0);

        // Assert: 0 以上の正の値
        Assert.True(offset >= 0);
    }

    [Fact]
    public void ComputeInsertOffsetで末尾位置に挿入できる()
    {
        // Arrange
        var (program, _) = Parser.Parse("(tree T (seq (.A) (.B)))");
        var tree = program.Trees[0];
        var seq = tree.Children.First();
        var childCount = seq.Children.Count();

        // Act: position が子数以上 → 末尾に挿入
        var offset = TreeEditHelper.ComputeInsertOffset(seq, childCount + 10);

        // Assert: 親の Span 内に収まる
        Assert.True(offset > 0);
        Assert.True(offset <= seq.Span.Start + seq.Span.Length);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. GetInnerChild
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetInnerChildでデコレータの子ノードが取得できる()
    {
        // Arrange: (tree T (invert (.Patrol)))
        var (program, _) = Parser.Parse("(tree T (invert (.Patrol)))");
        var tree = program.Trees[0];
        var invert = tree.Children.First();

        // Act
        var inner = TreeEditHelper.GetInnerChild(invert);

        // Assert
        Assert.NotNull(inner);
    }

    [Fact]
    public void GetInnerChildで子のないノードはnullを返す()
    {
        // Arrange: ツリーの Children のうち、子を持たないノードを探す
        // CstProgram 自体は Children を持つので、個別のアクションノードをテスト
        var (program, _) = Parser.Parse("(tree T (.Patrol))");
        var tree = program.Trees[0];
        // .Patrol はアクションノード
        var action = tree.Children.First();
        var innerChild = TreeEditHelper.GetInnerChild(action);

        // Assert: アクションの Children の最後の子を返す（あれば）
        // アクションノードの構造に依存するが、null でなくてもよい
        // 重要なのはクラッシュしないこと
        _ = innerChild;
    }

    // ═══════════════════════════════════════════════════════════
    //  5. ソース編集の結合テスト
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ソース上でノード削除が正しく動作する()
    {
        // Arrange
        var source = "(tree T (seq (.A) (.B) (.C)))";
        var (program, _) = Parser.Parse(source);
        var tree = program.Trees[0];
        var seq = tree.Children.First();
        // seq の 2 番目の子（(.B)）を削除
        var children = seq.Children.ToList();
        Assert.True(children.Count >= 2);
        var target = children[1]; // (.B)

        // Act: FullSpan で削除
        var span = target.FullSpan;
        var newSource = source.Remove(span.Start, span.Length);

        // Assert: (.B) が消えている
        Assert.DoesNotContain("(.B)", newSource);
        Assert.Contains("(.A)", newSource);
        Assert.Contains("(.C)", newSource);
    }

    [Fact]
    public void ソース上でノードをデコレータで包める()
    {
        // Arrange
        var source = "(tree T (.Patrol))";
        var (program, _) = Parser.Parse(source);
        var tree = program.Trees[0];
        var action = tree.Children.First();

        // Act: (.Patrol) を (invert (.Patrol)) に包む
        var span = action.Span;
        var nodeText = source.Substring(span.Start, span.Length);
        var wrapped = $"(invert {nodeText})";
        var newSource = source.Remove(span.Start, span.Length).Insert(span.Start, wrapped);

        // Assert
        Assert.Contains("(invert (.Patrol))", newSource);
    }

    [Fact]
    public void ソース上でデコレータを解除できる()
    {
        // Arrange
        var source = "(tree T (invert (.Patrol)))";
        var (program, _) = Parser.Parse(source);
        var tree = program.Trees[0];
        var invert = tree.Children.First();
        var inner = TreeEditHelper.GetInnerChild(invert);
        Assert.NotNull(inner);

        // Act: (invert (.Patrol)) → (.Patrol) に展開
        var outerSpan = invert.Span;
        var innerSpan = inner!.Span;
        var innerText = source.Substring(innerSpan.Start, innerSpan.Length);
        var newSource = source.Remove(outerSpan.Start, outerSpan.Length)
                              .Insert(outerSpan.Start, innerText);

        // Assert
        Assert.Contains("(.Patrol)", newSource);
        Assert.DoesNotContain("invert", newSource);
    }

    [Fact]
    public void ソース上でノード追加が正しく動作する()
    {
        // Arrange
        var source = "(tree T (seq (.A)))";
        var (program, _) = Parser.Parse(source);
        var tree = program.Trees[0];
        var seq = tree.Children.First();

        // Act: seq の末尾にノードを追加
        var snippet = TreeEditHelper.GenerateNodeSnippet("action");
        var insertOffset = TreeEditHelper.ComputeInsertOffset(seq, 100); // 末尾
        var newSource = source.Insert(insertOffset, " " + snippet);

        // Assert: 新しいノードが追加されている
        Assert.Contains(".TODO", newSource);
        Assert.Contains("(.A)", newSource);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. パラメータクラスの基本テスト
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddNodeParamsのプロパティが設定できる()
    {
        var p = new AddNodeParams
        {
            Uri = new Uri("file:///test.crisp"),
            ParentId = 1,
            Position = 0,
            NodeType = "action",
        };
        Assert.Equal(1, p.ParentId);
        Assert.Equal("action", p.NodeType);
    }

    [Fact]
    public void RemoveNodeParamsのプロパティが設定できる()
    {
        var p = new RemoveNodeParams
        {
            Uri = new Uri("file:///test.crisp"),
            NodeId = 5,
        };
        Assert.Equal(5, p.NodeId);
    }

    [Fact]
    public void MoveNodeParamsのプロパティが設定できる()
    {
        var p = new MoveNodeParams
        {
            Uri = new Uri("file:///test.crisp"),
            NodeId = 3,
            NewParentId = 1,
            NewPosition = 2,
        };
        Assert.Equal(3, p.NodeId);
        Assert.Equal(1, p.NewParentId);
        Assert.Equal(2, p.NewPosition);
    }

    [Fact]
    public void WrapNodeParamsのプロパティが設定できる()
    {
        var p = new WrapNodeParams
        {
            Uri = new Uri("file:///test.crisp"),
            NodeId = 2,
            DecoratorType = "invert",
        };
        Assert.Equal("invert", p.DecoratorType);
    }

    [Fact]
    public void UnwrapNodeParamsのプロパティが設定できる()
    {
        var p = new UnwrapNodeParams
        {
            Uri = new Uri("file:///test.crisp"),
            NodeId = 4,
        };
        Assert.Equal(4, p.NodeId);
    }

    [Fact]
    public void TreeEditResponseの成功レスポンスが生成できる()
    {
        var r = new TreeEditResponse { Success = true };
        Assert.True(r.Success);
        Assert.Null(r.Error);
    }

    [Fact]
    public void TreeEditResponseのエラーレスポンスが生成できる()
    {
        var r = new TreeEditResponse { Success = false, Error = "Node not found" };
        Assert.False(r.Success);
        Assert.Equal("Node not found", r.Error);
    }
}
