using System.Collections.Immutable;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Tests;

/// <summary>
/// <see cref="IrDeserializer"/> の t-wada 式 TDD テスト。
///
/// IrSerializer との対称性（ラウンドトリップ）を段階的に検証する:
/// 1. 最小ノード（リテラル、メンバーロード）
/// 2. 式ノード（二項演算、単項演算、論理演算、型変換）
/// 3. 制御フローノード（条件、アクション、セレクター、シーケンス）
/// 4. デコレータノード（ガード、if、反転、リピート、タイムアウト、クールダウン、while）
/// 5. 複合ノード（パラレル、ツリー、メソッド呼び出し）
/// 6. ラウンドトリップ（serialize → deserialize → serialize の等価性）
/// </summary>
public class IrDeserializerTests
{
    // ═══════════════════════════════════════════════════════════
    //  1. 最小式ノード
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 整数リテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal 42 :int)");

        var lit = Assert.IsType<IrLiteral>(node);
        Assert.Equal(42, lit.Value);
        Assert.Equal(LiteralKind.Int, lit.Kind);
    }

    [Fact]
    public void 浮動小数点リテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal 3.14 :float)");

        var lit = Assert.IsType<IrLiteral>(node);
        Assert.Equal(3.14f, (float)lit.Value!);
        Assert.Equal(LiteralKind.Float, lit.Kind);
    }

    [Fact]
    public void 真偽値リテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal true :bool)");

        var lit = Assert.IsType<IrLiteral>(node);
        Assert.Equal(true, lit.Value);
        Assert.Equal(LiteralKind.Bool, lit.Kind);
    }

    [Fact]
    public void 文字列リテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal \"hello\" :string)");

        var lit = Assert.IsType<IrLiteral>(node);
        Assert.Equal("hello", lit.Value);
        Assert.Equal(LiteralKind.String, lit.Kind);
    }

    [Fact]
    public void nullリテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal null :null)");

        var lit = Assert.IsType<IrLiteral>(node);
        Assert.Null(lit.Value);
        Assert.Equal(LiteralKind.Null, lit.Kind);
    }

    [Fact]
    public void enumリテラルを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize("(ir-literal ::AIState.Combat :enum)");

        var lit = Assert.IsType<IrLiteral>(node);
        var enumRef = Assert.IsType<EnumRef>(lit.Value);
        Assert.Equal("AIState", enumRef.TypeName);
        Assert.Equal("Combat", enumRef.MemberName);
    }

    [Fact]
    public void メンバーロードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-member-load (\"Health\") :type \"System.Single\")");

        var mem = Assert.IsType<IrMemberLoad>(node);
        Assert.Single(mem.Ref.Chain);
        Assert.Equal("Health", mem.Ref.Chain[0]);
        Assert.Equal("System.Single", mem.TypeRef.FullName);
    }

    [Fact]
    public void メンバーチェーンを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-member-load (\"Target\" \"Position\" \"x\") :type \"System.Single\")");

        var mem = Assert.IsType<IrMemberLoad>(node);
        Assert.Equal(3, mem.Ref.Chain.Length);
        Assert.Equal("Target", mem.Ref.Chain[0]);
        Assert.Equal("Position", mem.Ref.Chain[1]);
        Assert.Equal("x", mem.Ref.Chain[2]);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 式ノード（演算子、型変換）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 二項演算を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-binary-op :lt (ir-literal 10 :int) (ir-literal 20 :int))");

        var bin = Assert.IsType<IrBinaryOp>(node);
        Assert.Equal(BinaryOp.Lt, bin.Op);
        Assert.IsType<IrLiteral>(bin.Lhs);
        Assert.IsType<IrLiteral>(bin.Rhs);
    }

    [Fact]
    public void 単項演算を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-unary-op :not (ir-literal true :bool))");

        var un = Assert.IsType<IrUnaryOp>(node);
        Assert.Equal(UnaryOp.Not, un.Op);
    }

    [Fact]
    public void 論理演算を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-logic-op :and (ir-literal true :bool) (ir-literal false :bool))");

        var log = Assert.IsType<IrLogicOp>(node);
        Assert.Equal(LogicOp.And, log.Op);
        Assert.Equal(2, log.Operands.Length);
    }

    [Fact]
    public void 暗黙型変換を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-convert (ir-literal 30 :int) :to \"float\")");

        var conv = Assert.IsType<IrConvert>(node);
        var lit = Assert.IsType<IrLiteral>(conv.Operand);
        Assert.Equal(30, lit.Value);
        Assert.Equal("float", conv.TargetType.FullName);
    }

    [Fact]
    public void メソッド呼び出し式を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-call \"EnemyAI\" \"DistanceTo\" ((ir-member-load (\"Target\") :type \"Entity\")) :type \"System.Single\")");

        var call = Assert.IsType<IrCall>(node);
        Assert.Equal("EnemyAI", call.Method.DeclaringType);
        Assert.Equal("DistanceTo", call.Method.Name);
        Assert.Single(call.Args);
        Assert.Equal("System.Single", call.TypeRef.FullName);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 制御フローノード
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 条件ノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-condition\n  (ir-literal true :bool))");

        var cond = Assert.IsType<IrCondition>(node);
        Assert.IsType<IrLiteral>(cond.Expr);
    }

    [Fact]
    public void アクションノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-action \"this\" \"Patrol\" () :type \"Crisp.Runtime.BtStatus\")");

        var act = Assert.IsType<IrAction>(node);
        Assert.Equal("this", act.Method.DeclaringType);
        Assert.Equal("Patrol", act.Method.Name);
        Assert.Empty(act.Args);
    }

    [Fact]
    public void セレクターノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-selector\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"B\" () :type \"Crisp.Runtime.BtStatus\"))");

        var sel = Assert.IsType<IrSelector>(node);
        Assert.Equal(2, sel.Children.Length);
    }

    [Fact]
    public void シーケンスノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-sequence\n  (ir-condition\n    (ir-literal true :bool))\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var seq = Assert.IsType<IrSequence>(node);
        Assert.Equal(2, seq.Children.Length);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. デコレータノード
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ガードノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-guard\n  (ir-literal true :bool)\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var guard = Assert.IsType<IrGuard>(node);
        Assert.IsType<IrLiteral>(guard.Condition);
        Assert.IsType<IrAction>(guard.Body);
    }

    [Fact]
    public void ifノードをelse付きで逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-if\n  (ir-literal true :bool)\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"B\" () :type \"Crisp.Runtime.BtStatus\"))");

        var ifNode = Assert.IsType<IrIf>(node);
        Assert.NotNull(ifNode.Else);
    }

    [Fact]
    public void ifノードをelseなしで逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-if\n  (ir-literal true :bool)\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\"))");

        var ifNode = Assert.IsType<IrIf>(node);
        Assert.Null(ifNode.Else);
    }

    [Fact]
    public void 反転ノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-invert\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var inv = Assert.IsType<IrInvert>(node);
        Assert.IsType<IrAction>(inv.Child);
    }

    [Fact]
    public void リピートノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-repeat 3\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var rep = Assert.IsType<IrRepeat>(node);
        Assert.Equal(3, rep.Count);
    }

    [Fact]
    public void タイムアウトノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-timeout 5.0\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var tmo = Assert.IsType<IrTimeout>(node);
        Assert.Equal(5.0f, tmo.Seconds);
    }

    [Fact]
    public void クールダウンノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-cooldown 2.5\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var cld = Assert.IsType<IrCooldown>(node);
        Assert.Equal(2.5f, cld.Seconds);
    }

    [Fact]
    public void whileノードを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-while\n  (ir-literal true :bool)\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))");

        var whl = Assert.IsType<IrWhile>(node);
        Assert.IsType<IrLiteral>(whl.Condition);
        Assert.IsType<IrAction>(whl.Body);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. パラレル・ツリー
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void パラレルAnyポリシーを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-parallel :any\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"B\" () :type \"Crisp.Runtime.BtStatus\"))");

        var par = Assert.IsType<IrParallel>(node);
        Assert.IsType<ParallelPolicy.Any>(par.Policy);
        Assert.Equal(2, par.Children.Length);
    }

    [Fact]
    public void パラレルNポリシーを逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-parallel :n 2\n  (ir-action \"this\" \"A\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"B\" () :type \"Crisp.Runtime.BtStatus\")\n  (ir-action \"this\" \"C\" () :type \"Crisp.Runtime.BtStatus\"))");

        var par = Assert.IsType<IrParallel>(node);
        var nPolicy = Assert.IsType<ParallelPolicy.N>(par.Policy);
        Assert.Equal(2, nPolicy.Count);
        Assert.Equal(3, par.Children.Length);
    }

    [Fact]
    public void ツリー定義を逆直列化できる()
    {
        var node = IrDeserializer.Deserialize(
            "(ir-tree \"SimpleCombat\"\n  (ir-action \"this\" \"Patrol\" () :type \"Crisp.Runtime.BtStatus\"))");

        var tree = Assert.IsType<IrTree>(node);
        Assert.Equal("SimpleCombat", tree.Name);
        Assert.Single(tree.Body);
    }

    [Fact]
    public void 複数ツリーを逆直列化できる()
    {
        var text = "(ir-tree \"A\"\n  (ir-action \"this\" \"Do\" () :type \"Crisp.Runtime.BtStatus\"))\n\n(ir-tree \"B\"\n  (ir-action \"this\" \"Run\" () :type \"Crisp.Runtime.BtStatus\"))";

        var trees = IrDeserializer.DeserializeTrees(text);

        Assert.Equal(2, trees.Length);
        Assert.Equal("A", trees[0].Name);
        Assert.Equal("B", trees[1].Name);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. ラウンドトリップ（Serialize → Deserialize → Serialize）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void アクションのラウンドトリップが等価である()
    {
        var original = new IrAction(
            new MethodRef("this", "Patrol", ImmutableArray<string>.Empty),
            ImmutableArray<IrExpr>.Empty)
        { TypeRef = TypeRef.BtStatus };

        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        Assert.Equal(text, roundTripped);
    }

    [Fact]
    public void 条件付きシーケンスのラウンドトリップが等価である()
    {
        var original = new IrSequence([
            new IrCondition(
                new IrBinaryOp(BinaryOp.Lt,
                    new IrMemberLoad(new MemberRef(ImmutableArray.Create("Health")))
                        { TypeRef = new TypeRef("System.Single") },
                    new IrConvert(
                        new IrLiteral(30, LiteralKind.Int) { TypeRef = TypeRef.Int },
                        TypeRef.Float)
                    { TypeRef = TypeRef.Float })
                { TypeRef = TypeRef.Bool }),
            new IrAction(
                new MethodRef("this", "Flee", ImmutableArray<string>.Empty),
                ImmutableArray<IrExpr>.Empty)
            { TypeRef = TypeRef.BtStatus },
        ]);

        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        Assert.Equal(text, roundTripped);
    }

    [Fact]
    public void ツリー定義のラウンドトリップが等価である()
    {
        var original = new IrTree("Combat", [
            new IrSelector([
                new IrSequence([
                    new IrCondition(
                        new IrBinaryOp(BinaryOp.Lt,
                            new IrMemberLoad(new MemberRef(ImmutableArray.Create("Health")))
                                { TypeRef = new TypeRef("System.Single") },
                            new IrConvert(
                                new IrLiteral(30, LiteralKind.Int) { TypeRef = TypeRef.Int },
                                TypeRef.Float)
                            { TypeRef = TypeRef.Float })
                        { TypeRef = TypeRef.Bool }),
                    new IrAction(
                        new MethodRef("this", "Flee", ImmutableArray<string>.Empty),
                        ImmutableArray<IrExpr>.Empty)
                    { TypeRef = TypeRef.BtStatus },
                ]),
                new IrAction(
                    new MethodRef("this", "Patrol", ImmutableArray<string>.Empty),
                    ImmutableArray<IrExpr>.Empty)
                { TypeRef = TypeRef.BtStatus },
            ]),
        ]);

        var text = IrSerializer.Serialize(original);
        var deserialized = IrDeserializer.Deserialize(text);
        var roundTripped = IrSerializer.Serialize(deserialized);

        Assert.Equal(text, roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. エラーケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 不明なタグで例外が発生する()
    {
        Assert.Throws<FormatException>(() =>
            IrDeserializer.Deserialize("(ir-invalid)"));
    }

    [Fact]
    public void 空文字列で例外が発生する()
    {
        Assert.Throws<FormatException>(() =>
            IrDeserializer.Deserialize(""));
    }
}
