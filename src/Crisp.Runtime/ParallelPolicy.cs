namespace Crisp.Runtime;

/// <summary>
/// Parallel ノードの完了判定ポリシー。
/// </summary>
public abstract record ParallelPolicy
{
    /// <summary>いずれかの子が Success なら全体 Success。</summary>
    public sealed record Any : ParallelPolicy;

    /// <summary>全ての子が Success なら全体 Success。</summary>
    public sealed record All : ParallelPolicy;

    /// <summary>N 個の子が Success なら全体 Success。</summary>
    public sealed record N(int Count) : ParallelPolicy;
}
