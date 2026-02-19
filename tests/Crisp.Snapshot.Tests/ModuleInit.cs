using System.Runtime.CompilerServices;

namespace Crisp.Snapshot.Tests;

/// <summary>
/// Verify.Xunit のモジュール初期化。
/// DerivePathInfo でスナップショットファイルの保存先を Snapshots/ サブディレクトリに設定する。
/// </summary>
public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // スナップショットファイルをテストプロジェクトの Snapshots/ ディレクトリに保存する
        Verifier.DerivePathInfo(
            (sourceFile, projectDirectory, type, method) =>
            {
                var snapshotsDir = Path.Combine(projectDirectory, "Snapshots");
                return new PathInfo(snapshotsDir, type.Name, method.Name);
            });
    }
}
