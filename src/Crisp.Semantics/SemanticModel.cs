using Crisp.Syntax.Ast;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// セマンティック解析の結果を保持するモデル。
///
/// 名前解決の結果（AST ノード → Roslyn <c>ISymbol</c>）と
/// 型推論の結果（AST 式 → <see cref="CrispType"/>）を辞書で管理する。
///
/// このモデルは <see cref="NameResolver"/>、<see cref="TypeInferer"/>、
/// <see cref="TypeChecker"/> の各フェーズで段階的に構築される。
/// </summary>
public class SemanticModel
{
    /// <summary>名前解決の結果。AST ノード → Roslyn シンボル。</summary>
    private readonly Dictionary<AstNode, ISymbol> _symbols = new();

    /// <summary>型推論の結果。AST 式 → Crisp 型。</summary>
    private readonly Dictionary<AstExpr, CrispType> _types = new();

    // ═══════════════════════════════════════════════════════════
    //  シンボル（名前解決結果）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ノードに対して解決された Roslyn シンボルを設定する。
    /// </summary>
    /// <param name="node">対象の AST ノード。</param>
    /// <param name="symbol">解決された Roslyn シンボル。</param>
    public void SetSymbol(AstNode node, ISymbol symbol) => _symbols[node] = symbol;

    /// <summary>
    /// AST ノードに対して解決された Roslyn シンボルを取得する。
    /// </summary>
    /// <param name="node">対象の AST ノード。</param>
    /// <returns>解決された Roslyn シンボル。未解決の場合は <c>null</c>。</returns>
    public ISymbol? GetSymbol(AstNode node)
    {
        _symbols.TryGetValue(node, out var symbol);
        return symbol;
    }

    /// <summary>指定の AST ノードがシンボル解決済みかどうかを返す。</summary>
    public bool HasSymbol(AstNode node) => _symbols.ContainsKey(node);

    // ═══════════════════════════════════════════════════════════
    //  型（型推論結果）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST 式に対して推論された型を設定する。
    /// </summary>
    /// <param name="expr">対象の AST 式。</param>
    /// <param name="type">推論された型。</param>
    public void SetType(AstExpr expr, CrispType type) => _types[expr] = type;

    /// <summary>
    /// AST 式に対して推論された型を取得する。
    /// </summary>
    /// <param name="expr">対象の AST 式。</param>
    /// <returns>推論された型。未推論の場合は <see cref="CrispType.Unknown"/>。</returns>
    public CrispType GetType(AstExpr expr)
    {
        _types.TryGetValue(expr, out var type);
        return type ?? CrispType.Unknown;
    }
}
