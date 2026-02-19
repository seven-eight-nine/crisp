using System.Collections.Immutable;

namespace Crisp.Syntax;

/// <summary>
/// Crisp DSL のレクサー（字句解析器）。
/// ソーステキストをトークン列に変換する。
///
/// 設計方針:
/// <list type="bullet">
///   <item><description>手書きレクサー — S式の字句構造は単純なためジェネレータ不要</description></item>
///   <item><description>位置追跡 — 各トークンに <see cref="TextSpan"/> を付与する</description></item>
///   <item><description>Trivia 分離 — 各トークンに LeadingTrivia / TrailingTrivia を付与する（Roslyn 方式）</description></item>
///   <item><description>エラー耐性 — 認識できない文字は <see cref="TokenKind.Error"/> トークンとして出力し、処理を続行する</description></item>
/// </list>
/// </summary>
public static class Lexer
{
    /// <summary>
    /// ソーステキスト全体をレキシングし、トークンの不変配列を返す。
    /// 末尾には必ず <see cref="TokenKind.EndOfFile"/> トークンが付与される。
    /// </summary>
    /// <param name="source">レキシング対象のソーステキスト。</param>
    /// <returns>全トークンの不変配列（EndOfFile を含む）。</returns>
    public static ImmutableArray<Token> Lex(string source)
    {
        var lexer = new LexerState(source);
        return lexer.Tokenize();
    }

    /// <summary>
    /// レクサーの内部状態を保持する構造体。
    /// ソーステキスト上の現在位置を追跡しながらトークンを生成する。
    /// </summary>
    private ref struct LexerState
    {
        private readonly ReadOnlySpan<char> _source;
        private readonly string _sourceString;
        private int _position;

        public LexerState(string source)
        {
            _source = source.AsSpan();
            _sourceString = source;
            _position = 0;
        }

        /// <summary>
        /// ソーステキスト全体をトークン化して不変配列を返す。
        /// </summary>
        public ImmutableArray<Token> Tokenize()
        {
            var tokens = ImmutableArray.CreateBuilder<Token>();

            while (true)
            {
                // 先行トリビアを収集
                var leadingTrivia = ReadLeadingTrivia();

                // メイントークンを読み取り
                if (_position >= _source.Length)
                {
                    // EndOfFile
                    tokens.Add(new Token(
                        TokenKind.EndOfFile, "", new TextSpan(_position, 0),
                        leadingTrivia, ImmutableArray<Trivia>.Empty));
                    break;
                }

                var tokenStart = _position;
                var (kind, text) = ReadToken();
                var span = new TextSpan(tokenStart, text.Length);

                // 後続トリビアを収集（次の改行まで、改行を含む）
                var trailingTrivia = ReadTrailingTrivia();

                tokens.Add(new Token(kind, text, span, leadingTrivia, trailingTrivia));
            }

            return tokens.ToImmutable();
        }

        // ──────────────────────────────────────────────
        //  Trivia の読み取り
        // ──────────────────────────────────────────────

        /// <summary>
        /// 先行トリビア（LeadingTrivia）を読み取る。
        /// 空白・改行・コメントをすべて消費し、次の実質トークンの直前で停止する。
        /// </summary>
        private ImmutableArray<Trivia> ReadLeadingTrivia()
        {
            var trivia = ImmutableArray.CreateBuilder<Trivia>();

            while (_position < _source.Length)
            {
                var ch = _source[_position];

                if (ch == '\n')
                {
                    trivia.Add(new Trivia(TriviaKind.Newline, "\n", new TextSpan(_position, 1)));
                    _position++;
                }
                else if (ch == '\r')
                {
                    // \r\n → 改行として扱う
                    if (_position + 1 < _source.Length && _source[_position + 1] == '\n')
                    {
                        trivia.Add(new Trivia(TriviaKind.Newline, "\r\n", new TextSpan(_position, 2)));
                        _position += 2;
                    }
                    else
                    {
                        trivia.Add(new Trivia(TriviaKind.Newline, "\r", new TextSpan(_position, 1)));
                        _position++;
                    }
                }
                else if (ch == ' ' || ch == '\t')
                {
                    var start = _position;
                    while (_position < _source.Length && (_source[_position] == ' ' || _source[_position] == '\t'))
                        _position++;
                    var text = _sourceString.Substring(start, _position - start);
                    trivia.Add(new Trivia(TriviaKind.Whitespace, text, new TextSpan(start, _position - start)));
                }
                else if (ch == ';')
                {
                    var start = _position;
                    while (_position < _source.Length && _source[_position] != '\n' && _source[_position] != '\r')
                        _position++;
                    var text = _sourceString.Substring(start, _position - start);
                    trivia.Add(new Trivia(TriviaKind.Comment, text, new TextSpan(start, _position - start)));
                }
                else
                {
                    break;
                }
            }

            return trivia.ToImmutable();
        }

        /// <summary>
        /// 後続トリビア（TrailingTrivia）を読み取る。
        /// Roslyn 方式: トークンの直後から、次の改行まで（改行自身を含む）の空白・コメントを消費する。
        /// 改行の後は次のトークンの LeadingTrivia になる。
        /// </summary>
        private ImmutableArray<Trivia> ReadTrailingTrivia()
        {
            var trivia = ImmutableArray.CreateBuilder<Trivia>();

            while (_position < _source.Length)
            {
                var ch = _source[_position];

                if (ch == ' ' || ch == '\t')
                {
                    var start = _position;
                    while (_position < _source.Length && (_source[_position] == ' ' || _source[_position] == '\t'))
                        _position++;
                    var text = _sourceString.Substring(start, _position - start);
                    trivia.Add(new Trivia(TriviaKind.Whitespace, text, new TextSpan(start, _position - start)));
                }
                else if (ch == ';')
                {
                    var start = _position;
                    while (_position < _source.Length && _source[_position] != '\n' && _source[_position] != '\r')
                        _position++;
                    var text = _sourceString.Substring(start, _position - start);
                    trivia.Add(new Trivia(TriviaKind.Comment, text, new TextSpan(start, _position - start)));
                }
                else if (ch == '\n')
                {
                    // 改行を含めて TrailingTrivia 完了
                    trivia.Add(new Trivia(TriviaKind.Newline, "\n", new TextSpan(_position, 1)));
                    _position++;
                    break;
                }
                else if (ch == '\r')
                {
                    if (_position + 1 < _source.Length && _source[_position + 1] == '\n')
                    {
                        trivia.Add(new Trivia(TriviaKind.Newline, "\r\n", new TextSpan(_position, 2)));
                        _position += 2;
                    }
                    else
                    {
                        trivia.Add(new Trivia(TriviaKind.Newline, "\r", new TextSpan(_position, 1)));
                        _position++;
                    }
                    break;
                }
                else
                {
                    // 非空白文字 → TrailingTrivia 終了
                    break;
                }
            }

            return trivia.ToImmutable();
        }

        // ──────────────────────────────────────────────
        //  メイントークンの読み取り
        // ──────────────────────────────────────────────

        /// <summary>
        /// 現在位置から1つのトークンを読み取り、(種別, テキスト) を返す。
        /// _position を進める。
        /// </summary>
        private (TokenKind kind, string text) ReadToken()
        {
            var ch = _source[_position];

            // 括弧
            if (ch == '(') { _position++; return (TokenKind.LeftParen, "("); }
            if (ch == ')') { _position++; return (TokenKind.RightParen, ")"); }

            // 文字列リテラル
            if (ch == '"') return ReadStringLiteral();

            // ブラックボードアクセス（F3）: $ から始まり、直後が .
            if (ch == '$' && _position + 1 < _source.Length && _source[_position + 1] == '.')
                return ReadBlackboardAccess();

            // <body> プレースホルダ（F2）: <body> を単一トークンとして認識
            if (ch == '<' && _position + 5 < _source.Length
                && _source[_position + 1] == 'b' && _source[_position + 2] == 'o'
                && _source[_position + 3] == 'd' && _source[_position + 4] == 'y'
                && _source[_position + 5] == '>')
            {
                var bodyStart = _position;
                _position += 6;
                return (TokenKind.BodyPlaceholder, _sourceString.Substring(bodyStart, 6));
            }

            // メンバーアクセス: . から始まり、直後が英字か _
            if (ch == '.' && _position + 1 < _source.Length && IsIdentStart(_source[_position + 1]))
                return ReadMemberAccess();

            // enum リテラル: :: から始まる
            if (ch == ':' && _position + 1 < _source.Length && _source[_position + 1] == ':')
                return ReadEnumLiteral();

            // キーワード引数: : から始まり、直後が英字
            if (ch == ':' && _position + 1 < _source.Length && IsIdentStart(_source[_position + 1]))
                return ReadKeyword();

            // 演算子（2文字）
            if (_position + 1 < _source.Length)
            {
                var next = _source[_position + 1];
                if (ch == '<' && next == '=') { _position += 2; return (TokenKind.LessEqual, "<="); }
                if (ch == '>' && next == '=') { _position += 2; return (TokenKind.GreaterEqual, ">="); }
                if (ch == '!' && next == '=') { _position += 2; return (TokenKind.NotEqual, "!="); }
            }

            // 演算子（1文字）
            if (ch == '+') { _position++; return (TokenKind.Plus, "+"); }
            if (ch == '*') { _position++; return (TokenKind.Star, "*"); }
            if (ch == '/') { _position++; return (TokenKind.Slash, "/"); }
            if (ch == '%') { _position++; return (TokenKind.Percent, "%"); }
            if (ch == '<') { _position++; return (TokenKind.LessThan, "<"); }
            if (ch == '>') { _position++; return (TokenKind.GreaterThan, ">"); }
            if (ch == '=') { _position++; return (TokenKind.Equal, "="); }

            // マイナス演算子 vs 負リテラル
            if (ch == '-')
                return ReadMinusOrNegativeLiteral();

            // 数値リテラル
            if (char.IsDigit(ch))
                return ReadNumberLiteral();

            // 識別子（予約語を含む）
            if (IsIdentStart(ch))
                return ReadIdentifierOrKeyword();

            // 認識できない文字 → Error トークン
            return ReadErrorToken();
        }

        /// <summary>
        /// 文字列リテラルを読み取る。エスケープシーケンスを認識する。
        /// 閉じ引用符がない場合でもエラー回復で続行する。
        /// </summary>
        private (TokenKind, string) ReadStringLiteral()
        {
            var start = _position;
            _position++; // 開き "

            while (_position < _source.Length)
            {
                var ch = _source[_position];
                if (ch == '\\')
                {
                    _position += 2; // エスケープシーケンスをスキップ
                    continue;
                }
                if (ch == '"')
                {
                    _position++; // 閉じ "
                    break;
                }
                _position++;
            }

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.StringLiteral, text);
        }

        /// <summary>
        /// メンバーアクセストークンを読み取る。
        /// <c>.Foo.Bar.Baz</c> のようなドットで連鎖する識別子を単一トークンとして認識する。
        /// </summary>
        private (TokenKind, string) ReadMemberAccess()
        {
            var start = _position;

            while (_position < _source.Length && _source[_position] == '.')
            {
                _position++; // ドットをスキップ
                // 識別子部分を消費
                while (_position < _source.Length && IsIdentContinue(_source[_position]))
                    _position++;
            }

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.MemberAccess, text);
        }

        /// <summary>
        /// ブラックボードアクセストークンを読み取る。
        /// <c>$.Foo.Bar</c> のようなドルサインで始まるメンバーチェーンを単一トークンとして認識する。
        /// </summary>
        private (TokenKind, string) ReadBlackboardAccess()
        {
            var start = _position;
            _position++; // $ をスキップ

            // メンバーチェーン (.Foo.Bar) を消費
            while (_position < _source.Length && _source[_position] == '.')
            {
                _position++; // ドットをスキップ
                while (_position < _source.Length && IsIdentContinue(_source[_position]))
                    _position++;
            }

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.BlackboardAccess, text);
        }

        /// <summary>
        /// enum リテラルを読み取る。
        /// <c>::TypeName.MemberName</c> 形式。
        /// </summary>
        private (TokenKind, string) ReadEnumLiteral()
        {
            var start = _position;
            _position += 2; // :: をスキップ

            // 型名を読む
            while (_position < _source.Length && IsIdentContinue(_source[_position]))
                _position++;

            // ドットを読む
            if (_position < _source.Length && _source[_position] == '.')
            {
                _position++;
                // メンバー名を読む
                while (_position < _source.Length && IsIdentContinue(_source[_position]))
                    _position++;
            }

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.EnumLiteral, text);
        }

        /// <summary>
        /// キーワード引数を読み取る。
        /// <c>:all</c>, <c>:any</c>, <c>:n</c> 等の形式。
        /// </summary>
        private (TokenKind, string) ReadKeyword()
        {
            var start = _position;
            _position++; // : をスキップ

            while (_position < _source.Length && IsIdentContinue(_source[_position]))
                _position++;

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.Keyword, text);
        }

        /// <summary>
        /// マイナス記号を演算子として読むか、負の数値リテラルとして読むかを判定する。
        ///
        /// 規則:
        /// - <c>(</c> の直後の <c>-</c> は常に演算子（二項演算の可能性があるため）
        /// - それ以外の文脈で <c>-</c> の直後に数字が続く場合は負のリテラル
        /// - <c>-</c> の直後に数字がなければ演算子
        /// </summary>
        private (TokenKind, string) ReadMinusOrNegativeLiteral()
        {
            // 直前のトークンが ( かどうかをチェック
            // 簡易実装: 直前の非空白文字が ( なら演算子
            var isPrevLeftParen = false;
            for (var i = _position - 1; i >= 0; i--)
            {
                var c = _source[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                isPrevLeftParen = c == '(';
                break;
            }

            // ( の直後なら演算子
            if (isPrevLeftParen)
            {
                _position++;
                return (TokenKind.Minus, "-");
            }

            // 直後に数字が続く場合は負リテラル
            if (_position + 1 < _source.Length && char.IsDigit(_source[_position + 1]))
            {
                return ReadNegativeNumberLiteral();
            }

            // それ以外は演算子
            _position++;
            return (TokenKind.Minus, "-");
        }

        /// <summary>
        /// 負の数値リテラルを読み取る。<c>-</c> を含む。
        /// </summary>
        private (TokenKind, string) ReadNegativeNumberLiteral()
        {
            var start = _position;
            _position++; // - をスキップ
            return ReadNumberLiteralCore(start);
        }

        /// <summary>
        /// 数値リテラルを読み取る。整数か浮動小数点かを区別する。
        /// </summary>
        private (TokenKind, string) ReadNumberLiteral()
        {
            return ReadNumberLiteralCore(_position);
        }

        /// <summary>
        /// 数値リテラルのコア読み取り。start 位置（負号を含む場合がある）から開始する。
        /// </summary>
        private (TokenKind, string) ReadNumberLiteralCore(int start)
        {
            // 整数部分
            while (_position < _source.Length && char.IsDigit(_source[_position]))
                _position++;

            // 小数点があれば浮動小数点リテラル
            if (_position < _source.Length && _source[_position] == '.'
                && _position + 1 < _source.Length && char.IsDigit(_source[_position + 1]))
            {
                _position++; // .
                while (_position < _source.Length && char.IsDigit(_source[_position]))
                    _position++;

                var floatText = _sourceString.Substring(start, _position - start);
                return (TokenKind.FloatLiteral, floatText);
            }

            var intText = _sourceString.Substring(start, _position - start);
            return (TokenKind.IntLiteral, intText);
        }

        /// <summary>
        /// 識別子または予約語を読み取る。
        /// レクサー段階では予約語を Identifier として認識する（パーサーが文脈判定する）。
        /// ただし <c>true</c>, <c>false</c>, <c>null</c> はリテラルトークンとして区別する。
        /// </summary>
        private (TokenKind, string) ReadIdentifierOrKeyword()
        {
            var start = _position;

            while (_position < _source.Length && IsIdentContinue(_source[_position]))
                _position++;

            var text = _sourceString.Substring(start, _position - start);

            var kind = text switch
            {
                "true" => TokenKind.BoolTrue,
                "false" => TokenKind.BoolFalse,
                "null" => TokenKind.NullLiteral,
                _ => TokenKind.Identifier,
            };

            return (kind, text);
        }

        /// <summary>
        /// 認識できない文字をエラートークンとして読み取る。
        /// 次の空白または括弧まで読み飛ばしてエラーに含める。
        /// </summary>
        private (TokenKind, string) ReadErrorToken()
        {
            var start = _position;

            while (_position < _source.Length)
            {
                var ch = _source[_position];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n'
                    || ch == '(' || ch == ')')
                    break;
                _position++;
            }

            var text = _sourceString.Substring(start, _position - start);
            return (TokenKind.Error, text);
        }

        // ──────────────────────────────────────────────
        //  文字分類ヘルパー
        // ──────────────────────────────────────────────

        /// <summary>識別子の先頭文字として有効かどうか（英字または <c>_</c>）。</summary>
        private static bool IsIdentStart(char ch) => char.IsLetter(ch) || ch == '_';

        /// <summary>識別子の継続文字として有効かどうか（英数字、<c>_</c>、<c>-</c>）。</summary>
        private static bool IsIdentContinue(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-';
    }
}
