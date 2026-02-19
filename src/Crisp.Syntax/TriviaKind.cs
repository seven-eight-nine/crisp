namespace Crisp.Syntax;

/// <summary>
/// セマンティックに無意味なトークン（Trivia）の種別。
/// 空白、改行、コメント等が該当する。
/// CST ではトリビアを保持し、LSP やフォーマッタの基盤とする。
/// </summary>
public enum TriviaKind
{
    /// <summary>スペースやタブ等の水平空白。</summary>
    Whitespace,

    /// <summary>改行文字 <c>\n</c>（<c>\r\n</c> は正規化される）。</summary>
    Newline,

    /// <summary>行コメント。<c>;</c> から行末まで。</summary>
    Comment,
}
