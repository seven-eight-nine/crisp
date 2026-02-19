using System.Collections.Immutable;
using Crisp.Syntax.Cst;
#pragma warning disable CS0219 // Variable is assigned but its value is never used

namespace Crisp.Syntax;

/// <summary>
/// Crisp DSL の再帰下降パーサー。
/// トークン列からロスレスな CST（具象構文木）を生成する。
///
/// 設計方針:
/// <list type="bullet">
///   <item><description>ロスレス CST — 全トークン（Trivia 含む）を保持する</description></item>
///   <item><description>エラー回復 — CSTは常に生成される。CstMissing / CstError ノードを含みうる</description></item>
///   <item><description>文脈追跡 — ノード位置と式位置を区別し、lowering の基盤とする</description></item>
/// </list>
/// </summary>
public static class Parser
{
    /// <summary>
    /// ソーステキストをパースして CST と診断メッセージを返す。
    /// </summary>
    /// <param name="source">パース対象のソーステキスト。</param>
    /// <returns>パース結果の CST プログラムと診断バッグのタプル。</returns>
    public static (CstProgram Program, DiagnosticBag Diagnostics) Parse(string source)
    {
        var tokens = Lexer.Lex(source);
        var state = new ParserState(tokens);
        var result = state.ParseProgram();
        AssignNodeIds(result.Item1);
        return result;
    }

    /// <summary>
    /// トークン列をパースして CST と診断メッセージを返す。
    /// Query DB 等で Lex と Parse を分離する場合に使用する。
    /// </summary>
    /// <param name="tokens">Lexer.Lex で生成されたトークン列。</param>
    /// <returns>パース結果の CST プログラムと診断バッグのタプル。</returns>
    public static (CstProgram Program, DiagnosticBag Diagnostics) Parse(ImmutableArray<Token> tokens)
    {
        var state = new ParserState(tokens);
        var result = state.ParseProgram();
        AssignNodeIds(result.Item1);
        return result;
    }

    /// <summary>
    /// CST ノードツリー全体に連番の安定 Id を付与する。
    /// パース完了後に呼ばれ、CstNode.Id を深さ優先・ソース順に採番する。
    /// この Id は AST → IR を経由して、デバッガ（F7）やビジュアルエディタ（F4）で使用される。
    /// </summary>
    private static void AssignNodeIds(CstProgram program)
    {
        int nextId = 0;
        Assign(program);

        void Assign(CstNode node)
        {
            node.Id = nextId++;
            if (node is CstProgram prog)
            {
                // CstProgram.Children は Trees のみを返すため、
                // Defdecs, Defmacros, Imports も明示的にトラバースする
                foreach (var tree in prog.Trees) Assign(tree);
                foreach (var defdec in prog.Defdecs) Assign(defdec);
                foreach (var defmacro in prog.Defmacros) Assign(defmacro);
                foreach (var import in prog.Imports) Assign(import);
            }
            else
            {
                foreach (var child in node.Children)
                    Assign(child);
            }
        }
    }

    /// <summary>
    /// パーサーの内部状態。トークン列上の現在位置と診断バッグを管理する。
    /// </summary>
    private class ParserState
    {
        private readonly ImmutableArray<Token> _tokens;
        private int _position;
        private readonly DiagnosticBag _diagnostics = new();

        public ParserState(ImmutableArray<Token> tokens)
        {
            _tokens = tokens;
            _position = 0;
        }

        // ──────────────────────────────────────────────
        //  トークンアクセスヘルパー
        // ──────────────────────────────────────────────

        /// <summary>現在のトークンを返す（消費しない）。</summary>
        private Token Peek() => _position < _tokens.Length
            ? _tokens[_position]
            : _tokens[^1]; // EndOfFile

        /// <summary>先読み: 現在位置 + offset のトークンを返す。</summary>
        private Token PeekAt(int offset)
        {
            var idx = _position + offset;
            return idx < _tokens.Length ? _tokens[idx] : _tokens[^1];
        }

        /// <summary>現在のトークンを消費して返す。</summary>
        private Token Advance()
        {
            var token = Peek();
            if (_position < _tokens.Length) _position++;
            return token;
        }

        /// <summary>指定された種別のトークンを期待して消費する。不一致の場合は Diagnostic を報告する。</summary>
        private Token Expect(TokenKind kind, string expected)
        {
            var token = Peek();
            if (token.Kind == kind)
                return Advance();

            _diagnostics.Report(DiagnosticDescriptors.BS0016, token.Span, null, expected, token.Text);
            // 消費せずにダミートークンを返す
            return new Token(kind, "", new TextSpan(token.Span.Start, 0),
                ImmutableArray<Trivia>.Empty, ImmutableArray<Trivia>.Empty);
        }

        /// <summary>現在のソース位置（テキスト上のオフセット）。</summary>
        private int CurrentPosition => Peek().Span.Start;

        // ──────────────────────────────────────────────
        //  トップレベルパース
        // ──────────────────────────────────────────────

        /// <summary>プログラム全体をパースする。</summary>
        public (CstProgram, DiagnosticBag) ParseProgram()
        {
            var trees = ImmutableArray.CreateBuilder<CstTree>();
            var defdecs = ImmutableArray.CreateBuilder<CstDefdec>();
            var defmacros = ImmutableArray.CreateBuilder<CstDefmacro>();
            var imports = ImmutableArray.CreateBuilder<CstImport>();

            while (Peek().Kind != TokenKind.EndOfFile)
            {
                if (Peek().Kind == TokenKind.LeftParen && PeekAt(1).Kind == TokenKind.Identifier)
                {
                    var nextText = PeekAt(1).Text;
                    if (nextText == "tree")
                    {
                        trees.Add(ParseTree());
                    }
                    else if (nextText == "import")
                    {
                        // F11: import はトップレベルで認識しパースし、Imports に格納
                        imports.Add(ParseImport());
                        continue;
                    }
                    else if (nextText == "defdec")
                    {
                        // F2: defdec はトップレベルで認識し、Defdecs に格納
                        defdecs.Add(ParseDefdec());
                        continue;
                    }
                    else if (nextText == "defmacro")
                    {
                        // F10: defmacro はトップレベルで認識し、Defmacros に格納
                        defmacros.Add(ParseDefmacro());
                        continue;
                    }
                    else
                    {
                        _diagnostics.Report(DiagnosticDescriptors.BS0016, Peek().Span, null, "tree definition", Peek().Text);
                        Advance();
                    }
                }
                else
                {
                    // トップレベルで tree 以外の要素があればスキップ
                    _diagnostics.Report(DiagnosticDescriptors.BS0016, Peek().Span, null, "tree definition", Peek().Text);
                    Advance();
                }
            }

            var eof = Advance(); // EndOfFile
            var program = new CstProgram(trees.ToImmutable(), defdecs.ToImmutable(), defmacros.ToImmutable(), imports.ToImmutable(), eof);
            return (program, _diagnostics);
        }

        /// <summary>
        /// ツリー定義 <c>(tree Name [:blackboard Type] body...)</c> をパースする。
        /// オプションの <c>:blackboard</c> キーワード属性を認識する（F3: Typed Blackboard）。
        /// </summary>
        private CstTree ParseTree()
        {
            var openParen = Advance(); // (
            var treeKeyword = Advance(); // tree
            var name = Expect(TokenKind.Identifier, "tree name");

            // F3: :blackboard 属性のパース
            Token? blackboardType = null;
            if (Peek().Kind == TokenKind.Keyword && Peek().Text == ":blackboard")
            {
                Advance(); // :blackboard
                blackboardType = Expect(TokenKind.Identifier, "blackboard type name");
            }

            var body = ImmutableArray.CreateBuilder<CstNode>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                body.Add(ParseNode());
            }

            var closeParen = ExpectCloseParen(openParen);
            return new CstTree(openParen, treeKeyword, name, body.ToImmutable(), closeParen, blackboardType);
        }

        // ──────────────────────────────────────────────
        //  ノードパース（ノード位置）
        // ──────────────────────────────────────────────

        /// <summary>
        /// ノード位置でのパース。
        /// ノード位置に来る可能性があるのは:
        /// - メンバーアクセス（括弧なしアクション）
        /// - ( で始まるリスト（予約語ノード、呼び出し）
        /// </summary>
        private CstNode ParseNode()
        {
            var current = Peek();

            // 括弧なしメンバーアクセス → ノード位置のアクション呼び出し
            if (current.Kind == TokenKind.MemberAccess)
            {
                return new CstMemberAccess(Advance());
            }

            // <body> プレースホルダ（F2: defdec 内で使用）
            if (current.Kind == TokenKind.BodyPlaceholder)
            {
                return new CstBodyPlaceholder(Advance());
            }

            // ( で始まるリスト
            if (current.Kind == TokenKind.LeftParen)
            {
                return ParseListInNodePosition();
            }

            // エラー: 予期しないトークン
            _diagnostics.Report(DiagnosticDescriptors.BS0016, current.Span, null, "node", current.Text);
            return new CstError([Advance()]);
        }

        /// <summary>
        /// ノード位置での <c>( ... )</c> リストパース。
        /// 先頭の識別子を見て予約語ノードか呼び出しかを判定する。
        /// </summary>
        private CstNode ParseListInNodePosition()
        {
            var next = PeekAt(1);

            // 先頭が識別子 → 予約語の判定
            if (next.Kind == TokenKind.Identifier)
            {
                return next.Text switch
                {
                    "select" => ParseSelect(),
                    "seq" => ParseSequence(),
                    "parallel" => ParseParallel(),
                    "check" => ParseCheck(),
                    "guard" => ParseGuard(),
                    "if" => ParseIfNode(),
                    "invert" => ParseInvert(),
                    "repeat" => ParseRepeat(),
                    "timeout" => ParseTimeout(),
                    "cooldown" => ParseCooldown(),
                    "while" => ParseWhile(),
                    "reactive" => ParseReactive(),
                    "reactive-select" => ParseReactiveSelect(),
                    "ref" => ParseRef(),
                    "import" => ParseImport(),
                    "defdec" => ParseDefdec(),
                    "defmacro" => ParseDefmacro(),
                    _ => ParseDefdecCall(), // F2: 予約語でない識別子 → defdec 呼び出しとしてパース
                };
            }

            // 先頭がメンバーアクセス → 呼び出しノード
            if (next.Kind == TokenKind.MemberAccess)
            {
                return ParseCallInNodePosition();
            }

            // 先頭が演算子 → ノード位置では不正
            _diagnostics.Report(DiagnosticDescriptors.BS0016, next.Span, null, "node keyword or member access", next.Text);
            return SkipToMatchingParen();
        }

        // ──────────────────────────────────────────────
        //  複合ノード
        // ──────────────────────────────────────────────

        /// <summary><c>(select child1 child2 ...)</c></summary>
        private CstSelect ParseSelect()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // select
            var children = ParseNodeChildren();
            var closeParen = ExpectCloseParen(openParen);
            return new CstSelect(openParen, keyword, children, closeParen);
        }

        /// <summary><c>(seq child1 child2 ...)</c></summary>
        private CstSequence ParseSequence()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // seq
            var children = ParseNodeChildren();
            var closeParen = ExpectCloseParen(openParen);
            return new CstSequence(openParen, keyword, children, closeParen);
        }

        /// <summary><c>(parallel :policy [n] child1 child2 ...)</c></summary>
        private CstParallel ParseParallel()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // parallel
            var policy = Expect(TokenKind.Keyword, "parallel policy (:any, :all, :n)");

            Token? policyArg = null;
            if (policy.Text == ":n" && Peek().Kind == TokenKind.IntLiteral)
            {
                policyArg = Advance();
            }

            var children = ParseNodeChildren();
            var closeParen = ExpectCloseParen(openParen);
            return new CstParallel(openParen, keyword, policy, policyArg, children, closeParen);
        }

        // ──────────────────────────────────────────────
        //  デコレータノード
        // ──────────────────────────────────────────────

        /// <summary><c>(check expr)</c></summary>
        private CstCheck ParseCheck()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // check
            var expr = ParseExpr();
            var closeParen = ExpectCloseParen(openParen);
            return new CstCheck(openParen, keyword, expr, closeParen);
        }

        /// <summary><c>(guard condition body)</c></summary>
        private CstGuard ParseGuard()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // guard
            var condition = ParseExpr();
            var body = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstGuard(openParen, keyword, condition, body, closeParen);
        }

        /// <summary><c>(if condition then [else])</c></summary>
        private CstIf ParseIfNode()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // if
            var condition = ParseExpr();
            var then = ParseNode();

            CstNode? elseBranch = null;
            // else 分岐: ) でなければまだ子がある → else
            if (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                elseBranch = ParseNode();
            }

            var closeParen = ExpectCloseParen(openParen);
            return new CstIf(openParen, keyword, condition, then, elseBranch, closeParen);
        }

        /// <summary><c>(invert child)</c></summary>
        private CstInvert ParseInvert()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // invert
            var child = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstInvert(openParen, keyword, child, closeParen);
        }

        /// <summary><c>(repeat count child)</c></summary>
        private CstRepeat ParseRepeat()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // repeat
            var count = Expect(TokenKind.IntLiteral, "repeat count (integer)");
            var child = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstRepeat(openParen, keyword, count, child, closeParen);
        }

        /// <summary><c>(timeout seconds child)</c></summary>
        private CstTimeout ParseTimeout()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // timeout
            var seconds = ReadNumberToken();
            var child = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstTimeout(openParen, keyword, seconds, child, closeParen);
        }

        /// <summary><c>(cooldown seconds child)</c></summary>
        private CstCooldown ParseCooldown()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // cooldown
            var seconds = ReadNumberToken();
            var child = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstCooldown(openParen, keyword, seconds, child, closeParen);
        }

        /// <summary><c>(while condition body)</c></summary>
        private CstWhile ParseWhile()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // while
            var condition = ParseExpr();
            var body = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstWhile(openParen, keyword, condition, body, closeParen);
        }

        // ──────────────────────────────────────────────
        //  リアクティブノード（F1）
        // ──────────────────────────────────────────────

        /// <summary><c>(reactive condition body)</c></summary>
        private CstReactive ParseReactive()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // reactive
            var condition = ParseExpr();
            var body = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstReactive(openParen, keyword, condition, body, closeParen);
        }

        /// <summary><c>(reactive-select child1 child2 ...)</c></summary>
        private CstReactiveSelect ParseReactiveSelect()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // reactive-select
            var children = ParseNodeChildren();
            var closeParen = ExpectCloseParen(openParen);
            return new CstReactiveSelect(openParen, keyword, children, closeParen);
        }

        // ──────────────────────────────────────────────
        //  Multi-Tree ノード（F11）
        // ──────────────────────────────────────────────

        /// <summary><c>(ref TreeName)</c></summary>
        private CstRef ParseRef()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // ref
            var name = Expect(TokenKind.Identifier, "tree name");
            var closeParen = ExpectCloseParen(openParen);
            return new CstRef(openParen, keyword, name, closeParen);
        }

        /// <summary><c>(import "path")</c></summary>
        private CstImport ParseImport()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // import
            var path = Expect(TokenKind.StringLiteral, "import path");
            var closeParen = ExpectCloseParen(openParen);
            return new CstImport(openParen, keyword, path, closeParen);
        }

        // ──────────────────────────────────────────────
        //  User-Defined Decorators（F2）
        // ──────────────────────────────────────────────

        /// <summary><c>(defdec name (param1 param2 ...) body)</c></summary>
        private CstDefdec ParseDefdec()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // defdec
            var name = Expect(TokenKind.Identifier, "defdec name");

            // パラメータリスト (param1 param2 ...)
            var paramsOpen = Expect(TokenKind.LeftParen, "(");
            var parameters = ImmutableArray.CreateBuilder<Token>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                parameters.Add(Expect(TokenKind.Identifier, "parameter name"));
            }
            var paramsClose = ExpectCloseParen(paramsOpen);

            var body = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstDefdec(openParen, keyword, name, paramsOpen, parameters.ToImmutable(), paramsClose, body, closeParen);
        }

        // ──────────────────────────────────────────────
        //  Macro System（F10）
        // ──────────────────────────────────────────────

        /// <summary><c>(defmacro name (param1 param2 ...) body)</c></summary>
        private CstDefmacro ParseDefmacro()
        {
            var openParen = Advance(); // (
            var keyword = Advance(); // defmacro
            var name = Expect(TokenKind.Identifier, "defmacro name");

            // パラメータリスト (param1 param2 ...)
            var paramsOpen = Expect(TokenKind.LeftParen, "(");
            var parameters = ImmutableArray.CreateBuilder<Token>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                parameters.Add(Expect(TokenKind.Identifier, "parameter name"));
            }
            var paramsClose = ExpectCloseParen(paramsOpen);

            var body = ParseNode();
            var closeParen = ExpectCloseParen(openParen);
            return new CstDefmacro(openParen, keyword, name, paramsOpen, parameters.ToImmutable(), paramsClose, body, closeParen);
        }

        /// <summary>
        /// defdec 呼び出し <c>(name arg1 arg2 ... child-node)</c>（F2）。
        /// 最後の引数がノード位置の子（<c>&lt;body&gt;</c> に代入される）。
        /// それ以外は式引数（パラメータに渡される）。
        /// </summary>
        private CstDefdecCall ParseDefdecCall()
        {
            var openParen = Advance(); // (
            var name = Advance(); // defdec 名

            // 式引数をパース（最後の1つがノード位置の子、残りがパラメータ引数）
            var args = ImmutableArray.CreateBuilder<CstNode>();
            CstNode? body = null;

            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                // 次のトークンがノード位置（LeftParen + キーワード or MemberAccess）かを判定
                if (IsNodePosition())
                {
                    body = ParseNode();
                    break;
                }
                args.Add(ParseExpr());
            }

            // body が見つからなかった場合はエラー回復
            body ??= new CstMissing("defdec body", Peek().Span.Start);

            var closeParen = ExpectCloseParen(openParen);
            return new CstDefdecCall(openParen, name, args.ToImmutable(), body, closeParen);
        }

        /// <summary>
        /// 現在位置がノード位置（子ノード）を開始するかを判定するヘルパー。
        /// <c>(keyword ...)</c> or <c>(.Member ...)</c> or <c>.Member</c> の形式を検出する。
        /// </summary>
        private bool IsNodePosition()
        {
            var current = Peek();

            // .Member（括弧なしアクション呼び出し）
            if (current.Kind == TokenKind.MemberAccess)
                return true;

            // (keyword ...) or (.Member ...)
            if (current.Kind == TokenKind.LeftParen)
            {
                var next = PeekAt(1);
                if (next.Kind == TokenKind.MemberAccess)
                    return true;
                if (next.Kind == TokenKind.Identifier)
                {
                    var text = next.Text;
                    // 組み込みノードキーワード or defdec呼び出し用の識別子
                    return text is "select" or "seq" or "parallel" or "check" or "guard"
                        or "if" or "invert" or "repeat" or "timeout" or "cooldown"
                        or "while" or "reactive" or "reactive-select" or "ref"
                        || !text.StartsWith(".", StringComparison.Ordinal);
                }
            }

            return false;
        }

        // ──────────────────────────────────────────────
        //  呼び出しノード
        // ──────────────────────────────────────────────

        /// <summary>
        /// ノード位置での呼び出し <c>(.Member arg1 arg2 ...)</c>。
        /// CSTレベルでは CstCall として統一的にパースする。
        /// </summary>
        private CstCall ParseCallInNodePosition()
        {
            var openParen = Advance(); // (
            var memberToken = Expect(TokenKind.MemberAccess, "member access");
            var member = new CstMemberAccess(memberToken);

            var args = ImmutableArray.CreateBuilder<CstNode>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                args.Add(ParseExpr());
            }

            var closeParen = ExpectCloseParen(openParen);
            return new CstCall(openParen, member, args.ToImmutable(), closeParen);
        }

        // ──────────────────────────────────────────────
        //  式パース（式位置）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 式位置でのパース。
        /// 式に来る可能性があるのは:
        /// - メンバーアクセス
        /// - リテラル（int, float, bool, string, null, enum）
        /// - ( で始まるリスト（演算子式、メソッド呼び出し、論理式）
        /// </summary>
        private CstNode ParseExpr()
        {
            var current = Peek();

            return current.Kind switch
            {
                TokenKind.MemberAccess => new CstMemberAccess(Advance()),
                TokenKind.BlackboardAccess => new CstBlackboardAccess(Advance()),
                TokenKind.IntLiteral => new CstIntLiteral(Advance()),
                TokenKind.FloatLiteral => new CstFloatLiteral(Advance()),
                TokenKind.BoolTrue or TokenKind.BoolFalse => new CstBoolLiteral(Advance()),
                TokenKind.StringLiteral => new CstStringLiteral(Advance()),
                TokenKind.NullLiteral => new CstNullLiteral(Advance()),
                TokenKind.EnumLiteral => new CstEnumLiteral(Advance()),
                TokenKind.BodyPlaceholder => new CstBodyPlaceholder(Advance()),
                TokenKind.LeftParen => ParseExprList(),
                _ => ParseExprError(),
            };
        }

        /// <summary>
        /// 式位置での <c>( ... )</c> リストパース。
        /// 先頭要素を見て演算子式・論理式・メソッド呼び出しを判定する。
        /// </summary>
        private CstNode ParseExprList()
        {
            var next = PeekAt(1);

            // 演算子
            if (IsOperatorToken(next.Kind))
                return ParseBinaryOrUnaryExpr();

            // 論理演算子 / not
            if (next.Kind == TokenKind.Identifier)
            {
                if (next.Text == "and" || next.Text == "or")
                    return ParseLogicExpr();
                if (next.Text == "not")
                    return ParseNotExpr();
            }

            // メンバーアクセス → 式コンテキストの呼び出し
            if (next.Kind == TokenKind.MemberAccess)
                return ParseCallExpr();

            // エラー
            _diagnostics.Report(DiagnosticDescriptors.BS0016, next.Span, null, "operator, logic, or method call", next.Text);
            return SkipToMatchingParen();
        }

        /// <summary>
        /// 二項式または単項マイナス式 <c>(op lhs rhs)</c> / <c>(- expr)</c> をパースする。
        /// </summary>
        private CstNode ParseBinaryOrUnaryExpr()
        {
            var openParen = Advance(); // (
            var op = Advance(); // 演算子

            var first = ParseExpr();

            // ) が来たら単項式
            if (Peek().Kind == TokenKind.RightParen)
            {
                var closeParen = Advance();
                return new CstUnaryExpr(openParen, op, first, closeParen);
            }

            // 二項式
            var second = ParseExpr();
            var close = ExpectCloseParen(openParen);
            return new CstBinaryExpr(openParen, op, first, second, close);
        }

        /// <summary>論理式 <c>(and/or expr expr+)</c> をパースする。</summary>
        private CstLogicExpr ParseLogicExpr()
        {
            var openParen = Advance(); // (
            var op = Advance(); // and / or

            var operands = ImmutableArray.CreateBuilder<CstNode>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                operands.Add(ParseExpr());
            }

            var closeParen = ExpectCloseParen(openParen);
            return new CstLogicExpr(openParen, op, operands.ToImmutable(), closeParen);
        }

        /// <summary>not 式 <c>(not expr)</c> をパースする。</summary>
        private CstUnaryExpr ParseNotExpr()
        {
            var openParen = Advance(); // (
            var op = Advance(); // not
            var operand = ParseExpr();
            var closeParen = ExpectCloseParen(openParen);
            return new CstUnaryExpr(openParen, op, operand, closeParen);
        }

        /// <summary>式コンテキストでのメソッド呼び出し <c>(.Method arg1 arg2 ...)</c> をパースする。</summary>
        private CstCall ParseCallExpr()
        {
            var openParen = Advance(); // (
            var memberToken = Advance(); // .Method
            var member = new CstMemberAccess(memberToken);

            var args = ImmutableArray.CreateBuilder<CstNode>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                args.Add(ParseExpr());
            }

            var closeParen = ExpectCloseParen(openParen);
            return new CstCall(openParen, member, args.ToImmutable(), closeParen);
        }

        // ──────────────────────────────────────────────
        //  共通ヘルパー
        // ──────────────────────────────────────────────

        /// <summary>ノード位置の子ノード列をパースする。</summary>
        private ImmutableArray<CstNode> ParseNodeChildren()
        {
            var children = ImmutableArray.CreateBuilder<CstNode>();
            while (Peek().Kind != TokenKind.RightParen && Peek().Kind != TokenKind.EndOfFile)
            {
                children.Add(ParseNode());
            }
            return children.ToImmutable();
        }

        /// <summary>閉じ括弧を期待する。ない場合は BS0018 を報告してダミーを返す。</summary>
        private Token ExpectCloseParen(Token openParen)
        {
            if (Peek().Kind == TokenKind.RightParen)
                return Advance();

            _diagnostics.Report(DiagnosticDescriptors.BS0018, openParen.Span, null);
            return new Token(TokenKind.RightParen, ")", new TextSpan(CurrentPosition, 0),
                ImmutableArray<Trivia>.Empty, ImmutableArray<Trivia>.Empty);
        }

        /// <summary>数値トークン（int または float）を読む。</summary>
        private Token ReadNumberToken()
        {
            var current = Peek();
            if (current.Kind == TokenKind.IntLiteral || current.Kind == TokenKind.FloatLiteral)
                return Advance();

            _diagnostics.Report(DiagnosticDescriptors.BS0016, current.Span, null, "number literal", current.Text);
            return new Token(TokenKind.IntLiteral, "0", new TextSpan(current.Span.Start, 0),
                ImmutableArray<Trivia>.Empty, ImmutableArray<Trivia>.Empty);
        }

        /// <summary>対応する閉じ括弧までスキップしてエラーノードを返す。</summary>
        private CstError SkipToMatchingParen()
        {
            var tokens = ImmutableArray.CreateBuilder<Token>();
            var depth = 0;

            tokens.Add(Advance()); // 開き括弧
            depth++;

            while (depth > 0 && Peek().Kind != TokenKind.EndOfFile)
            {
                if (Peek().Kind == TokenKind.LeftParen) depth++;
                if (Peek().Kind == TokenKind.RightParen) depth--;
                tokens.Add(Advance());
            }

            return new CstError(tokens.ToImmutable());
        }

        /// <summary>式位置でのエラー。</summary>
        private CstNode ParseExprError()
        {
            _diagnostics.Report(DiagnosticDescriptors.BS0016, Peek().Span, null, "expression", Peek().Text);
            return new CstError([Advance()]);
        }

        /// <summary>トークン種別が演算子かどうかを判定する。</summary>
        private static bool IsOperatorToken(TokenKind kind) => kind switch
        {
            TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                or TokenKind.LessThan or TokenKind.GreaterThan or TokenKind.LessEqual or TokenKind.GreaterEqual
                or TokenKind.Equal or TokenKind.NotEqual => true,
            _ => false,
        };
    }
}
