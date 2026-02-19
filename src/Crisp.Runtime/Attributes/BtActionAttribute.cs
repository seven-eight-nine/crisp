namespace Crisp.Runtime;

/// <summary>
/// メソッドがビヘイビアツリーのアクションとして使用可能であることを示す属性。
/// 属性が付与されたメソッドは <see cref="BtStatus"/> を戻り値として返す必要がある。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BtActionAttribute : Attribute { }
