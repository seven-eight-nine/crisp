namespace Crisp.Syntax;

/// <summary>
/// ソーステキスト上の位置範囲を表す不変構造体。
/// <see cref="Start"/> はテキスト先頭からのオフセット（0始まり）、
/// <see cref="Length"/> は範囲の文字数を示す。
/// </summary>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>範囲の終端位置（排他的）。</summary>
    public int End => Start + Length;

    public override string ToString() => $"[{Start}..{End})";
}
