namespace Crisp.Runtime;

/// <summary>
/// ビヘイビアツリーのブラックボード（共有データストア）を示すマーカーインターフェース。
///
/// Context (this) 以外のデータストアを DSL から型安全にアクセスするための基盤。
/// 複数の AI エージェント間でデータを共有する場合や、
/// Context に含めたくないグローバルな状態にアクセスする場合に使用する。
///
/// DSL 側では <c>$</c> プレフィクスでブラックボードメンバーにアクセスする:
/// <code>
/// (tree CombatAI
///   :context EnemyAI
///   :blackboard WorldState
///   (select
///     (check $.IsAlarmTriggered)
///     (.Patrol)))
/// </code>
/// </summary>
public interface IBtBlackboard { }
