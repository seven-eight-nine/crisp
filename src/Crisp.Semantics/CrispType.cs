namespace Crisp.Semantics;

/// <summary>
/// Crisp DSL の型システムにおける型を表すレコード。
///
/// 基本型のシングルトン（<see cref="Int"/>, <see cref="Float"/> 等）を提供し、
/// Roslyn の <c>ITypeSymbol</c> から変換されたカスタム型もサポートする。
///
/// 型の同一性は <see cref="Name"/> の文字列比較で判定される。
///
/// 特殊な型:
/// <list type="bullet">
///   <item><description><see cref="Error"/>: 型エラーを表すセンチネル値。
///     親式への伝搬により、下流のカスケードエラーを抑制する。</description></item>
///   <item><description><see cref="Unknown"/>: 型が判定不能な場合のフォールバック。
///     スタンドアロンモード（Roslyn 未使用）で使用される。</description></item>
/// </list>
/// </summary>
public sealed record CrispType(string Name)
{
    // ═══════════════════════════════════════════════════════════
    //  基本型シングルトン
    // ═══════════════════════════════════════════════════════════

    /// <summary>整数型 <c>int</c>。整数リテラルおよび int 型プロパティの推論結果。</summary>
    public static readonly CrispType Int = new("int");

    /// <summary>浮動小数点型 <c>float</c>。浮動小数点リテラルおよび float 型プロパティの推論結果。</summary>
    public static readonly CrispType Float = new("float");

    /// <summary>真偽値型 <c>bool</c>。真偽値リテラル、比較演算、論理演算の推論結果。</summary>
    public static readonly CrispType Bool = new("bool");

    /// <summary>文字列型 <c>string</c>。文字列リテラルおよび string 型プロパティの推論結果。</summary>
    public static readonly CrispType String = new("string");

    /// <summary>null 型。null リテラルの推論結果。</summary>
    public static readonly CrispType Null = new("null");

    /// <summary>void 型。戻り値なしを表す。</summary>
    public static readonly CrispType Void = new("void");

    /// <summary>
    /// ビヘイビアツリーのステータス型。
    /// アクションメソッドの戻り値として要求される型。
    /// </summary>
    public static readonly CrispType BtStatus = new("Crisp.Runtime.BtStatus");

    /// <summary>
    /// ビヘイビアツリーのノード型。
    /// サブツリーを返すメソッドの戻り値として許容される型。
    /// </summary>
    public static readonly CrispType BtNode = new("Crisp.Runtime.BtNode");

    /// <summary>
    /// 型エラーを表すセンチネル値。
    /// 型推論中にエラーが検出された場合にこの型が割り当てられ、
    /// 親式に伝搬することでカスケードエラーの発生を抑制する。
    /// </summary>
    public static readonly CrispType Error = new("<error>");

    /// <summary>
    /// 型不明のフォールバック値。
    /// Roslyn シンボル情報が利用できない場合に使用される。
    /// </summary>
    public static readonly CrispType Unknown = new("<unknown>");

    // ═══════════════════════════════════════════════════════════
    //  判定メソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>この型がエラーセンチネルかどうか。</summary>
    public bool IsError => this == Error;

    /// <summary>この型が数値型（int または float）かどうか。</summary>
    public bool IsNumeric => this == Int || this == Float;

    /// <summary>表示用の型名を返す。</summary>
    public override string ToString() => Name;
}
