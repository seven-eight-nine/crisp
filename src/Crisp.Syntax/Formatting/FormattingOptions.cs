namespace Crisp.Syntax.Formatting;

/// <summary>
/// Crisp DSL フォーマッタのオプション。
///
/// フォーマッタの挙動を制御するための設定値を保持する。
/// レコード型を使用し、不変性とデフォルト値付きの簡潔な定義を実現する。
/// </summary>
/// <param name="IndentSize">インデント幅（スペース数）。既定値は 2。</param>
/// <param name="MaxLineWidth">1行の最大文字数。この幅を超える場合は複数行に折り返す。既定値は 80。</param>
/// <param name="AlignCloseParen">
/// 閉じ括弧を独立した行に配置するかどうか。
/// <c>false</c>（既定値）の場合は Lisp 標準スタイル: 最後の子要素の行末に閉じ括弧を置く。
/// <c>true</c> の場合は開き括弧のインデントに揃えて閉じ括弧を配置する。
/// </param>
/// <param name="InsertBlankLineBetweenTrees">
/// トップレベルの tree 定義間に空行を挿入するかどうか。既定値は <c>true</c>。
/// </param>
public record FormattingOptions(
    int IndentSize = 2,
    int MaxLineWidth = 80,
    bool AlignCloseParen = false,
    bool InsertBlankLineBetweenTrees = true);
