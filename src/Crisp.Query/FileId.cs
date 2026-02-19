namespace Crisp.Query;

/// <summary>
/// ソースファイルの一意識別子。
///
/// Query Database 内でファイルを識別するために使用する。
/// パスの形式は用途によって異なる:
/// <list type="bullet">
///   <item><description>
///     インライン属性の場合: <c>"inline://完全修飾型名/メソッド名"</c>
///   </description></item>
///   <item><description>
///     外部 .crisp ファイルの場合: ファイルの絶対パスまたはプロジェクト相対パス
///   </description></item>
/// </list>
/// </summary>
/// <param name="Path">ファイルを識別するパス文字列。</param>
public readonly record struct FileId(string Path)
{
    /// <summary>
    /// インライン属性用の FileId を生成する。
    /// </summary>
    /// <param name="fullyQualifiedTypeName">完全修飾型名。</param>
    /// <param name="methodName">メソッド名。</param>
    /// <returns>インライン属性を識別する FileId。</returns>
    public static FileId ForInline(string fullyQualifiedTypeName, string methodName)
        => new($"inline://{fullyQualifiedTypeName}/{methodName}");

    /// <summary>
    /// 外部ファイル用の FileId を生成する。
    /// </summary>
    /// <param name="filePath">ファイルパス。</param>
    /// <returns>外部ファイルを識別する FileId。</returns>
    public static FileId ForFile(string filePath)
        => new(filePath);

    public override string ToString() => Path;
}
