namespace Crisp.Runtime;

/// <summary>
/// ビヘイビアツリーの tick 結果を表す列挙型。
/// <list type="bullet">
///   <item><description><see cref="Success"/> — ノードの処理が成功した</description></item>
///   <item><description><see cref="Failure"/> — ノードの処理が失敗した</description></item>
///   <item><description><see cref="Running"/> — ノードの処理が継続中である（次の tick で再評価が必要）</description></item>
/// </list>
/// </summary>
public enum BtStatus
{
    /// <summary>ノードの処理が成功した。</summary>
    Success,

    /// <summary>ノードの処理が失敗した。</summary>
    Failure,

    /// <summary>ノードの処理が継続中。次の tick で再評価が必要。</summary>
    Running,
}
