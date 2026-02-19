namespace Crisp.Runtime;

/// <summary>
/// ビヘイビアツリーのノード基底クラス。
/// 全てのノードは <see cref="Tick"/> メソッドで1回の評価を実行し、
/// <see cref="BtStatus"/> を返す。
///
/// U1 決定事項: <see cref="Tick"/> は <see cref="TickContext"/> を受け取る。
/// これにより DeltaTime 等のフレーム固有情報を統一的に注入できる。
///
/// 状態操作メソッド:
/// <list type="bullet">
///   <item><description><see cref="Reset"/>: 正常終了後の状態初期化。Parallel確定時、ツリー再起動時に呼ばれる。</description></item>
///   <item><description><see cref="Abort"/>: 外的要因による強制キャンセル。Reactive条件のfalse化、Async中断等で呼ばれる。</description></item>
/// </list>
/// </summary>
public abstract class BtNode
{
    /// <summary>
    /// ノードの1回の評価を実行する。
    /// </summary>
    /// <param name="ctx">現在フレームのコンテキスト情報（DeltaTime 等）。</param>
    /// <returns>評価結果の <see cref="BtStatus"/>。</returns>
    public abstract BtStatus Tick(TickContext ctx);

    /// <summary>
    /// 正常終了後の状態初期化。
    /// Parallel確定時（U4）、ツリー再起動時に呼ばれる。
    /// サブクラスは内部状態をクリアし、次回Tick時に初期状態から実行されるようにする。
    /// デフォルト実装は何もしない。
    /// </summary>
    public virtual void Reset() { }

    /// <summary>
    /// 外的要因による強制キャンセル。
    /// Reactive条件のfalse化（F1）、Async中断（F8）、タイムアウト等で呼ばれる。
    /// サブクラスでオーバーライドして中断固有の後処理（アニメーション停止等）を実装できる。
    /// デフォルト実装は <see cref="Reset"/> への委譲。
    /// 拡張がなければ <see cref="Abort"/> は使われず、Coreの挙動に影響しない。
    /// </summary>
    public virtual void Abort() => Reset();
}
