using CsCheck;

namespace Crisp.Syntax.Tests;

/// <summary>
/// CsCheck を使ったプロパティベーステスト（アーキテクチャ仕様 Section 10.3）。
///
/// 核心的な不変条件:
/// <c>source == Parser.Parse(source).Program.ToFullString()</c>
/// すなわち、ロスレス CST のラウンドトリップ性を検証する。
///
/// <para>
/// ランダム生成は S式の文法に従うカスタムジェネレータを使用する。
/// これにより「パーサーが正常にパースできる有効なソース」のみが生成され、
/// パーサーのエラー回復パスではなく、正常パスでの完全なラウンドトリップが検証される。
/// </para>
///
/// <para>
/// テスト方針（t-wada 式）:
/// <list type="number">
///   <item><description>最小のリーフ式からラウンドトリップを検証する</description></item>
///   <item><description>段階的に複合ノード、デコレータ、ツリーへと複雑さを増す</description></item>
///   <item><description>トリビア（空白・コメント）の保持も検証する</description></item>
///   <item><description>最後にプログラム全体のランダム生成でラウンドトリップを検証する</description></item>
/// </list>
/// </para>
/// </summary>
public class PropertyBasedRoundTripTests
{
    // ═══════════════════════════════════════════════════════════
    //  ジェネレータ定義: アトム（リーフ式）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// メンバーアクセス名ジェネレータ。
    /// パーサーが認識する有効なメンバー名のみを生成する。
    /// </summary>
    private static readonly Gen<string> GenMemberName =
        Gen.OneOfConst("Patrol", "Flee", "Attack", "Health", "IsVisible", "Target", "Speed", "Do");

    /// <summary>
    /// メンバーアクセス式（<c>.Foo</c>）ジェネレータ。
    /// </summary>
    private static readonly Gen<string> GenMemberAccess =
        GenMemberName.Select(name => $".{name}");

    /// <summary>
    /// 整数リテラルジェネレータ。非負整数のみ（単項マイナスは別途処理）。
    /// </summary>
    private static readonly Gen<string> GenIntLiteral =
        Gen.Int[0, 999].Select(i => i.ToString());

    /// <summary>
    /// 浮動小数点リテラルジェネレータ。<c>N.M</c> 形式。
    /// </summary>
    private static readonly Gen<string> GenFloatLiteral =
        Gen.Select(Gen.Int[0, 99], Gen.Int[1, 9]).Select((whole, frac) => $"{whole}.{frac}");

    /// <summary>
    /// 文字列リテラルジェネレータ。安全な ASCII 文字列のみ。
    /// </summary>
    private static readonly Gen<string> GenStringLiteral =
        Gen.OneOfConst("\"hello\"", "\"world\"", "\"test\"", "\"\"");

    /// <summary>
    /// enum リテラルジェネレータ。<c>::TypeName.MemberName</c> 形式。
    /// </summary>
    private static readonly Gen<string> GenEnumLiteral =
        Gen.OneOfConst("::AIState.Combat", "::AIState.Patrol", "::Direction.Left");

    /// <summary>
    /// リーフ式ジェネレータ。全てのリテラル種別とメンバーアクセスを含む。
    /// </summary>
    private static readonly Gen<string> GenLeafExpr =
        Gen.Frequency(
            (4, GenMemberAccess),
            (2, GenIntLiteral),
            (1, GenFloatLiteral),
            (1, Gen.OneOfConst("true", "false")),
            (1, Gen.OneOfConst("null")),
            (1, GenStringLiteral),
            (1, GenEnumLiteral));

    // ═══════════════════════════════════════════════════════════
    //  ジェネレータ定義: 式（再帰的）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 二項演算子ジェネレータ。
    /// </summary>
    /// <summary>
    /// 二項演算子ジェネレータ。
    /// Lexer が認識する演算子トークンに対応:
    /// <c>=</c> は等値（<c>==</c> ではない）、<c>!=</c> は不等値。
    /// </summary>
    private static readonly Gen<string> GenBinaryOp =
        Gen.OneOfConst("<", ">", "<=", ">=", "=", "!=", "+", "-", "*", "/", "%");

    /// <summary>
    /// 式ジェネレータ。再帰的に二項式・単項式・論理式を生成する。
    /// 深さ制限により無限再帰を防止する。
    /// </summary>
    private static Gen<string> GenExpr(int depth)
    {
        if (depth <= 0) return GenLeafExpr;

        var inner = GenExpr(depth - 1);

        var genBinary = Gen.Select(GenBinaryOp, inner, inner)
            .Select((op, lhs, rhs) => $"({op} {lhs} {rhs})");

        var genUnary = inner.Select(operand => $"(not {operand})");

        var genLogicAnd = inner.Array[2, 3]
            .Select(operands => $"(and {string.Join(" ", operands)})");

        var genLogicOr = inner.Array[2, 3]
            .Select(operands => $"(or {string.Join(" ", operands)})");

        return Gen.Frequency(
            (4, GenLeafExpr),
            (2, genBinary),
            (1, genUnary),
            (1, genLogicAnd),
            (1, genLogicOr));
    }

    // ═══════════════════════════════════════════════════════════
    //  ジェネレータ定義: ビヘイビアツリーノード（再帰的）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ビヘイビアツリーノードのジェネレータ。
    /// <c>Gen.Recursive</c> を使用して、深さに応じて再帰的にノードを生成する。
    ///
    /// 深さ 0: リーフ（メンバーアクセス = アクション呼び出し）
    /// 深さ 1+: 複合ノード（select, seq）、デコレータ（check, invert, guard, if, repeat, timeout, cooldown, while）
    /// </summary>
    private static Gen<string> GenNode(int depth)
    {
        if (depth <= 0) return GenMemberAccess;

        var self = GenNode(depth - 1);

        // 複合ノード: (select child1 child2 ...)
        var genSelect = self.Array[2, 4]
            .Select(children => $"(select {string.Join(" ", children)})");

        // 複合ノード: (seq child1 child2 ...)
        var genSeq = self.Array[2, 4]
            .Select(children => $"(seq {string.Join(" ", children)})");

        // デコレータ: (check expr)
        var genCheck = GenExpr(1)
            .Select(expr => $"(check {expr})");

        // デコレータ: (invert child)
        var genInvert = self
            .Select(child => $"(invert {child})");

        // デコレータ: (guard expr child)
        var genGuard = Gen.Select(GenExpr(1), self)
            .Select((cond, body) => $"(guard {cond} {body})");

        // デコレータ: (if cond then)
        var genIf = Gen.Select(GenExpr(1), self)
            .Select((cond, then) => $"(if {cond} {then})");

        // デコレータ: (if cond then else)
        var genIfElse = Gen.Select(GenExpr(1), self, self)
            .Select((cond, then, @else) => $"(if {cond} {then} {@else})");

        // デコレータ: (repeat N child)
        var genRepeat = Gen.Select(Gen.Int[1, 10], self)
            .Select((n, child) => $"(repeat {n} {child})");

        // デコレータ: (timeout N child)
        var genTimeout = Gen.Select(GenFloatLiteral, self)
            .Select((sec, child) => $"(timeout {sec} {child})");

        // デコレータ: (cooldown N child)
        var genCooldown = Gen.Select(GenFloatLiteral, self)
            .Select((sec, child) => $"(cooldown {sec} {child})");

        // デコレータ: (while expr child)
        var genWhile = Gen.Select(GenExpr(1), self)
            .Select((cond, body) => $"(while {cond} {body})");

        // パラレル: (parallel :policy child1 child2 ...)
        var genParallel = Gen.Select(
                Gen.OneOfConst(":any", ":all"),
                self.Array[2, 3])
            .Select((policy, children) => $"(parallel {policy} {string.Join(" ", children)})");

        return Gen.Frequency(
            (5, GenMemberAccess),
            (2, genSelect),
            (2, genSeq),
            (2, genCheck),
            (1, genInvert),
            (1, genGuard),
            (1, genIf),
            (1, genIfElse),
            (1, genRepeat),
            (1, genTimeout),
            (1, genCooldown),
            (1, genWhile),
            (1, genParallel));
    }

    /// <summary>
    /// ツリー名ジェネレータ。
    /// </summary>
    private static readonly Gen<string> GenTreeName =
        Gen.OneOfConst("T", "Combat", "Patrol", "SimpleAI", "Guard", "Root");

    /// <summary>
    /// ツリー定義ジェネレータ。<c>(tree Name body...)</c> 形式。
    /// </summary>
    private static Gen<string> GenTree(int depth) =>
        Gen.Select(GenTreeName, GenNode(depth).Array[1, 3])
            .Select((name, body) => $"(tree {name} {string.Join(" ", body)})");

    /// <summary>
    /// プログラム全体ジェネレータ。1〜2 個のツリー定義を含む。
    /// </summary>
    private static readonly Gen<string> GenProgram =
        GenTree(2).Array[1, 2]
            .Select(trees => string.Join("\n", trees));

    // ═══════════════════════════════════════════════════════════
    //  トリビアジェネレータ
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 空白トリビア付きプログラムジェネレータ。
    /// ツリー定義間に改行やコメントを挿入して、トリビアの保持を検証する。
    /// </summary>
    private static readonly Gen<string> GenProgramWithTrivia =
        Gen.Select(
                Gen.OneOfConst("", " ", "  "),
                GenTree(2).Array[1, 2],
                Gen.OneOfConst("", "\n"))
            .Select((leadingWs, trees, trailingWs) =>
                $"{leadingWs}{string.Join("\n", trees)}{trailingWs}");

    // ═══════════════════════════════════════════════════════════
    //  1. リーフ式のラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void リーフ式を含むツリーのラウンドトリップが成り立つ()
    {
        var gen = GenLeafExpr.Select(expr => $"(tree T (check {expr}))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 200);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 式のラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 二項式のラウンドトリップが成り立つ()
    {
        var gen = GenExpr(2).Select(expr => $"(tree T (check {expr}))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 200);
    }

    [Fact]
    public void 論理式のラウンドトリップが成り立つ()
    {
        var gen = GenExpr(1).Array[2, 4]
            .Select(exprs => $"(tree T (check (and {string.Join(" ", exprs)})))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 100);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 複合ノードのラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void セレクターのラウンドトリップが成り立つ()
    {
        var gen = GenMemberAccess.Array[2, 5]
            .Select(members => $"(tree T (select {string.Join(" ", members)}))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 100);
    }

    [Fact]
    public void シーケンスのラウンドトリップが成り立つ()
    {
        var gen = GenMemberAccess.Array[2, 5]
            .Select(members => $"(tree T (seq {string.Join(" ", members)}))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 100);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. デコレータのラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void デコレータノードのラウンドトリップが成り立つ()
    {
        var gen = GenNode(2).Select(node => $"(tree T {node})");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 300);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. ツリー定義のラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 単一ツリーのラウンドトリップが成り立つ()
    {
        Check.Sample(GenTree(2), source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 200);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. プログラム全体のラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void プログラム全体のラウンドトリップが成り立つ()
    {
        Check.Sample(GenProgram, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 300);
    }

    [Fact]
    public void トリビア付きプログラムのラウンドトリップが成り立つ()
    {
        Check.Sample(GenProgramWithTrivia, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 200);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. コメント付きのラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void コメント付きソースのラウンドトリップが成り立つ()
    {
        var genComment = Gen.OneOfConst(
            ";; combat behavior\n",
            ";; patrol logic\n",
            ";; guard check\n");

        var gen = Gen.Select(genComment, GenTree(1))
            .Select((comment, tree) => $"{comment}{tree}");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 100);
    }

    // ═══════════════════════════════════════════════════════════
    //  8. パラレルノードのラウンドトリップ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void パラレルノードのラウンドトリップが成り立つ()
    {
        var gen = Gen.Select(
                Gen.OneOfConst(":any", ":all"),
                GenMemberAccess.Array[2, 4])
            .Select((policy, children) =>
                $"(tree T (parallel {policy} {string.Join(" ", children)}))");

        Check.Sample(gen, source =>
        {
            var (program, _) = Parser.Parse(source);
            Assert.Equal(source, program.ToFullString());
        }, iter: 100);
    }
}
