using System.Collections.Immutable;

namespace Crisp.Syntax;

/// <summary>
/// Crisp DSL の字句トークンを表す不変構造体。
/// 各トークンは種別、原文テキスト、ソース上の位置、先行・後続トリビアを保持する。
/// </summary>
/// <param name="Kind">トークンの種別。</param>
/// <param name="Text">トークンの原文テキスト。</param>
/// <param name="Span">ソーステキスト上の位置範囲（トリビアを含まない）。</param>
/// <param name="LeadingTrivia">トークン直前の空白・コメント等。</param>
/// <param name="TrailingTrivia">トークン直後、次の改行まで（改行を含む）の空白・コメント等。</param>
public readonly record struct Token(
    TokenKind Kind,
    string Text,
    TextSpan Span,
    ImmutableArray<Trivia> LeadingTrivia,
    ImmutableArray<Trivia> TrailingTrivia)
{
    public override string ToString() => $"{Kind} \"{Text}\" {Span}";
}
