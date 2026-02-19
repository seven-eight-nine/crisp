using System.Collections.Immutable;

namespace Crisp.Syntax;

/// <summary>
/// コンパイル時の診断メッセージ（エラー・警告・情報）を表す。
/// ソース上の位置情報とフォーマット引数を保持し、ユーザー向けメッセージを生成できる。
/// </summary>
public record Diagnostic(
    DiagnosticDescriptor Descriptor,
    TextSpan Span,
    string? FilePath,
    ImmutableArray<object> Args)
{
    /// <summary>診断ID（例: <c>BS0001</c>）。</summary>
    public string Id => Descriptor.Id;

    /// <summary>重大度。</summary>
    public DiagnosticSeverity Severity => Descriptor.Severity;

    /// <summary>フォーマット引数を適用した診断メッセージ。</summary>
    public string Message => string.Format(Descriptor.MessageFormat, Args.ToArray());

    public override string ToString() => $"{Id}: {Message} {Span}";
}

/// <summary>
/// 診断メッセージのテンプレート定義。
/// 各診断コード（BS0001 等）に対して1つのインスタンスが存在する。
/// </summary>
public record DiagnosticDescriptor(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity,
    DiagnosticCategory Category);

/// <summary>診断メッセージの重大度。</summary>
public enum DiagnosticSeverity
{
    /// <summary>情報（参考）。</summary>
    Info,

    /// <summary>警告。コンパイルは続行する。</summary>
    Warning,

    /// <summary>エラー。コード生成を阻止する。</summary>
    Error,
}

/// <summary>診断メッセージの分類。</summary>
public enum DiagnosticCategory
{
    /// <summary>構文関連（パース）。</summary>
    Syntax,

    /// <summary>名前解決関連。</summary>
    Resolution,

    /// <summary>型関連。</summary>
    TypeCheck,

    /// <summary>構造関連（ノード制約）。</summary>
    Structure,

    /// <summary>その他。</summary>
    Other,
}

/// <summary>
/// 診断メッセージを蓄積するバッグ。
/// パーサーやセマンティクス解析器がここに診断を追加していく。
/// </summary>
public class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>蓄積された全診断メッセージ。</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>エラーレベルの診断が存在するかどうか。</summary>
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>診断メッセージを追加する。</summary>
    public void Report(DiagnosticDescriptor descriptor, TextSpan span, string? filePath, params object[] args)
    {
        _diagnostics.Add(new Diagnostic(descriptor, span, filePath, [..args]));
    }

    /// <summary>不変配列として返す。</summary>
    public ImmutableArray<Diagnostic> ToImmutableArray() => [.._diagnostics];
}

/// <summary>
/// 全ての診断コードの定義を集約する静的クラス。
/// </summary>
public static class DiagnosticDescriptors
{
    // ──────────── 構文エラー (BS00xx) ────────────

    public static readonly DiagnosticDescriptor BS0009 = new(
        "BS0009", "Parse error",
        "Parse error at {0}:{1}: {2}",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    public static readonly DiagnosticDescriptor BS0016 = new(
        "BS0016", "Unexpected token",
        "Expected '{0}', found '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    public static readonly DiagnosticDescriptor BS0017 = new(
        "BS0017", "Unterminated string",
        "Unterminated string literal",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    public static readonly DiagnosticDescriptor BS0018 = new(
        "BS0018", "Unmatched paren",
        "Unmatched '('",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    public static readonly DiagnosticDescriptor BS0019 = new(
        "BS0019", "Unexpected close paren",
        "Unexpected ')'",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    public static readonly DiagnosticDescriptor BS0020 = new(
        "BS0020", "Unused tree",
        "Unused tree '{0}'",
        DiagnosticSeverity.Warning, DiagnosticCategory.Syntax);

    // ──────────── 名前解決エラー (BS01xx) ────────────

    public static readonly DiagnosticDescriptor BS0001 = new(
        "BS0001", "Member not found",
        "Member '{0}' not found on type '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0011 = new(
        "BS0011", "External file not found",
        "External file '{0}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0012 = new(
        "BS0012", "Ambiguous overload",
        "Overload resolution ambiguous for '{0}' with arguments ({1})",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0101 = new(
        "BS0101", "Missing IBtContext",
        "Context type '{0}' does not implement IBtContext",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0102 = new(
        "BS0102", "Enum type not found",
        "Enum type '{0}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0103 = new(
        "BS0103", "Enum member not found",
        "Enum member '{0}.{1}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0104 = new(
        "BS0104", "Ambiguous member name",
        "Ambiguous member resolution for '{0}': matched '{1}' and '{2}', using '{1}'",
        DiagnosticSeverity.Warning, DiagnosticCategory.Resolution);

    // ──────────── 型エラー (BS02xx) ────────────

    public static readonly DiagnosticDescriptor BS0002 = new(
        "BS0002", "Type mismatch",
        "Type mismatch: expected '{0}', got '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0003 = new(
        "BS0003", "Cannot compare",
        "Cannot compare '{0}' with '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0004 = new(
        "BS0004", "Invalid arithmetic",
        "Arithmetic operator '{0}' not applicable to '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0005 = new(
        "BS0005", "Argument count mismatch",
        "Method '{0}' expects {1} arguments, got {2}",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0006 = new(
        "BS0006", "Argument type mismatch",
        "Argument {0} of '{1}': expected '{2}', got '{3}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0007 = new(
        "BS0007", "Bool required",
        "Expression in '{0}' must be bool, got '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0008 = new(
        "BS0008", "BtStatus required",
        "Action method '{0}' must return BtStatus",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    // ──────────── 構造エラー (BS03xx) ────────────

    public static readonly DiagnosticDescriptor BS0013 = new(
        "BS0013", "Invalid repeat count",
        "'repeat' count must be a positive integer literal",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0014 = new(
        "BS0014", "Invalid duration",
        "'{0}' duration must be a positive number literal",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0015 = new(
        "BS0015", "Insufficient children",
        "'{0}' requires at least {1} children",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0301 = new(
        "BS0301", "Unreachable node",
        "Unreachable node after unconditional Success",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0302 = new(
        "BS0302", "If without else",
        "'if' without else branch always returns Failure on false",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structure);

    // ──────────── F2: defdec 関連 (BS002x) ────────────

    public static readonly DiagnosticDescriptor BS0022 = new(
        "BS0022", "Defdec parameter type mismatch",
        "Defdec parameter '{0}' inferred as '{1}', got '{2}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0023 = new(
        "BS0023", "Defdec not found",
        "Defdec '{0}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0024 = new(
        "BS0024", "Defdec parameter count mismatch",
        "Defdec '{0}' expects {1} parameters, got {2}",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0025 = new(
        "BS0025", "Recursive defdec",
        "Recursive defdec call detected: '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0026 = new(
        "BS0026", "Missing body placeholder",
        "Missing <body> placeholder in defdec '{0}'",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0027 = new(
        "BS0027", "Multiple body placeholders",
        "Multiple <body> placeholders in defdec '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    // ──────────── F3: Blackboard 関連 (BS002x) ────────────

    public static readonly DiagnosticDescriptor BS0028 = new(
        "BS0028", "Blackboard access without declaration",
        "Blackboard access '$' used but no :blackboard declared in tree '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0029 = new(
        "BS0029", "Blackboard member not found",
        "Blackboard member '{0}' not found on type '{1}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0030 = new(
        "BS0030", "Blackboard type missing IBtBlackboard",
        "Blackboard type '{0}' does not implement IBtBlackboard",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    // ──────────── F11: Multi-Tree 関連 (BS003x) ────────────

    public static readonly DiagnosticDescriptor BS0036 = new(
        "BS0036", "Context type constraint mismatch",
        "Context type '{0}' does not satisfy constraint '{1}' required by tree '{2}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0037 = new(
        "BS0037", "Circular tree reference",
        "Circular tree reference detected: {0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0038 = new(
        "BS0038", "Tree not found",
        "Tree '{0}' not found (in current file or imports)",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0039 = new(
        "BS0039", "Ambiguous tree reference",
        "Ambiguous tree '{0}': found in multiple imported files",
        DiagnosticSeverity.Warning, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0040 = new(
        "BS0040", "Import file not found",
        "Import file '{0}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    // ──────────── F1: Reactive 関連 (BS0021) ────────────

    public static readonly DiagnosticDescriptor BS0021 = new(
        "BS0021", "Reactive condition type",
        "Expression in 'reactive' must be bool, got '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    // ──────────── F12: ジェネリクス (BS004x) ────────────

    public static readonly DiagnosticDescriptor BS0041 = new(
        "BS0041", "Type argument constraint violation",
        "Type argument '{0}' does not satisfy constraint '{1}' on type parameter '{2}' of '{3}'",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0042 = new(
        "BS0042", "Wrong number of type arguments",
        "Generic type '{0}' requires {1} type argument(s), but {2} were provided",
        DiagnosticSeverity.Error, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0043 = new(
        "BS0043", "Open generic type used as context",
        "Open generic type '{0}' cannot be used as a context type; provide type arguments",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    // ──────────── F13: Nullable 解析 (BS004x) ────────────

    public static readonly DiagnosticDescriptor BS0044 = new(
        "BS0044", "Dereference of possibly null member",
        "Member '{0}' may be null at this point",
        DiagnosticSeverity.Warning, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0045 = new(
        "BS0045", "Comparison with null always true",
        "Comparison of '{0}' with null is always true",
        DiagnosticSeverity.Warning, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0046 = new(
        "BS0046", "Comparison with null always false",
        "Comparison of '{0}' with null is always false",
        DiagnosticSeverity.Warning, DiagnosticCategory.TypeCheck);

    public static readonly DiagnosticDescriptor BS0047 = new(
        "BS0047", "Null check on non-nullable member unnecessary",
        "Null check on '{0}' is unnecessary because it is non-nullable",
        DiagnosticSeverity.Info, DiagnosticCategory.TypeCheck);

    // ──────────── F10: defmacro 関連 (BS003x) ────────────

    public static readonly DiagnosticDescriptor BS0031 = new(
        "BS0031", "Macro not found",
        "Macro '{0}' not found",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0032 = new(
        "BS0032", "Macro argument count mismatch",
        "Macro '{0}' expects {1} arguments, got {2}",
        DiagnosticSeverity.Error, DiagnosticCategory.Resolution);

    public static readonly DiagnosticDescriptor BS0033 = new(
        "BS0033", "Macro expansion depth exceeded",
        "Macro expansion exceeded depth limit ({0})",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0034 = new(
        "BS0034", "Recursive macro detected",
        "Recursive macro detected: {0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Structure);

    public static readonly DiagnosticDescriptor BS0035 = new(
        "BS0035", "Invalid macro expansion",
        "Macro expansion produced invalid syntax: {0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Syntax);

    // ──────────── その他 (BS09xx) ────────────

    public static readonly DiagnosticDescriptor BS0010 = new(
        "BS0010", "Obsolete member",
        "Member '{0}' is obsolete: {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Other);

    public static readonly DiagnosticDescriptor BS0901 = new(
        "BS0901", "Internal error",
        "Internal compiler error: {0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Other);
}
