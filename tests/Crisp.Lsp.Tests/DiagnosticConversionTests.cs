using System.Collections.Immutable;
using Crisp.Lsp.Handlers;
using Crisp.Syntax;

namespace Crisp.Lsp.Tests;

/// <summary>
/// 診断変換の t-wada 式 TDD テスト。
///
/// Crisp 内部の <see cref="Crisp.Syntax.Diagnostic"/> を
/// LSP の Diagnostic に正しく変換できることを検証する:
/// 1. TextSpan → LSP Range 変換
/// 2. DiagnosticSeverity 変換
/// 3. 診断コードとメッセージの伝搬
/// </summary>
public class DiagnosticConversionTests
{
    // ═══════════════════════════════════════════════════════════
    //  1. TextSpan → LSP Range 変換
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 単一行のSpanが正しくRangeに変換される()
    {
        var source = "(tree T (.Do))";
        var mapper = new PositionMapper(source);
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(0, 5), "Health", "AI");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal(0, lspDiag.Range.Start.Line);
        Assert.Equal(0, lspDiag.Range.Start.Character);
        Assert.Equal(0, lspDiag.Range.End.Line);
        Assert.Equal(5, lspDiag.Range.End.Character);
    }

    [Fact]
    public void 複数行のSpanが正しくRangeに変換される()
    {
        var source = "(tree T\n  (.Do))";
        var mapper = new PositionMapper(source);
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(8, 6), "Do", "T");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal(1, lspDiag.Range.Start.Line);
        Assert.Equal(0, lspDiag.Range.Start.Character);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. DiagnosticSeverity 変換
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ErrorがLSPのErrorに変換される()
    {
        var mapper = new PositionMapper("test");
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(0, 4), "Health", "AI");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, lspDiag.Severity);
    }

    [Fact]
    public void WarningがLSPのWarningに変換される()
    {
        var mapper = new PositionMapper("test");
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0020, new TextSpan(0, 4), "T");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning, lspDiag.Severity);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 診断コードとメッセージ
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 診断コードが正しく伝搬される()
    {
        var mapper = new PositionMapper("test");
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(0, 4), "Health", "AI");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal("BS0001", lspDiag.Code?.String);
    }

    [Fact]
    public void ソースがcrispに設定される()
    {
        var mapper = new PositionMapper("test");
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(0, 4), "Health", "AI");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Equal("crisp", lspDiag.Source);
    }

    [Fact]
    public void メッセージにフォーマット引数が適用される()
    {
        var mapper = new PositionMapper("test");
        var crispDiag = MakeDiagnostic(DiagnosticDescriptors.BS0001, new TextSpan(0, 4), "Health", "AI");

        var lspDiag = TextDocumentSyncHandler.ConvertDiagnostic(crispDiag, mapper);

        Assert.Contains("Health", lspDiag.Message);
        Assert.Contains("AI", lspDiag.Message);
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    private static Syntax.Diagnostic MakeDiagnostic(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        params object[] args)
    {
        return new Syntax.Diagnostic(descriptor, span, null, [..args]);
    }
}
