namespace Crisp.Runtime;

/// <summary>
/// メソッドに Crisp DSL のビヘイビアツリーソースを関連付ける属性。
/// Source Generator がこの属性を検出して C# コードを生成する。
///
/// ソースはインライン S式文字列（例: <c>"(tree T (.Patrol))"</c>）
/// または外部 .crisp ファイル名（例: <c>"EnemyCombat.crisp"</c>）。
///
/// 外部ファイルの場合は MSBuild の AdditionalFiles に含まれている必要がある（U7 決定事項）。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BehaviorTreeAttribute : Attribute
{
    /// <summary>
    /// S式文字列（インライン）または .crisp ファイルパス（外部ファイル）。
    /// </summary>
    public string Source { get; }

    public BehaviorTreeAttribute(string source) => Source = source;
}
