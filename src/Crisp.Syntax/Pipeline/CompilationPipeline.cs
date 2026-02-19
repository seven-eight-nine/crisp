using System.Collections.Immutable;
using Crisp.Runtime;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.Formatting;
using Crisp.Syntax.Interpreter;
using Crisp.Syntax.IR;

namespace Crisp.Syntax.Pipeline;

/// <summary>
/// Crisp DSL のコンパイルパイプラインを統合する便利クラス。
///
/// ソーステキストから各中間表現を経て、最終的な出力（C# コード、IR テキスト、直接実行）
/// までの全工程を一貫したインターフェースで提供する。
///
/// <code>
/// データフロー:
/// Source → Lexer → Parser/CST → AST Lowering → IR Lowering → C# Emitter / Evaluator
/// </code>
///
/// 各工程は個別にも利用可能であり、デバッグやテストで中間結果を検査できる。
/// </summary>
public static class CompilationPipeline
{
    // ═══════════════════════════════════════════════════════════
    //  個別ステージ
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストを解析して CST（具象構文木）と診断情報を返す。
    /// </summary>
    /// <param name="source">解析対象のソーステキスト。</param>
    /// <returns>
    /// CST プログラムと診断情報のタプル。
    /// 構文エラーがあっても CST は常に生成される（エラーノード含む）。
    /// </returns>
    public static (Cst.CstProgram Program, DiagnosticBag Diagnostics) Parse(string source)
    {
        return Parser.Parse(source);
    }

    /// <summary>
    /// トークン列を解析して CST（具象構文木）と診断情報を返す。
    /// Query DB 等で Lex と Parse を分離する場合に使用する。
    /// </summary>
    /// <param name="tokens">Lexer.Lex で生成されたトークン列。</param>
    /// <returns>
    /// CST プログラムと診断情報のタプル。
    /// 構文エラーがあっても CST は常に生成される（エラーノード含む）。
    /// </returns>
    public static (Cst.CstProgram Program, DiagnosticBag Diagnostics) Parse(ImmutableArray<Token> tokens)
    {
        return Parser.Parse(tokens);
    }

    /// <summary>
    /// CST プログラムを AST（抽象構文木）に lowering する。
    /// defdec 定義がある場合は展開パスを実行し、defmacro があればマクロ展開も行う。
    /// さらに ref 解決（F11）も実行する。
    /// </summary>
    /// <param name="program">lowering 対象の CST プログラム。</param>
    /// <param name="diagnostics">診断バッグ。null の場合は新規作成する。</param>
    /// <returns>AST ツリーの配列。</returns>
    public static ImmutableArray<AstTree> LowerToAst(Cst.CstProgram program, DiagnosticBag? diagnostics = null)
    {
        diagnostics ??= new DiagnosticBag();

        // defdec/defmacro の有無に応じて適切な lowering パスを選択
        if (program.Defmacros.Length > 0)
        {
            var (trees, defdecs, defmacros) = CstToAstLowering.LowerWithDefdecsAndDefmacros(program);

            // F10: マクロ展開
            trees = MacroExpander.Expand(trees, defmacros, diagnostics);

            // F2: defdec 展開
            if (defdecs.Length > 0)
                trees = AstDefdecExpander.Expand(trees, defdecs, diagnostics);

            // F11: ref 解決
            trees = AstRefResolver.Resolve(trees, diagnostics);

            return trees;
        }
        else if (program.Defdecs.Length > 0)
        {
            var (trees, defdecs) = CstToAstLowering.LowerWithDefdecs(program);

            // F2: defdec 展開
            trees = AstDefdecExpander.Expand(trees, defdecs, diagnostics);

            // F11: ref 解決
            trees = AstRefResolver.Resolve(trees, diagnostics);

            return trees;
        }
        else
        {
            var trees = CstToAstLowering.Lower(program);

            // F11: ref 解決
            trees = AstRefResolver.Resolve(trees, diagnostics);

            return trees;
        }
    }

    /// <summary>
    /// AST ツリー配列を IR（中間表現）に lowering する。
    /// 暗黙型変換（IrConvert）の自動挿入を含む。
    /// </summary>
    /// <param name="astTrees">lowering 対象の AST ツリー配列。</param>
    /// <returns>IR ツリーの配列。</returns>
    public static ImmutableArray<IrTree> LowerToIr(ImmutableArray<AstTree> astTrees)
    {
        return AstToIrLowering.Lower(astTrees);
    }

    /// <summary>
    /// IR ツリー配列を S 式テキストに直列化する。
    /// デバッグ、テスト、中間出力に使用する。
    /// </summary>
    /// <param name="irTrees">直列化対象の IR ツリー配列。</param>
    /// <returns>S 式テキスト。</returns>
    public static string SerializeIr(ImmutableArray<IrTree> irTrees)
    {
        return IrSerializer.Serialize(irTrees);
    }

    /// <summary>
    /// IR ツリーから C# ソースコードを生成する。
    /// </summary>
    /// <param name="tree">変換対象の IR ツリー。</param>
    /// <param name="namespaceName">生成クラスの名前空間。</param>
    /// <param name="className">生成クラスの名前。</param>
    /// <param name="methodName">BtNode を返すメソッドの名前。</param>
    /// <returns>C# ソースコード文字列。</returns>
    public static string EmitCSharp(IrTree tree, string namespaceName, string className, string methodName)
    {
        return CSharpEmitter.Emit(tree, namespaceName, className, methodName);
    }

    /// <summary>
    /// ソーステキストをフォーマットして返す。
    /// </summary>
    /// <param name="source">フォーマット対象のソーステキスト。</param>
    /// <param name="options">フォーマットオプション。null の場合は既定値を使用する。</param>
    /// <returns>フォーマット済みのテキスト。</returns>
    public static string Format(string source, FormattingOptions? options = null)
    {
        return Formatter.FormatSource(source, options);
    }

    // ═══════════════════════════════════════════════════════════
    //  全パイプライン（ソースから一括処理）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストから IR ツリー配列を生成する。
    /// Parse → AST Lowering（defdec/macro展開 + ref解決含む） → IR Lowering の全工程を実行する。
    /// </summary>
    /// <param name="source">コンパイル対象のソーステキスト。</param>
    /// <returns>IR ツリーの配列。</returns>
    public static ImmutableArray<IrTree> CompileToIr(string source)
    {
        var (program, diagnostics) = Parser.Parse(source);
        var astTrees = LowerToAst(program, diagnostics);
        return AstToIrLowering.Lower(astTrees);
    }

    /// <summary>
    /// ソーステキストから IR の S 式テキストを生成する。
    /// Parse → AST Lowering → IR Lowering → IR Serialization の全工程を実行する。
    /// </summary>
    /// <param name="source">コンパイル対象のソーステキスト。</param>
    /// <returns>IR の S 式テキスト。</returns>
    public static string CompileToIrText(string source)
    {
        var irTrees = CompileToIr(source);
        return IrSerializer.Serialize(irTrees);
    }

    /// <summary>
    /// ソーステキストから C# ソースコードを生成する。
    /// Parse → AST Lowering → IR Lowering → C# Emission の全工程を実行する。
    /// 複数ツリーがある場合は最初のツリーのみを変換する。
    /// </summary>
    /// <param name="source">コンパイル対象のソーステキスト。</param>
    /// <param name="namespaceName">生成クラスの名前空間。</param>
    /// <param name="className">生成クラスの名前。</param>
    /// <param name="methodName">BtNode を返すメソッドの名前。</param>
    /// <returns>C# ソースコード文字列。</returns>
    public static string CompileToCSharp(string source, string namespaceName, string className, string methodName)
    {
        var irTrees = CompileToIr(source);
        return CSharpEmitter.Emit(irTrees[0], namespaceName, className, methodName);
    }

    /// <summary>
    /// ソーステキストから IR を生成し、指定されたコンテキストオブジェクトで直接評価する。
    /// Parse → AST Lowering → IR Lowering → Evaluation の全工程を実行する。
    /// 複数ツリーがある場合は最初のツリーのみを評価する。
    /// </summary>
    /// <param name="source">コンパイル対象のソーステキスト。</param>
    /// <param name="context">評価に使用するコンテキストオブジェクト（this 相当）。</param>
    /// <param name="ctx">現在の tick のコンテキスト情報。</param>
    /// <returns>ビヘイビアツリーの評価結果。</returns>
    public static BtStatus Evaluate(string source, object context, TickContext ctx = default)
    {
        var irTrees = CompileToIr(source);
        var evaluator = new Evaluator(context);
        return evaluator.Tick(irTrees[0], ctx);
    }

    // ═══════════════════════════════════════════════════════════
    //  パイプライン結果（全中間表現を一度に取得）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// コンパイルパイプラインの全ステージの結果を保持するレコード。
    /// デバッグやテストで各中間表現を検査する場合に使用する。
    /// </summary>
    /// <param name="Source">入力ソーステキスト。</param>
    /// <param name="Program">CST プログラム。</param>
    /// <param name="Diagnostics">診断情報。</param>
    /// <param name="AstTrees">AST ツリー配列。</param>
    /// <param name="IrTrees">IR ツリー配列。</param>
    public record CompilationResult(
        string Source,
        Cst.CstProgram Program,
        DiagnosticBag Diagnostics,
        ImmutableArray<AstTree> AstTrees,
        ImmutableArray<IrTree> IrTrees);

    /// <summary>
    /// ソーステキストを全ステージでコンパイルし、各中間表現を含む結果を返す。
    /// 各ステージの結果を個別に検査する必要がある場合に使用する。
    /// defdec/macro 展開と ref 解決を含む。
    /// </summary>
    /// <param name="source">コンパイル対象のソーステキスト。</param>
    /// <returns>全ステージの中間結果を含む <see cref="CompilationResult"/>。</returns>
    public static CompilationResult Compile(string source)
    {
        var (program, diagnostics) = Parser.Parse(source);
        var astTrees = LowerToAst(program, diagnostics);
        var irTrees = AstToIrLowering.Lower(astTrees);
        return new CompilationResult(source, program, diagnostics, astTrees, irTrees);
    }
}
