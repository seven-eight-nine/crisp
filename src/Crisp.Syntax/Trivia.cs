namespace Crisp.Syntax;

/// <summary>
/// セマンティックに無意味なトークン（空白、改行、コメント）を表す不変構造体。
/// 各 <see cref="Token"/> は先行トリビア（LeadingTrivia）と後続トリビア（TrailingTrivia）を保持する。
/// </summary>
/// <param name="Kind">トリビアの種別。</param>
/// <param name="Text">トリビアの原文テキスト。</param>
/// <param name="Span">ソーステキスト上の位置範囲。</param>
public readonly record struct Trivia(
    TriviaKind Kind,
    string Text,
    TextSpan Span);
