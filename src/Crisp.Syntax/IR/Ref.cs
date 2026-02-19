using System.Collections.Immutable;

namespace Crisp.Syntax.IR;

/// <summary>
/// メソッドへの参照。宣言型名・メソッド名・パラメータ型名で一意に識別する。
/// Roslyn シンボルに依存しない文字列ベースの参照。
///
/// 例: <c>MethodRef("EnemyAI", "Attack", ["Entity"])</c>
/// </summary>
public record MethodRef(string DeclaringType, string Name, ImmutableArray<string> ParamTypes);

/// <summary>
/// メンバー（プロパティ・フィールド）チェーンへの参照。
/// ドットで連鎖するメンバーアクセスを文字列の配列で表現する。
///
/// 例: <c>MemberRef(["Target", "Position", "x"])</c>
/// → <c>this.Target.Position.x</c> に対応する参照。
/// </summary>
public record MemberRef(ImmutableArray<string> Chain);

/// <summary>
/// 型へのの参照。完全修飾名で一意に識別する。
/// Roslyn の ITypeSymbol に依存しない文字列ベースの参照。
///
/// 基本型のシングルトンを提供し、IR 全体で型参照の一貫性を保つ。
/// </summary>
public record TypeRef(string FullName)
{
    /// <summary>整数型 <c>int</c> への参照。</summary>
    public static readonly TypeRef Int = new("int");

    /// <summary>浮動小数点型 <c>float</c> への参照。</summary>
    public static readonly TypeRef Float = new("float");

    /// <summary>真偽値型 <c>bool</c> への参照。</summary>
    public static readonly TypeRef Bool = new("bool");

    /// <summary>文字列型 <c>string</c> への参照。</summary>
    public static readonly TypeRef String = new("string");

    /// <summary>void 型への参照。戻り値なしを表す。</summary>
    public static readonly TypeRef Void = new("void");

    /// <summary>ビヘイビアツリーのステータス型への参照。</summary>
    public static readonly TypeRef BtStatus = new("Crisp.Runtime.BtStatus");

    /// <summary>ビヘイビアツリーのノード型への参照。サブツリー埋め込み用。</summary>
    public static readonly TypeRef BtNode = new("Crisp.Runtime.BtNode");
}
