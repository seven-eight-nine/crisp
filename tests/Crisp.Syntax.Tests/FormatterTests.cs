using Crisp.Syntax.Formatting;

namespace Crisp.Syntax.Tests;

/// <summary>
/// Formatter のユニットテスト。
///
/// t-wada 式 TDD に基づき、最小の入力から段階的に複雑さを増す。
/// 各テストは Arrange-Act-Assert パターンに従う。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>短い式は1行に収まること（フラットフォーマット）</description></item>
///   <item><description>長い式は適切に改行・インデントされること（複数行フォーマット）</description></item>
///   <item><description>ネストした構造が正しくインデントされること</description></item>
///   <item><description>コメントが保持されること</description></item>
///   <item><description>冪等性: フォーマット済みコードを再フォーマットしても結果が変わらないこと</description></item>
/// </list>
/// </summary>
public class FormatterTests
{
    // ══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストを既定オプションでフォーマットする。
    /// 末尾の改行・空白を除去して返す。
    /// </summary>
    private static string Format(string source, FormattingOptions? options = null)
    {
        return Formatter.FormatSource(source, options).TrimEnd();
    }

    /// <summary>
    /// フォーマット結果の冪等性を検証する。
    /// フォーマット済みテキストを再度フォーマットしても結果が変わらないことを確認する。
    /// </summary>
    private static void AssertIdempotent(string source, FormattingOptions? options = null)
    {
        var first = Formatter.FormatSource(source, options);
        var second = Formatter.FormatSource(first, options);
        Assert.Equal(first, second);
    }

    // ══════════════════════════════════════════════════════════
    //  1. 短い式は1行に収まる（フラットフォーマット）
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void 短いtree定義は1行にフォーマットされる()
    {
        // Arrange: 行幅に十分収まる短い tree 定義
        var source = "(tree T .Patrol)";

        // Act: フォーマットを実行する
        var result = Format(source);

        // Assert: 1行のフラットフォーマットになること
        Assert.Equal("(tree T .Patrol)", result);
    }

    [Fact]
    public void 短いselectは1行にフォーマットされる()
    {
        // Arrange: 短い select ノードを含む tree
        var source = "(tree T (select .A .B))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (select .A .B))", result);
    }

    [Fact]
    public void 短いseqは1行にフォーマットされる()
    {
        // Arrange
        var source = "(tree T (seq .A .B))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (seq .A .B))", result);
    }

    [Fact]
    public void 短いcheckは1行にフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check .IsAlive))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check .IsAlive))", result);
    }

    [Fact]
    public void 短い二項式は1行にフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check (< .Health 30)))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check (< .Health 30)))", result);
    }

    [Fact]
    public void 短い呼び出しは1行にフォーマットされる()
    {
        // Arrange
        var source = "(tree T (.Attack .Target))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (.Attack .Target))", result);
    }

    [Fact]
    public void 引数なし呼び出しは1行にフォーマットされる()
    {
        // Arrange
        var source = "(tree T (.Patrol))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (.Patrol))", result);
    }

    // ══════════════════════════════════════════════════════════
    //  2. 長い式は複数行にフォーマットされる
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void 行幅を超えるselectは複数行にフォーマットされる()
    {
        // Arrange: 行幅を超える長い select
        var source = "(tree T (select .VeryLongActionNameAlpha .VeryLongActionNameBeta .VeryLongActionNameGamma))";

        // Act
        var result = Format(source);

        // Assert: キーワードが先頭行、子要素がインデントされた複数行になること
        var expected = string.Join(Environment.NewLine, [
            "(tree T",
            "  (select",
            "    .VeryLongActionNameAlpha",
            "    .VeryLongActionNameBeta",
            "    .VeryLongActionNameGamma))",
        ]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void 行幅を超えるseqは複数行にフォーマットされる()
    {
        // Arrange: 行幅を20に設定し、seq の子要素も含めて複数行に展開させる
        // "(seq .ActionA .ActionB)" は22文字、インデント2で計24文字 > 20
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 20);
        var source = "(tree T (seq .ActionA .ActionB))";

        // Act
        var result = Format(source, options);

        // Assert
        var expected = string.Join(Environment.NewLine, [
            "(tree T",
            "  (seq",
            "    .ActionA",
            "    .ActionB))",
        ]);
        Assert.Equal(expected, result);
    }

    // ══════════════════════════════════════════════════════════
    //  3. ネストした構造の正しいインデント
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void ネストしたselectとseqが正しくインデントされる()
    {
        // Arrange: ネストした構造（行幅30で強制的に複数行にする）
        // "(select (seq .A .B) (seq .C .D))" は32文字、インデント2で計34文字 > 30
        // "(seq .A .B)" は10文字、インデント4で計14文字 <= 30（フラットで収まる）
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 30);
        var source = "(tree T (select (seq .A .B) (seq .C .D)))";

        // Act
        var result = Format(source, options);

        // Assert: select が複数行になり、各 seq は1行に収まること
        var expected = string.Join(Environment.NewLine, [
            "(tree T",
            "  (select",
            "    (seq .A .B)",
            "    (seq .C .D)))",
        ]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void 深くネストした構造が正しくインデントされる()
    {
        // Arrange: 3段のネスト（行幅30で強制的に複数行にする）
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 30);
        var source = "(tree T (select (seq (check .IsAlive) .Flee) .Patrol))";

        // Act
        var result = Format(source, options);

        // Assert: 各レベルが適切にインデントされること
        var expected = string.Join(Environment.NewLine, [
            "(tree T",
            "  (select",
            "    (seq",
            "      (check .IsAlive)",
            "      .Flee)",
            "    .Patrol))",
        ]);
        Assert.Equal(expected, result);
    }

    // ══════════════════════════════════════════════════════════
    //  4. コメントの保持
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void プログラム先頭のコメントが保持される()
    {
        // Arrange: 先頭にコメントがある場合
        var source = ";; メインAI\n(tree T .Patrol)";

        // Act
        var result = Format(source);

        // Assert: コメントが保持されること
        Assert.Contains(";; メインAI", result);
        Assert.Contains("(tree T .Patrol)", result);
    }

    // ══════════════════════════════════════════════════════════
    //  5. 冪等性: フォーマット済みコードの再フォーマット
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void 短い式のフォーマットは冪等である()
    {
        // Arrange & Act & Assert
        AssertIdempotent("(tree T .Patrol)");
    }

    [Fact]
    public void 複数行フォーマットは冪等である()
    {
        // Arrange: 既にフォーマット済みの複数行コード
        var source = string.Join("\n", [
            "(tree T",
            "  (select",
            "    (seq",
            "      (check (< .Health 30))",
            "      (.Flee))",
            "    (.Patrol)))",
        ]);

        // Act & Assert: 再フォーマットしても変わらないこと
        AssertIdempotent(source);
    }

    [Fact]
    public void 不正なインデントのコードをフォーマットすると正規化される()
    {
        // Arrange: インデントが不統一なコード
        var source = "(tree T (select    .A     .B))";

        // Act
        var result = Format(source);

        // Assert: 空白が正規化されること
        Assert.Equal("(tree T (select .A .B))", result);
    }

    [Fact]
    public void 改行を含む入力でもフォーマットは冪等になる()
    {
        // Arrange: 改行を含む入力
        var source = "(tree T\n  (select\n    .A\n    .B))";
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 30);

        // Act & Assert: フォーマットの冪等性を検証する
        AssertIdempotent(source, options);
    }

    // ══════════════════════════════════════════════════════════
    //  6. Pattern 1: Minimal のフォーマット
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalが正しくフォーマットされる()
    {
        // Arrange: Pattern 1 の入力（空白が不統一な状態）
        // 行幅35を使用して、アーキテクチャ仕様の正規形に一致させる
        // 各レベルの行幅判定:
        //   tree: "(tree SimpleCombat ...)" = 75文字 > 35 → 複数行
        //   select: "(select (seq ...) (.Patrol))" = 55文字, indent 2 → 57 > 35 → 複数行
        //   seq: "(seq (check ...) (.Flee))" = 36文字, indent 4 → 40 > 35 → 複数行
        //   check: "(check (< .Health 30))" = 22文字, indent 6 → 28 <= 35 → フラット
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 35);
        var source = "(tree SimpleCombat (select (seq (check (< .Health 30)) (.Flee)) (.Patrol)))";

        // Act
        var result = Format(source, options);

        // Assert: 正しくフォーマットされた出力になること
        var expected = string.Join(Environment.NewLine, [
            "(tree SimpleCombat",
            "  (select",
            "    (seq",
            "      (check (< .Health 30))",
            "      (.Flee))",
            "    (.Patrol)))",
        ]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Pattern1_Minimalのフォーマットは冪等である()
    {
        // Arrange: Pattern 1 の正規形（行幅35で生成される形式）
        var options = new FormattingOptions(IndentSize: 2, MaxLineWidth: 35);
        var source = string.Join("\n", [
            "(tree SimpleCombat",
            "  (select",
            "    (seq",
            "      (check (< .Health 30))",
            "      (.Flee))",
            "    (.Patrol)))",
        ]);

        // Act & Assert: 冪等性を検証する
        AssertIdempotent(source, options);
    }

    // ══════════════════════════════════════════════════════════
    //  7. オプションによる挙動変更
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void インデント幅4でフォーマットされる()
    {
        // Arrange: インデント幅を4、行幅を15に設定する
        // "(tree T (select .A .B))" は23文字 > 15 なので tree は複数行になる
        // "(select .A .B)" は14文字、インデント4で計18文字 > 15 なので select も複数行になる
        var options = new FormattingOptions(IndentSize: 4, MaxLineWidth: 15);
        var source = "(tree T (select .A .B))";

        // Act
        var result = Format(source, options);

        // Assert: インデントが4スペースになること
        var expected = string.Join(Environment.NewLine, [
            "(tree T",
            "    (select",
            "        .A",
            "        .B))",
        ]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void tree間に空行が挿入される()
    {
        // Arrange: 複数の tree 定義
        var source = "(tree A .X)\n(tree B .Y)";

        // Act
        var result = Format(source, new FormattingOptions(InsertBlankLineBetweenTrees: true));

        // Assert: tree 間に空行があること
        Assert.Contains("(tree A .X)", result);
        Assert.Contains("(tree B .Y)", result);
        // 2つの tree 間に空行があることを確認
        var lines = result.Split('\n');
        var treeAIndex = Array.FindIndex(lines, l => l.Contains("(tree A .X)"));
        var treeBIndex = Array.FindIndex(lines, l => l.Contains("(tree B .Y)"));
        Assert.True(treeBIndex > treeAIndex + 1, "tree間に空行が挿入されていること");
    }

    [Fact]
    public void tree間の空行を無効化できる()
    {
        // Arrange: 空行挿入を無効化
        var source = "(tree A .X)\n(tree B .Y)";

        // Act
        var result = Format(source, new FormattingOptions(InsertBlankLineBetweenTrees: false));

        // Assert: tree 間に空行がないこと
        var lines = result.Split('\n');
        var treeAIndex = Array.FindIndex(lines, l => l.Contains("(tree A .X)"));
        var treeBIndex = Array.FindIndex(lines, l => l.Contains("(tree B .Y)"));
        Assert.Equal(treeAIndex + 1, treeBIndex);
    }

    // ══════════════════════════════════════════════════════════
    //  8. 各種ノードタイプのフォーマット
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void guardノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (guard .IsAlive .Patrol))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (guard .IsAlive .Patrol))", result);
    }

    [Fact]
    public void ifノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (if .IsAlive .A .B))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (if .IsAlive .A .B))", result);
    }

    [Fact]
    public void invertノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (invert .A))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (invert .A))", result);
    }

    [Fact]
    public void repeatノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (repeat 3 .A))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (repeat 3 .A))", result);
    }

    [Fact]
    public void timeoutノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (timeout 5.0 .A))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (timeout 5.0 .A))", result);
    }

    [Fact]
    public void parallelノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (parallel :any .A .B))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (parallel :any .A .B))", result);
    }

    [Fact]
    public void parallel_nノードがパラメータ付きで正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (parallel :n 2 .A .B .C))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (parallel :n 2 .A .B .C))", result);
    }

    [Fact]
    public void whileノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (while .IsAlive .Patrol))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (while .IsAlive .Patrol))", result);
    }

    [Fact]
    public void cooldownノードが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (cooldown 2.5 .A))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (cooldown 2.5 .A))", result);
    }

    [Fact]
    public void 二項式が正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check (= .State ::AIState.Combat)))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check (= .State ::AIState.Combat)))", result);
    }

    [Fact]
    public void 論理式andが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check (and .A .B)))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check (and .A .B)))", result);
    }

    [Fact]
    public void not式が正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check (not .A)))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check (not .A)))", result);
    }

    [Fact]
    public void 式コンテキストの呼び出しが正しくフォーマットされる()
    {
        // Arrange
        var source = "(tree T (check (< (.DistanceTo .Target) 5.0)))";

        // Act
        var result = Format(source);

        // Assert
        Assert.Equal("(tree T (check (< (.DistanceTo .Target) 5.0)))", result);
    }
}
