using System.Collections.Immutable;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F4: Visual Editor — <see cref="TreeLayoutBuilder"/> のユニットテスト。
/// t-wada 式 TDD に基づき、最小のツリーから段階的に複雑なパターンをテストする。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>TreeLayoutBuilder.Build: IrTree → TreeLayout 変換</description></item>
///   <item><description>ノード種別の正しいマッピング</description></item>
///   <item><description>Id / Origin の伝播</description></item>
///   <item><description>ラベル生成（式フォーマット、アクション名）</description></item>
///   <item><description>子ノードの再帰変換</description></item>
///   <item><description>診断情報の付加</description></item>
/// </list>
/// </summary>
public class TreeLayoutBuilderTests
{
    // ──────────────────────────────────────────────
    //  テストヘルパー
    // ──────────────────────────────────────────────

    /// <summary>テスト用の TextSpan を生成する。</summary>
    private static TextSpan Span(int start, int length) => new(start, length);

    /// <summary>テスト用のアクション IrNode を生成する。</summary>
    private static IrAction Action(string name, int id = 0, int start = 0, int length = 10) =>
        new(new MethodRef("TestAI", name, ImmutableArray<string>.Empty), ImmutableArray<IrExpr>.Empty)
        {
            TypeRef = TypeRef.BtStatus, Id = id, Origin = Span(start, length)
        };

    /// <summary>テスト用のメンバーロード式を生成する。</summary>
    private static IrMemberLoad Member(params string[] chain) =>
        new(new MemberRef(chain.ToImmutableArray()))
        {
            TypeRef = TypeRef.Bool, Id = -1, Origin = default
        };

    /// <summary>テスト用のリテラル式を生成する。</summary>
    private static IrLiteral Literal(object? value, LiteralKind kind, TypeRef typeRef) =>
        new(value, kind) { TypeRef = typeRef, Id = -1, Origin = default };

    // ═══════════════════════════════════════════════
    //  1. 最小ツリーの変換
    // ═══════════════════════════════════════════════

    [Fact]
    public void 最小のツリーが正しくレイアウトに変換される()
    {
        var tree = new IrTree("Simple", ImmutableArray.Create<IrNode>(Action("Patrol", id: 1)))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(0, 30)
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Equal("Simple", layout.TreeName);
        Assert.Equal(0, layout.Root.Id);
        Assert.Equal("tree", layout.Root.NodeType);
        Assert.Equal("Simple", layout.Root.Label);
        Assert.Single(layout.Root.Children);
    }

    [Fact]
    public void ルートノードのOriginが正しく設定される()
    {
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(Action("Act", id: 1)))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(5, 25)
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Equal(5, layout.Root.Origin.Start);
        Assert.Equal(25, layout.Root.Origin.Length);
    }

    // ═══════════════════════════════════════════════
    //  2. ノード種別のマッピング
    // ═══════════════════════════════════════════════

    [Fact]
    public void アクションノードが正しくマッピングされる()
    {
        var action = Action("Attack", id: 2, start: 10, length: 15);
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(action))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(0, 30)
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal(2, node.Id);
        Assert.Equal("action", node.NodeType);
        Assert.Equal("Attack()", node.Label);
        Assert.Equal(10, node.Origin.Start);
        Assert.Equal(15, node.Origin.Length);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void セレクターノードが正しくマッピングされる()
    {
        var selector = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("A", id: 2), Action("B", id: 3)))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = Span(5, 20)
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(selector))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(0, 30)
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("selector", node.NodeType);
        Assert.Equal(2, node.Children.Length);
    }

    [Fact]
    public void シーケンスノードが正しくマッピングされる()
    {
        var seq = new IrSequence(ImmutableArray.Create<IrNode>(
            Action("A", id: 2), Action("B", id: 3)))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = Span(5, 20)
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(seq))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(0, 30)
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("sequence", node.NodeType);
        Assert.Equal(2, node.Children.Length);
    }

    [Fact]
    public void パラレルノードのポリシーがラベルに含まれる()
    {
        var par = new IrParallel(new ParallelPolicy.Any(), ImmutableArray.Create<IrNode>(
            Action("A", id: 2)))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(par))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("parallel", node.NodeType);
        Assert.Contains("any", node.Label!.ToLowerInvariant());
    }

    [Fact]
    public void 条件ノードのラベルに式が表示される()
    {
        var condition = new IrCondition(Member("IsAlive"))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(condition))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("check", node.NodeType);
        Assert.Equal(".IsAlive", node.Label);
    }

    [Fact]
    public void ガードノードが正しくマッピングされる()
    {
        var guard = new IrGuard(Member("IsAlive"), Action("Patrol", id: 3))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(guard))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("guard", node.NodeType);
        Assert.Equal(".IsAlive", node.Label);
        Assert.Single(node.Children);
    }

    [Fact]
    public void リピートノードが正しくマッピングされる()
    {
        var repeat = new IrRepeat(3, Action("Step", id: 2))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(repeat))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("repeat", node.NodeType);
        Assert.Equal("3", node.Label);
        Assert.Single(node.Children);
    }

    [Fact]
    public void 反転ノードが正しくマッピングされる()
    {
        var invert = new IrInvert(Action("Check", id: 2))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(invert))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("invert", node.NodeType);
        Assert.Single(node.Children);
    }

    [Fact]
    public void ifノードのThenとElseが子として含まれる()
    {
        var ifNode = new IrIf(
            Member("IsReady"),
            Action("Attack", id: 3),
            Action("Retreat", id: 4))
        {
            TypeRef = TypeRef.BtStatus, Id = 2, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(ifNode))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("if", node.NodeType);
        Assert.Equal(".IsReady", node.Label);
        Assert.Equal(2, node.Children.Length);
    }

    [Fact]
    public void ifノードでElseがnullの場合は子が1つ()
    {
        var ifNode = new IrIf(
            Member("IsReady"),
            Action("Attack", id: 3),
            null)
        {
            TypeRef = TypeRef.BtStatus, Id = 2, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(ifNode))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Single(node.Children);
    }

    // ═══════════════════════════════════════════════
    //  3. ラベル生成（式フォーマット）
    // ═══════════════════════════════════════════════

    [Fact]
    public void 二項比較式のラベルが正しく生成される()
    {
        var lhs = Member("Health");
        var rhs = Literal(30.0f, LiteralKind.Float, TypeRef.Float);
        var expr = new IrBinaryOp(BinaryOp.Lt, lhs, rhs)
        {
            TypeRef = TypeRef.Bool, Id = -1, Origin = default
        };
        var condition = new IrCondition(expr)
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(condition))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal(".Health < 30", node.Label);
    }

    [Fact]
    public void 論理AND式のラベルが正しく生成される()
    {
        var expr = new IrLogicOp(LogicOp.And, ImmutableArray.Create<IrExpr>(
            Member("IsAlive"), Member("IsReady")))
        {
            TypeRef = TypeRef.Bool, Id = -1, Origin = default
        };
        var condition = new IrCondition(expr)
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(condition))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal(".IsAlive and .IsReady", node.Label);
    }

    [Fact]
    public void 引数付きアクションのラベルが正しく生成される()
    {
        var action = new IrAction(
            new MethodRef("TestAI", "Attack", ImmutableArray.Create("int")),
            ImmutableArray.Create<IrExpr>(Literal(10, LiteralKind.Int, TypeRef.Int)))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(action))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("Attack(10)", node.Label);
    }

    [Fact]
    public void メンバーチェーンのラベルが正しく生成される()
    {
        var condition = new IrCondition(Member("Target", "IsVisible"))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(condition))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Equal(".Target.IsVisible", layout.Root.Children[0].Label);
    }

    [Fact]
    public void 否定式のラベルにnotが含まれる()
    {
        var expr = new IrUnaryOp(UnaryOp.Not, Member("IsAlive"))
        {
            TypeRef = TypeRef.Bool, Id = -1, Origin = default
        };
        var condition = new IrCondition(expr)
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(condition))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Equal("not .IsAlive", layout.Root.Children[0].Label);
    }

    // ═══════════════════════════════════════════════
    //  4. 型情報の伝播
    // ═══════════════════════════════════════════════

    [Fact]
    public void ノードの型情報がResolvedTypeに反映される()
    {
        var action = Action("Patrol", id: 1);
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(action))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Equal("Crisp.Runtime.BtStatus", layout.Root.ResolvedType);
        Assert.Equal("Crisp.Runtime.BtStatus", layout.Root.Children[0].ResolvedType);
    }

    // ═══════════════════════════════════════════════
    //  5. 診断情報の付加
    // ═══════════════════════════════════════════════

    [Fact]
    public void 診断情報が空の場合はEmptyが返る()
    {
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(Action("A", id: 1)))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);

        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void 診断情報が正しく付加される()
    {
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(Action("A", id: 1)))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };
        var diags = ImmutableArray.Create(
            new TreeLayoutDiagnostic(1, "error", "テストエラー"),
            new TreeLayoutDiagnostic(-1, "warning", "テスト警告"));

        var layout = TreeLayoutBuilder.Build(tree, diags);

        Assert.Equal(2, layout.Diagnostics.Length);
        Assert.Equal("error", layout.Diagnostics[0].Severity);
        Assert.Equal(1, layout.Diagnostics[0].NodeId);
        Assert.Equal("テスト警告", layout.Diagnostics[1].Message);
    }

    // ═══════════════════════════════════════════════
    //  6. 複雑なツリー構造
    // ═══════════════════════════════════════════════

    [Fact]
    public void ネストした複合ノードが再帰的に変換される()
    {
        // (tree T (select (seq (check .IsAlive) (.Attack)) (.Patrol)))
        var tree = new IrTree("Combat", ImmutableArray.Create<IrNode>(
            new IrSelector(ImmutableArray.Create<IrNode>(
                new IrSequence(ImmutableArray.Create<IrNode>(
                    new IrCondition(Member("IsAlive"))
                    {
                        TypeRef = TypeRef.BtStatus, Id = 4, Origin = Span(30, 10)
                    },
                    Action("Attack", id: 5, start: 42, length: 8)))
                {
                    TypeRef = TypeRef.BtStatus, Id = 2, Origin = Span(15, 40)
                },
                Action("Patrol", id: 3, start: 58, length: 8)))
            {
                TypeRef = TypeRef.BtStatus, Id = 1, Origin = Span(8, 60)
            }))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = Span(0, 70)
        };

        var layout = TreeLayoutBuilder.Build(tree);

        // ルート: tree
        Assert.Equal("tree", layout.Root.NodeType);
        Assert.Equal("Combat", layout.Root.Label);

        // 第1階層: selector
        var selector = layout.Root.Children[0];
        Assert.Equal("selector", selector.NodeType);
        Assert.Equal(1, selector.Id);
        Assert.Equal(2, selector.Children.Length);

        // 第2階層: sequence, action(Patrol)
        var sequence = selector.Children[0];
        Assert.Equal("sequence", sequence.NodeType);
        Assert.Equal(2, sequence.Id);
        Assert.Equal(2, sequence.Children.Length);

        var patrol = selector.Children[1];
        Assert.Equal("action", patrol.NodeType);
        Assert.Equal(3, patrol.Id);
        Assert.Equal("Patrol()", patrol.Label);

        // 第3階層: check, action(Attack)
        var check = sequence.Children[0];
        Assert.Equal("check", check.NodeType);
        Assert.Equal(4, check.Id);
        Assert.Equal(".IsAlive", check.Label);

        var attack = sequence.Children[1];
        Assert.Equal("action", attack.NodeType);
        Assert.Equal(5, attack.Id);
    }

    [Fact]
    public void ツリー参照ノードが正しくマッピングされる()
    {
        var treeRef = new IrTreeRef("SubTree")
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = Span(5, 12)
        };
        var tree = new IrTree("Main", ImmutableArray.Create<IrNode>(treeRef))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("tree-ref", node.NodeType);
        Assert.Equal("SubTree", node.Label);
    }

    [Fact]
    public void タイムアウトノードのラベルに秒数が含まれる()
    {
        var timeout = new IrTimeout(5.0f, Action("LongTask", id: 2))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(timeout))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("timeout", node.NodeType);
        Assert.Equal("5s", node.Label);
    }

    [Fact]
    public void クールダウンノードのラベルに秒数が含まれる()
    {
        var cooldown = new IrCooldown(2.5f, Action("Skill", id: 2))
        {
            TypeRef = TypeRef.BtStatus, Id = 1, Origin = default
        };
        var tree = new IrTree("T", ImmutableArray.Create<IrNode>(cooldown))
        {
            TypeRef = TypeRef.BtStatus, Id = 0, Origin = default
        };

        var layout = TreeLayoutBuilder.Build(tree);
        var node = layout.Root.Children[0];

        Assert.Equal("cooldown", node.NodeType);
        Assert.Equal("2.5s", node.Label);
    }
}
