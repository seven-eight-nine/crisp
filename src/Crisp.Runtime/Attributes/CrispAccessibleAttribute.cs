using System;

namespace Crisp.Runtime;

/// <summary>
/// AOT 安全なアクセサテーブルの生成対象としてマークする属性（F6: AOT Support）。
///
/// クロスアセンブリのコンテキスト型に対して、Source Generator がアクセサテーブルを
/// 生成できるようにする。この属性を付与した型のメンバー（プロパティ、フィールド、メソッド）に
/// 対して、リフレクション不要のアクセサデリゲートが事前生成される。
///
/// <para>
/// 使用例:
/// <code>
/// [CrispAccessible]
/// public partial class EnemyAI
/// {
///     public int Health { get; set; }
///     public BtStatus Attack() => BtStatus.Success;
/// }
/// </code>
/// </para>
///
/// <para>
/// 注意:
/// <list type="bullet">
///   <item><description><c>[BehaviorTree]</c> 属性付きメソッドを含むクラスには自動的にアクセサが生成される</description></item>
///   <item><description>この属性は、チェーンアクセス先の外部型（<c>.Target.Position.x</c> の <c>Position</c> 型等）に付与する</description></item>
///   <item><description>属性を付与しない型のチェーンアクセスはリフレクションにフォールバックする（AOT 非安全）</description></item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CrispAccessibleAttribute : Attribute
{
}
