using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// AST 式に対してボトムアップの型推論を行う。
///
/// リーフ式（リテラル、メンバーアクセス）から始めて、親式へ向かって型を伝搬する。
/// 型推論の結果は <see cref="SemanticModel"/> に格納される。
///
/// <para>
/// 推論規則:
/// <list type="bullet">
///   <item><description>リテラル: <c>LiteralKind</c> から直接マッピング</description></item>
///   <item><description>メンバーアクセス: 解決済みシンボルの型から決定</description></item>
///   <item><description>二項演算:
///     算術（+, -, *, /, %）は数値型を要求し、int+float → float の昇格を行う。
///     比較（&lt;, &gt;, &lt;=, &gt;=, ==, !=）は比較可能な型を要求し、結果は bool。</description></item>
///   <item><description>単項演算: not → bool、negate → オペランドの型</description></item>
///   <item><description>論理演算: 常に bool</description></item>
///   <item><description>メソッド呼び出し: 解決済みメソッドの戻り値型から決定</description></item>
/// </list>
/// </para>
///
/// <para>
/// ErrorType 伝搬（カスケード抑制）:
/// いずれかのサブ式が <see cref="CrispType.Error"/> の場合、親式も Error となり、
/// 追加の型エラー診断を報告しない。これにより、1つの根本エラーから連鎖する
/// 無意味なエラーメッセージの発生を防ぐ。
/// </para>
///
/// <para>
/// 報告する診断:
/// <list type="bullet">
///   <item><description>BS0003: 比較不可能な型</description></item>
///   <item><description>BS0004: 算術演算子が型に適用不可</description></item>
/// </list>
/// </para>
/// </summary>
public class TypeInferer
{
    private readonly SemanticModel _model;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>
    /// <see cref="TypeInferer"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="model">名前解決結果を含むセマンティックモデル。推論結果もここに格納される。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    public TypeInferer(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ツリー全体を走査し、全ての式に対して型推論を実行する。
    /// </summary>
    /// <param name="tree">型推論対象の AST ツリー。</param>
    public void InferTree(AstTree tree)
    {
        foreach (var node in tree.Body)
        {
            InferNode(node);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ノード走査
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ノードを再帰的に走査し、含まれる式の型を推論する。
    /// </summary>
    private void InferNode(AstNode node)
    {
        switch (node)
        {
            case AstSelector sel:
                foreach (var child in sel.Children_) InferNode(child);
                break;

            case AstSequence seq:
                foreach (var child in seq.Children_) InferNode(child);
                break;

            case AstParallel par:
                foreach (var child in par.Children_) InferNode(child);
                break;

            case AstCheck chk:
                InferExpr(chk.Expr);
                break;

            case AstGuard grd:
                InferExpr(grd.Condition);
                InferNode(grd.Body);
                break;

            case AstIf ifn:
                InferExpr(ifn.Condition);
                InferNode(ifn.Then);
                if (ifn.Else != null) InferNode(ifn.Else);
                break;

            case AstInvert inv:
                InferNode(inv.Child);
                break;

            case AstRepeat rep:
                InferNode(rep.Child);
                break;

            case AstTimeout tmo:
                InferNode(tmo.Child);
                break;

            case AstCooldown cld:
                InferNode(cld.Child);
                break;

            case AstWhile whl:
                InferExpr(whl.Condition);
                InferNode(whl.Body);
                break;

            case AstActionCall act:
                foreach (var arg in act.Args) InferExpr(arg);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  式の型推論
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST 式の型をボトムアップで推論し、<see cref="SemanticModel"/> に格納する。
    /// </summary>
    /// <param name="expr">推論対象の AST 式。</param>
    /// <returns>推論された型。</returns>
    private CrispType InferExpr(AstExpr expr)
    {
        var type = expr switch
        {
            AstLiteralExpr lit => InferLiteral(lit),
            AstMemberAccessExpr mem => InferMemberAccess(mem),
            AstBinaryExpr bin => InferBinary(bin),
            AstUnaryExpr un => InferUnary(un),
            AstLogicExpr log => InferLogic(log),
            AstCallExpr call => InferCall(call),
            _ => CrispType.Unknown,
        };

        _model.SetType(expr, type);
        return type;
    }

    /// <summary>
    /// リテラル式の型を推論する。
    /// <c>LiteralKind</c> から直接的にマッピングする。
    /// </summary>
    private static CrispType InferLiteral(AstLiteralExpr lit) => lit.LiteralKind switch
    {
        LiteralKind.Int => CrispType.Int,
        LiteralKind.Float => CrispType.Float,
        LiteralKind.Bool => CrispType.Bool,
        LiteralKind.String => CrispType.String,
        LiteralKind.Null => CrispType.Null,
        LiteralKind.Enum => CrispType.Unknown, // enum の型は名前解決が必要
        _ => CrispType.Unknown,
    };

    /// <summary>
    /// メンバーアクセス式の型を推論する。
    /// <see cref="SemanticModel"/> から解決済みシンボルを取得し、その型を変換する。
    /// シンボルが未解決の場合は <see cref="CrispType.Error"/> を返す。
    /// </summary>
    private CrispType InferMemberAccess(AstMemberAccessExpr mem)
    {
        var symbol = _model.GetSymbol(mem);
        if (symbol == null)
        {
            // 名前解決に失敗したメンバー → ErrorType（診断は NameResolver で報告済み）
            return CrispType.Error;
        }

        return NameResolver.ToCrispType(GetSymbolType(symbol));
    }

    /// <summary>
    /// 二項演算式の型を推論する。
    ///
    /// 算術演算子の場合:
    /// <list type="bullet">
    ///   <item><description>int + int → int</description></item>
    ///   <item><description>float + float → float</description></item>
    ///   <item><description>int + float → float（型昇格）</description></item>
    ///   <item><description>その他 → BS0004、ErrorType</description></item>
    /// </list>
    ///
    /// 比較演算子の場合:
    /// <list type="bullet">
    ///   <item><description>数値型同士 → bool</description></item>
    ///   <item><description>同一型同士（==, !=）→ bool</description></item>
    ///   <item><description>その他 → BS0003、ErrorType</description></item>
    /// </list>
    /// </summary>
    private CrispType InferBinary(AstBinaryExpr bin)
    {
        var lhsType = InferExpr(bin.Lhs);
        var rhsType = InferExpr(bin.Rhs);

        // ErrorType 伝搬（カスケード抑制）
        if (lhsType.IsError || rhsType.IsError)
        {
            return CrispType.Error;
        }

        if (IsArithmeticOp(bin.Op))
        {
            return InferArithmetic(bin, lhsType, rhsType);
        }
        else
        {
            return InferComparison(bin, lhsType, rhsType);
        }
    }

    /// <summary>
    /// 算術演算の結果型を推論する。
    /// 両オペランドが数値型でない場合は BS0004 を報告する。
    /// </summary>
    private CrispType InferArithmetic(AstBinaryExpr bin, CrispType lhs, CrispType rhs)
    {
        if (!lhs.IsNumeric)
        {
            var opName = BinaryOpToString(bin.Op);
            _diagnostics.Report(
                DiagnosticDescriptors.BS0004, bin.CstOrigin.Span, null,
                opName, lhs.Name);
            return CrispType.Error;
        }

        if (!rhs.IsNumeric)
        {
            var opName = BinaryOpToString(bin.Op);
            _diagnostics.Report(
                DiagnosticDescriptors.BS0004, bin.CstOrigin.Span, null,
                opName, rhs.Name);
            return CrispType.Error;
        }

        // 数値型昇格: int + float → float
        if (lhs == CrispType.Float || rhs == CrispType.Float)
            return CrispType.Float;

        return CrispType.Int;
    }

    /// <summary>
    /// 比較演算の結果型を推論する。
    /// 比較不可能な型の組み合わせの場合は BS0003 を報告する。
    /// </summary>
    private CrispType InferComparison(AstBinaryExpr bin, CrispType lhs, CrispType rhs)
    {
        // 同一型同士は常に比較可能
        if (lhs == rhs)
            return CrispType.Bool;

        // 数値型同士は比較可能（int ↔ float、型昇格あり）
        if (lhs.IsNumeric && rhs.IsNumeric)
            return CrispType.Bool;

        // それ以外は比較不可能
        _diagnostics.Report(
            DiagnosticDescriptors.BS0003, bin.CstOrigin.Span, null,
            lhs.Name, rhs.Name);
        return CrispType.Error;
    }

    /// <summary>
    /// 単項演算式の型を推論する。
    /// <c>not</c> → bool、<c>negate</c> → オペランドの型。
    /// </summary>
    private CrispType InferUnary(AstUnaryExpr un)
    {
        var operandType = InferExpr(un.Operand);
        if (operandType.IsError) return CrispType.Error;

        return un.Op == UnaryOp.Not ? CrispType.Bool : operandType;
    }

    /// <summary>
    /// 論理演算式の型を推論する。結果は常に bool。
    /// </summary>
    private CrispType InferLogic(AstLogicExpr log)
    {
        var hasError = false;
        foreach (var operand in log.Operands)
        {
            var type = InferExpr(operand);
            if (type.IsError) hasError = true;
        }

        return hasError ? CrispType.Error : CrispType.Bool;
    }

    /// <summary>
    /// メソッド呼び出し式の型を推論する。
    /// 解決済みメソッドの戻り値型から決定する。
    /// </summary>
    private CrispType InferCall(AstCallExpr call)
    {
        // 引数の型も推論する
        foreach (var arg in call.Args) InferExpr(arg);

        var symbol = _model.GetSymbol(call);
        if (symbol is IMethodSymbol method)
        {
            return NameResolver.ToCrispType(method.ReturnType);
        }

        // メソッドが未解決 → ErrorType
        return CrispType.Error;
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// シンボルの型を取得する。プロパティはその型、フィールドはその型、メソッドは戻り値型。
    /// </summary>
    private static ITypeSymbol GetSymbolType(ISymbol symbol) => symbol switch
    {
        IPropertySymbol prop => prop.Type,
        IFieldSymbol field => field.Type,
        IMethodSymbol method => method.ReturnType,
        _ => null!,
    };

    /// <summary>二項演算子が算術演算子かどうかを判定する。</summary>
    private static bool IsArithmeticOp(BinaryOp op) => op switch
    {
        BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Mod => true,
        _ => false,
    };

    /// <summary>二項演算子を文字列表現に変換する。</summary>
    private static string BinaryOpToString(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Sub => "-",
        BinaryOp.Mul => "*",
        BinaryOp.Div => "/",
        BinaryOp.Mod => "%",
        BinaryOp.Lt => "<",
        BinaryOp.Gt => ">",
        BinaryOp.Le => "<=",
        BinaryOp.Ge => ">=",
        BinaryOp.Eq => "==",
        BinaryOp.Ne => "!=",
        _ => op.ToString(),
    };
}
