namespace Crisp.Runtime;

/// <summary>
/// ビヘイビアツリーの1回の tick に必要なコンテキスト情報。
/// <c>record struct</c> によりアロケーションフリーで値渡し可能。
/// </summary>
/// <param name="DeltaTime">前回 tick からの経過秒数。TimeoutNode, CooldownNode 等の時間計測に使用する。</param>
/// <param name="FrameIndex">デバッグ・ログ用のフレーム番号。任意。</param>
/// <param name="Debug">
/// デバッグシンク。null なら通常実行、非 null ならデバッグモード（F7）。
/// <see cref="IDebugSink"/> を TickContext に含めることで、Coreの BtNode シグネチャを変更せずに
/// デバッグ機能を注入できる。
/// </param>
public readonly record struct TickContext(
    float DeltaTime,
    int FrameIndex = 0,
    IDebugSink? Debug = null);
