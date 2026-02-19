using System;
using System.Collections.Generic;

namespace Crisp.Runtime;

/// <summary>
/// AOT 安全なメンバーアクセサテーブルのインターフェース（F6: AOT Support）。
///
/// NativeAOT / IL2CPP 環境ではリフレクションが制限されるため、
/// Source Generator がコンテキスト型の全メンバーに対するアクセサデリゲートを事前生成する。
/// このインターフェースはその生成コードの共通契約を定義する。
///
/// <para>
/// 使用例:
/// <code>
/// // Source Generator が生成するコード
/// partial class EnemyAI
/// {
///     public static IAccessorTable&lt;EnemyAI&gt; GetAccessorTable()
///         =&gt; new AccessorTable&lt;EnemyAI&gt;(_memberAccessors, _methodInvokers);
/// }
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">コンテキスト型。</typeparam>
public interface IAccessorTable<T>
{
    /// <summary>
    /// メンバー（プロパティ・フィールド）の値を取得するデリゲートの辞書。
    /// キーはメンバー名（例: "Health", "Target"）。
    /// </summary>
    IReadOnlyDictionary<string, Func<T, object?>> MemberAccessors { get; }

    /// <summary>
    /// メソッドを呼び出すデリゲートの辞書。
    /// キーはメソッド名（例: "Attack", "Flee"）。
    /// デリゲートの第2引数はメソッドへの引数配列。
    /// </summary>
    IReadOnlyDictionary<string, Func<T, object?[], object?>> MethodInvokers { get; }

    /// <summary>
    /// 指定された名前のメンバーアクセサが存在するかを返す。
    /// </summary>
    /// <param name="memberName">メンバー名。</param>
    /// <returns>アクセサが存在する場合 <c>true</c>。</returns>
    bool HasMember(string memberName);

    /// <summary>
    /// 指定された名前のメソッドインボーカが存在するかを返す。
    /// </summary>
    /// <param name="methodName">メソッド名。</param>
    /// <returns>インボーカが存在する場合 <c>true</c>。</returns>
    bool HasMethod(string methodName);

    /// <summary>
    /// メンバーの値を取得する。
    /// </summary>
    /// <param name="instance">コンテキストのインスタンス。</param>
    /// <param name="memberName">メンバー名。</param>
    /// <returns>メンバーの値。</returns>
    /// <exception cref="KeyNotFoundException">メンバーが見つからない場合。</exception>
    object? GetMember(T instance, string memberName);

    /// <summary>
    /// メソッドを呼び出す。
    /// </summary>
    /// <param name="instance">コンテキストのインスタンス。</param>
    /// <param name="methodName">メソッド名。</param>
    /// <param name="args">メソッドへの引数配列。</param>
    /// <returns>メソッドの戻り値。</returns>
    /// <exception cref="KeyNotFoundException">メソッドが見つからない場合。</exception>
    object? InvokeMethod(T instance, string methodName, params object?[] args);
}
