using System.Text;
using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Formatting;

/// <summary>
/// Crisp DSL の CST フォーマッタ。
///
/// CST（具象構文木）に対して動作し、正規化されたフォーマットを出力する。
/// フォーマッタは以下のアルゴリズムで動作する:
///
/// <list type="number">
///   <item><description>
///     まずノードをフラット（1行）にフォーマットを試行する（<see cref="TryFlatFormat"/>）。
///   </description></item>
///   <item><description>
///     1行に収まる場合はフラット表示を採用する。
///   </description></item>
///   <item><description>
///     収まらない場合は、キーワードを先頭行に残し、子要素をインデント付きで
///     後続行に配置する複数行フォーマットを適用する（<see cref="MultiLineFormat"/>）。
///   </description></item>
/// </list>
///
/// コメントは元の位置を尊重して保持される。
/// 閉じ括弧は Lisp スタイル（最後の子要素の行末に配置）が既定。
/// </summary>
public sealed class Formatter
{
    private readonly FormattingOptions _options;

    /// <summary>
    /// 指定されたオプションでフォーマッタを生成する。
    /// </summary>
    /// <param name="options">フォーマットオプション。<c>null</c> の場合は既定値を使用する。</param>
    public Formatter(FormattingOptions? options = null)
    {
        _options = options ?? new FormattingOptions();
    }

    /// <summary>
    /// ソーステキストをパースしてフォーマット済みテキストを返す便利メソッド。
    /// </summary>
    /// <param name="source">フォーマット対象のソーステキスト。</param>
    /// <param name="options">フォーマットオプション。<c>null</c> の場合は既定値を使用する。</param>
    /// <returns>フォーマット済みのソーステキスト。</returns>
    public static string FormatSource(string source, FormattingOptions? options = null)
    {
        var (program, _) = Parser.Parse(source);
        var formatter = new Formatter(options);
        return formatter.Format(program);
    }

    /// <summary>
    /// CST プログラム全体をフォーマットする。
    ///
    /// 各 tree 定義をフォーマットし、オプションに応じて tree 間に空行を挿入する。
    /// プログラム先頭のコメント（EndOfFile トークンの LeadingTrivia に格納された末尾コメント含む）も保持する。
    /// </summary>
    /// <param name="program">フォーマット対象の CST プログラム。</param>
    /// <returns>フォーマット済みのテキスト。</returns>
    public string Format(CstProgram program)
    {
        var sb = new StringBuilder();

        // プログラム先頭のコメントを収集する（最初の tree の最初のトークンの LeadingTrivia）
        if (program.Trees.Length > 0)
        {
            var leadingComments = ExtractLeadingComments(program.Trees[0]);
            if (leadingComments.Length > 0)
            {
                sb.Append(leadingComments);
            }
        }

        for (var i = 0; i < program.Trees.Length; i++)
        {
            if (i > 0 && _options.InsertBlankLineBetweenTrees)
            {
                sb.AppendLine();
            }

            // tree 間のコメントを保持する（2番目以降の tree の先行コメント）
            if (i > 0)
            {
                var interTreeComments = ExtractLeadingComments(program.Trees[i]);
                if (interTreeComments.Length > 0)
                {
                    sb.Append(interTreeComments);
                }
            }

            sb.Append(FormatTree(program.Trees[i], 0));
            sb.AppendLine();
        }

        // EndOfFile トークンの LeadingTrivia に含まれるコメントを出力する
        var eofComments = ExtractCommentsFromTrivia(program.EndOfFile.LeadingTrivia);
        if (eofComments.Length > 0)
        {
            sb.Append(eofComments);
        }

        return sb.ToString();
    }

    /// <summary>
    /// tree ノードをフォーマットする。
    ///
    /// tree は常に複数行フォーマットで出力する（body が空でない限り）。
    /// <c>(tree Name</c> を先頭行に、body の各要素をインデントして後続行に配置する。
    /// </summary>
    /// <param name="tree">フォーマット対象の tree ノード。</param>
    /// <param name="indent">現在のインデントレベル（スペース数）。</param>
    /// <returns>フォーマット済みの tree テキスト。</returns>
    private string FormatTree(CstTree tree, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        // tree の body が空の場合はフラット表示
        if (tree.Body.Length == 0)
        {
            sb.Append($"{indentStr}(tree {tree.Name.Text})");
            return sb.ToString();
        }

        // 1行に収まるか試行する
        var flat = TryFlatFormatTree(tree);
        if (indent + flat.Length <= _options.MaxLineWidth)
        {
            sb.Append($"{indentStr}{flat}");
            return sb.ToString();
        }

        // 複数行フォーマット
        sb.Append($"{indentStr}(tree {tree.Name.Text}");

        for (var i = 0; i < tree.Body.Length; i++)
        {
            var child = tree.Body[i];

            // 子要素の先行コメントを出力する
            var childComments = ExtractLeadingComments(child);

            sb.AppendLine();

            if (childComments.Length > 0)
            {
                sb.Append(IndentComments(childComments, childIndent));
            }

            sb.Append(FormatNode(child, childIndent));

            // 最後の子要素の後に閉じ括弧を付ける（Lisp スタイル）
            if (i == tree.Body.Length - 1 && !_options.AlignCloseParen)
            {
                sb.Append(')');
            }
        }

        // AlignCloseParen が有効な場合は閉じ括弧を独立行に配置する
        if (_options.AlignCloseParen)
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// CST ノードをフォーマットする。
    ///
    /// ノードの種別に応じて適切なフォーマット処理にディスパッチする。
    /// まずフラットフォーマットを試行し、行幅に収まらなければ複数行フォーマットに切り替える。
    /// </summary>
    /// <param name="node">フォーマット対象のノード。</param>
    /// <param name="indent">現在のインデントレベル（スペース数）。</param>
    /// <returns>フォーマット済みのテキスト。</returns>
    private string FormatNode(CstNode node, int indent)
    {
        var indentStr = new string(' ', indent);

        // フラットフォーマットを試行する
        var flat = TryFlatFormat(node);
        if (indent + flat.Length <= _options.MaxLineWidth)
        {
            return $"{indentStr}{flat}";
        }

        // 複数行フォーマットにフォールバックする
        return MultiLineFormat(node, indent);
    }

    // ═══════════════════════════════════════════════════════════
    //  フラットフォーマット
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// tree ノードのフラット（1行）フォーマットを生成する。
    /// <c>(tree Name body...)</c> の形式。
    /// </summary>
    /// <param name="tree">フォーマット対象の tree ノード。</param>
    /// <returns>フラットフォーマットのテキスト。</returns>
    private string TryFlatFormatTree(CstTree tree)
    {
        var sb = new StringBuilder();
        sb.Append($"(tree {tree.Name.Text}");
        foreach (var child in tree.Body)
        {
            sb.Append(' ');
            sb.Append(TryFlatFormat(child));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// ノードをフラット（1行）にフォーマットする。
    ///
    /// 全ての子要素をスペースで連結し、括弧で囲む。
    /// コメントは含まない（フラット表示ではコメントを省略する）。
    /// 複数行フォーマットに切り替えるかどうかの判定にも使用される。
    /// </summary>
    /// <param name="node">フォーマット対象のノード。</param>
    /// <returns>フラットフォーマットのテキスト。</returns>
    private string TryFlatFormat(CstNode node)
    {
        return node switch
        {
            // リテラル・リーフノード — トークンのテキストをそのまま返す
            CstTokenNode tokenNode => tokenNode.Token.Text,
            CstMemberAccess memberAccess => memberAccess.Token.Text,
            CstMissing => "",
            CstError errorNode => string.Concat(errorNode.Tokens.Select(t => t.Text)),

            // 複合ノード — キーワードと子要素をスペースで連結する
            CstSelect select => FlatFormatWithKeywordAndChildren("select", select.Children_),
            CstSequence seq => FlatFormatWithKeywordAndChildren("seq", seq.Children_),
            CstParallel parallel => FlatFormatParallel(parallel),

            // デコレータノード
            CstCheck check => $"(check {TryFlatFormat(check.Expr)})",
            CstGuard guard => $"(guard {TryFlatFormat(guard.Condition)} {TryFlatFormat(guard.Body)})",
            CstIf ifNode => FlatFormatIf(ifNode),
            CstInvert invert => $"(invert {TryFlatFormat(invert.Child)})",
            CstRepeat repeat => $"(repeat {repeat.Count.Text} {TryFlatFormat(repeat.Child)})",
            CstTimeout timeout => $"(timeout {timeout.Seconds.Text} {TryFlatFormat(timeout.Child)})",
            CstCooldown cooldown => $"(cooldown {cooldown.Seconds.Text} {TryFlatFormat(cooldown.Child)})",
            CstWhile whileNode => $"(while {TryFlatFormat(whileNode.Condition)} {TryFlatFormat(whileNode.Body)})",

            // 呼び出しノード
            CstCall call => FlatFormatCall(call),

            // 式ノード
            CstBinaryExpr binary => $"({binary.Operator.Text} {TryFlatFormat(binary.Lhs)} {TryFlatFormat(binary.Rhs)})",
            CstUnaryExpr unary => $"({unary.Operator.Text} {TryFlatFormat(unary.Operand)})",
            CstLogicExpr logic => FlatFormatWithKeywordAndChildren(logic.Operator.Text, logic.Operands),

            // 不明なノード — DescendantTokens からテキストを再構成する
            _ => string.Concat(node.DescendantTokens().Select(t => t.Text)),
        };
    }

    /// <summary>
    /// キーワードと子要素リストのフラットフォーマットを生成するヘルパー。
    /// <c>(keyword child1 child2 ...)</c> の形式。
    /// </summary>
    private string FlatFormatWithKeywordAndChildren(string keyword,
        System.Collections.Immutable.ImmutableArray<CstNode> children)
    {
        var sb = new StringBuilder();
        sb.Append($"({keyword}");
        foreach (var child in children)
        {
            sb.Append(' ');
            sb.Append(TryFlatFormat(child));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// parallel ノードのフラットフォーマットを生成する。
    /// <c>(parallel :policy [n] child1 child2 ...)</c> の形式。
    /// </summary>
    private string FlatFormatParallel(CstParallel parallel)
    {
        var sb = new StringBuilder();
        sb.Append($"(parallel {parallel.Policy.Text}");
        if (parallel.PolicyArg.HasValue)
        {
            sb.Append($" {parallel.PolicyArg.Value.Text}");
        }
        foreach (var child in parallel.Children_)
        {
            sb.Append(' ');
            sb.Append(TryFlatFormat(child));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// if ノードのフラットフォーマットを生成する。
    /// else 分岐の有無に応じて形式を変える。
    /// </summary>
    private string FlatFormatIf(CstIf ifNode)
    {
        var sb = new StringBuilder();
        sb.Append($"(if {TryFlatFormat(ifNode.Condition)} {TryFlatFormat(ifNode.Then)}");
        if (ifNode.Else != null)
        {
            sb.Append($" {TryFlatFormat(ifNode.Else)}");
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// 呼び出しノードのフラットフォーマットを生成する。
    /// 引数がある場合は <c>(.Member arg1 arg2 ...)</c>、ない場合は <c>(.Member)</c>。
    /// </summary>
    private string FlatFormatCall(CstCall call)
    {
        var sb = new StringBuilder();
        sb.Append($"({call.Member.Token.Text}");
        foreach (var arg in call.Args)
        {
            sb.Append(' ');
            sb.Append(TryFlatFormat(arg));
        }
        sb.Append(')');
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //  複数行フォーマット
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ノードを複数行でフォーマットする。
    ///
    /// キーワードを先頭行に残し、子要素をインデント付きで後続行に配置する。
    /// 閉じ括弧は Lisp スタイルでは最後の子要素の行末に配置する。
    /// </summary>
    /// <param name="node">フォーマット対象のノード。</param>
    /// <param name="indent">現在のインデントレベル（スペース数）。</param>
    /// <returns>複数行フォーマット済みのテキスト。</returns>
    private string MultiLineFormat(CstNode node, int indent)
    {
        return node switch
        {
            CstSelect select => MultiLineFormatComposite("select", select.Children_, indent),
            CstSequence seq => MultiLineFormatComposite("seq", seq.Children_, indent),
            CstParallel parallel => MultiLineFormatParallel(parallel, indent),
            CstCheck check => MultiLineFormatCheck(check, indent),
            CstGuard guard => MultiLineFormatGuard(guard, indent),
            CstIf ifNode => MultiLineFormatIf(ifNode, indent),
            CstInvert invert => MultiLineFormatDecorator("invert", invert.Child, indent),
            CstRepeat repeat => MultiLineFormatWithParam("repeat", repeat.Count.Text, repeat.Child, indent),
            CstTimeout timeout => MultiLineFormatWithParam("timeout", timeout.Seconds.Text, timeout.Child, indent),
            CstCooldown cooldown => MultiLineFormatWithParam("cooldown", cooldown.Seconds.Text, cooldown.Child, indent),
            CstWhile whileNode => MultiLineFormatGuard(whileNode.Keyword.Text, whileNode.Condition, whileNode.Body, indent),
            CstCall call => MultiLineFormatCall(call, indent),
            CstBinaryExpr binary => MultiLineFormatBinary(binary, indent),
            CstUnaryExpr unary => MultiLineFormatDecorator(unary.Operator.Text, unary.Operand, indent),
            CstLogicExpr logic => MultiLineFormatComposite(logic.Operator.Text, logic.Operands, indent),

            // フラットでも収まらないが構造的に分割不可能なノード
            // — そのままフラット出力する（リテラル等）
            _ => $"{new string(' ', indent)}{TryFlatFormat(node)}",
        };
    }

    /// <summary>
    /// 複合ノード（select, seq, and, or 等）の複数行フォーマットを生成する。
    ///
    /// 先頭行にキーワードを配置し、子要素をインデントして後続行に配置する。
    /// 閉じ括弧は最後の子要素の行末に置く（Lisp スタイル）。
    /// </summary>
    private string MultiLineFormatComposite(string keyword,
        System.Collections.Immutable.ImmutableArray<CstNode> children, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}({keyword}");

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var childComments = ExtractLeadingComments(child);

            sb.AppendLine();

            if (childComments.Length > 0)
            {
                sb.Append(IndentComments(childComments, childIndent));
            }

            sb.Append(FormatNode(child, childIndent));

            // 最後の子要素の後に閉じ括弧を付ける
            if (i == children.Length - 1 && !_options.AlignCloseParen)
            {
                sb.Append(')');
            }
        }

        if (_options.AlignCloseParen)
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// parallel ノードの複数行フォーマットを生成する。
    /// <c>(parallel :policy [n]</c> を先頭行に、子要素をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatParallel(CstParallel parallel, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}(parallel {parallel.Policy.Text}");
        if (parallel.PolicyArg.HasValue)
        {
            sb.Append($" {parallel.PolicyArg.Value.Text}");
        }

        for (var i = 0; i < parallel.Children_.Length; i++)
        {
            var child = parallel.Children_[i];
            var childComments = ExtractLeadingComments(child);

            sb.AppendLine();

            if (childComments.Length > 0)
            {
                sb.Append(IndentComments(childComments, childIndent));
            }

            sb.Append(FormatNode(child, childIndent));

            if (i == parallel.Children_.Length - 1 && !_options.AlignCloseParen)
            {
                sb.Append(')');
            }
        }

        if (_options.AlignCloseParen)
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// check ノードの複数行フォーマットを生成する。
    /// check は子が1つなので、式が長い場合のみ複数行になる。
    /// </summary>
    private string MultiLineFormatCheck(CstCheck check, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}(check");
        sb.AppendLine();
        sb.Append(FormatNode(check.Expr, childIndent));

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// guard / while ノードの複数行フォーマットを生成する。
    /// 条件と本体をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatGuard(CstGuard guard, int indent)
    {
        return MultiLineFormatGuard(guard.Keyword.Text, guard.Condition, guard.Body, indent);
    }

    /// <summary>
    /// guard / while 形式（keyword condition body）の複数行フォーマットを生成するヘルパー。
    /// </summary>
    private string MultiLineFormatGuard(string keyword, CstNode condition, CstNode body, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        // 条件がフラットに収まる場合は先頭行に含める
        var flatCondition = TryFlatFormat(condition);
        var firstLine = $"({keyword} {flatCondition}";
        if (indent + firstLine.Length + 1 <= _options.MaxLineWidth)
        {
            sb.Append($"{indentStr}{firstLine}");
            sb.AppendLine();
        }
        else
        {
            sb.Append($"{indentStr}({keyword}");
            sb.AppendLine();
            sb.Append(FormatNode(condition, childIndent));
            sb.AppendLine();
        }

        sb.Append(FormatNode(body, childIndent));

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// if ノードの複数行フォーマットを生成する。
    /// condition, then, else の各要素をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatIf(CstIf ifNode, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        // 条件がフラットに収まる場合は先頭行に含める
        var flatCondition = TryFlatFormat(ifNode.Condition);
        var firstLine = $"(if {flatCondition}";
        if (indent + firstLine.Length + 1 <= _options.MaxLineWidth)
        {
            sb.Append($"{indentStr}{firstLine}");
        }
        else
        {
            sb.Append($"{indentStr}(if");
            sb.AppendLine();
            sb.Append(FormatNode(ifNode.Condition, childIndent));
        }

        sb.AppendLine();
        sb.Append(FormatNode(ifNode.Then, childIndent));

        if (ifNode.Else != null)
        {
            sb.AppendLine();
            sb.Append(FormatNode(ifNode.Else, childIndent));
        }

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 単一子デコレータ（invert, not 等）の複数行フォーマットを生成する。
    /// </summary>
    private string MultiLineFormatDecorator(string keyword, CstNode child, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}({keyword}");
        sb.AppendLine();
        sb.Append(FormatNode(child, childIndent));

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// パラメータ付きデコレータ（repeat, timeout, cooldown）の複数行フォーマットを生成する。
    /// <c>(keyword param</c> を先頭行に、子要素をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatWithParam(string keyword, string param, CstNode child, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}({keyword} {param}");
        sb.AppendLine();
        sb.Append(FormatNode(child, childIndent));

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 呼び出しノードの複数行フォーマットを生成する。
    /// <c>(.Member</c> を先頭行に、引数をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatCall(CstCall call, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        sb.Append($"{indentStr}({call.Member.Token.Text}");

        if (call.Args.Length == 0)
        {
            sb.Append(')');
            return sb.ToString();
        }

        for (var i = 0; i < call.Args.Length; i++)
        {
            sb.AppendLine();
            sb.Append(FormatNode(call.Args[i], childIndent));

            if (i == call.Args.Length - 1 && !_options.AlignCloseParen)
            {
                sb.Append(')');
            }
        }

        if (_options.AlignCloseParen)
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 二項式の複数行フォーマットを生成する。
    /// <c>(op</c> を先頭行に、左辺と右辺をインデントして後続行に配置する。
    /// </summary>
    private string MultiLineFormatBinary(CstBinaryExpr binary, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent);
        var childIndent = indent + _options.IndentSize;

        // 演算子と左辺がフラットに収まる場合は先頭行に含める
        var flatLhs = TryFlatFormat(binary.Lhs);
        var firstLine = $"({binary.Operator.Text} {flatLhs}";
        if (indent + firstLine.Length + 1 <= _options.MaxLineWidth)
        {
            sb.Append($"{indentStr}{firstLine}");
            sb.AppendLine();
        }
        else
        {
            sb.Append($"{indentStr}({binary.Operator.Text}");
            sb.AppendLine();
            sb.Append(FormatNode(binary.Lhs, childIndent));
            sb.AppendLine();
        }

        sb.Append(FormatNode(binary.Rhs, childIndent));

        if (!_options.AlignCloseParen)
        {
            sb.Append(')');
        }
        else
        {
            sb.AppendLine();
            sb.Append($"{indentStr})");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //  コメント（Trivia）処理ヘルパー
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ノードの先頭トークンから先行コメントを抽出する。
    ///
    /// ノードの最初のトークンの LeadingTrivia からコメント行を抽出する。
    /// コメントと改行のみを返し、空白は除外する。
    /// </summary>
    /// <param name="node">コメントを抽出する対象のノード。</param>
    /// <returns>コメントテキスト。コメントがない場合は空文字列。</returns>
    private static string ExtractLeadingComments(CstNode node)
    {
        var tokens = node.DescendantTokens().ToArray();
        if (tokens.Length == 0) return "";

        var firstToken = tokens[0];
        return ExtractCommentsFromTrivia(firstToken.LeadingTrivia);
    }

    /// <summary>
    /// Trivia 配列からコメントと改行を抽出する。
    /// </summary>
    /// <param name="trivia">Trivia の配列。</param>
    /// <returns>コメントテキスト（改行付き）。</returns>
    private static string ExtractCommentsFromTrivia(
        System.Collections.Immutable.ImmutableArray<Trivia> trivia)
    {
        var sb = new StringBuilder();
        foreach (var t in trivia)
        {
            if (t.Kind == TriviaKind.Comment)
            {
                sb.Append(t.Text);
            }
            else if (t.Kind == TriviaKind.Newline)
            {
                sb.Append(t.Text);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// コメントテキストの各行にインデントを付与する。
    /// </summary>
    /// <param name="comments">コメントテキスト。</param>
    /// <param name="indent">インデントレベル（スペース数）。</param>
    /// <returns>インデント付きのコメントテキスト。</returns>
    private static string IndentComments(string comments, int indent)
    {
        if (string.IsNullOrEmpty(comments)) return "";

        var indentStr = new string(' ', indent);
        var sb = new StringBuilder();
        var lines = comments.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (string.IsNullOrEmpty(line))
            {
                // 空行はそのまま出力する
                if (i < lines.Length - 1)
                {
                    sb.AppendLine();
                }
                continue;
            }

            sb.Append($"{indentStr}{line}");
            if (i < lines.Length - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //  構造判定ヘルパー
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ノード配列に複合（括弧付き）子要素が含まれるかどうかを判定する。
    ///
    /// 複合子要素とは、自身が括弧で囲まれた S 式であるノードのこと。
    /// リテラルやメンバーアクセスのようなリーフノードは複合ではない。
    /// 複合子要素がある場合、親ノードは可読性のために複数行フォーマットを採用する。
    /// </summary>
    /// <param name="children">判定対象の子ノード配列。</param>
    /// <returns>複合子要素が含まれる場合は <c>true</c>。</returns>
    private static bool HasCompoundChildren(
        System.Collections.Immutable.ImmutableArray<CstNode> children)
    {
        foreach (var child in children)
        {
            if (IsCompoundNode(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// ノードの子要素に複合ノードが含まれるかを、ノード種別に応じて判定する。
    ///
    /// tree 以外のノードに対して使用する。ノード種別ごとに子要素リストを取得して判定する。
    /// </summary>
    /// <param name="node">判定対象のノード。</param>
    /// <returns>複合子要素が含まれる場合は <c>true</c>。</returns>
    private static bool HasCompoundChildrenForNode(CstNode node)
    {
        return node switch
        {
            CstSelect select => HasCompoundChildren(select.Children_),
            CstSequence seq => HasCompoundChildren(seq.Children_),
            CstParallel parallel => HasCompoundChildren(parallel.Children_),
            CstLogicExpr logic => HasCompoundChildren(logic.Operands),
            CstCall call => HasCompoundChildren(call.Args),
            // check, guard, if, invert 等のデコレータノード:
            // 子が1-2個で、その子が複合なら複数行にする
            CstCheck check => IsCompoundNode(check.Expr),
            CstGuard guard => IsCompoundNode(guard.Condition) || IsCompoundNode(guard.Body),
            CstIf ifNode => IsCompoundNode(ifNode.Condition) || IsCompoundNode(ifNode.Then)
                            || (ifNode.Else != null && IsCompoundNode(ifNode.Else)),
            CstInvert invert => IsCompoundNode(invert.Child),
            CstRepeat repeat => IsCompoundNode(repeat.Child),
            CstTimeout timeout => IsCompoundNode(timeout.Child),
            CstCooldown cooldown => IsCompoundNode(cooldown.Child),
            CstWhile whileNode => IsCompoundNode(whileNode.Condition) || IsCompoundNode(whileNode.Body),
            CstBinaryExpr binary => IsCompoundNode(binary.Lhs) || IsCompoundNode(binary.Rhs),
            CstUnaryExpr unary => IsCompoundNode(unary.Operand),
            _ => false,
        };
    }

    /// <summary>
    /// ノードが複合（括弧付き S 式）であるかどうかを判定する。
    ///
    /// リテラル、メンバーアクセス、エラーノード以外の、
    /// 括弧で囲まれた構造を持つノードを複合ノードとみなす。
    /// </summary>
    /// <param name="node">判定対象のノード。</param>
    /// <returns>複合ノードの場合は <c>true</c>。</returns>
    private static bool IsCompoundNode(CstNode node)
    {
        return node switch
        {
            CstTokenNode => false,      // リテラル
            CstMemberAccess => false,   // メンバーアクセス
            CstMissing => false,        // 欠損ノード
            CstError => false,          // エラーノード
            _ => true,                  // それ以外は全て複合ノード
        };
    }
}
