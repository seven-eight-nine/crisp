using System;
using System.Collections.Generic;

namespace Crisp.Runtime;

/// <summary>
/// <see cref="IAccessorTable{T}"/> の標準実装（F6: AOT Support）。
///
/// Source Generator が生成するメンバーアクセサデリゲートとメソッドインボーカデリゲートの
/// 辞書を受け取り、AOT 安全なメンバーアクセスを提供する。
///
/// <para>
/// 使用例（Source Generator が生成するコード）:
/// <code>
/// private static readonly Dictionary&lt;string, Func&lt;EnemyAI, object?&gt;&gt; _memberAccessors = new()
/// {
///     ["Health"] = ctx =&gt; ctx.Health,
///     ["Target"] = ctx =&gt; ctx.Target,
/// };
///
/// private static readonly Dictionary&lt;string, Func&lt;EnemyAI, object?[], object?&gt;&gt; _methodInvokers = new()
/// {
///     ["Attack"] = (ctx, args) =&gt; ctx.Attack(),
/// };
///
/// public static IAccessorTable&lt;EnemyAI&gt; GetAccessorTable()
///     =&gt; new AccessorTable&lt;EnemyAI&gt;(_memberAccessors, _methodInvokers);
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">コンテキスト型。</typeparam>
public sealed class AccessorTable<T> : IAccessorTable<T>
{
    private readonly Dictionary<string, Func<T, object?>> _memberAccessors;
    private readonly Dictionary<string, Func<T, object?[], object?>> _methodInvokers;

    /// <summary>
    /// <see cref="AccessorTable{T}"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="memberAccessors">メンバーアクセサデリゲートの辞書。</param>
    /// <param name="methodInvokers">メソッドインボーカデリゲートの辞書。</param>
    public AccessorTable(
        Dictionary<string, Func<T, object?>> memberAccessors,
        Dictionary<string, Func<T, object?[], object?>> methodInvokers)
    {
        _memberAccessors = memberAccessors ?? throw new ArgumentNullException(nameof(memberAccessors));
        _methodInvokers = methodInvokers ?? throw new ArgumentNullException(nameof(methodInvokers));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Func<T, object?>> MemberAccessors => _memberAccessors;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Func<T, object?[], object?>> MethodInvokers => _methodInvokers;

    /// <inheritdoc/>
    public bool HasMember(string memberName) => _memberAccessors.ContainsKey(memberName);

    /// <inheritdoc/>
    public bool HasMethod(string methodName) => _methodInvokers.ContainsKey(methodName);

    /// <inheritdoc/>
    public object? GetMember(T instance, string memberName)
    {
        if (!_memberAccessors.TryGetValue(memberName, out var accessor))
            throw new KeyNotFoundException(
                $"メンバー '{memberName}' のアクセサが見つかりません。[CrispAccessible] 属性を確認してください。");
        return accessor(instance);
    }

    /// <inheritdoc/>
    public object? InvokeMethod(T instance, string methodName, params object?[] args)
    {
        if (!_methodInvokers.TryGetValue(methodName, out var invoker))
            throw new KeyNotFoundException(
                $"メソッド '{methodName}' のインボーカが見つかりません。[CrispAccessible] 属性を確認してください。");
        return invoker(instance, args);
    }
}
