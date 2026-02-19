using System.Collections.Immutable;
using System.Globalization;
using Crisp.Syntax.Ast;

namespace Crisp.Syntax.IR;

/// <summary>
/// S 式テキストを IR ノードに逆直列化するデシリアライザ。
/// <see cref="IrSerializer"/> の逆操作。
///
/// 用途:
/// <list type="bullet">
///   <item><description>テストでの IR ラウンドトリップ検証（serialize → deserialize → 比較）</description></item>
///   <item><description>将来的な IR レベル最適化パスの入力</description></item>
///   <item><description>デバッグ時の手書き IR 読み込み</description></item>
/// </list>
///
/// 入力形式の例:
/// <code>
/// (ir-selector
///   (ir-sequence
///     (ir-condition
///       (ir-binary-op :lt
///         (ir-member-load ("Health") :type "System.Single")
///         (ir-literal 30 :int)))
///     (ir-action "this" "Flee" () :type "Crisp.Runtime.BtStatus"))
///   (ir-action "this" "Patrol" () :type "Crisp.Runtime.BtStatus"))
/// </code>
/// </summary>
public static class IrDeserializer
{
    /// <summary>
    /// S 式テキストから IR ノードを逆直列化する。
    /// </summary>
    /// <param name="text">IrSerializer が生成した S 式テキスト。</param>
    /// <returns>逆直列化された IR ノード。</returns>
    /// <exception cref="FormatException">入力テキストの形式が不正な場合。</exception>
    public static IrNode Deserialize(string text)
    {
        var reader = new SExprReader(text);
        var node = ReadNode(ref reader);
        return node;
    }

    /// <summary>
    /// S 式テキストから複数の IR ツリーを逆直列化する。
    /// </summary>
    /// <param name="text">IrSerializer が生成した S 式テキスト（複数ツリー対応）。</param>
    /// <returns>逆直列化された IR ツリーの不変配列。</returns>
    public static ImmutableArray<IrTree> DeserializeTrees(string text)
    {
        var reader = new SExprReader(text);
        var trees = ImmutableArray.CreateBuilder<IrTree>();

        while (reader.HasMore())
        {
            var node = ReadNode(ref reader);
            if (node is IrTree tree)
            {
                trees.Add(tree);
            }
        }

        return trees.ToImmutable();
    }

    // ═══════════════════════════════════════════════════════════
    //  ノード読み取り
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 次の IR ノードを読み取る。タグ名に応じてノード種別を決定する。
    /// </summary>
    private static IrNode ReadNode(ref SExprReader reader)
    {
        reader.Expect('(');
        var tag = reader.ReadAtom();

        IrNode node = tag switch
        {
            "ir-tree" => ReadTree(ref reader),
            "ir-selector" => ReadSelector(ref reader),
            "ir-sequence" => ReadSequence(ref reader),
            "ir-parallel" => ReadParallel(ref reader),
            "ir-condition" => ReadCondition(ref reader),
            "ir-action" => ReadAction(ref reader),
            "ir-guard" => ReadGuard(ref reader),
            "ir-if" => ReadIf(ref reader),
            "ir-invert" => ReadInvert(ref reader),
            "ir-repeat" => ReadRepeat(ref reader),
            "ir-timeout" => ReadFloatChild(ref reader, static (s, c) => new IrTimeout(s, c)),
            "ir-cooldown" => ReadFloatChild(ref reader, static (s, c) => new IrCooldown(s, c)),
            "ir-while" => ReadWhile(ref reader),
            "ir-reactive" => ReadReactive(ref reader),
            "ir-reactive-select" => ReadReactiveSelect(ref reader),
            "ir-tree-ref" => ReadTreeRef(ref reader),
            // 式ノード
            "ir-blackboard-load" => ReadBlackboardLoad(ref reader),
            "ir-member-load" => ReadMemberLoad(ref reader),
            "ir-literal" => ReadLiteral(ref reader),
            "ir-binary-op" => ReadBinaryOp(ref reader),
            "ir-unary-op" => ReadUnaryOp(ref reader),
            "ir-logic-op" => ReadLogicOp(ref reader),
            "ir-call" => ReadCall(ref reader),
            "ir-convert" => ReadConvert(ref reader),
            _ => throw new FormatException($"不明な IR タグ: '{tag}'"),
        };

        reader.Expect(')');
        return node;
    }

    /// <summary>次の IR 式ノードを読み取る。</summary>
    private static IrExpr ReadExpr(ref SExprReader reader)
    {
        var node = ReadNode(ref reader);
        if (node is IrExpr expr)
            return expr;
        throw new FormatException($"式ノードが期待されましたが、{node.GetType().Name} が見つかりました。");
    }

    // ──────────── ノード種別ごとの読み取り ────────────

    /// <summary>ツリー定義を読み取る。</summary>
    private static IrTree ReadTree(ref SExprReader reader)
    {
        var name = reader.ReadString();
        var body = ReadChildNodes(ref reader);
        return new IrTree(name, body);
    }

    /// <summary>セレクターノードを読み取る。</summary>
    private static IrSelector ReadSelector(ref SExprReader reader)
    {
        return new IrSelector(ReadChildNodes(ref reader));
    }

    /// <summary>シーケンスノードを読み取る。</summary>
    private static IrSequence ReadSequence(ref SExprReader reader)
    {
        return new IrSequence(ReadChildNodes(ref reader));
    }

    /// <summary>パラレルノードを読み取る。ポリシー（:any, :all, :n N）を含む。</summary>
    private static IrParallel ReadParallel(ref SExprReader reader)
    {
        var policyAtom = reader.ReadAtom();
        ParallelPolicy policy = policyAtom switch
        {
            ":any" => new ParallelPolicy.Any(),
            ":all" => new ParallelPolicy.All(),
            ":n" => new ParallelPolicy.N(int.Parse(reader.ReadAtom(), CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"不明な parallel ポリシー: '{policyAtom}'"),
        };
        return new IrParallel(policy, ReadChildNodes(ref reader));
    }

    /// <summary>条件ノードを読み取る。</summary>
    private static IrCondition ReadCondition(ref SExprReader reader)
    {
        return new IrCondition(ReadExpr(ref reader));
    }

    /// <summary>アクションノードを読み取る。</summary>
    private static IrAction ReadAction(ref SExprReader reader)
    {
        var declaringType = reader.ReadString();
        var methodName = reader.ReadString();
        var args = ReadExprList(ref reader);

        TypeRef? typeRef = null;
        if (reader.PeekIsKeyword(":type"))
        {
            reader.ReadAtom(); // ":type" を消費
            typeRef = new TypeRef(reader.ReadString());
        }

        return new IrAction(
            new MethodRef(declaringType, methodName, ImmutableArray<string>.Empty),
            args) { TypeRef = typeRef };
    }

    /// <summary>ガードノードを読み取る。</summary>
    private static IrGuard ReadGuard(ref SExprReader reader)
    {
        var condition = ReadExpr(ref reader);
        var body = ReadNode(ref reader);
        return new IrGuard(condition, body);
    }

    /// <summary>if ノードを読み取る。else 節はオプション。</summary>
    private static IrIf ReadIf(ref SExprReader reader)
    {
        var condition = ReadExpr(ref reader);
        var then = ReadNode(ref reader);
        IrNode? elseNode = null;
        if (reader.PeekIsOpen())
        {
            elseNode = ReadNode(ref reader);
        }
        return new IrIf(condition, then, elseNode);
    }

    /// <summary>反転ノードを読み取る。</summary>
    private static IrInvert ReadInvert(ref SExprReader reader)
    {
        return new IrInvert(ReadNode(ref reader));
    }

    /// <summary>リピートノードを読み取る。</summary>
    private static IrRepeat ReadRepeat(ref SExprReader reader)
    {
        var count = int.Parse(reader.ReadAtom(), CultureInfo.InvariantCulture);
        var child = ReadNode(ref reader);
        return new IrRepeat(count, child);
    }

    /// <summary>float 値 + 子ノードの共通読み取り（timeout, cooldown）。</summary>
    private static IrNode ReadFloatChild(ref SExprReader reader, Func<float, IrNode, IrNode> factory)
    {
        var value = float.Parse(reader.ReadAtom(), CultureInfo.InvariantCulture);
        var child = ReadNode(ref reader);
        return factory(value, child);
    }

    /// <summary>while ノードを読み取る。</summary>
    private static IrWhile ReadWhile(ref SExprReader reader)
    {
        var condition = ReadExpr(ref reader);
        var body = ReadNode(ref reader);
        return new IrWhile(condition, body);
    }

    /// <summary>リアクティブノードを読み取る。条件式とボディを含む。</summary>
    private static IrReactive ReadReactive(ref SExprReader reader)
    {
        var condition = ReadExpr(ref reader);
        var body = ReadNode(ref reader);
        return new IrReactive(condition, body);
    }

    /// <summary>リアクティブセレクターノードを読み取る。</summary>
    private static IrReactiveSelect ReadReactiveSelect(ref SExprReader reader)
    {
        return new IrReactiveSelect(ReadChildNodes(ref reader));
    }

    /// <summary>ツリー参照ノードを読み取る。</summary>
    private static IrTreeRef ReadTreeRef(ref SExprReader reader)
    {
        var name = reader.ReadString();
        return new IrTreeRef(name);
    }

    // ═══════════════════════════════════════════════════════════
    //  式ノード読み取り
    // ═══════════════════════════════════════════════════════════

    /// <summary>ブラックボードロード式を読み取る。</summary>
    private static IrBlackboardLoad ReadBlackboardLoad(ref SExprReader reader)
    {
        reader.Expect('(');
        var chain = ImmutableArray.CreateBuilder<string>();
        while (!reader.PeekIsClose())
        {
            chain.Add(reader.ReadString());
        }
        reader.Expect(')');

        reader.ReadAtom(); // ":type" を消費
        var typeRef = new TypeRef(reader.ReadString());

        return new IrBlackboardLoad(new MemberRef(chain.ToImmutable())) { TypeRef = typeRef };
    }

    /// <summary>メンバーロード式を読み取る。</summary>
    private static IrMemberLoad ReadMemberLoad(ref SExprReader reader)
    {
        // (ir-member-load ("chain1" "chain2") :type "typeName")
        reader.Expect('(');
        var chain = ImmutableArray.CreateBuilder<string>();
        while (!reader.PeekIsClose())
        {
            chain.Add(reader.ReadString());
        }
        reader.Expect(')');

        reader.ReadAtom(); // ":type" を消費
        var typeRef = new TypeRef(reader.ReadString());

        return new IrMemberLoad(new MemberRef(chain.ToImmutable())) { TypeRef = typeRef };
    }

    /// <summary>リテラル式を読み取る。値と型種別を復元する。</summary>
    private static IrLiteral ReadLiteral(ref SExprReader reader)
    {
        // (ir-literal <value> :<kind>)
        var valueStr = reader.ReadAtom();
        var kindStr = reader.ReadAtom(); // ":int", ":float", ":bool", ":string", ":null", ":enum"

        return kindStr switch
        {
            ":int" => new IrLiteral(int.Parse(valueStr, CultureInfo.InvariantCulture), LiteralKind.Int)
                { TypeRef = TypeRef.Int },
            ":float" => new IrLiteral(float.Parse(valueStr, CultureInfo.InvariantCulture), LiteralKind.Float)
                { TypeRef = TypeRef.Float },
            ":bool" => new IrLiteral(valueStr == "true", LiteralKind.Bool)
                { TypeRef = TypeRef.Bool },
            ":string" => new IrLiteral(UnescapeString(valueStr), LiteralKind.String)
                { TypeRef = TypeRef.String },
            ":null" => new IrLiteral(null, LiteralKind.Null)
                { TypeRef = new TypeRef("null") },
            ":enum" => ReadEnumLiteral(valueStr),
            _ => throw new FormatException($"不明なリテラル種別: '{kindStr}'"),
        };
    }

    /// <summary>enum リテラルを解析する。<c>::TypeName.MemberName</c> 形式。</summary>
    private static IrLiteral ReadEnumLiteral(string valueStr)
    {
        // "::AIState.Combat" → EnumRef("AIState", "Combat")
        var withoutPrefix = valueStr.StartsWith("::", StringComparison.Ordinal)
            ? valueStr.Substring(2)
            : valueStr;
        var dotIndex = withoutPrefix.IndexOf('.');
        if (dotIndex < 0)
            throw new FormatException($"不正な enum リテラル形式: '{valueStr}'");

        var typeName = withoutPrefix.Substring(0, dotIndex);
        var memberName = withoutPrefix.Substring(dotIndex + 1);
        var enumRef = new EnumRef(typeName, memberName);

        return new IrLiteral(enumRef, LiteralKind.Enum)
            { TypeRef = new TypeRef(typeName) };
    }

    /// <summary>二項演算式を読み取る。</summary>
    private static IrBinaryOp ReadBinaryOp(ref SExprReader reader)
    {
        var opStr = reader.ReadAtom();
        var lhs = ReadExpr(ref reader);
        var rhs = ReadExpr(ref reader);

        var op = ParseBinaryOp(opStr);
        var isComparison = op is BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le
            or BinaryOp.Ge or BinaryOp.Eq or BinaryOp.Ne;

        return new IrBinaryOp(op, lhs, rhs)
            { TypeRef = isComparison ? TypeRef.Bool : lhs.TypeRef };
    }

    /// <summary>単項演算式を読み取る。</summary>
    private static IrUnaryOp ReadUnaryOp(ref SExprReader reader)
    {
        var opStr = reader.ReadAtom();
        var operand = ReadExpr(ref reader);
        var op = opStr switch
        {
            ":not" => UnaryOp.Not,
            ":negate" => UnaryOp.Negate,
            _ => throw new FormatException($"不明な単項演算子: '{opStr}'"),
        };
        return new IrUnaryOp(op, operand)
            { TypeRef = op == UnaryOp.Not ? TypeRef.Bool : operand.TypeRef };
    }

    /// <summary>論理演算式を読み取る。</summary>
    private static IrLogicOp ReadLogicOp(ref SExprReader reader)
    {
        var opStr = reader.ReadAtom();
        var op = opStr switch
        {
            ":and" => LogicOp.And,
            ":or" => LogicOp.Or,
            _ => throw new FormatException($"不明な論理演算子: '{opStr}'"),
        };

        var operands = ImmutableArray.CreateBuilder<IrExpr>();
        while (reader.PeekIsOpen())
        {
            operands.Add(ReadExpr(ref reader));
        }

        return new IrLogicOp(op, operands.ToImmutable()) { TypeRef = TypeRef.Bool };
    }

    /// <summary>メソッド呼び出し式を読み取る。</summary>
    private static IrCall ReadCall(ref SExprReader reader)
    {
        var declaringType = reader.ReadString();
        var methodName = reader.ReadString();
        var args = ReadExprList(ref reader);

        reader.ReadAtom(); // ":type" を消費
        var typeRef = new TypeRef(reader.ReadString());

        return new IrCall(
            new MethodRef(declaringType, methodName, ImmutableArray<string>.Empty),
            args) { TypeRef = typeRef };
    }

    /// <summary>暗黙型変換式を読み取る。</summary>
    private static IrConvert ReadConvert(ref SExprReader reader)
    {
        var operand = ReadExpr(ref reader);
        reader.ReadAtom(); // ":to" を消費
        var targetType = new TypeRef(reader.ReadString());
        return new IrConvert(operand, targetType) { TypeRef = targetType };
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>閉じ括弧が来るまで子ノードを読み取る。</summary>
    private static ImmutableArray<IrNode> ReadChildNodes(ref SExprReader reader)
    {
        var children = ImmutableArray.CreateBuilder<IrNode>();
        while (reader.PeekIsOpen())
        {
            children.Add(ReadNode(ref reader));
        }
        return children.ToImmutable();
    }

    /// <summary>括弧で囲まれた式リストを読み取る。</summary>
    private static ImmutableArray<IrExpr> ReadExprList(ref SExprReader reader)
    {
        reader.Expect('(');
        var args = ImmutableArray.CreateBuilder<IrExpr>();
        while (!reader.PeekIsClose())
        {
            args.Add(ReadExpr(ref reader));
        }
        reader.Expect(')');
        return args.ToImmutable();
    }

    /// <summary>二項演算子文字列を BinaryOp 列挙値に変換する。</summary>
    private static BinaryOp ParseBinaryOp(string opStr) => opStr switch
    {
        ":add" => BinaryOp.Add,
        ":sub" => BinaryOp.Sub,
        ":mul" => BinaryOp.Mul,
        ":div" => BinaryOp.Div,
        ":mod" => BinaryOp.Mod,
        ":lt" => BinaryOp.Lt,
        ":gt" => BinaryOp.Gt,
        ":le" => BinaryOp.Le,
        ":ge" => BinaryOp.Ge,
        ":eq" => BinaryOp.Eq,
        ":ne" => BinaryOp.Ne,
        _ => throw new FormatException($"不明な二項演算子: '{opStr}'"),
    };

    /// <summary>エスケープされた文字列リテラルから引用符を除去する。</summary>
    private static string UnescapeString(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            return s.Substring(1, s.Length - 2);
        return s;
    }

    // ═══════════════════════════════════════════════════════════
    //  S式リーダー（軽量パーサー）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// S式テキストを逐次的に読み取る軽量リーダー。
    /// 空白・改行をスキップしながら、括弧・アトム・文字列を読み取る。
    /// ref struct として確保し、アロケーションを最小化する。
    /// </summary>
    private ref struct SExprReader
    {
        private readonly ReadOnlySpan<char> _text;
        private int _pos;

        public SExprReader(string text)
        {
            _text = text.AsSpan();
            _pos = 0;
        }

        /// <summary>まだ読み取る内容が残っているか。空白をスキップした上で判定する。</summary>
        public bool HasMore()
        {
            SkipWhitespace();
            return _pos < _text.Length;
        }

        /// <summary>次の文字が開き括弧 '(' であるかを確認する。位置は進めない。</summary>
        public bool PeekIsOpen()
        {
            SkipWhitespace();
            return _pos < _text.Length && _text[_pos] == '(';
        }

        /// <summary>次の文字が閉じ括弧 ')' であるかを確認する。位置は進めない。</summary>
        public bool PeekIsClose()
        {
            SkipWhitespace();
            return _pos >= _text.Length || _text[_pos] == ')';
        }

        /// <summary>次のアトムが指定キーワードであるかを確認する。位置は進めない。</summary>
        public bool PeekIsKeyword(string keyword)
        {
            SkipWhitespace();
            return _pos < _text.Length
                && _text.Slice(_pos).StartsWith(keyword.AsSpan(), StringComparison.Ordinal);
        }

        /// <summary>指定した文字を読み取る。一致しない場合は例外を投げる。</summary>
        public void Expect(char c)
        {
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != c)
            {
                var found = _pos < _text.Length ? _text[_pos].ToString() : "EOF";
                throw new FormatException($"'{c}' が期待されましたが、'{found}' が見つかりました（位置 {_pos}）。");
            }
            _pos++;
        }

        /// <summary>
        /// アトム（空白・括弧以外の文字列）を読み取る。
        /// 文字列リテラル（"..."）もアトムとして扱う。
        /// </summary>
        public string ReadAtom()
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
                throw new FormatException("予期しない入力終端。");

            // 文字列リテラル
            if (_text[_pos] == '"')
                return ReadQuotedString();

            // 通常のアトム
            var start = _pos;
            while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos])
                && _text[_pos] != '(' && _text[_pos] != ')')
            {
                _pos++;
            }

            if (_pos == start)
                throw new FormatException($"アトムが期待されましたが、'{_text[_pos]}' が見つかりました（位置 {_pos}）。");

            return _text.Slice(start, _pos - start).ToString();
        }

        /// <summary>引用符で囲まれた文字列を読み取る。</summary>
        public string ReadString()
        {
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != '"')
                throw new FormatException($"文字列が期待されましたが（位置 {_pos}）。");
            return UnescapeString(ReadQuotedString());
        }

        /// <summary>引用符付き文字列（"..."）を読み取る。エスケープシーケンスに対応する。</summary>
        private string ReadQuotedString()
        {
            _pos++; // 開始の '"' をスキップ
            var start = _pos;
            while (_pos < _text.Length && _text[_pos] != '"')
            {
                if (_text[_pos] == '\\')
                    _pos++; // エスケープ文字のスキップ
                _pos++;
            }

            var content = _text.Slice(start, _pos - start).ToString();
            if (_pos < _text.Length) _pos++; // 終了の '"' をスキップ
            return $"\"{content}\"";
        }

        /// <summary>空白文字（スペース、タブ、改行）をスキップする。</summary>
        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }
    }
}
