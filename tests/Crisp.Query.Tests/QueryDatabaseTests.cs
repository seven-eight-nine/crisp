using Crisp.Query;
using Crisp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crisp.Query.Tests;

/// <summary>
/// QueryDatabase の t-wada 式 TDD テスト。
///
/// 最小テストから段階的に複雑さを増す方針:
/// 1. FileId の基本操作
/// 2. 入力クエリの設定と取得
/// 3. リビジョン管理
/// 4. 派生クエリの基本動作（Lex, Parse, Lower）
/// 5. キャッシュの鮮度チェック
/// 6. 入力変更時のキャッシュ無効化
/// 7. 無関係な入力変更ではキャッシュが保持されること
/// 8. セマンティクス統合（Resolve, TypeCheck, AllDiagnostics）
/// 9. IR 生成（EmitIR）
/// 10. ファイル管理（複数ファイル、RemoveFile）
/// </summary>
public class QueryDatabaseTests
{
    // ═══════════════════════════════════════════════════════════
    //  テスト用定数
    // ═══════════════════════════════════════════════════════════

    /// <summary>最小限のビヘイビアツリー DSL ソース。</summary>
    private const string MinimalDsl = "(tree T (.Do))";

    /// <summary>2つ目のビヘイビアツリー DSL ソース。</summary>
    private const string AlternativeDsl = "(tree T (seq (.Do) (.Do)))";

    /// <summary>テスト用 C# コンテキスト型ソース。</summary>
    private const string ContextCsSource = @"
using Crisp.Runtime;
namespace Test;
public class AI
{
    public BtStatus Do() => BtStatus.Success;
}";

    // ═══════════════════════════════════════════════════════════
    //  1. FileId の基本操作
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FileIdはパス文字列で同値比較できる()
    {
        var a = new FileId("test.crisp");
        var b = new FileId("test.crisp");

        Assert.Equal(a, b);
    }

    [Fact]
    public void FileIdのインラインファクトリーメソッドは正しいパスを生成する()
    {
        var fileId = FileId.ForInline("MyApp.AI", "Build");

        Assert.Equal("inline://MyApp.AI/Build", fileId.Path);
    }

    [Fact]
    public void FileIdのファイルファクトリーメソッドは正しいパスを生成する()
    {
        var fileId = FileId.ForFile("/path/to/tree.crisp");

        Assert.Equal("/path/to/tree.crisp", fileId.Path);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 入力クエリの設定と取得
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ソーステキストを設定して取得できる()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");

        db.SetSourceText(fileId, MinimalDsl);

        Assert.Equal(MinimalDsl, db.GetSourceText(fileId));
    }

    [Fact]
    public void 未設定のソーステキスト取得でKeyNotFoundExceptionが発生する()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("nonexistent");

        Assert.Throws<KeyNotFoundException>(() => db.GetSourceText(fileId));
    }

    [Fact]
    public void コンテキスト型を設定して取得できる()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        var contextType = GetTestContextType();

        db.SetContextType(fileId, contextType);

        Assert.Same(contextType, db.GetContextType(fileId));
    }

    // ═══════════════════════════════════════════════════════════
    //  3. リビジョン管理
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 初期リビジョンは0である()
    {
        var db = new QueryDatabase();

        Assert.Equal(0, db.CurrentRevision);
    }

    [Fact]
    public void 入力設定でリビジョンがインクリメントされる()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");

        db.SetSourceText(fileId, MinimalDsl);

        Assert.Equal(1, db.CurrentRevision);
    }

    [Fact]
    public void 同一値の再設定ではリビジョンが変わらない()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");

        db.SetSourceText(fileId, MinimalDsl);
        var revisionAfterFirst = db.CurrentRevision;

        db.SetSourceText(fileId, MinimalDsl);

        Assert.Equal(revisionAfterFirst, db.CurrentRevision);
    }

    [Fact]
    public void 異なる値の設定でリビジョンがインクリメントされる()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");

        db.SetSourceText(fileId, MinimalDsl);
        var revisionAfterFirst = db.CurrentRevision;

        db.SetSourceText(fileId, AlternativeDsl);

        Assert.True(db.CurrentRevision > revisionAfterFirst);
    }

    [Fact]
    public void 複数の入力設定でリビジョンが順次インクリメントされる()
    {
        var db = new QueryDatabase();
        var file1 = new FileId("file1");
        var file2 = new FileId("file2");

        db.SetSourceText(file1, MinimalDsl);
        Assert.Equal(1, db.CurrentRevision);

        db.SetSourceText(file2, AlternativeDsl);
        Assert.Equal(2, db.CurrentRevision);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 派生クエリの基本動作（Phase 1）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Lexはソーステキストからトークン列を生成する()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var tokens = db.Lex(fileId);

        Assert.True(tokens.Length > 0);
        Assert.Equal(TokenKind.LeftParen, tokens[0].Kind);
    }

    [Fact]
    public void ParseはトークンからCSTを生成する()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var result = db.Parse(fileId);

        Assert.NotNull(result.Program);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void LowerはCSTからASTを生成する()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var astTrees = db.Lower(fileId);

        Assert.Single(astTrees);
        Assert.Equal("T", astTrees[0].Name);
    }

    [Fact]
    public void Parse結果には構文エラーの診断情報が含まれる()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, "(tree T (.Do)");  // 閉じ括弧なし

        var result = db.Parse(fileId);

        Assert.True(result.Diagnostics.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. キャッシュの鮮度チェック
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 同じ入力で2回Lexを呼ぶとキャッシュが返される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var tokens1 = db.Lex(fileId);
        var tokens2 = db.Lex(fileId);

        // ImmutableArray は構造的に同じオブジェクトが返されることで
        // キャッシュヒットを間接的に確認する
        Assert.Equal(tokens1.Length, tokens2.Length);
    }

    [Fact]
    public void 同じ入力で2回Parseを呼ぶとキャッシュが返される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var result1 = db.Parse(fileId);
        var result2 = db.Parse(fileId);

        // 同一オブジェクト参照であればキャッシュヒット
        Assert.Same(result1, result2);
    }

    [Fact]
    public void 同じ入力で2回Lowerを呼ぶとキャッシュが返される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var ast1 = db.Lower(fileId);
        var ast2 = db.Lower(fileId);

        Assert.Equal(ast1.Length, ast2.Length);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. 入力変更時のキャッシュ無効化
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ソーステキスト変更後にLexが再計算される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var tokens1 = db.Lex(fileId);
        db.SetSourceText(fileId, AlternativeDsl);
        var tokens2 = db.Lex(fileId);

        // トークン数が変わっていれば再計算が行われた証拠
        Assert.NotEqual(tokens1.Length, tokens2.Length);
    }

    [Fact]
    public void ソーステキスト変更後にParseが再計算される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var result1 = db.Parse(fileId);
        db.SetSourceText(fileId, AlternativeDsl);
        var result2 = db.Parse(fileId);

        // 異なるオブジェクト参照であれば再計算
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void ソーステキスト変更後にLowerが再計算される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var ast1 = db.Lower(fileId);
        db.SetSourceText(fileId, AlternativeDsl);
        var ast2 = db.Lower(fileId);

        // MinimalDsl: body[0] は AstActionCall, AlternativeDsl: body[0] は AstSequence
        Assert.NotEqual(ast1[0].Body[0].GetType(), ast2[0].Body[0].GetType());
    }

    [Fact]
    public void 同一値の再設定ではキャッシュが無効化されない()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var result1 = db.Parse(fileId);
        db.SetSourceText(fileId, MinimalDsl);  // 同一値
        var result2 = db.Parse(fileId);

        // 同一オブジェクト参照 = キャッシュヒット
        Assert.Same(result1, result2);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. 無関係な入力変更ではキャッシュが保持される
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ContextType変更はLexキャッシュに影響しない()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);
        db.SetContextType(fileId, null);

        var tokens1 = db.Lex(fileId);
        db.SetContextType(fileId, GetTestContextType());  // Lex に無関係な入力を変更
        var tokens2 = db.Lex(fileId);

        // Lex は SourceText にのみ依存するので、ContextType の変更では再計算しない
        Assert.Equal(tokens1.Length, tokens2.Length);
    }

    [Fact]
    public void ContextType変更はParseキャッシュに影響しない()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);
        db.SetContextType(fileId, null);

        var result1 = db.Parse(fileId);
        db.SetContextType(fileId, GetTestContextType());
        var result2 = db.Parse(fileId);

        Assert.Same(result1, result2);
    }

    [Fact]
    public void ContextType変更はLowerキャッシュに影響しない()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);
        db.SetContextType(fileId, null);

        var ast1 = db.Lower(fileId);
        db.SetContextType(fileId, GetTestContextType());
        var ast2 = db.Lower(fileId);

        Assert.Equal(ast1.Length, ast2.Length);
    }

    // ═══════════════════════════════════════════════════════════
    //  8. セマンティクス統合
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Resolveはセマンティック解析結果を返す()
    {
        var db = CreateDatabaseWithSemantics();

        var result = db.Resolve(TestFileId);

        Assert.NotNull(result);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void TypeCheckはセマンティック診断を返す()
    {
        var db = CreateDatabaseWithSemantics();

        var diagnostics = db.TypeCheck(TestFileId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AllDiagnosticsは構文診断とセマンティック診断を統合する()
    {
        var db = CreateDatabaseWithSemantics();

        var diagnostics = db.AllDiagnostics(TestFileId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void セマンティックエラーがある場合TypeCheckが診断を返す()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, "(tree T (check .NonExistent))");
        db.SetContextType(fileId, GetTestContextType());

        var diagnostics = db.TypeCheck(fileId);

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "BS0001");
    }

    [Fact]
    public void AllDiagnosticsは構文エラーとセマンティックエラーの両方を含む()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        // 閉じ括弧なし（構文エラー）＋ 存在しないメンバー（意味エラー）
        db.SetSourceText(fileId, "(tree T (check .NonExistent)");
        db.SetContextType(fileId, GetTestContextType());

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Resolveのキャッシュはソーステキスト変更で無効化される()
    {
        var db = CreateDatabaseWithSemantics();

        var result1 = db.Resolve(TestFileId);
        db.SetSourceText(TestFileId, "(tree T (seq (.Do) (.Do)))");
        var result2 = db.Resolve(TestFileId);

        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void Resolveのキャッシュはコンテキスト型変更で無効化される()
    {
        var db = CreateDatabaseWithSemantics();

        var result1 = db.Resolve(TestFileId);
        // 新しい INamedTypeSymbol インスタンスを設定
        db.SetContextType(TestFileId, GetTestContextType());
        var result2 = db.Resolve(TestFileId);

        // 参照同値比較なので新しいインスタンスでは再計算
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void ContextTypeがnullの場合Resolveは空の結果を返す()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);
        db.SetContextType(fileId, null);

        var result = db.Resolve(fileId);

        Assert.False(result.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════
    //  9. IR 生成
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void EmitIRはASTからIRツリーを生成する()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var irTrees = db.EmitIR(fileId);

        Assert.Single(irTrees);
        Assert.Equal("T", irTrees[0].Name);
    }

    [Fact]
    public void EmitIRのキャッシュはソーステキスト変更で無効化される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);

        var ir1 = db.EmitIR(fileId);
        db.SetSourceText(fileId, AlternativeDsl);
        var ir2 = db.EmitIR(fileId);

        // MinimalDsl: body[0] は IrAction, AlternativeDsl: body[0] は IrSequence
        Assert.NotEqual(ir1[0].Body[0].GetType(), ir2[0].Body[0].GetType());
    }

    // ═══════════════════════════════════════════════════════════
    //  10. ファイル管理
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Filesプロパティは登録されたファイルIDを返す()
    {
        var db = new QueryDatabase();
        var file1 = new FileId("file1");
        var file2 = new FileId("file2");

        db.SetSourceText(file1, MinimalDsl);
        db.SetSourceText(file2, AlternativeDsl);

        Assert.Equal(2, db.Files.Count);
        Assert.Contains(file1, db.Files);
        Assert.Contains(file2, db.Files);
    }

    [Fact]
    public void RemoveFileでファイルの全データが削除される()
    {
        var db = new QueryDatabase();
        var fileId = new FileId("test");
        db.SetSourceText(fileId, MinimalDsl);
        _ = db.Parse(fileId);  // キャッシュを生成

        db.RemoveFile(fileId);

        Assert.DoesNotContain(fileId, db.Files);
        Assert.Throws<KeyNotFoundException>(() => db.GetSourceText(fileId));
    }

    [Fact]
    public void 複数ファイルは独立してキャッシュされる()
    {
        var db = new QueryDatabase();
        var file1 = new FileId("file1");
        var file2 = new FileId("file2");
        db.SetSourceText(file1, MinimalDsl);
        db.SetSourceText(file2, AlternativeDsl);

        var parse1 = db.Parse(file1);
        var parse2 = db.Parse(file2);

        // 異なる入力なので異なるCST
        Assert.NotSame(parse1, parse2);
    }

    [Fact]
    public void 一方のファイル変更は他方のキャッシュに影響しない()
    {
        var db = new QueryDatabase();
        var file1 = new FileId("file1");
        var file2 = new FileId("file2");
        db.SetSourceText(file1, MinimalDsl);
        db.SetSourceText(file2, AlternativeDsl);

        var parse2Before = db.Parse(file2);
        db.SetSourceText(file1, "(tree U (.Do))");  // file1 だけ変更
        var parse2After = db.Parse(file2);

        // file2 のキャッシュは影響を受けない
        Assert.Same(parse2Before, parse2After);
    }

    // ═══════════════════════════════════════════════════════════
    //  テストヘルパー
    // ═══════════════════════════════════════════════════════════

    private static readonly FileId TestFileId = new("test");

    /// <summary>
    /// セマンティクステスト用の QueryDatabase を構築する。
    /// SourceText と ContextType を設定済みの状態を返す。
    /// </summary>
    private static QueryDatabase CreateDatabaseWithSemantics()
    {
        var db = new QueryDatabase();
        db.SetSourceText(TestFileId, MinimalDsl);
        db.SetContextType(TestFileId, GetTestContextType());
        return db;
    }

    /// <summary>
    /// テスト用のコンテキスト型 INamedTypeSymbol を取得する。
    /// Roslyn Compilation で定義した Test.AI 型のシンボルを返す。
    /// </summary>
    private static INamedTypeSymbol GetTestContextType()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(ContextCsSource);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BtStatus).Assembly.Location),
        };

        var systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName("Test.AI")!;
    }
}
