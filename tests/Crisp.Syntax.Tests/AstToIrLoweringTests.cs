using System.Collections.Immutable;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// AST → IR Lowering のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>ソースを parse → CST lower → IR lower のパイプラインで IR を生成する</description></item>
///   <item><description>各 AST ノード型が正しい IR ノード型に変換されることを検証する</description></item>
///   <item><description>int + float 混合演算で IrConvert が自動挿入されることを検証する</description></item>
///   <item><description>式の TypeRef が正しく推論されることを検証する</description></item>
/// </list>
/// </summary>
public class AstToIrLoweringTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>
    /// ソーステキストから IR ツリー配列を生成する。
    /// parse → CST lower → IR lower のフルパイプラインを実行する。
    /// </summary>
    private static ImmutableArray<IrTree> LowerToIr(string source)
    {
        var (program, _) = Parser.Parse(source);
        var astTrees = CstToAstLowering.Lower(program);
        return AstToIrLowering.Lower(astTrees);
    }

    /// <summary>最初のツリーの IR を返す。</summary>
    private static IrTree LowerFirstTree(string source)
    {
        return LowerToIr(source)[0];
    }

    // ═══════════════════════════════════════════════
    //  1. 最小のツリー
    // ═══════════════════════════════════════════════

    [Fact]
    public void 最小のtreeがIrTreeに変換される()
    {
        // Arrange & Act: 括弧なしアクションのみのツリー
        var tree = LowerFirstTree("(tree T .Patrol)");

        // Assert: IrTree として生成される
        Assert.Equal("T", tree.Name);
        Assert.Single(tree.Body);
    }

    [Fact]
    public void 括弧なしアクションがIrActionに変換される()
    {
        // Arrange & Act
        var tree = LowerFirstTree("(tree T .Patrol)");

        // Assert: IrAction に変換され、メソッド名が正しいこと
        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal("Patrol", action.Method.Name);
        Assert.Equal("this", action.Method.DeclaringType);
        Assert.Empty(action.Args);
    }

    [Fact]
    public void 括弧付き引数なしアクションがIrActionに変換される()
    {
        var tree = LowerFirstTree("(tree T (.Patrol))");

        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal("Patrol", action.Method.Name);
        Assert.Empty(action.Args);
    }

    [Fact]
    public void 引数ありアクションがIrActionに変換される()
    {
        var tree = LowerFirstTree("(tree T (.Attack .Target))");

        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal("Attack", action.Method.Name);
        Assert.Single(action.Args);
        var arg = Assert.IsType<IrMemberLoad>(action.Args[0]);
        Assert.Equal("Target", arg.Ref.Chain[0]);
    }

    // ═══════════════════════════════════════════════
    //  2. 複合ノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void selectがIrSelectorに変換される()
    {
        var tree = LowerFirstTree("(tree T (select .A .B))");

        var selector = Assert.IsType<IrSelector>(tree.Body[0]);
        Assert.Equal(2, selector.Children.Length);
        Assert.All(selector.Children, child => Assert.IsType<IrAction>(child));
    }

    [Fact]
    public void seqがIrSequenceに変換される()
    {
        var tree = LowerFirstTree("(tree T (seq .A .B))");

        var sequence = Assert.IsType<IrSequence>(tree.Body[0]);
        Assert.Equal(2, sequence.Children.Length);
    }

    [Fact]
    public void parallelがIrParallelに変換される_Anyポリシー()
    {
        var tree = LowerFirstTree("(tree T (parallel :any .A .B))");

        var parallel = Assert.IsType<IrParallel>(tree.Body[0]);
        Assert.IsType<ParallelPolicy.Any>(parallel.Policy);
        Assert.Equal(2, parallel.Children.Length);
    }

    [Fact]
    public void parallelがIrParallelに変換される_Allポリシー()
    {
        var tree = LowerFirstTree("(tree T (parallel :all .A .B))");

        var parallel = Assert.IsType<IrParallel>(tree.Body[0]);
        Assert.IsType<ParallelPolicy.All>(parallel.Policy);
    }

    [Fact]
    public void parallelがIrParallelに変換される_Nポリシー()
    {
        var tree = LowerFirstTree("(tree T (parallel :n 2 .A .B .C))");

        var parallel = Assert.IsType<IrParallel>(tree.Body[0]);
        var nPolicy = Assert.IsType<ParallelPolicy.N>(parallel.Policy);
        Assert.Equal(2, nPolicy.Count);
        Assert.Equal(3, parallel.Children.Length);
    }

    // ═══════════════════════════════════════════════
    //  3. 条件・ガードノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void checkがIrConditionに変換される()
    {
        var tree = LowerFirstTree("(tree T (check .IsAlive))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        Assert.IsType<IrMemberLoad>(cond.Expr);
    }

    [Fact]
    public void guardがIrGuardに変換される()
    {
        var tree = LowerFirstTree("(tree T (guard .IsAlive (.Patrol)))");

        var guard = Assert.IsType<IrGuard>(tree.Body[0]);
        Assert.IsType<IrMemberLoad>(guard.Condition);
        Assert.IsType<IrAction>(guard.Body);
    }

    [Fact]
    public void ifがIrIfに変換される_elseなし()
    {
        var tree = LowerFirstTree("(tree T (if .IsAlive .Patrol))");

        var ifNode = Assert.IsType<IrIf>(tree.Body[0]);
        Assert.IsType<IrMemberLoad>(ifNode.Condition);
        Assert.IsType<IrAction>(ifNode.Then);
        Assert.Null(ifNode.Else);
    }

    [Fact]
    public void ifがIrIfに変換される_elseあり()
    {
        var tree = LowerFirstTree("(tree T (if .IsAlive .Patrol .Flee))");

        var ifNode = Assert.IsType<IrIf>(tree.Body[0]);
        Assert.NotNull(ifNode.Else);
        Assert.IsType<IrAction>(ifNode.Else);
    }

    // ═══════════════════════════════════════════════
    //  4. デコレータノード
    // ═══════════════════════════════════════════════

    [Fact]
    public void invertがIrInvertに変換される()
    {
        var tree = LowerFirstTree("(tree T (invert .A))");

        var invert = Assert.IsType<IrInvert>(tree.Body[0]);
        Assert.IsType<IrAction>(invert.Child);
    }

    [Fact]
    public void repeatがIrRepeatに変換される()
    {
        var tree = LowerFirstTree("(tree T (repeat 3 .A))");

        var repeat = Assert.IsType<IrRepeat>(tree.Body[0]);
        Assert.Equal(3, repeat.Count);
        Assert.IsType<IrAction>(repeat.Child);
    }

    [Fact]
    public void timeoutがIrTimeoutに変換される()
    {
        var tree = LowerFirstTree("(tree T (timeout 5.0 .A))");

        var timeout = Assert.IsType<IrTimeout>(tree.Body[0]);
        Assert.Equal(5.0f, timeout.Seconds);
        Assert.IsType<IrAction>(timeout.Child);
    }

    [Fact]
    public void cooldownがIrCooldownに変換される()
    {
        var tree = LowerFirstTree("(tree T (cooldown 2.5 .A))");

        var cooldown = Assert.IsType<IrCooldown>(tree.Body[0]);
        Assert.Equal(2.5f, cooldown.Seconds);
        Assert.IsType<IrAction>(cooldown.Child);
    }

    [Fact]
    public void whileがIrWhileに変換される()
    {
        var tree = LowerFirstTree("(tree T (while .IsAlive .Patrol))");

        var whileNode = Assert.IsType<IrWhile>(tree.Body[0]);
        Assert.IsType<IrMemberLoad>(whileNode.Condition);
        Assert.IsType<IrAction>(whileNode.Body);
    }

    // ═══════════════════════════════════════════════
    //  5. リテラル式
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数リテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (< .X 42)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal(42, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
        Assert.Equal(TypeRef.Int, lit.TypeRef);
    }

    [Fact]
    public void 浮動小数点リテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (< .X 3.14)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal(3.14f, lit.Value);
        Assert.Equal(LiteralKind.Float, lit.Kind);
        Assert.Equal(TypeRef.Float, lit.TypeRef);
    }

    [Fact]
    public void boolリテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (= .X true)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal(true, lit.Value);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
        Assert.Equal(TypeRef.Bool, lit.TypeRef);
    }

    [Fact]
    public void 文字列リテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (= .X \"hello\")))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal("hello", lit.Value);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal(TypeRef.String, lit.TypeRef);
    }

    [Fact]
    public void nullリテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (!= .X null)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Null(lit.Value);
        Assert.Equal(LiteralKind.Null, lit.Kind);
    }

    [Fact]
    public void enumリテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (= .State ::AIState.Combat)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal(LiteralKind.Enum, lit.Kind);
        var enumRef = Assert.IsType<EnumRef>(lit.Value);
        Assert.Equal("AIState", enumRef.TypeName);
        Assert.Equal("Combat", enumRef.MemberName);
    }

    [Fact]
    public void 負リテラルがIrLiteralに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (> .X -1)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var lit = Assert.IsType<IrLiteral>(bin.Rhs);
        Assert.Equal(-1, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
    }

    // ═══════════════════════════════════════════════
    //  6. 演算式
    // ═══════════════════════════════════════════════

    [Fact]
    public void 二項演算式がIrBinaryOpに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (< .Health 30)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        Assert.Equal(BinaryOp.Lt, bin.Op);
        Assert.IsType<IrMemberLoad>(bin.Lhs);
        Assert.IsType<IrLiteral>(bin.Rhs);
    }

    [Fact]
    public void 比較演算の結果型はboolである()
    {
        var tree = LowerFirstTree("(tree T (check (< .X 30)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        Assert.Equal(TypeRef.Bool, bin.TypeRef);
    }

    [Fact]
    public void 算術演算の結果型はオペランドの型に従う()
    {
        // int + int → int
        var tree = LowerFirstTree("(tree T (check (= (+ 1 2) 3)))");
        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var eq = Assert.IsType<IrBinaryOp>(cond.Expr);
        var add = Assert.IsType<IrBinaryOp>(eq.Lhs);
        Assert.Equal(TypeRef.Int, add.TypeRef);
    }

    [Fact]
    public void not式がIrUnaryOpに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (not .IsAlive)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var unary = Assert.IsType<IrUnaryOp>(cond.Expr);
        Assert.Equal(UnaryOp.Not, unary.Op);
        Assert.Equal(TypeRef.Bool, unary.TypeRef);
    }

    [Fact]
    public void and式がIrLogicOpに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (and .A .B)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var logic = Assert.IsType<IrLogicOp>(cond.Expr);
        Assert.Equal(LogicOp.And, logic.Op);
        Assert.Equal(2, logic.Operands.Length);
        Assert.Equal(TypeRef.Bool, logic.TypeRef);
    }

    [Fact]
    public void or式がIrLogicOpに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (or .A .B)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var logic = Assert.IsType<IrLogicOp>(cond.Expr);
        Assert.Equal(LogicOp.Or, logic.Op);
    }

    // ═══════════════════════════════════════════════
    //  7. 式位置のメソッド呼び出し
    // ═══════════════════════════════════════════════

    [Fact]
    public void 式位置の呼び出しがIrCallに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (< (.DistanceTo .Target) 5.0)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var call = Assert.IsType<IrCall>(bin.Lhs);
        Assert.Equal("DistanceTo", call.Method.Name);
        Assert.Equal("this", call.Method.DeclaringType);
        Assert.Single(call.Args);
    }

    // ═══════════════════════════════════════════════
    //  8. メンバーチェーン
    // ═══════════════════════════════════════════════

    [Fact]
    public void メンバーチェーンがIrMemberLoadのチェーンに変換される()
    {
        var tree = LowerFirstTree("(tree T (check (> .Target.Health 0)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var bin = Assert.IsType<IrBinaryOp>(cond.Expr);
        var member = Assert.IsType<IrMemberLoad>(bin.Lhs);
        Assert.Equal(2, member.Ref.Chain.Length);
        Assert.Equal("Target", member.Ref.Chain[0]);
        Assert.Equal("Health", member.Ref.Chain[1]);
    }

    // ═══════════════════════════════════════════════
    //  9. IrConvert の自動挿入（int + float 混合演算）
    // ═══════════════════════════════════════════════

    [Fact]
    public void int_float混合比較でint側にIrConvertが挿入される()
    {
        // .X は unknown 型（スタンドアロンモード）なので、リテラル同士で検証する
        // (+ 1 2.0) → 左辺 1(int) が IrConvert で float に昇格される
        var tree = LowerFirstTree("(tree T (check (< (+ 1 2.0) 5.0)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var lt = Assert.IsType<IrBinaryOp>(cond.Expr);
        var add = Assert.IsType<IrBinaryOp>(lt.Lhs);

        // 左辺（1）が IrConvert で float に変換される
        var convertedLhs = Assert.IsType<IrConvert>(add.Lhs);
        Assert.Equal(TypeRef.Float, convertedLhs.TargetType);
        Assert.Equal(TypeRef.Float, convertedLhs.TypeRef);
        var originalLhs = Assert.IsType<IrLiteral>(convertedLhs.Operand);
        Assert.Equal(1, originalLhs.Value);

        // 右辺（2.0）はそのまま
        var rhs = Assert.IsType<IrLiteral>(add.Rhs);
        Assert.Equal(2.0f, rhs.Value);

        // 加算結果は float
        Assert.Equal(TypeRef.Float, add.TypeRef);
    }

    [Fact]
    public void float_int混合比較でint側にIrConvertが挿入される()
    {
        // (+ 1.5 3) → 右辺 3(int) が IrConvert で float に昇格される
        var tree = LowerFirstTree("(tree T (check (< (+ 1.5 3) 5.0)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var lt = Assert.IsType<IrBinaryOp>(cond.Expr);
        var add = Assert.IsType<IrBinaryOp>(lt.Lhs);

        // 左辺（1.5）はそのまま
        Assert.IsType<IrLiteral>(add.Lhs);

        // 右辺（3）が IrConvert で float に変換される
        var convertedRhs = Assert.IsType<IrConvert>(add.Rhs);
        Assert.Equal(TypeRef.Float, convertedRhs.TargetType);
    }

    [Fact]
    public void 同一型同士ではIrConvertが挿入されない()
    {
        // (+ 1 2) → int + int なので IrConvert は不要
        var tree = LowerFirstTree("(tree T (check (< (+ 1 2) 5)))");

        var cond = Assert.IsType<IrCondition>(tree.Body[0]);
        var lt = Assert.IsType<IrBinaryOp>(cond.Expr);
        var add = Assert.IsType<IrBinaryOp>(lt.Lhs);

        // 両辺とも IrLiteral のまま（IrConvert なし）
        Assert.IsType<IrLiteral>(add.Lhs);
        Assert.IsType<IrLiteral>(add.Rhs);
        Assert.Equal(TypeRef.Int, add.TypeRef);
    }

    // ═══════════════════════════════════════════════
    //  10. MethodRef のセグメント分解
    // ═══════════════════════════════════════════════

    [Fact]
    public void 単一セグメントのアクションはthisのメソッドになる()
    {
        var tree = LowerFirstTree("(tree T (.Patrol))");

        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal("this", action.Method.DeclaringType);
        Assert.Equal("Patrol", action.Method.Name);
    }

    [Fact]
    public void 複数セグメントのアクションは先頭が宣言型になる()
    {
        var tree = LowerFirstTree("(tree T (.Nav.MoveTo))");

        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal("Nav", action.Method.DeclaringType);
        Assert.Equal("MoveTo", action.Method.Name);
    }

    // ═══════════════════════════════════════════════
    //  11. TypeRef の付与
    // ═══════════════════════════════════════════════

    [Fact]
    public void ノード系IRにはBtStatusのTypeRefが付与される()
    {
        var tree = LowerFirstTree("(tree T (select .A .B))");

        Assert.Equal(TypeRef.BtStatus, tree.TypeRef);
        var selector = Assert.IsType<IrSelector>(tree.Body[0]);
        Assert.Equal(TypeRef.BtStatus, selector.TypeRef);
    }

    [Fact]
    public void アクションにはBtStatusのTypeRefが付与される()
    {
        var tree = LowerFirstTree("(tree T .Patrol)");

        var action = Assert.IsType<IrAction>(tree.Body[0]);
        Assert.Equal(TypeRef.BtStatus, action.TypeRef);
    }

    // ═══════════════════════════════════════════════
    //  12. 複数ツリー
    // ═══════════════════════════════════════════════

    [Fact]
    public void 複数ツリーが全てIRに変換される()
    {
        var trees = LowerToIr("(tree A .X) (tree B .Y)");

        Assert.Equal(2, trees.Length);
        Assert.Equal("A", trees[0].Name);
        Assert.Equal("B", trees[1].Name);
    }

    // ═══════════════════════════════════════════════
    //  13. Pattern 1: Minimal の全パイプライン
    // ═══════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalが正しくIRに変換される()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;

        var trees = LowerToIr(source);
        Assert.Single(trees);

        var tree = trees[0];
        Assert.Equal("SimpleCombat", tree.Name);
        Assert.Single(tree.Body);

        // select > seq + action
        var selector = Assert.IsType<IrSelector>(tree.Body[0]);
        Assert.Equal(2, selector.Children.Length);

        // seq > condition + action
        var seq = Assert.IsType<IrSequence>(selector.Children[0]);
        Assert.Equal(2, seq.Children.Length);

        // condition: (< .Health 30)
        var condition = Assert.IsType<IrCondition>(seq.Children[0]);
        var binary = Assert.IsType<IrBinaryOp>(condition.Expr);
        Assert.Equal(BinaryOp.Lt, binary.Op);
        Assert.Equal(TypeRef.Bool, binary.TypeRef);

        var health = Assert.IsType<IrMemberLoad>(binary.Lhs);
        Assert.Equal("Health", health.Ref.Chain[0]);

        var lit30 = Assert.IsType<IrLiteral>(binary.Rhs);
        Assert.Equal(30, lit30.Value);

        // action: .Flee
        var flee = Assert.IsType<IrAction>(seq.Children[1]);
        Assert.Equal("Flee", flee.Method.Name);

        // action: .Patrol
        var patrol = Assert.IsType<IrAction>(selector.Children[1]);
        Assert.Equal("Patrol", patrol.Method.Name);
    }
}
