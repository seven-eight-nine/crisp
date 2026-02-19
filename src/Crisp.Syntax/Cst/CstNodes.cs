using System.Collections.Immutable;

namespace Crisp.Syntax.Cst;

// ═══════════════════════════════════════════════════════════
//  トップレベルノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// プログラム全体を表すルートノード。
/// 0個以上の <see cref="CstTree"/> を子に持つ。
/// </summary>
public sealed class CstProgram : CstNode
{
    /// <summary>ファイル内の全ツリー定義。</summary>
    public ImmutableArray<CstTree> Trees { get; }

    /// <summary>ファイル内の全デコレータ定義（F2: defdec）。</summary>
    public ImmutableArray<CstDefdec> Defdecs { get; }

    /// <summary>ファイル内の全マクロ定義（F10: defmacro）。</summary>
    public ImmutableArray<CstDefmacro> Defmacros { get; }

    /// <summary>ファイル内の全インポート宣言（F11: import）。</summary>
    public ImmutableArray<CstImport> Imports { get; }

    /// <summary>ファイル終端トークン（EndOfFile の LeadingTrivia に末尾の空白・コメントが付く）。</summary>
    public Token EndOfFile { get; }

    public CstProgram(ImmutableArray<CstTree> trees, Token endOfFile)
        : this(trees, ImmutableArray<CstDefdec>.Empty, ImmutableArray<CstDefmacro>.Empty,
               ImmutableArray<CstImport>.Empty, endOfFile) { }

    public CstProgram(ImmutableArray<CstTree> trees, ImmutableArray<CstDefdec> defdecs, Token endOfFile)
        : this(trees, defdecs, ImmutableArray<CstDefmacro>.Empty,
               ImmutableArray<CstImport>.Empty, endOfFile) { }

    public CstProgram(ImmutableArray<CstTree> trees, ImmutableArray<CstDefdec> defdecs,
        ImmutableArray<CstDefmacro> defmacros, Token endOfFile)
        : this(trees, defdecs, defmacros, ImmutableArray<CstImport>.Empty, endOfFile) { }

    public CstProgram(ImmutableArray<CstTree> trees, ImmutableArray<CstDefdec> defdecs,
        ImmutableArray<CstDefmacro> defmacros, ImmutableArray<CstImport> imports, Token endOfFile)
    {
        Trees = trees;
        Defdecs = defdecs;
        Defmacros = defmacros;
        Imports = imports;
        EndOfFile = endOfFile;
        foreach (var tree in trees) tree.Parent = this;
        foreach (var defdec in defdecs) defdec.Parent = this;
        foreach (var defmacro in defmacros) defmacro.Parent = this;
    }

    public override TextSpan Span => Trees.Length > 0
        ? new TextSpan(Trees[0].Span.Start, EndOfFile.Span.End - Trees[0].Span.Start)
        : new TextSpan(EndOfFile.Span.Start, 0);

    public override TextSpan FullSpan => Trees.Length > 0
        ? new TextSpan(Trees[0].FullSpan.Start, EndOfFile.Span.End - Trees[0].FullSpan.Start)
        : EndOfFile.Span;

    public override IEnumerable<CstNode> Children => Trees;

    public override IEnumerable<Token> DescendantTokens()
    {
        foreach (var tree in Trees)
            foreach (var token in tree.DescendantTokens())
                yield return token;
        foreach (var defdec in Defdecs)
            foreach (var token in defdec.DescendantTokens())
                yield return token;
        foreach (var defmacro in Defmacros)
            foreach (var token in defmacro.DescendantTokens())
                yield return token;
        foreach (var import in Imports)
            foreach (var token in import.DescendantTokens())
                yield return token;
        yield return EndOfFile;
    }
}

/// <summary>
/// ツリー定義 <c>(tree Name body...)</c> を表す CST ノード。
/// </summary>
public sealed class CstTree : CstNode
{
    public Token OpenParen { get; }
    public Token TreeKeyword { get; }
    public Token Name { get; }

    /// <summary>:blackboard 属性で指定されたブラックボード型名。未指定の場合は null。</summary>
    public Token? BlackboardType { get; }

    public ImmutableArray<CstNode> Body { get; }
    public Token CloseParen { get; }

    public CstTree(Token openParen, Token treeKeyword, Token name,
        ImmutableArray<CstNode> body, Token closeParen, Token? blackboardType = null)
    {
        OpenParen = openParen;
        TreeKeyword = treeKeyword;
        Name = name;
        BlackboardType = blackboardType;
        Body = body;
        CloseParen = closeParen;
        foreach (var node in body) node.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);

    public override IEnumerable<CstNode> Children => Body;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return TreeKeyword;
        yield return Name;
        foreach (var node in Body)
            foreach (var token in node.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  複合ノード（Composite）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// セレクターノード <c>(select child1 child2 ...)</c> を表す CST ノード。
/// 子ノードを順に評価し、最初に Success または Running を返すものを採用する。
/// </summary>
public sealed class CstSelect : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public ImmutableArray<CstNode> Children_ { get; }
    public Token CloseParen { get; }

    public CstSelect(Token openParen, Token keyword, ImmutableArray<CstNode> children, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Children_ = children;
        CloseParen = closeParen;
        foreach (var child in children) child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Children_;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var child in Children_)
            foreach (var token in child.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// シーケンスノード <c>(seq child1 child2 ...)</c> を表す CST ノード。
/// 子ノードを順に評価し、全て Success なら Success を返す。
/// </summary>
public sealed class CstSequence : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public ImmutableArray<CstNode> Children_ { get; }
    public Token CloseParen { get; }

    public CstSequence(Token openParen, Token keyword, ImmutableArray<CstNode> children, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Children_ = children;
        CloseParen = closeParen;
        foreach (var child in children) child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Children_;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var child in Children_)
            foreach (var token in child.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// パラレルノード <c>(parallel :policy [n] child1 child2 ...)</c> を表す CST ノード。
/// 全子ノードを同時に評価し、ポリシーに応じて結果を判定する。
/// </summary>
public sealed class CstParallel : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Policy { get; }
    public Token? PolicyArg { get; }
    public ImmutableArray<CstNode> Children_ { get; }
    public Token CloseParen { get; }

    public CstParallel(Token openParen, Token keyword, Token policy, Token? policyArg,
        ImmutableArray<CstNode> children, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Policy = policy;
        PolicyArg = policyArg;
        Children_ = children;
        CloseParen = closeParen;
        foreach (var child in children) child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Children_;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Policy;
        if (PolicyArg.HasValue) yield return PolicyArg.Value;
        foreach (var child in Children_)
            foreach (var token in child.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  デコレータノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// チェックノード <c>(check expr)</c> を表す CST ノード。
/// 式を評価し、true なら Success、false なら Failure を返す。
/// </summary>
public sealed class CstCheck : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Expr { get; }
    public Token CloseParen { get; }

    public CstCheck(Token openParen, Token keyword, CstNode expr, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Expr = expr;
        CloseParen = closeParen;
        expr.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Expr];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Expr.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// ガードノード <c>(guard condition body)</c> を表す CST ノード。
/// 条件が true なら body を評価し、false なら Failure を返す。
/// </summary>
public sealed class CstGuard : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Condition { get; }
    public CstNode Body { get; }
    public Token CloseParen { get; }

    public CstGuard(Token openParen, Token keyword, CstNode condition, CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Condition = condition;
        Body = body;
        CloseParen = closeParen;
        condition.Parent = this;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Condition, Body];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Condition.DescendantTokens())
            yield return token;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// if ノード <c>(if condition then [else])</c> を表す CST ノード。
/// </summary>
public sealed class CstIf : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Condition { get; }
    public CstNode Then { get; }
    public CstNode? Else { get; }
    public Token CloseParen { get; }

    public CstIf(Token openParen, Token keyword, CstNode condition, CstNode then, CstNode? @else, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Condition = condition;
        Then = then;
        Else = @else;
        CloseParen = closeParen;
        condition.Parent = this;
        then.Parent = this;
        if (@else != null) @else.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);

    public override IEnumerable<CstNode> Children
    {
        get
        {
            yield return Condition;
            yield return Then;
            if (Else != null) yield return Else;
        }
    }

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Condition.DescendantTokens())
            yield return token;
        foreach (var token in Then.DescendantTokens())
            yield return token;
        if (Else != null)
            foreach (var token in Else.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// 反転ノード <c>(invert child)</c> を表す CST ノード。
/// Success ↔ Failure を反転する。Running はそのまま。
/// </summary>
public sealed class CstInvert : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Child { get; }
    public Token CloseParen { get; }

    public CstInvert(Token openParen, Token keyword, CstNode child, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Child = child;
        CloseParen = closeParen;
        child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Child];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Child.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// リピートノード <c>(repeat count child)</c> を表す CST ノード。
/// 子ノードを指定回数繰り返し評価する。
/// </summary>
public sealed class CstRepeat : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Count { get; }
    public CstNode Child { get; }
    public Token CloseParen { get; }

    public CstRepeat(Token openParen, Token keyword, Token count, CstNode child, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Count = count;
        Child = child;
        CloseParen = closeParen;
        child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Child];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Count;
        foreach (var token in Child.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// タイムアウトノード <c>(timeout seconds child)</c> を表す CST ノード。
/// 指定秒数以内に子ノードが完了しない場合 Failure を返す。
/// </summary>
public sealed class CstTimeout : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Seconds { get; }
    public CstNode Child { get; }
    public Token CloseParen { get; }

    public CstTimeout(Token openParen, Token keyword, Token seconds, CstNode child, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Seconds = seconds;
        Child = child;
        CloseParen = closeParen;
        child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Child];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Seconds;
        foreach (var token in Child.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// クールダウンノード <c>(cooldown seconds child)</c> を表す CST ノード。
/// 子ノードが Success を返した後、指定秒数が経過するまで Failure を返す。
/// </summary>
public sealed class CstCooldown : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Seconds { get; }
    public CstNode Child { get; }
    public Token CloseParen { get; }

    public CstCooldown(Token openParen, Token keyword, Token seconds, CstNode child, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Seconds = seconds;
        Child = child;
        CloseParen = closeParen;
        child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Child];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Seconds;
        foreach (var token in Child.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// while ノード <c>(while condition body)</c> を表す CST ノード。
/// 条件が true の間、body を毎 tick 評価する。
/// </summary>
public sealed class CstWhile : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Condition { get; }
    public CstNode Body { get; }
    public Token CloseParen { get; }

    public CstWhile(Token openParen, Token keyword, CstNode condition, CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Condition = condition;
        Body = body;
        CloseParen = closeParen;
        condition.Parent = this;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Condition, Body];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Condition.DescendantTokens())
            yield return token;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  リアクティブノード（F1: Reactive Reevaluation）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// リアクティブノード <c>(reactive condition body)</c> を表す CST ノード。
/// 毎 tick 条件を再評価し、false になったら Running 中の子を中断する。
/// </summary>
public sealed class CstReactive : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public CstNode Condition { get; }
    public CstNode Body { get; }
    public Token CloseParen { get; }

    public CstReactive(Token openParen, Token keyword, CstNode condition, CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Condition = condition;
        Body = body;
        CloseParen = closeParen;
        condition.Parent = this;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Condition, Body];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var token in Condition.DescendantTokens())
            yield return token;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// リアクティブセレクターノード <c>(reactive-select child1 child2 ...)</c> を表す CST ノード。
/// 毎 tick 先頭から再評価し、上位が復活したら下位の Running 中の子を中断する。
/// </summary>
public sealed class CstReactiveSelect : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public ImmutableArray<CstNode> Children_ { get; }
    public Token CloseParen { get; }

    public CstReactiveSelect(Token openParen, Token keyword, ImmutableArray<CstNode> children, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Children_ = children;
        CloseParen = closeParen;
        foreach (var child in children) child.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Children_;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        foreach (var child in Children_)
            foreach (var token in child.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  呼び出し・メンバーアクセス
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 呼び出しノード <c>(.Member arg1 arg2 ...)</c> を表す CST ノード。
/// パーサー段階ではアクション呼び出しとメソッド呼び出し式を区別せず、
/// lowering 時に文脈で <c>AstActionCall</c> または <c>AstCallExpr</c> に変換する。
/// </summary>
public sealed class CstCall : CstNode
{
    public Token OpenParen { get; }
    public CstMemberAccess Member { get; }
    public ImmutableArray<CstNode> Args { get; }
    public Token CloseParen { get; }

    public CstCall(Token openParen, CstMemberAccess member, ImmutableArray<CstNode> args, Token closeParen)
    {
        OpenParen = openParen;
        Member = member;
        Args = args;
        CloseParen = closeParen;
        member.Parent = this;
        foreach (var arg in args) arg.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Member, .. Args];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        foreach (var token in Member.DescendantTokens())
            yield return token;
        foreach (var arg in Args)
            foreach (var token in arg.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// メンバーアクセス <c>.Foo.Bar.Baz</c> を表す CST ノード。
/// ドットトークンとセグメント（識別子）トークンを交互に保持する。
/// LSP でのホバー・補完のために各セグメントの個別 Span を参照可能。
/// </summary>
public sealed class CstMemberAccess : CstNode
{
    /// <summary>メンバーアクセストークン全体（レクサーが単一トークンとして認識したもの）。</summary>
    public Token Token { get; }

    /// <summary>
    /// セグメント名のリスト。<c>.Foo.Bar.Baz</c> → <c>["Foo", "Bar", "Baz"]</c>。
    /// CstMemberAccess のコンストラクタ内で分割される。
    /// </summary>
    public ImmutableArray<string> Segments { get; }

    public CstMemberAccess(Token token)
    {
        Token = token;
        // ".Foo.Bar.Baz" → ["Foo", "Bar", "Baz"]
        Segments = token.Text.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableArray();
    }

    public override TextSpan Span => Token.Span;

    public override TextSpan FullSpan
    {
        get
        {
            var start = Token.LeadingTrivia.Length > 0
                ? Token.LeadingTrivia[0].Span.Start
                : Token.Span.Start;
            var end = Token.TrailingTrivia.Length > 0
                ? Token.TrailingTrivia[^1].Span.End
                : Token.Span.End;
            return new TextSpan(start, end - start);
        }
    }

    public override IEnumerable<CstNode> Children => [];
    public override IEnumerable<Token> DescendantTokens() => [Token];
}

// ═══════════════════════════════════════════════════════════
//  式ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 二項式 <c>(op lhs rhs)</c> を表す CST ノード。
/// 算術演算・比較演算等。
/// </summary>
public sealed class CstBinaryExpr : CstNode
{
    public Token OpenParen { get; }
    public Token Operator { get; }
    public CstNode Lhs { get; }
    public CstNode Rhs { get; }
    public Token CloseParen { get; }

    public CstBinaryExpr(Token openParen, Token op, CstNode lhs, CstNode rhs, Token closeParen)
    {
        OpenParen = openParen;
        Operator = op;
        Lhs = lhs;
        Rhs = rhs;
        CloseParen = closeParen;
        lhs.Parent = this;
        rhs.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Lhs, Rhs];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Operator;
        foreach (var token in Lhs.DescendantTokens())
            yield return token;
        foreach (var token in Rhs.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// 単項式 <c>(op operand)</c> を表す CST ノード。
/// <c>(not expr)</c> や <c>(- expr)</c>（単項マイナス）。
/// </summary>
public sealed class CstUnaryExpr : CstNode
{
    public Token OpenParen { get; }
    public Token Operator { get; }
    public CstNode Operand { get; }
    public Token CloseParen { get; }

    public CstUnaryExpr(Token openParen, Token op, CstNode operand, Token closeParen)
    {
        OpenParen = openParen;
        Operator = op;
        Operand = operand;
        CloseParen = closeParen;
        operand.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Operand];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Operator;
        foreach (var token in Operand.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// 論理式 <c>(and/or expr expr+)</c> を表す CST ノード。
/// 2つ以上のオペランドを取る。
/// </summary>
public sealed class CstLogicExpr : CstNode
{
    public Token OpenParen { get; }
    public Token Operator { get; }
    public ImmutableArray<CstNode> Operands { get; }
    public Token CloseParen { get; }

    public CstLogicExpr(Token openParen, Token op, ImmutableArray<CstNode> operands, Token closeParen)
    {
        OpenParen = openParen;
        Operator = op;
        Operands = operands;
        CloseParen = closeParen;
        foreach (var operand in operands) operand.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Operands;

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Operator;
        foreach (var operand in Operands)
            foreach (var token in operand.DescendantTokens())
                yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  リテラルノード
// ═══════════════════════════════════════════════════════════

/// <summary>整数リテラル <c>42</c>, <c>-3</c>。</summary>
public sealed class CstIntLiteral : CstTokenNode
{
    public CstIntLiteral(Token token) : base(token) { }
}

/// <summary>浮動小数点リテラル <c>3.14</c>, <c>-0.5</c>。</summary>
public sealed class CstFloatLiteral : CstTokenNode
{
    public CstFloatLiteral(Token token) : base(token) { }
}

/// <summary>真偽リテラル <c>true</c>, <c>false</c>。</summary>
public sealed class CstBoolLiteral : CstTokenNode
{
    public CstBoolLiteral(Token token) : base(token) { }
}

/// <summary>文字列リテラル <c>"hello"</c>。</summary>
public sealed class CstStringLiteral : CstTokenNode
{
    public CstStringLiteral(Token token) : base(token) { }
}

/// <summary>null リテラル <c>null</c>。</summary>
public sealed class CstNullLiteral : CstTokenNode
{
    public CstNullLiteral(Token token) : base(token) { }
}

/// <summary>
/// enum リテラル <c>::TypeName.MemberName</c>。
/// </summary>
public sealed class CstEnumLiteral : CstTokenNode
{
    /// <summary>enum の型名。<c>::AIState.Combat</c> → <c>"AIState"</c>。</summary>
    public string TypeName { get; }

    /// <summary>enum のメンバー名。<c>::AIState.Combat</c> → <c>"Combat"</c>。</summary>
    public string MemberName { get; }

    public CstEnumLiteral(Token token) : base(token)
    {
        // "::AIState.Combat" → TypeName="AIState", MemberName="Combat"
        var text = token.Text[2..]; // "::" を除去
        var dotIndex = text.IndexOf('.');
        TypeName = dotIndex >= 0 ? text[..dotIndex] : text;
        MemberName = dotIndex >= 0 ? text[(dotIndex + 1)..] : "";
    }
}

// ═══════════════════════════════════════════════════════════
//  Blackboard アクセス（F3: Typed Blackboard）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ブラックボードアクセス式 <c>$.Foo.Bar</c> を表す CST ノード。
/// <c>$</c> プレフィクスで始まるメンバーチェーンは、context ではなく blackboard オブジェクトへのアクセスとなる。
/// </summary>
public sealed class CstBlackboardAccess : CstTokenNode
{
    /// <summary>
    /// ブラックボードメンバーパスのセグメント配列。
    /// <c>$.Foo.Bar</c> → <c>["Foo", "Bar"]</c>。
    /// </summary>
    public ImmutableArray<string> Segments { get; }

    public CstBlackboardAccess(Token token) : base(token)
    {
        // "$.Foo.Bar" → ["Foo", "Bar"]
        var text = token.Text.Substring(1); // $ を除去
        Segments = text.Split(new[] { '.' }, System.StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableArray();
    }
}

// ═══════════════════════════════════════════════════════════
//  Multi-Tree ノード（F11: Multi-Tree & Cross-Reference）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ツリー参照ノード <c>(ref TreeName)</c> を表す CST ノード。
/// 別のツリー定義を参照し、メソッド呼び出しとして展開される。
/// </summary>
public sealed class CstRef : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Name { get; }
    public Token CloseParen { get; }

    public CstRef(Token openParen, Token keyword, Token name, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Name = name;
        CloseParen = closeParen;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Name;
        yield return CloseParen;
    }
}

/// <summary>
/// インポートノード <c>(import "path")</c> を表す CST ノード。
/// 別ファイルのツリー定義を現在のスコープに取り込む。
/// </summary>
public sealed class CstImport : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Path { get; }
    public Token CloseParen { get; }

    public CstImport(Token openParen, Token keyword, Token path, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Path = path;
        CloseParen = closeParen;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Path;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  User-Defined Decorators（F2: defdec）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// デコレータ定義ノード <c>(defdec name (params...) body)</c> を表す CST ノード。
/// DSL側でカスタムデコレータを定義する。body 内に <c>&lt;body&gt;</c> プレースホルダを含む。
/// </summary>
public sealed class CstDefdec : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Name { get; }
    public Token ParamsOpen { get; }
    public ImmutableArray<Token> Parameters { get; }
    public Token ParamsClose { get; }
    public CstNode Body { get; }
    public Token CloseParen { get; }

    public CstDefdec(Token openParen, Token keyword, Token name,
        Token paramsOpen, ImmutableArray<Token> parameters, Token paramsClose,
        CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Name = name;
        ParamsOpen = paramsOpen;
        Parameters = parameters;
        ParamsClose = paramsClose;
        Body = body;
        CloseParen = closeParen;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Body];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Name;
        yield return ParamsOpen;
        foreach (var p in Parameters) yield return p;
        yield return ParamsClose;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// defdec 呼び出しノード <c>(name arg1 arg2 ... child-node)</c> を表す CST ノード（F2）。
/// <c>&lt;body&gt;</c> プレースホルダの実引数として child-node を渡す。
/// パラメータ引数は式（リテラル等）、子ノードは最後の引数。
/// </summary>
public sealed class CstDefdecCall : CstNode
{
    public Token OpenParen { get; }

    /// <summary>呼び出す defdec の名前トークン。</summary>
    public Token Name { get; }

    /// <summary>パラメータに渡す式引数（リテラルや式）。</summary>
    public ImmutableArray<CstNode> Args { get; }

    /// <summary><c>&lt;body&gt;</c> プレースホルダに代入される子ノード。</summary>
    public CstNode Body { get; }

    public Token CloseParen { get; }

    public CstDefdecCall(Token openParen, Token name, ImmutableArray<CstNode> args, CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Name = name;
        Args = args;
        Body = body;
        CloseParen = closeParen;
        foreach (var arg in args) arg.Parent = this;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => Args.Add(Body);

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Name;
        foreach (var arg in Args)
            foreach (var token in arg.DescendantTokens())
                yield return token;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

/// <summary>
/// <c>&lt;body&gt;</c> プレースホルダを表す CST ノード（F2: defdec 用）。
/// defdec のテンプレート body 内で使用場所を明示する専用ノード。
/// </summary>
public sealed class CstBodyPlaceholder : CstTokenNode
{
    public CstBodyPlaceholder(Token token) : base(token) { }
}

// ═══════════════════════════════════════════════════════════
//  Macro System（F10: defmacro）
// ═══════════════════════════════════════════════════════════

/// <summary>
/// マクロ定義ノード <c>(defmacro name (params...) body)</c> を表す CST ノード。
/// Lisp 風のマクロシステムを提供し、DSL レベルのメタプログラミングを可能にする。
/// body は展開テンプレートとして、パラメータを置換して構文木を生成する。
/// </summary>
public sealed class CstDefmacro : CstNode
{
    public Token OpenParen { get; }
    public Token Keyword { get; }
    public Token Name { get; }
    public Token ParamsOpen { get; }
    public ImmutableArray<Token> Parameters { get; }
    public Token ParamsClose { get; }
    public CstNode Body { get; }
    public Token CloseParen { get; }

    public CstDefmacro(Token openParen, Token keyword, Token name,
        Token paramsOpen, ImmutableArray<Token> parameters, Token paramsClose,
        CstNode body, Token closeParen)
    {
        OpenParen = openParen;
        Keyword = keyword;
        Name = name;
        ParamsOpen = paramsOpen;
        Parameters = parameters;
        ParamsClose = paramsClose;
        Body = body;
        CloseParen = closeParen;
        body.Parent = this;
    }

    public override TextSpan Span => new(OpenParen.Span.Start, CloseParen.Span.End - OpenParen.Span.Start);
    public override TextSpan FullSpan => ComputeFullSpan(OpenParen, CloseParen);
    public override IEnumerable<CstNode> Children => [Body];

    public override IEnumerable<Token> DescendantTokens()
    {
        yield return OpenParen;
        yield return Keyword;
        yield return Name;
        yield return ParamsOpen;
        foreach (var p in Parameters) yield return p;
        yield return ParamsClose;
        foreach (var token in Body.DescendantTokens())
            yield return token;
        yield return CloseParen;
    }
}

// ═══════════════════════════════════════════════════════════
//  エラー回復ノード
// ═══════════════════════════════════════════════════════════

/// <summary>
/// パーサーが挿入した欠損ノード。
/// 閉じ括弧の不足等で、CSTの構造を完成させるために生成される。
/// </summary>
public sealed class CstMissing : CstNode
{
    /// <summary>期待されていたノードの種別の説明文。</summary>
    public string ExpectedKind { get; }

    /// <summary>CSTに挿入される位置。</summary>
    public int Position { get; }

    public CstMissing(string expectedKind, int position)
    {
        ExpectedKind = expectedKind;
        Position = position;
    }

    public override TextSpan Span => new(Position, 0);
    public override TextSpan FullSpan => new(Position, 0);
    public override IEnumerable<CstNode> Children => [];
    public override IEnumerable<Token> DescendantTokens() => [];
}

/// <summary>
/// パースエラーを含むノード。認識できないトークン列を保持する。
/// エラー回復後もパース続行できるように、問題のあるトークンをここに格納する。
/// </summary>
public sealed class CstError : CstNode
{
    public ImmutableArray<Token> Tokens { get; }

    public CstError(ImmutableArray<Token> tokens)
    {
        Tokens = tokens;
    }

    public override TextSpan Span => Tokens.Length > 0
        ? new TextSpan(Tokens[0].Span.Start, Tokens[^1].Span.End - Tokens[0].Span.Start)
        : new TextSpan(0, 0);

    public override TextSpan FullSpan => Span;
    public override IEnumerable<CstNode> Children => [];

    public override IEnumerable<Token> DescendantTokens()
    {
        foreach (var token in Tokens)
            yield return token;
    }
}

// ═══════════════════════════════════════════════════════════
//  ヘルパー
// ═══════════════════════════════════════════════════════════

/// <summary>
/// CstNode の拡張メソッドおよび共通ユーティリティ。
/// </summary>
public static class CstNodeExtensions
{
    /// <summary>
    /// 開きトークンと閉じトークンから FullSpan を計算する。
    /// 開きトークンの LeadingTrivia から閉じトークンの TrailingTrivia までを範囲とする。
    /// </summary>
    internal static TextSpan ComputeFullSpan(Token open, Token close)
    {
        var start = open.LeadingTrivia.Length > 0
            ? open.LeadingTrivia[0].Span.Start
            : open.Span.Start;
        var end = close.TrailingTrivia.Length > 0
            ? close.TrailingTrivia[^1].Span.End
            : close.Span.End;
        return new TextSpan(start, end - start);
    }
}

// CstNode に ComputeFullSpan を使えるようにする部分メソッド
public partial class CstNode
{
    /// <summary>開きトークンと閉じトークンから FullSpan を計算するヘルパー。</summary>
    protected static TextSpan ComputeFullSpan(Token open, Token close)
        => CstNodeExtensions.ComputeFullSpan(open, close);
}
