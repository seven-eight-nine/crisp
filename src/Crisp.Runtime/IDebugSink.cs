namespace Crisp.Runtime;

/// <summary>
/// ビヘイビアツリーのデバッグイベントを受信するインターフェース（F7: Debugger）。
/// <see cref="TickContext.Debug"/> に設定することで、ノード評価のライフサイクルを監視できる。
///
/// null なら通常実行パスが選択され、パフォーマンスに影響しない。
/// 非 null の場合、各ノード評価の前後でイベントが発火する。
///
/// 主な用途:
/// <list type="bullet">
///   <item><description>ブレークポイント設定・ステップ実行</description></item>
///   <item><description>ノード評価結果のリアルタイムトレース</description></item>
///   <item><description>式の値のインスペクション</description></item>
/// </list>
/// </summary>
public interface IDebugSink
{
    /// <summary>
    /// ノード評価の開始前に呼ばれる。
    /// </summary>
    /// <param name="nodeId">評価されるノードの一意識別子（IrNode.Id に対応）。</param>
    /// <param name="ctx">現在の tick コンテキスト。</param>
    void OnBeforeTick(int nodeId, TickContext ctx);

    /// <summary>
    /// ノード評価の完了後に呼ばれる。
    /// </summary>
    /// <param name="nodeId">評価されたノードの一意識別子。</param>
    /// <param name="status">ノードの評価結果。</param>
    /// <param name="ctx">現在の tick コンテキスト。</param>
    void OnAfterTick(int nodeId, BtStatus status, TickContext ctx);

    /// <summary>
    /// 式が評価された際に呼ばれる。値のインスペクションに使用する。
    /// </summary>
    /// <param name="nodeId">式を含むノードの一意識別子。</param>
    /// <param name="expr">評価された式の文字列表現。</param>
    /// <param name="value">式の評価結果の値。</param>
    void OnExprEvaluated(int nodeId, string expr, object? value);

    /// <summary>
    /// ノードが外的要因により中断（Abort）された際に呼ばれる。
    /// </summary>
    /// <param name="nodeId">中断されたノードの一意識別子。</param>
    void OnAbort(int nodeId);

    /// <summary>
    /// 指定されたノードにブレークポイントが設定されているかどうかを返す。
    /// </summary>
    /// <param name="nodeId">確認対象のノード識別子。</param>
    /// <returns>ブレークポイントが設定されていれば <c>true</c>。</returns>
    bool ShouldBreak(int nodeId);

    /// <summary>
    /// ブレークポイントで停止し、デバッグUIからの続行指示を待つ。
    /// </summary>
    void WaitForContinue();
}
