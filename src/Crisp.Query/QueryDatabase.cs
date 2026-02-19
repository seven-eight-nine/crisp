using System.Collections.Immutable;
using Crisp.Semantics;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Cst;
using Crisp.Syntax.IR;
using Microsoft.CodeAnalysis;
using Diagnostic = Crisp.Syntax.Diagnostic;
using SemanticModel = Crisp.Semantics.SemanticModel;

namespace Crisp.Query;

/// <summary>
/// salsa-like 増分計算データベース。
///
/// Rust の salsa クレートの設計思想を C# で再実装したもの。
/// 入力クエリの変更をリビジョンで追跡し、派生クエリの計算結果を
/// 自動的にキャッシュする。依存する入力が変更されていなければ
/// 再計算を省略してキャッシュ結果を返す。
///
/// <para>
/// 核となる概念:
/// <list type="bullet">
///   <item><description>
///     <b>Input Query</b>: 外部から設定される値。変更されるとリビジョンが上がる。
///   </description></item>
///   <item><description>
///     <b>Derived Query</b>: 入力に依存して計算される値。依存元が変更されなければキャッシュを返す。
///   </description></item>
///   <item><description>
///     <b>Revision</b>: グローバルなカウンター。入力が変更されるたびにインクリメント。
///   </description></item>
///   <item><description>
///     <b>Dependency Tracking</b>: 派生クエリの実行中にアクセスした他のクエリを自動記録。
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// クエリの依存関係:
/// <code>
/// SourceText ──→ Lex ──→ Parse ──→ Lower ──→ EmitIR
///                                      │
/// ContextType ─────────────────→ Resolve
///                                      │
/// RoslynCompilation ───────────→ TypeCheck
///                                      │
///                Parse(構文) + TypeCheck(意味) ──→ AllDiagnostics
/// </code>
/// </para>
///
/// <para>
/// 利用パターン:
/// <list type="bullet">
///   <item><description>
///     <b>Source Generator</b>: ビルド時に1回だけ実行。DB はビルドごとに新規作成。
///     プロジェクト内の複数ファイル間でキャッシュ共有が可能。
///   </description></item>
///   <item><description>
///     <b>LSP</b>: ファイル変更のたびに入力を更新。変更されたファイルの
///     派生クエリのみ再計算（増分の恩恵大）。DB はサーバーのライフタイム中保持。
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class QueryDatabase
{
    // ═══════════════════════════════════════════════════════════
    //  リビジョン管理
    // ═══════════════════════════════════════════════════════════

    /// <summary>グローバルリビジョンカウンター。入力変更のたびにインクリメントする。</summary>
    private long _globalRevision;

    /// <summary>現在のグローバルリビジョン値。</summary>
    public long CurrentRevision => _globalRevision;

    // ═══════════════════════════════════════════════════════════
    //  入力クエリストレージ
    // ═══════════════════════════════════════════════════════════

    /// <summary>ソーステキスト入力。DSL のソースコード（.crisp 内容または属性文字列）。</summary>
    private readonly Dictionary<FileId, InputEntry<string>> _sourceTexts = new();

    /// <summary>コンテキスト型入力。C# 側の Roslyn INamedTypeSymbol。</summary>
    private readonly Dictionary<FileId, InputEntry<INamedTypeSymbol?>> _contextTypes = new();

    /// <summary>Roslyn コンパイレーション入力。型解決に必要。</summary>
    private readonly Dictionary<FileId, InputEntry<Compilation?>> _roslynCompilations = new();

    // ═══════════════════════════════════════════════════════════
    //  派生クエリキャッシュ
    // ═══════════════════════════════════════════════════════════

    private readonly Dictionary<FileId, DerivedEntry<ImmutableArray<Token>>> _lexCache = new();
    private readonly Dictionary<FileId, DerivedEntry<ParseResult>> _parseCache = new();
    private readonly Dictionary<FileId, DerivedEntry<ImmutableArray<AstTree>>> _lowerCache = new();
    private readonly Dictionary<FileId, DerivedEntry<SemanticAnalysisResult>> _resolveCache = new();
    private readonly Dictionary<FileId, DerivedEntry<ImmutableArray<Diagnostic>>> _typeCheckCache = new();
    private readonly Dictionary<FileId, DerivedEntry<ImmutableArray<Diagnostic>>> _allDiagnosticsCache = new();
    private readonly Dictionary<FileId, DerivedEntry<ImmutableArray<IrTree>>> _emitIrCache = new();

    // ═══════════════════════════════════════════════════════════
    //  依存関係記録スタック
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 派生クエリ計算中の依存関係記録スタック。
    /// 各レベルは現在計算中のクエリが依存する入力リビジョンのセット。
    /// スタック構造により、派生クエリの再帰呼び出しに対応する。
    /// </summary>
    private readonly Stack<HashSet<InputRevision>> _recordingStack = new();

    // ═══════════════════════════════════════════════════════════
    //  入力クエリ — セッター
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストを設定する。値が変更された場合のみリビジョンを更新する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <param name="text">ソーステキスト。</param>
    public void SetSourceText(FileId fileId, string text)
    {
        SetInput(_sourceTexts, fileId, text);
    }

    /// <summary>
    /// コンテキスト型を設定する。値が変更された場合のみリビジョンを更新する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <param name="contextType">コンテキスト型の Roslyn シンボル。</param>
    public void SetContextType(FileId fileId, INamedTypeSymbol? contextType)
    {
        SetInput(_contextTypes, fileId, contextType, ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Roslyn コンパイレーションを設定する。値が変更された場合のみリビジョンを更新する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <param name="compilation">Roslyn コンパイレーション。</param>
    public void SetRoslynCompilation(FileId fileId, Compilation? compilation)
    {
        SetInput(_roslynCompilations, fileId, compilation, ReferenceEqualityComparer.Instance);
    }

    // ═══════════════════════════════════════════════════════════
    //  入力クエリ — ゲッター（依存関係記録付き）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストを取得する。派生クエリの計算中に呼ばれた場合、依存関係を自動記録する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>ソーステキスト。</returns>
    /// <exception cref="KeyNotFoundException">指定した FileId が未設定の場合。</exception>
    public string GetSourceText(FileId fileId)
    {
        return GetInput(_sourceTexts, QueryKind.SourceText, fileId);
    }

    /// <summary>
    /// コンテキスト型を取得する。派生クエリの計算中に呼ばれた場合、依存関係を自動記録する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>コンテキスト型。</returns>
    /// <exception cref="KeyNotFoundException">指定した FileId が未設定の場合。</exception>
    public INamedTypeSymbol? GetContextType(FileId fileId)
    {
        return GetInput(_contextTypes, QueryKind.ContextType, fileId);
    }

    /// <summary>
    /// Roslyn コンパイレーションを取得する。派生クエリの計算中に呼ばれた場合、依存関係を自動記録する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>Roslyn コンパイレーション。</returns>
    /// <exception cref="KeyNotFoundException">指定した FileId が未設定の場合。</exception>
    public Compilation? GetRoslynCompilation(FileId fileId)
    {
        return GetInput(_roslynCompilations, QueryKind.RoslynCompilation, fileId);
    }

    // ═══════════════════════════════════════════════════════════
    //  派生クエリ — Phase 1（構文処理）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ソーステキストを字句解析してトークン列を返す。
    /// 結果はキャッシュされ、SourceText が変更されない限り再計算しない。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>トークンの不変配列。</returns>
    public ImmutableArray<Token> Lex(FileId fileId)
    {
        return ComputeDerived(_lexCache, fileId, fid =>
        {
            var source = GetSourceText(fid);
            return Lexer.Lex(source);
        });
    }

    /// <summary>
    /// トークン列を構文解析して CST と診断情報を返す。
    /// 結果はキャッシュされ、Lex の結果が変更されない限り再計算しない。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>CST プログラムと構文診断のペア。</returns>
    public ParseResult Parse(FileId fileId)
    {
        return ComputeDerived(_parseCache, fileId, fid =>
        {
            var tokens = Lex(fid);
            var (program, diagnostics) = Parser.Parse(tokens);
            return new ParseResult(program, diagnostics);
        });
    }

    /// <summary>
    /// CST を AST に lowering する。
    /// 結果はキャッシュされ、Parse の結果が変更されない限り再計算しない。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>AST ツリーの不変配列。</returns>
    public ImmutableArray<AstTree> Lower(FileId fileId)
    {
        return ComputeDerived(_lowerCache, fileId, fid =>
        {
            var parseResult = Parse(fid);
            return CstToAstLowering.Lower(parseResult.Program);
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  派生クエリ — Phase 2（意味解析）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ツリーに対してセマンティック解析を実行する。
    /// 名前解決 → 型推論 → 型検査 の3フェーズを実行し、結果をキャッシュする。
    /// Lower と ContextType に依存する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>セマンティック解析結果（モデル + 診断情報）。</returns>
    public SemanticAnalysisResult Resolve(FileId fileId)
    {
        return ComputeDerived(_resolveCache, fileId, fid =>
        {
            var astTrees = Lower(fid);
            var contextType = GetContextType(fid);

            if (astTrees.Length == 0 || contextType is null)
            {
                return new SemanticAnalysisResult(new SemanticModel(), new DiagnosticBag());
            }

            var result = SemanticAnalyzer.Analyze(astTrees[0], contextType);

            // 複数ツリーが定義されている場合、最初以外は未使用として警告（BS0020）。
            // 現在のアーキテクチャでは1ファイル1ツリーを前提としており、
            // サブツリー参照（U5）が実装されるまで追加ツリーは到達不能。
            for (var i = 1; i < astTrees.Length; i++)
            {
                result.Diagnostics.Report(
                    DiagnosticDescriptors.BS0020,
                    astTrees[i].CstOrigin.Span,
                    null,
                    astTrees[i].Name);
            }

            return result;
        });
    }

    /// <summary>
    /// セマンティック解析の診断情報のみを返す。
    /// Resolve の結果から診断を抽出する。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>セマンティック診断の不変配列。</returns>
    public ImmutableArray<Diagnostic> TypeCheck(FileId fileId)
    {
        return ComputeDerived(_typeCheckCache, fileId, fid =>
        {
            var result = Resolve(fid);
            return result.Diagnostics.ToImmutableArray();
        });
    }

    /// <summary>
    /// 構文診断とセマンティック診断を統合して返す。
    /// Parse（構文エラー）と TypeCheck（意味エラー）の両方を含む。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>全診断の不変配列。</returns>
    public ImmutableArray<Diagnostic> AllDiagnostics(FileId fileId)
    {
        return ComputeDerived(_allDiagnosticsCache, fileId, fid =>
        {
            var parseResult = Parse(fid);
            var semanticDiags = TypeCheck(fid);

            var builder = ImmutableArray.CreateBuilder<Diagnostic>(
                parseResult.Diagnostics.Diagnostics.Count + semanticDiags.Length);

            foreach (var d in parseResult.Diagnostics.Diagnostics)
            {
                builder.Add(d);
            }

            foreach (var d in semanticDiags)
            {
                builder.Add(d);
            }

            return builder.ToImmutable();
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  派生クエリ — Phase 3（IR 生成）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST を IR に lowering する。
    /// Lower の結果に依存する。セマンティックエラーの有無は呼び出し側で確認すること。
    /// </summary>
    /// <param name="fileId">対象ファイルの識別子。</param>
    /// <returns>IR ツリーの不変配列。</returns>
    public ImmutableArray<IrTree> EmitIR(FileId fileId)
    {
        return ComputeDerived(_emitIrCache, fileId, fid =>
        {
            var astTrees = Lower(fid);
            var ir = AstToIrLowering.Lower(astTrees);

            // BtNode を返すメソッドをサブツリーとしてアノテーション
            if (_contextTypes.TryGetValue(fid, out var ctEntry) && ctEntry.Value != null)
            {
                var subtreeNames = new HashSet<string>();
                foreach (var m in ctEntry.Value.GetMembers())
                {
                    if (m is IMethodSymbol ms
                        && ms.MethodKind == MethodKind.Ordinary
                        && ms.ReturnType.ToDisplayString() == "Crisp.Runtime.BtNode")
                    {
                        subtreeNames.Add(ms.Name);
                    }
                }

                ir = AstToIrLowering.AnnotateSubtrees(ir, subtreeNames);
            }

            return ir;
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  ファイル管理
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// データベースに登録されている全ファイル ID を返す。
    /// SourceText が設定されたファイルの集合。
    /// </summary>
    public IReadOnlyCollection<FileId> Files => _sourceTexts.Keys;

    /// <summary>
    /// 指定したファイルの全データ（入力 + キャッシュ）を削除する。
    /// ファイルが閉じられた場合やプロジェクトから除外された場合に使用する。
    /// </summary>
    /// <param name="fileId">削除対象のファイル識別子。</param>
    public void RemoveFile(FileId fileId)
    {
        _sourceTexts.Remove(fileId);
        _contextTypes.Remove(fileId);
        _roslynCompilations.Remove(fileId);

        _lexCache.Remove(fileId);
        _parseCache.Remove(fileId);
        _lowerCache.Remove(fileId);
        _resolveCache.Remove(fileId);
        _typeCheckCache.Remove(fileId);
        _allDiagnosticsCache.Remove(fileId);
        _emitIrCache.Remove(fileId);
    }

    // ═══════════════════════════════════════════════════════════
    //  内部: 入力クエリ操作
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 入力値を設定する。既存の値と異なる場合のみリビジョンをインクリメントする。
    /// これにより、同一値の再設定では派生クエリの再計算が発生しない。
    /// </summary>
    private void SetInput<T>(
        Dictionary<FileId, InputEntry<T>> store,
        FileId fileId,
        T value,
        IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        if (store.TryGetValue(fileId, out var existing) && comparer.Equals(existing.Value, value))
        {
            return;
        }

        store[fileId] = new InputEntry<T>(value, ++_globalRevision);
    }

    /// <summary>
    /// 入力値を取得し、計算中であれば依存関係を記録する。
    /// </summary>
    private T GetInput<T>(
        Dictionary<FileId, InputEntry<T>> store,
        QueryKind kind,
        FileId fileId)
    {
        if (!store.TryGetValue(fileId, out var entry))
        {
            throw new KeyNotFoundException(
                $"入力クエリ {kind} が FileId '{fileId}' に対して設定されていません。");
        }

        RecordInputAccess(kind, fileId, entry.Revision);
        return entry.Value;
    }

    // ═══════════════════════════════════════════════════════════
    //  内部: 派生クエリ計算 + キャッシュ
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 派生クエリの値を計算またはキャッシュから取得する。
    ///
    /// キャッシュの鮮度チェック:
    /// <list type="number">
    ///   <item><description>キャッシュエントリが存在するか確認</description></item>
    ///   <item><description>全依存入力のリビジョンがキャッシュ作成時から変更されていないか確認</description></item>
    ///   <item><description>鮮度が保たれていればキャッシュ値を返す（再計算省略）</description></item>
    ///   <item><description>鮮度が失われていれば再計算し、新しい依存関係とともにキャッシュを更新</description></item>
    /// </list>
    /// </summary>
    private T ComputeDerived<T>(
        Dictionary<FileId, DerivedEntry<T>> cache,
        FileId fileId,
        Func<FileId, T> compute)
    {
        // キャッシュヒット判定: エントリが存在し、全依存入力が変更されていなければ返す
        if (cache.TryGetValue(fileId, out var cached) && IsFresh(cached))
        {
            // キャッシュヒットでも、親クエリの依存記録にはこのクエリの依存を伝搬する
            PropagateInputDependencies(cached.InputDependencies);
            return cached.Value;
        }

        // 依存関係の記録を開始する
        BeginRecording();

        try
        {
            var value = compute(fileId);
            var dependencies = EndRecording();
            cache[fileId] = new DerivedEntry<T>(value, dependencies);

            // 親クエリの依存記録にもこのクエリの依存を伝搬する
            PropagateInputDependencies(dependencies);

            return value;
        }
        catch
        {
            // 例外時は記録をクリーンアップする
            EndRecording();
            throw;
        }
    }

    /// <summary>
    /// キャッシュエントリの鮮度を検証する。
    /// 全ての依存入力のリビジョンがキャッシュ作成時と同一であれば鮮度が保たれている。
    /// </summary>
    private bool IsFresh<T>(DerivedEntry<T> entry)
    {
        foreach (var dep in entry.InputDependencies)
        {
            var currentRevision = GetCurrentInputRevision(dep.Kind, dep.FileId);
            if (currentRevision != dep.Revision)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 指定した入力クエリの現在のリビジョンを返す。
    /// 入力が存在しない場合は -1 を返す（削除された入力を検出するため）。
    /// </summary>
    private long GetCurrentInputRevision(QueryKind kind, FileId fileId)
    {
        return kind switch
        {
            QueryKind.SourceText => _sourceTexts.TryGetValue(fileId, out var s) ? s.Revision : -1,
            QueryKind.ContextType => _contextTypes.TryGetValue(fileId, out var c) ? c.Revision : -1,
            QueryKind.RoslynCompilation => _roslynCompilations.TryGetValue(fileId, out var r) ? r.Revision : -1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  内部: 依存関係記録
    // ═══════════════════════════════════════════════════════════

    /// <summary>新しい依存関係記録コンテキストを開始する。</summary>
    private void BeginRecording()
    {
        _recordingStack.Push(new HashSet<InputRevision>());
    }

    /// <summary>
    /// 現在の記録コンテキストを終了し、収集した依存関係を返す。
    /// </summary>
    private ImmutableArray<InputRevision> EndRecording()
    {
        var set = _recordingStack.Pop();
        return [..set];
    }

    /// <summary>
    /// 入力クエリへのアクセスを記録する。
    /// 記録コンテキストが存在する場合のみ記録する。
    /// </summary>
    private void RecordInputAccess(QueryKind kind, FileId fileId, long revision)
    {
        if (_recordingStack.Count > 0)
        {
            _recordingStack.Peek().Add(new InputRevision(kind, fileId, revision));
        }
    }

    /// <summary>
    /// 子クエリの入力依存関係を親クエリの記録に伝搬する。
    /// キャッシュヒットした子クエリの依存関係も正しく親に伝搬するために必要。
    /// </summary>
    private void PropagateInputDependencies(ImmutableArray<InputRevision> dependencies)
    {
        if (_recordingStack.Count > 0)
        {
            var current = _recordingStack.Peek();
            foreach (var dep in dependencies)
            {
                current.Add(dep);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  内部型定義
    // ═══════════════════════════════════════════════════════════

    /// <summary>入力クエリの値とリビジョンを保持する。</summary>
    private sealed record InputEntry<T>(T Value, long Revision);

    /// <summary>派生クエリのキャッシュ値と依存入力リビジョンを保持する。</summary>
    private sealed record DerivedEntry<T>(T Value, ImmutableArray<InputRevision> InputDependencies);

    /// <summary>
    /// 参照同値性で比較するための IEqualityComparer。
    /// Roslyn シンボルやコンパイレーションの変更検出に使用する。
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<object?>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object? obj) => obj is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>
/// 入力クエリの種類を表す列挙型。
/// 依存関係の記録と鮮度チェックに使用する。
/// </summary>
public enum QueryKind
{
    /// <summary>ソーステキスト入力。</summary>
    SourceText,

    /// <summary>コンテキスト型入力。</summary>
    ContextType,

    /// <summary>Roslyn コンパイレーション入力。</summary>
    RoslynCompilation,
}

/// <summary>
/// 入力クエリのリビジョンスナップショット。
/// 派生クエリの依存関係として記録される。
/// 鮮度チェック時に、現在のリビジョンとこの値を比較する。
/// </summary>
/// <param name="Kind">入力クエリの種類。</param>
/// <param name="FileId">対象ファイルの識別子。</param>
/// <param name="Revision">記録時点のリビジョン値。</param>
public readonly record struct InputRevision(QueryKind Kind, FileId FileId, long Revision);

/// <summary>
/// パース結果を保持するレコード。
/// CST プログラムと構文診断情報のペア。
/// </summary>
/// <param name="Program">CST プログラム。</param>
/// <param name="Diagnostics">構文診断バッグ。</param>
public record ParseResult(CstProgram Program, DiagnosticBag Diagnostics);
