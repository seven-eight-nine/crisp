using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// F11: Multi-Tree &amp; Cross-Reference のユニットテスト。
/// t-wada 式 TDD に基づき、最小のケースから段階的に複雑さを増す。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>Lexer: "ref" が Identifier トークンとして認識される</description></item>
///   <item><description>Parser: (ref TreeName) が CstRef に変換される</description></item>
///   <item><description>CstToAstLowering: CstRef が AstRef に変換される</description></item>
///   <item><description>AstToIrLowering: AstRef が IrTreeRef に変換される</description></item>
///   <item><description>CSharpEmitter: IrTreeRef が BuildTreeName() メソッド呼び出しに変換される</description></item>
///   <item><description>IrSerializer/Deserializer: IrTreeRef のラウンドトリップ</description></item>
///   <item><description>Evaluator: TickTreeRef の NotSupportedException</description></item>
///   <item><description>End-to-end: 複数ツリーのクロスリファレンス</description></item>
/// </list>
/// </summary>
public class MultiTreeTests
{
    // ──────────────────────────────────────────────
    //  IR ノード構築ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = TypeRef.Bool };

    // ═══════════════════════════════════════════════
    //  1. Lexer: "ref" のトークン化
    // ═══════════════════════════════════════════════

    [Fact]
    public void refがIdentifierトークンとしてレキシングされる()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("ref")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("ref", tokens[0].Text);
    }

    [Fact]
    public void ref前後の括弧もトークン化される()
    {
        // Arrange & Act
        var tokens = Lexer.Lex("(ref Combat)")
            .Where(t => t.Kind != TokenKind.EndOfFile).ToList();

        // Assert
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("ref", tokens[1].Text);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("Combat", tokens[2].Text);
        Assert.Equal(TokenKind.RightParen, tokens[3].Kind);
    }

    // ═══════════════════════════════════════════════
    //  2. Parser: CstRef の生成
    // ═══════════════════════════════════════════════

    [Fact]
    public void refノードをパースできる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse("(tree Main (ref Combat))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var cstRef = Assert.IsType<CstRef>(tree.Body[0]);
        Assert.Equal("ref", cstRef.Keyword.Text);
        Assert.Equal("Combat", cstRef.Name.Text);
    }

    [Fact]
    public void refノードのキーワードがrefである()
    {
        var (program, _) = Parser.Parse("(tree T (ref SubTree))");

        var tree = Assert.Single(program.Trees);
        var cstRef = Assert.IsType<CstRef>(tree.Body[0]);
        Assert.Equal("ref", cstRef.Keyword.Text);
    }

    [Fact]
    public void refノードの参照先ツリー名が保持される()
    {
        var (program, _) = Parser.Parse("(tree T (ref PatrolBehavior))");

        var tree = Assert.Single(program.Trees);
        var cstRef = Assert.IsType<CstRef>(tree.Body[0]);
        Assert.Equal("PatrolBehavior", cstRef.Name.Text);
    }

    [Fact]
    public void refノードが他ノードと共存できる()
    {
        // Arrange: seq の中に ref と action が混在
        var (program, diagnostics) = Parser.Parse("(tree Main (seq (ref Combat) .Patrol))");

        // Assert
        Assert.False(diagnostics.HasErrors);
        var tree = Assert.Single(program.Trees);
        var seq = Assert.IsType<CstSequence>(tree.Body[0]);
        Assert.Equal(2, seq.Children_.Length);
        Assert.IsType<CstRef>(seq.Children_[0]);
        Assert.IsType<CstMemberAccess>(seq.Children_[1]);
    }

    // ═══════════════════════════════════════════════
    //  3. CstToAstLowering: CstRef -> AstRef
    // ═══════════════════════════════════════════════

    [Fact]
    public void CstRefがAstRefに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree Main (ref Combat))");
        var trees = CstToAstLowering.Lower(program);

        // Assert
        var tree = trees[0];
        var astRef = Assert.IsType<AstRef>(tree.Body[0]);
        Assert.Equal("Combat", astRef.TreeName);
    }

    [Fact]
    public void AstRefの参照先ツリーが初期状態でnullである()
    {
        var (program, _) = Parser.Parse("(tree Main (ref Sub))");
        var trees = CstToAstLowering.Lower(program);

        var astRef = Assert.IsType<AstRef>(trees[0].Body[0]);
        Assert.Null(astRef.ResolvedTree);
    }

    [Fact]
    public void CstRefがseq内でAstRefに正しく変換される()
    {
        var (program, _) = Parser.Parse("(tree Main (seq (ref A) (ref B)))");
        var trees = CstToAstLowering.Lower(program);

        var seq = Assert.IsType<AstSequence>(trees[0].Body[0]);
        Assert.Equal(2, seq.Children_.Length);
        var ref1 = Assert.IsType<AstRef>(seq.Children_[0]);
        var ref2 = Assert.IsType<AstRef>(seq.Children_[1]);
        Assert.Equal("A", ref1.TreeName);
        Assert.Equal("B", ref2.TreeName);
    }

    // ═══════════════════════════════════════════════
    //  4. AstToIrLowering: AstRef -> IrTreeRef
    // ═══════════════════════════════════════════════

    [Fact]
    public void AstRefがIrTreeRefに変換される()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree Main (ref Combat))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var irTree = irTrees[0];
        var treeRef = Assert.IsType<IrTreeRef>(irTree.Body[0]);
        Assert.Equal("Combat", treeRef.TreeName);
    }

    [Fact]
    public void IrTreeRefのTypeRefがBtStatusである()
    {
        var (program, _) = Parser.Parse("(tree Main (ref Sub))");
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        var treeRef = Assert.IsType<IrTreeRef>(irTrees[0].Body[0]);
        Assert.Equal(TypeRef.BtStatus, treeRef.TypeRef);
    }

    // ═══════════════════════════════════════════════
    //  5. CSharpEmitter: IrTreeRef -> BuildTreeName()
    // ═══════════════════════════════════════════════

    [Fact]
    public void IrTreeRefがBuildメソッド呼び出しに変換される()
    {
        // Arrange
        var node = new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus };

        // Act
        var result = CSharpEmitter.EmitExpression(node);

        // Assert
        Assert.Equal("BuildCombat()", result);
    }

    [Fact]
    public void IrTreeRefのツリー名がメソッド名に反映される()
    {
        var node = new IrTreeRef("PatrolBehavior") { TypeRef = TypeRef.BtStatus };

        var result = CSharpEmitter.EmitExpression(node);

        Assert.Equal("BuildPatrolBehavior()", result);
    }

    [Fact]
    public void IrTreeRefがseq内で正しいコードを生成する()
    {
        // Arrange: sequence の中に ref と action が混在
        var seqNode = new IrSequence(ImmutableArray.Create<IrNode>(
            new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus },
            Action("Patrol")));

        // Act
        var result = CSharpEmitter.EmitExpression(seqNode);

        // Assert
        Assert.Contains("BuildCombat()", result);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", result);
        Assert.Contains("new SequenceNode(", result);
    }

    [Fact]
    public void Emit完全なファイルでIrTreeRefが出力される()
    {
        // Arrange
        var tree = new IrTree("Main", ImmutableArray.Create<IrNode>(
            new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus }));

        // Act
        var result = CSharpEmitter.Emit(tree, "Game.AI", "AgentContext", "BuildMain");

        // Assert
        Assert.Contains("return BuildCombat();", result);
        Assert.Contains("partial class AgentContext", result);
        Assert.Contains("namespace Game.AI;", result);
    }

    // ═══════════════════════════════════════════════
    //  6. IrSerializer: IrTreeRef の直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void IrTreeRefが正しく直列化される()
    {
        // Arrange
        var node = new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus };

        // Act
        var result = IrSerializer.Serialize(node);

        // Assert
        Assert.Contains("ir-tree-ref", result);
        Assert.Contains("\"Combat\"", result);
    }

    [Fact]
    public void IrTreeRefのラウンドトリップが等価である()
    {
        // Arrange
        var original = new IrTreeRef("PatrolBehavior") { TypeRef = TypeRef.BtStatus };

        // Act: Serialize -> Deserialize -> Serialize
        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        // Assert
        Assert.Equal(text, roundTripped);
        Assert.IsType<IrTreeRef>(deserialized);
    }

    [Fact]
    public void IrTreeRefを逆直列化できる()
    {
        // Arrange: 手書きのS式
        var text = "(ir-tree-ref \"Combat\")";

        // Act
        var node = IrDeserializer.Deserialize(text);

        // Assert
        var treeRef = Assert.IsType<IrTreeRef>(node);
        Assert.Equal("Combat", treeRef.TreeName);
    }

    [Fact]
    public void 複数ツリーの直列化でIrTreeRefが含まれる()
    {
        // Arrange: ツリー内に ref を含む
        var trees = ImmutableArray.Create(
            new IrTree("Main", ImmutableArray.Create<IrNode>(
                new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus })),
            new IrTree("Combat", ImmutableArray.Create<IrNode>(
                Action("Attack"))));

        // Act
        var text = IrSerializer.Serialize(trees);

        // Assert
        Assert.Contains("ir-tree-ref", text);
        Assert.Contains("\"Combat\"", text);
        Assert.Contains("ir-tree \"Main\"", text);
        Assert.Contains("ir-tree \"Combat\"", text);
    }

    // ═══════════════════════════════════════════════
    //  7. Evaluator: TickTreeRef は NotSupportedException
    // ═══════════════════════════════════════════════

    [Fact]
    public void Evaluatorでrefを評価するとNotSupportedExceptionが投げられる()
    {
        // Arrange
        var agent = new object();
        var eval = new Evaluator(agent);
        var node = new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus };

        // Act & Assert
        var ex = Assert.Throws<NotSupportedException>(() => eval.Tick(node));
        Assert.Contains("Combat", ex.Message);
    }

    // ═══════════════════════════════════════════════
    //  8. End-to-end: 複数ツリーの定義とクロスリファレンス
    // ═══════════════════════════════════════════════

    [Fact]
    public void 複数ツリー定義をパースできる()
    {
        // Arrange
        var source = "(tree Main (ref Combat)) (tree Combat .Attack)";

        // Act
        var (program, diagnostics) = Parser.Parse(source);

        // Assert
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, program.Trees.Length);
        Assert.Equal("Main", program.Trees[0].Name.Text);
        Assert.Equal("Combat", program.Trees[1].Name.Text);
    }

    [Fact]
    public void 複数ツリーのクロスリファレンスがIRまで変換される()
    {
        // Arrange: Main が Combat を参照、Combat が Attack アクションを持つ
        var source = "(tree Main (ref Combat)) (tree Combat .Attack)";

        // Act
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert: Main ツリー
        Assert.Equal(2, irTrees.Length);
        var mainTree = irTrees[0];
        Assert.Equal("Main", mainTree.Name);
        var treeRef = Assert.IsType<IrTreeRef>(mainTree.Body[0]);
        Assert.Equal("Combat", treeRef.TreeName);

        // Assert: Combat ツリー
        var combatTree = irTrees[1];
        Assert.Equal("Combat", combatTree.Name);
        Assert.IsType<IrAction>(combatTree.Body[0]);
    }

    [Fact]
    public void 複数refを持つツリーがパースからIRまで変換される()
    {
        // Arrange: select 内で 2 つの ref を持つ
        var source = "(tree Main (select (ref CombatTree) (ref PatrolTree)))";

        // Act
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var mainTree = irTrees[0];
        var selector = Assert.IsType<IrSelector>(mainTree.Body[0]);
        Assert.Equal(2, selector.Children.Length);

        var ref1 = Assert.IsType<IrTreeRef>(selector.Children[0]);
        Assert.Equal("CombatTree", ref1.TreeName);
        var ref2 = Assert.IsType<IrTreeRef>(selector.Children[1]);
        Assert.Equal("PatrolTree", ref2.TreeName);
    }

    [Fact]
    public void 複数ツリーのクロスリファレンスがCSharpコードに変換される()
    {
        // Arrange
        var source = "(tree Main (select (ref Combat) .Patrol)) (tree Combat .Attack)";
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Act: Main ツリーのコードを生成
        var code = CSharpEmitter.Emit(irTrees[0], "Game", "Agent", "BuildMain");

        // Assert
        Assert.Contains("BuildCombat()", code);
        Assert.Contains("new ActionNode(() => this.Patrol(), \"Patrol()\")", code);
    }

    [Fact]
    public void 複数ツリーのIRラウンドトリップが等価である()
    {
        // Arrange: 2 ツリーの IR を構築
        var trees = ImmutableArray.Create(
            new IrTree("Main", ImmutableArray.Create<IrNode>(
                new IrSelector(ImmutableArray.Create<IrNode>(
                    new IrTreeRef("Combat") { TypeRef = TypeRef.BtStatus },
                    Action("Idle"))))),
            new IrTree("Combat", ImmutableArray.Create<IrNode>(
                new IrSequence(ImmutableArray.Create<IrNode>(
                    new IrCondition(Member("HasEnemy")),
                    Action("Attack"))))));

        // Act: Serialize -> Deserialize -> Serialize
        var text = IrSerializer.Serialize(trees);
        var deserialized = IrDeserializer.DeserializeTrees(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        // Assert
        Assert.Equal(text, roundTripped);
        Assert.Equal(2, deserialized.Length);
        Assert.Equal("Main", deserialized[0].Name);
        Assert.Equal("Combat", deserialized[1].Name);

        // Main ツリー内に IrTreeRef が存在する
        var mainSelector = Assert.IsType<IrSelector>(deserialized[0].Body[0]);
        Assert.IsType<IrTreeRef>(mainSelector.Children[0]);
    }

    [Fact]
    public void refとreactiveが混在するツリーがIRまで変換される()
    {
        // Arrange: reactive の body に ref を持つ複合パターン
        var source = "(tree Main (reactive .IsAlive (ref Combat))) (tree Combat .Attack)";

        // Act
        var (program, diagnostics) = Parser.Parse(source);
        Assert.False(diagnostics.HasErrors);
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);

        // Assert
        var mainTree = irTrees[0];
        var reactive = Assert.IsType<IrReactive>(mainTree.Body[0]);
        var condition = Assert.IsType<IrMemberLoad>(reactive.Condition);
        Assert.Equal("IsAlive", condition.Ref.Chain[0]);
        var bodyRef = Assert.IsType<IrTreeRef>(reactive.Body);
        Assert.Equal("Combat", bodyRef.TreeName);
    }

    // ═══════════════════════════════════════════════
    //  9. AstRefResolver: ツリー参照の解決
    // ═══════════════════════════════════════════════

    [Fact]
    public void AstRefResolverが同一ファイル内の参照を解決する()
    {
        // Arrange: Main が Combat を参照
        var (program, _) = Parser.Parse("(tree Main (ref Combat)) (tree Combat .Attack)");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: Main 内の AstRef.ResolvedTree が Combat ツリーに設定される
        var astRef = Assert.IsType<AstRef>(trees[0].Body[0]);
        Assert.NotNull(astRef.ResolvedTree);
        Assert.Equal("Combat", astRef.ResolvedTree!.Name);
        Assert.Empty(bag.Diagnostics);
    }

    [Fact]
    public void AstRefResolverが複数の参照を同時に解決する()
    {
        // Arrange: Main が A と B を参照
        var (program, _) = Parser.Parse(
            "(tree Main (seq (ref A) (ref B))) (tree A .Attack) (tree B .Patrol)");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert
        var seq = Assert.IsType<AstSequence>(trees[0].Body[0]);
        var refA = Assert.IsType<AstRef>(seq.Children_[0]);
        var refB = Assert.IsType<AstRef>(seq.Children_[1]);
        Assert.Equal("A", refA.ResolvedTree!.Name);
        Assert.Equal("B", refB.ResolvedTree!.Name);
        Assert.Empty(bag.Diagnostics);
    }

    [Fact]
    public void AstRefResolverが未定義ツリーでBS0038を報告する()
    {
        // Arrange: Main が存在しない NonExistent を参照
        var (program, _) = Parser.Parse("(tree Main (ref NonExistent))");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: BS0038 エラーが報告される
        var astRef = Assert.IsType<AstRef>(trees[0].Body[0]);
        Assert.Null(astRef.ResolvedTree);
        Assert.Single(bag.Diagnostics);
        Assert.Equal("BS0038", bag.Diagnostics[0].Id);
        Assert.Contains("NonExistent", bag.Diagnostics[0].Message);
    }

    [Fact]
    public void AstRefResolverが複数の未定義参照で複数のBS0038を報告する()
    {
        // Arrange: Main が 2 つの未定義ツリーを参照
        var (program, _) = Parser.Parse("(tree Main (seq (ref X) (ref Y)))");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: 2 つの BS0038 エラー
        Assert.Equal(2, bag.Diagnostics.Count);
        Assert.All(bag.Diagnostics, d => Assert.Equal("BS0038", d.Id));
    }

    // ═══════════════════════════════════════════════
    //  10. AstRefResolver: 循環参照の検出
    // ═══════════════════════════════════════════════

    [Fact]
    public void AstRefResolverが直接循環参照を検出する()
    {
        // Arrange: A → B → A の循環
        var (program, _) = Parser.Parse("(tree A (ref B)) (tree B (ref A))");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: BS0037 が報告される
        var circularDiags = bag.Diagnostics.Where(d => d.Id == "BS0037").ToList();
        Assert.NotEmpty(circularDiags);
        // 循環パスが含まれる
        Assert.Contains("A", circularDiags[0].Message);
        Assert.Contains("B", circularDiags[0].Message);
    }

    [Fact]
    public void AstRefResolverが間接循環参照を検出する()
    {
        // Arrange: A → B → C → A の間接循環
        var (program, _) = Parser.Parse(
            "(tree A (ref B)) (tree B (ref C)) (tree C (ref A))");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: BS0037 が報告される
        var circularDiags = bag.Diagnostics.Where(d => d.Id == "BS0037").ToList();
        Assert.NotEmpty(circularDiags);
        Assert.Contains("→", circularDiags[0].Message);
    }

    [Fact]
    public void AstRefResolverが循環のないグラフでエラーを報告しない()
    {
        // Arrange: A → B, A → C, B → C（DAG、循環なし）
        var (program, _) = Parser.Parse(
            "(tree A (seq (ref B) (ref C))) (tree B (ref C)) (tree C .Attack)");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: 循環参照エラーなし
        var circularDiags = bag.Diagnostics.Where(d => d.Id == "BS0037").ToList();
        Assert.Empty(circularDiags);
    }

    [Fact]
    public void AstRefResolverが自己参照を循環として検出する()
    {
        // Arrange: A が自身を参照
        var (program, _) = Parser.Parse("(tree A (ref A))");
        var trees = CstToAstLowering.Lower(program);
        var bag = new DiagnosticBag();

        // Act
        AstRefResolver.Resolve(trees, bag);

        // Assert: BS0037 が報告される
        var circularDiags = bag.Diagnostics.Where(d => d.Id == "BS0037").ToList();
        Assert.NotEmpty(circularDiags);
        Assert.Contains("A → A", circularDiags[0].Message);
    }

    // ═══════════════════════════════════════════════
    //  11. Import のパース
    // ═══════════════════════════════════════════════

    [Fact]
    public void importノードがパースされてCstProgramに保存される()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse(
            "(import \"shared.crisp\") (tree Main .Attack)");

        // Assert
        Assert.False(diagnostics.HasErrors);
        Assert.Single(program.Trees);
        Assert.Single(program.Imports);
        Assert.Equal("\"shared.crisp\"", program.Imports[0].Path.Text);
    }

    [Fact]
    public void 複数のimportがパースされる()
    {
        // Arrange & Act
        var (program, diagnostics) = Parser.Parse(
            "(import \"a.crisp\") (import \"b.crisp\") (tree Main .Attack)");

        // Assert
        Assert.False(diagnostics.HasErrors);
        Assert.Single(program.Trees);
        Assert.Equal(2, program.Imports.Length);
        Assert.Equal("\"a.crisp\"", program.Imports[0].Path.Text);
        Assert.Equal("\"b.crisp\"", program.Imports[1].Path.Text);
    }

    [Fact]
    public void importがツリー定義と混在してパースされる()
    {
        // Arrange: import の前後に tree がある
        var (program, diagnostics) = Parser.Parse(
            "(tree Before .Patrol) (import \"shared.crisp\") (tree After .Attack)");

        // Assert
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, program.Trees.Length);
        Assert.Single(program.Imports);
        Assert.Equal("Before", program.Trees[0].Name.Text);
        Assert.Equal("After", program.Trees[1].Name.Text);
    }

    [Fact]
    public void importのないプログラムではImportsが空配列()
    {
        // Arrange & Act
        var (program, _) = Parser.Parse("(tree Main .Attack)");

        // Assert
        Assert.Empty(program.Imports);
    }
}
