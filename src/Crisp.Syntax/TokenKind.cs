namespace Crisp.Syntax;

/// <summary>
/// Crisp DSL のトークン種別。
/// レクサーはソーステキストをこの種別に分類する。
/// </summary>
public enum TokenKind
{
    // ──────────── 区切り ────────────

    /// <summary>開き括弧 <c>(</c></summary>
    LeftParen,

    /// <summary>閉じ括弧 <c>)</c></summary>
    RightParen,

    // ──────────── リテラル ────────────

    /// <summary>整数リテラル。負号を含む場合がある。例: <c>42</c>, <c>-3</c></summary>
    IntLiteral,

    /// <summary>浮動小数点リテラル。負号を含む場合がある。例: <c>3.14</c>, <c>-0.5</c></summary>
    FloatLiteral,

    /// <summary>文字列リテラル。ダブルクォートで囲まれる。例: <c>"hello"</c></summary>
    StringLiteral,

    /// <summary>真偽リテラル <c>true</c></summary>
    BoolTrue,

    /// <summary>真偽リテラル <c>false</c></summary>
    BoolFalse,

    /// <summary>null リテラル <c>null</c></summary>
    NullLiteral,

    // ──────────── 識別子 ────────────

    /// <summary>
    /// 識別子。先頭は英字または <c>_</c>、以降は英数字・<c>_</c>・<c>-</c> を許容する。
    /// 予約語（select, seq 等）もレクサー段階では Identifier として認識する。
    /// </summary>
    Identifier,

    /// <summary>
    /// メンバーアクセス。<c>.</c> で始まる識別子チェーン。
    /// レクサーは <c>.Foo.Bar.Baz</c> を単一トークンとして認識する。
    /// </summary>
    MemberAccess,

    /// <summary>
    /// enum リテラル。<c>::</c> で始まり、型名と <c>.</c> でメンバー名が続く。
    /// 例: <c>::AIState.Combat</c>
    /// </summary>
    EnumLiteral,

    // ──────────── キーワード引数 ────────────

    /// <summary>
    /// キーワード引数。<c>:</c> で始まる識別子。
    /// 例: <c>:all</c>, <c>:any</c>, <c>:n</c>
    /// </summary>
    Keyword,

    // ──────────── 演算子 ────────────

    /// <summary>加算演算子 <c>+</c></summary>
    Plus,

    /// <summary>減算演算子 <c>-</c></summary>
    Minus,

    /// <summary>乗算演算子 <c>*</c></summary>
    Star,

    /// <summary>除算演算子 <c>/</c></summary>
    Slash,

    /// <summary>剰余演算子 <c>%</c></summary>
    Percent,

    /// <summary>小なり比較 <c>&lt;</c></summary>
    LessThan,

    /// <summary>大なり比較 <c>&gt;</c></summary>
    GreaterThan,

    /// <summary>以下比較 <c>&lt;=</c></summary>
    LessEqual,

    /// <summary>以上比較 <c>&gt;=</c></summary>
    GreaterEqual,

    /// <summary>等値比較 <c>=</c></summary>
    Equal,

    /// <summary>非等値比較 <c>!=</c></summary>
    NotEqual,

    // ──────────── 拡張 ────────────

    /// <summary>
    /// ブラックボードアクセス（F3）。<c>$</c> で始まるメンバーチェーン。
    /// レクサーは <c>$.Foo.Bar</c> を単一トークンとして認識する。
    /// </summary>
    BlackboardAccess,

    /// <summary>
    /// <c>&lt;body&gt;</c> プレースホルダ（F2: defdec 用）。
    /// defdec のテンプレート body 内で使用場所を明示する専用トークン。
    /// </summary>
    BodyPlaceholder,

    // ──────────── 特殊 ────────────

    /// <summary>ファイル終端。</summary>
    EndOfFile,

    /// <summary>レクサーが認識できなかった文字列。エラー回復で使用する。</summary>
    Error,
}
