using System.Collections.Immutable;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// IrSerializer のユニットテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>IR ノードを直接構築して直列化結果を検証する</description></item>
///   <item><description>ソースからの end-to-end パイプラインでも検証する</description></item>
///   <item><description>S 式テキストのフォーマットが仕様通りであることを検証する</description></item>
/// </list>
/// </summary>
public class IrSerializerTests
{
    // ──────────────────────────────────────────────
    //  ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>ソースから IR を生成して直列化する。</summary>
    private static string SerializeFromSource(string source)
    {
        var (program, _) = Parser.Parse(source);
        var astTrees = CstToAstLowering.Lower(program);
        var irTrees = AstToIrLowering.Lower(astTrees);
        return IrSerializer.Serialize(irTrees);
    }

    /// <summary>引数なしアクションの IR ノードを構築する。</summary>
    private static IrAction Action(string name)
        => new(new MethodRef("this", name, ImmutableArray<string>.Empty),
               ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

    /// <summary>メンバーロード式を構築する。</summary>
    private static IrMemberLoad Member(params string[] chain)
        => new(new MemberRef(chain.ToImmutableArray())) { TypeRef = new TypeRef("unknown") };

    /// <summary>整数リテラル式を構築する。</summary>
    private static IrLiteral IntLit(int value)
        => new(value, LiteralKind.Int) { TypeRef = TypeRef.Int };

    /// <summary>浮動小数点リテラル式を構築する。</summary>
    private static IrLiteral FloatLit(float value)
        => new(value, LiteralKind.Float) { TypeRef = TypeRef.Float };

    // ═══════════════════════════════════════════════
    //  1. 最小のノード直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void アクションノードが正しく直列化される()
    {
        var node = Action("Patrol");

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-action", result);
        Assert.Contains("\"this\"", result);
        Assert.Contains("\"Patrol\"", result);
        Assert.Contains("Crisp.Runtime.BtStatus", result);
    }

    [Fact]
    public void 条件ノードが正しく直列化される()
    {
        var node = new IrCondition(Member("IsAlive"));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-condition", result);
        Assert.Contains("ir-member-load", result);
        Assert.Contains("\"IsAlive\"", result);
    }

    // ═══════════════════════════════════════════════
    //  2. 式ノードの直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void 整数リテラルが正しく直列化される()
    {
        var node = IntLit(42);

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-literal", result);
        Assert.Contains("42", result);
        Assert.Contains(":int", result);
    }

    [Fact]
    public void 浮動小数点リテラルが正しく直列化される()
    {
        var node = FloatLit(2.5f);

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-literal", result);
        Assert.Contains("2.5", result);
        Assert.Contains(":float", result);
    }

    [Fact]
    public void boolリテラルが正しく直列化される()
    {
        var node = new IrLiteral(true, LiteralKind.Bool) { TypeRef = TypeRef.Bool };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("true", result);
        Assert.Contains(":bool", result);
    }

    [Fact]
    public void 文字列リテラルが正しく直列化される()
    {
        var node = new IrLiteral("hello", LiteralKind.String) { TypeRef = TypeRef.String };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("\"hello\"", result);
        Assert.Contains(":string", result);
    }

    [Fact]
    public void nullリテラルが正しく直列化される()
    {
        var node = new IrLiteral(null, LiteralKind.Null) { TypeRef = new TypeRef("null") };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("null", result);
        Assert.Contains(":null", result);
    }

    [Fact]
    public void enumリテラルが正しく直列化される()
    {
        var node = new IrLiteral(
            new EnumRef("AIState", "Combat"), LiteralKind.Enum)
        { TypeRef = new TypeRef("AIState") };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("::AIState.Combat", result);
        Assert.Contains(":enum", result);
    }

    // ═══════════════════════════════════════════════
    //  3. 二項・単項・論理式の直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void 二項演算が正しく直列化される()
    {
        var node = new IrBinaryOp(BinaryOp.Lt, Member("Health"), IntLit(30))
        { TypeRef = TypeRef.Bool };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-binary-op", result);
        Assert.Contains(":lt", result);
        Assert.Contains("ir-member-load", result);
        Assert.Contains("ir-literal", result);
    }

    [Fact]
    public void 単項演算が正しく直列化される()
    {
        var node = new IrUnaryOp(UnaryOp.Not, Member("IsAlive"))
        { TypeRef = TypeRef.Bool };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-unary-op", result);
        Assert.Contains(":not", result);
    }

    [Fact]
    public void 論理演算が正しく直列化される()
    {
        var node = new IrLogicOp(
            LogicOp.And,
            ImmutableArray.Create<IrExpr>(Member("A"), Member("B")))
        { TypeRef = TypeRef.Bool };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-logic-op", result);
        Assert.Contains(":and", result);
    }

    // ═══════════════════════════════════════════════
    //  4. IrConvert の直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void IrConvertが正しく直列化される()
    {
        var node = new IrConvert(IntLit(30), TypeRef.Float)
        { TypeRef = TypeRef.Float };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-convert", result);
        Assert.Contains(":to \"float\"", result);
    }

    // ═══════════════════════════════════════════════
    //  5. メソッド呼び出し式の直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void メソッド呼び出し式が正しく直列化される()
    {
        var node = new IrCall(
            new MethodRef("this", "DistanceTo", ImmutableArray<string>.Empty),
            ImmutableArray.Create<IrExpr>(Member("Target")))
        { TypeRef = new TypeRef("unknown") };

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-call", result);
        Assert.Contains("\"this\"", result);
        Assert.Contains("\"DistanceTo\"", result);
    }

    // ═══════════════════════════════════════════════
    //  6. 複合ノードの直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void セレクターの直列化で子がインデントされる()
    {
        var node = new IrSelector(ImmutableArray.Create<IrNode>(
            Action("A"), Action("B")));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("(ir-selector", result);
        Assert.Contains("  (ir-action", result);
    }

    [Fact]
    public void パラレルの直列化でポリシーが含まれる()
    {
        var node = new IrParallel(
            new ParallelPolicy.Any(),
            ImmutableArray.Create<IrNode>(Action("A"), Action("B")));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-parallel :any", result);
    }

    [Fact]
    public void パラレルNポリシーの直列化でカウントが含まれる()
    {
        var node = new IrParallel(
            new ParallelPolicy.N(2),
            ImmutableArray.Create<IrNode>(Action("A"), Action("B"), Action("C")));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-parallel :n 2", result);
    }

    // ═══════════════════════════════════════════════
    //  7. デコレータの直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void ガードの直列化で条件とボディが含まれる()
    {
        var node = new IrGuard(Member("IsAlive"), Action("Patrol"));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-guard", result);
        Assert.Contains("ir-member-load", result);
        Assert.Contains("ir-action", result);
    }

    [Fact]
    public void リピートの直列化で回数が含まれる()
    {
        var node = new IrRepeat(3, Action("A"));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-repeat 3", result);
    }

    [Fact]
    public void タイムアウトの直列化で秒数が含まれる()
    {
        var node = new IrTimeout(5.0f, Action("A"));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-timeout 5.0", result);
    }

    [Fact]
    public void クールダウンの直列化で秒数が含まれる()
    {
        var node = new IrCooldown(2.5f, Action("A"));

        var result = IrSerializer.Serialize(node);

        Assert.Contains("ir-cooldown 2.5", result);
    }

    // ═══════════════════════════════════════════════
    //  8. ツリーの直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void ツリーの直列化で名前が含まれる()
    {
        var tree = new IrTree("Combat", ImmutableArray.Create<IrNode>(Action("Patrol")));

        var result = IrSerializer.Serialize(tree);

        Assert.Contains("ir-tree \"Combat\"", result);
    }

    // ═══════════════════════════════════════════════
    //  9. End-to-End: ソースからの直列化
    // ═══════════════════════════════════════════════

    [Fact]
    public void Pattern1_Minimalのソースが正しく直列化される()
    {
        var source = """
            (tree SimpleCombat
              (select
                (seq
                  (check (< .Health 30))
                  (.Flee))
                (.Patrol)))
            """;

        var result = SerializeFromSource(source);

        // ツリー名
        Assert.Contains("ir-tree \"SimpleCombat\"", result);

        // セレクター > シーケンス
        Assert.Contains("ir-selector", result);
        Assert.Contains("ir-sequence", result);

        // 条件: (< .Health 30)
        Assert.Contains("ir-condition", result);
        Assert.Contains("ir-binary-op :lt", result);
        Assert.Contains("\"Health\"", result);
        Assert.Contains("ir-literal 30 :int", result);

        // アクション: Flee, Patrol
        Assert.Contains("\"Flee\"", result);
        Assert.Contains("\"Patrol\"", result);
    }

    [Fact]
    public void 複数ツリーの直列化で空行で区切られる()
    {
        var source = "(tree A .X) (tree B .Y)";

        var result = SerializeFromSource(source);

        // 両方のツリーが含まれる
        Assert.Contains("ir-tree \"A\"", result);
        Assert.Contains("ir-tree \"B\"", result);
    }
}
