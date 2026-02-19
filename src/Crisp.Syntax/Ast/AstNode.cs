using Crisp.Syntax.Cst;

namespace Crisp.Syntax.Ast;

/// <summary>
/// Crisp AST（抽象構文木）ノードの抽象基底クラス。
/// AST は CST からの lowering により生成され、セマンティック解析の対象となる。
/// Trivia は破棄されるが、<see cref="CstOrigin"/> を通じてソース位置を参照可能。
/// </summary>
public abstract class AstNode
{
    /// <summary>
    /// 元となった CST ノードへの逆参照。
    /// エラー報告や LSP のソース位置特定に使用する。
    /// </summary>
    public CstNode CstOrigin { get; }

    protected AstNode(CstNode cstOrigin) => CstOrigin = cstOrigin;

    /// <summary>子ノードの列挙（デバッグ・走査用）。</summary>
    public abstract IEnumerable<AstNode> Children { get; }

    /// <summary>
    /// AST の構造をインデント付きテキストとしてダンプする（デバッグ・テスト用）。
    /// 各ノードを種別名と主要な属性値で表示し、子ノードを再帰的にインデントして出力する。
    /// </summary>
    /// <param name="indent">現在のインデントレベル（スペース数）。</param>
    /// <returns>ダンプされたテキスト。</returns>
    public string DumpTree(int indent = 0)
    {
        var prefix = new string(' ', indent);
        var sb = new System.Text.StringBuilder();
        sb.Append(prefix);
        sb.Append(DumpSelf());
        sb.AppendLine();

        foreach (var child in Children)
        {
            sb.Append(child.DumpTree(indent + 2));
        }

        return sb.ToString();
    }

    /// <summary>
    /// このノード自身の情報を1行で表す文字列を返す（サブクラスでオーバーライド可能）。
    /// </summary>
    protected virtual string DumpSelf() => GetType().Name;
}
