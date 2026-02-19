using System.Collections.Generic;

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
///
/// デバッグ検査 API:
/// <list type="bullet">
///   <item><description><see cref="LastStatus"/>: 最後の Tick 結果。null は未評価。</description></item>
///   <item><description><see cref="DebugChildren"/>: 子ノード一覧（読み取り専用）。</description></item>
///   <item><description><see cref="DebugNodeType"/>: ノード種別名（"selector", "action" 等）。</description></item>
///   <item><description><see cref="DebugLabel"/>: 表示用ラベル（"Patrol()", "Health &lt; 30" 等）。</description></item>
/// </list>
/// </summary>
public abstract class BtNode
{
    /// <summary>
    /// 最後の <see cref="Tick"/> で返された <see cref="BtStatus"/>。
    /// null はまだ一度も Tick されていない（または Reset 済み）ことを示す。
    /// </summary>
    public BtStatus? LastStatus { get; protected set; }

    /// <summary>
    /// デバッグ用: このノードの子ノード一覧を読み取り専用で返す。
    /// デフォルトは空。コンポジット・デコレータノードでオーバーライドする。
    /// </summary>
    public virtual IReadOnlyList<BtNode> DebugChildren => System.Array.Empty<BtNode>();

    /// <summary>
    /// デバッグ用: ノード種別名（例: "selector", "sequence", "action"）。
    /// デフォルトは型名から "Node" を除いた小文字形式。
    /// </summary>
    public virtual string DebugNodeType => GetType().Name.Replace("Node", "").ToLowerInvariant();

    /// <summary>
    /// デバッグ用: ノードの表示ラベル（例: "Patrol()", "Health &lt; 30"）。
    /// null の場合はラベルなし。リーフノードでオーバーライドする。
    /// </summary>
    public virtual string? DebugLabel => null;

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
    /// </summary>
    public virtual void Reset() { LastStatus = null; }

    /// <summary>
    /// 外的要因による強制キャンセル。
    /// Reactive条件のfalse化（F1）、Async中断（F8）、タイムアウト等で呼ばれる。
    /// サブクラスでオーバーライドして中断固有の後処理（アニメーション停止等）を実装できる。
    /// デフォルト実装は <see cref="Reset"/> への委譲。
    /// 拡張がなければ <see cref="Abort"/> は使われず、Coreの挙動に影響しない。
    /// </summary>
    public virtual void Abort() => Reset();

    /// <summary>
    /// LastStatus を記録して結果をそのまま返すヘルパー。
    /// <c>LastStatus</c> が <c>BtStatus?</c> のため、代入式は <c>BtStatus?</c> を返す。
    /// Tick の戻り値は <c>BtStatus</c> なのでこのヘルパーで型を揃える。
    /// </summary>
    protected BtStatus Track(BtStatus status) { LastStatus = status; return status; }
}
