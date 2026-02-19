using System.Collections.Immutable;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// Nullable フロー解析を行うアナライザー。
///
/// DSL 内のメンバーアクセスに対して、null 参照の可能性を検出し、
/// ガード条件による null 状態の絞り込み（narrowing）をサポートする。
///
/// <para>
/// 報告する診断:
/// <list type="bullet">
///   <item><description>BS0044: null の可能性があるメンバーの逆参照（警告）</description></item>
///   <item><description>BS0045: null との比較が常に true（警告）</description></item>
///   <item><description>BS0046: null との比較が常に false（警告）</description></item>
///   <item><description>BS0047: 非 nullable メンバーへの不要な null チェック（情報）</description></item>
/// </list>
/// </para>
///
/// <para>
/// 設計方針:
/// <list type="bullet">
///   <item><description>Roslyn の <c>NullableAnnotation</c> を使用して、メンバーの null 許容性を判定する</description></item>
///   <item><description>ガード条件（<c>guard</c>, <c>if</c>）内の null チェックを検出し、
///     body 内ではメンバーを非 null として扱う</description></item>
///   <item><description><see cref="NullState"/> で各メンバーの null 状態を追跡する</description></item>
/// </list>
/// </para>
/// </summary>
public static class NullableAnalyzer
{
    // ═══════════════════════════════════════════════════════════
    //  Null 状態追跡
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 各メンバーの null 状態を追跡するイミュータブルな状態オブジェクト。
    ///
    /// メンバーパス（<c>"Target"</c>, <c>"Target.Position"</c> 等）をキーとして、
    /// そのメンバーが非 null であることが確認されたかどうかを記録する。
    ///
    /// ガード条件による絞り込み後の状態は、新しい <see cref="NullState"/> インスタンスとして返され、
    /// 元のインスタンスは変更されない（イミュータブル）。
    /// </summary>
    public class NullState
    {
        /// <summary>非 null であることが確認されたメンバーパスのセット。</summary>
        private readonly ImmutableHashSet<string> _knownNonNull;

        /// <summary>常に null であることが確認されたメンバーパスのセット。</summary>
        private readonly ImmutableHashSet<string> _knownNull;

        /// <summary>
        /// 空の null 状態を持つ初期インスタンス。
        /// 解析開始時にこのインスタンスを使用する。
        /// </summary>
        public static readonly NullState Empty = new(
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        /// <summary>
        /// <see cref="NullState"/> の新しいインスタンスを初期化する。
        /// </summary>
        /// <param name="knownNonNull">非 null と確認済みのメンバーパスのセット。</param>
        /// <param name="knownNull">null と確認済みのメンバーパスのセット。</param>
        private NullState(ImmutableHashSet<string> knownNonNull, ImmutableHashSet<string> knownNull)
        {
            _knownNonNull = knownNonNull;
            _knownNull = knownNull;
        }

        /// <summary>
        /// 指定されたメンバーパスが非 null であることが確認されているかどうかを返す。
        /// </summary>
        /// <param name="memberPath">確認対象のメンバーパス文字列。</param>
        /// <returns>非 null が確認されている場合 <c>true</c>。</returns>
        public bool IsKnownNonNull(string memberPath) => _knownNonNull.Contains(memberPath);

        /// <summary>
        /// 指定されたメンバーパスが null であることが確認されているかどうかを返す。
        /// </summary>
        /// <param name="memberPath">確認対象のメンバーパス文字列。</param>
        /// <returns>null が確認されている場合 <c>true</c>。</returns>
        public bool IsKnownNull(string memberPath) => _knownNull.Contains(memberPath);

        /// <summary>
        /// メンバーパスを非 null として記録した新しい状態を返す。
        /// </summary>
        /// <param name="memberPath">非 null として記録するメンバーパス。</param>
        /// <returns>更新された新しい <see cref="NullState"/>。</returns>
        public NullState WithNonNull(string memberPath)
        {
            return new NullState(
                _knownNonNull.Add(memberPath),
                _knownNull.Remove(memberPath));
        }

        /// <summary>
        /// メンバーパスを null として記録した新しい状態を返す。
        /// </summary>
        /// <param name="memberPath">null として記録するメンバーパス。</param>
        /// <returns>更新された新しい <see cref="NullState"/>。</returns>
        public NullState WithNull(string memberPath)
        {
            return new NullState(
                _knownNonNull.Remove(memberPath),
                _knownNull.Add(memberPath));
        }

        /// <summary>
        /// 非 null と確認済みのメンバーパスの数を返す（テスト・デバッグ用）。
        /// </summary>
        public int KnownNonNullCount => _knownNonNull.Count;

        /// <summary>
        /// null と確認済みのメンバーパスの数を返す（テスト・デバッグ用）。
        /// </summary>
        public int KnownNullCount => _knownNull.Count;
    }

    // ═══════════════════════════════════════════════════════════
    //  条件式からの null 状態絞り込み
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ガード条件や if 条件から null 状態を絞り込む。
    ///
    /// 以下のパターンを検出する:
    /// <list type="bullet">
    ///   <item><description><c>(!= .Member null)</c> → <c>.Member</c> は非 null</description></item>
    ///   <item><description><c>(== .Member null)</c> → <c>.Member</c> は null（否定分岐用）</description></item>
    ///   <item><description><c>(not (== .Member null))</c> → <c>.Member</c> は非 null</description></item>
    /// </list>
    /// </summary>
    /// <param name="condition">解析対象の条件式。</param>
    /// <param name="current">現在の null 状態。</param>
    /// <returns>条件が true の場合に適用される絞り込み後の null 状態。</returns>
    public static NullState NarrowFromCondition(AstExpr condition, NullState current)
    {
        switch (condition)
        {
            // (not expr) — 内部の条件を反転して絞り込む
            case AstUnaryExpr { Op: UnaryOp.Not } unary:
                return NarrowFromNegatedCondition(unary.Operand, current);

            // (!= .Member null) — メンバーが非 null
            case AstBinaryExpr { Op: BinaryOp.Ne } binary:
                return NarrowFromNullComparison(binary, isNotEqual: true, current);

            // (== .Member null) — この条件が true ならメンバーは null
            case AstBinaryExpr { Op: BinaryOp.Eq } binary:
                return NarrowFromNullEqualityTrue(binary, current);

            // (and cond1 cond2 ...) — 全条件の絞り込みを累積
            case AstLogicExpr { Op: LogicOp.And } logic:
            {
                var state = current;
                foreach (var operand in logic.Operands)
                {
                    state = NarrowFromCondition(operand, state);
                }
                return state;
            }

            default:
                return current;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Nullable 判定
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Roslyn シンボルの型が null 許容かどうかを判定する。
    ///
    /// <c>NullableAnnotation.Annotated</c> の場合（<c>string?</c>, <c>int?</c> 等）に
    /// null 許容と判定する。参照型で <c>NullableAnnotation.None</c>（nullable context 外）の
    /// 場合も、保守的に null 許容と見なす。
    /// </summary>
    /// <param name="symbol">判定対象のシンボル。</param>
    /// <returns>null 許容の場合 <c>true</c>。</returns>
    public static bool IsNullableSymbol(ISymbol symbol)
    {
        var type = GetSymbolType(symbol);
        if (type == null) return false;

        // 明示的に Annotated の場合（string?, int? 等）
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        // 参照型で nullable annotation がない場合（nullable context 外）
        // 保守的に null 許容と見なす
        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.None)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Roslyn シンボルの型が確実に非 null かどうかを判定する。
    ///
    /// <c>NullableAnnotation.NotAnnotated</c> の場合（nullable context 内の
    /// <c>string</c>, <c>int</c> 等）に非 null と判定する。
    /// 値型（<c>Nullable&lt;T&gt;</c> 以外）も非 null と判定する。
    /// </summary>
    /// <param name="symbol">判定対象のシンボル。</param>
    /// <returns>確実に非 null の場合 <c>true</c>。</returns>
    public static bool IsDefinitelyNonNull(ISymbol symbol)
    {
        var type = GetSymbolType(symbol);
        if (type == null) return false;

        // 値型で Nullable<T> でない場合は常に非 null
        if (type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            return true;
        }

        // nullable context 内で NotAnnotated の場合は非 null
        if (type.NullableAnnotation == NullableAnnotation.NotAnnotated)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// null 比較式に対して、比較が常に true/false になるかを検出し、
    /// 該当する場合は BS0045/BS0046/BS0047 診断を報告する。
    ///
    /// <list type="bullet">
    ///   <item><description>非 nullable メンバーと null の <c>==</c> 比較 → 常に false（BS0046）</description></item>
    ///   <item><description>非 nullable メンバーと null の <c>!=</c> 比較 → 常に true（BS0045）</description></item>
    ///   <item><description>非 nullable メンバーに対する null チェック → 不要（BS0047）</description></item>
    /// </list>
    /// </summary>
    /// <param name="expr">検査対象の二項式。</param>
    /// <param name="model">解決済みシンボルを含むセマンティックモデル。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    public static void CheckNullComparison(
        AstBinaryExpr expr,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (expr.Op != BinaryOp.Eq && expr.Op != BinaryOp.Ne) return;

        // (op .Member null) または (op null .Member) のパターンを検出
        var (memberExpr, isNullOnRight) = ExtractNullComparison(expr);
        if (memberExpr == null) return;

        // メンバーのシンボルを取得
        var symbol = model.GetSymbol(memberExpr);
        if (symbol == null) return;

        var memberName = GetMemberDisplayName(memberExpr);

        if (IsDefinitelyNonNull(symbol))
        {
            if (expr.Op == BinaryOp.Eq)
            {
                // (== nonNullable null) → 常に false
                diagnostics.Report(
                    DiagnosticDescriptors.BS0046, expr.CstOrigin.Span, null,
                    memberName);
            }
            else
            {
                // (!= nonNullable null) → 常に true
                diagnostics.Report(
                    DiagnosticDescriptors.BS0045, expr.CstOrigin.Span, null,
                    memberName);
            }

            // 非 nullable メンバーへの不要な null チェック
            diagnostics.Report(
                DiagnosticDescriptors.BS0047, expr.CstOrigin.Span, null,
                memberName);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 否定された条件式から null 状態を絞り込む。
    ///
    /// <c>(not (== .Member null))</c> の場合、<c>.Member</c> は非 null と判定する。
    /// </summary>
    private static NullState NarrowFromNegatedCondition(AstExpr inner, NullState current)
    {
        switch (inner)
        {
            // (not (== .Member null)) → メンバーは非 null
            case AstBinaryExpr { Op: BinaryOp.Eq } binary:
                return NarrowFromNullComparison(binary, isNotEqual: true, current);

            // (not (!= .Member null)) → メンバーは null
            case AstBinaryExpr { Op: BinaryOp.Ne } binary:
                return NarrowFromNullEqualityTrue(binary, current);

            default:
                return current;
        }
    }

    /// <summary>
    /// null 比較式（<c>!= null</c>）から非 null 情報を抽出して状態を更新する。
    /// </summary>
    private static NullState NarrowFromNullComparison(
        AstBinaryExpr binary,
        bool isNotEqual,
        NullState current)
    {
        var (memberExpr, _) = ExtractNullComparison(binary);
        if (memberExpr == null) return current;

        var path = GetMemberDisplayName(memberExpr);
        return isNotEqual ? current.WithNonNull(path) : current;
    }

    /// <summary>
    /// null 等値比較式（<c>== null</c>）が true の場合の状態を返す。
    /// </summary>
    private static NullState NarrowFromNullEqualityTrue(AstBinaryExpr binary, NullState current)
    {
        var (memberExpr, _) = ExtractNullComparison(binary);
        if (memberExpr == null) return current;

        var path = GetMemberDisplayName(memberExpr);
        return current.WithNull(path);
    }

    /// <summary>
    /// 二項式から null 比較のメンバー式を抽出する。
    ///
    /// <c>(op .Member null)</c> または <c>(op null .Member)</c> のパターンを検出し、
    /// メンバー式と null リテラルの位置を返す。
    /// </summary>
    /// <returns>
    /// メンバーアクセス式と null が右辺にあるかのフラグ。
    /// null 比較パターンでない場合は <c>(null, false)</c>。
    /// </returns>
    private static (AstMemberAccessExpr? Member, bool NullOnRight) ExtractNullComparison(AstBinaryExpr binary)
    {
        // (op .Member null)
        if (binary.Lhs is AstMemberAccessExpr leftMember
            && binary.Rhs is AstLiteralExpr { LiteralKind: LiteralKind.Null })
        {
            return (leftMember, true);
        }

        // (op null .Member)
        if (binary.Lhs is AstLiteralExpr { LiteralKind: LiteralKind.Null }
            && binary.Rhs is AstMemberAccessExpr rightMember)
        {
            return (rightMember, false);
        }

        return (null, false);
    }

    /// <summary>
    /// メンバーアクセス式の表示名を取得する。
    /// メンバーパスのセグメントをドットで連結した文字列を返す。
    /// </summary>
    private static string GetMemberDisplayName(AstMemberAccessExpr expr)
    {
        return expr.Path.ToString();
    }

    /// <summary>
    /// シンボルの型情報を取得する。
    /// プロパティ、フィールド、メソッド（戻り値型）に対応する。
    /// </summary>
    private static ITypeSymbol? GetSymbolType(ISymbol symbol)
    {
        return symbol switch
        {
            IPropertySymbol prop => prop.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null,
        };
    }
}
