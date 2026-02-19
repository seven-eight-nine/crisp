using System.Collections.Immutable;
using Crisp.Syntax;
using Microsoft.CodeAnalysis;

namespace Crisp.Semantics;

/// <summary>
/// ジェネリック型の解決を行うユーティリティクラス。
///
/// DSL のコンテキスト型として <c>StateMachineAI&lt;EnemyState&gt;</c> のような
/// ジェネリック型が指定された場合に、型名を解析して基底型名と型引数に分離し、
/// 型引数の数と制約の妥当性を検証する。
///
/// <para>
/// 報告する診断:
/// <list type="bullet">
///   <item><description>BS0041: 型引数が型パラメータの制約を満たしていない</description></item>
///   <item><description>BS0042: 型引数の数が型パラメータの数と一致しない</description></item>
///   <item><description>BS0043: 開いたジェネリック型（型引数未指定）がコンテキストとして使用された</description></item>
/// </list>
/// </para>
/// </summary>
public static class GenericTypeResolver
{
    // ═══════════════════════════════════════════════════════════
    //  型名パーシング
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ジェネリック型名を基底型名と型引数に分解する。
    ///
    /// 例:
    /// <list type="bullet">
    ///   <item><description><c>"StateMachineAI&lt;EnemyState&gt;"</c> → <c>("StateMachineAI", ["EnemyState"])</c></description></item>
    ///   <item><description><c>"Dictionary&lt;string, int&gt;"</c> → <c>("Dictionary", ["string", "int"])</c></description></item>
    ///   <item><description><c>"SimpleAI"</c> → <c>("SimpleAI", [])</c>（非ジェネリック型）</description></item>
    /// </list>
    ///
    /// 型引数は外側の角括弧のみを対象とし、ネストされたジェネリック型
    /// （例: <c>Outer&lt;Inner&lt;T&gt;&gt;</c>）も正しく解析する。
    /// </summary>
    /// <param name="typeName">解析対象の型名文字列。</param>
    /// <returns>基底型名と型引数の配列のタプル。非ジェネリック型の場合、型引数は空。</returns>
    public static (string BaseName, ImmutableArray<string> TypeArgs) ParseGenericTypeName(string typeName)
    {
        var openIndex = typeName.IndexOf('<');
        if (openIndex < 0)
        {
            // ジェネリック型ではない
            return (typeName, ImmutableArray<string>.Empty);
        }

        var baseName = typeName.Substring(0, openIndex).Trim();

        // 末尾の '>' を探す（ネストを考慮）
        var closeIndex = typeName.LastIndexOf('>');
        if (closeIndex < 0 || closeIndex <= openIndex)
        {
            // 不正な形式 — そのまま返す
            return (typeName, ImmutableArray<string>.Empty);
        }

        var argsString = typeName.Substring(openIndex + 1, closeIndex - openIndex - 1);
        var typeArgs = SplitTypeArguments(argsString);

        return (baseName, typeArgs);
    }

    /// <summary>
    /// 型名がジェネリック型を表すかどうかを判定する。
    ///
    /// 角括弧 <c>&lt;...&gt;</c> を含む場合にジェネリック型と見なす。
    /// </summary>
    /// <param name="typeName">判定対象の型名文字列。</param>
    /// <returns>ジェネリック型の場合 <c>true</c>。</returns>
    public static bool IsGenericType(string typeName)
    {
        return typeName.IndexOf('<') >= 0 && typeName.IndexOf('>') >= 0;
    }

    // ═══════════════════════════════════════════════════════════
    //  型引数の検証
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ジェネリック型の型引数の数が型パラメータの数と一致するか検証する。
    ///
    /// 一致しない場合、BS0042 診断を報告する。
    /// </summary>
    /// <param name="typeSymbol">検証対象のジェネリック型シンボル。</param>
    /// <param name="providedArgCount">ユーザーが指定した型引数の数。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    /// <param name="span">DSL 上のソース位置。</param>
    /// <returns>型引数の数が正しい場合 <c>true</c>。</returns>
    public static bool ValidateTypeArgumentCount(
        INamedTypeSymbol typeSymbol,
        int providedArgCount,
        DiagnosticBag diagnostics,
        TextSpan span)
    {
        var expectedCount = typeSymbol.TypeParameters.Length;
        if (expectedCount != providedArgCount)
        {
            diagnostics.Report(
                DiagnosticDescriptors.BS0042, span, null,
                typeSymbol.Name, expectedCount, providedArgCount);
            return false;
        }

        return true;
    }

    /// <summary>
    /// ジェネリック型の型引数が型パラメータの制約を満たすか検証する。
    ///
    /// 検証する制約:
    /// <list type="bullet">
    ///   <item><description>値型制約 (<c>where T : struct</c>)</description></item>
    ///   <item><description>参照型制約 (<c>where T : class</c>)</description></item>
    ///   <item><description>基底型制約 (<c>where T : BaseType</c>)</description></item>
    ///   <item><description>インターフェース制約 (<c>where T : IInterface</c>)</description></item>
    /// </list>
    ///
    /// 制約を満たさない場合、BS0041 診断を報告する。
    /// </summary>
    /// <param name="typeSymbol">構築済みジェネリック型シンボル。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    /// <param name="span">DSL 上のソース位置。</param>
    /// <returns>全ての制約を満たす場合 <c>true</c>。</returns>
    public static bool ValidateTypeConstraints(
        INamedTypeSymbol typeSymbol,
        DiagnosticBag diagnostics,
        TextSpan span)
    {
        var originalDef = typeSymbol.OriginalDefinition;
        var typeParams = originalDef.TypeParameters;
        var typeArgs = typeSymbol.TypeArguments;
        var allValid = true;

        for (var i = 0; i < typeParams.Length; i++)
        {
            var param = typeParams[i];
            var arg = typeArgs[i];

            // 値型制約 (struct)
            if (param.HasValueTypeConstraint && !arg.IsValueType)
            {
                diagnostics.Report(
                    DiagnosticDescriptors.BS0041, span, null,
                    arg.ToDisplayString(), "struct", param.Name, typeSymbol.Name);
                allValid = false;
            }

            // 参照型制約 (class)
            if (param.HasReferenceTypeConstraint && !arg.IsReferenceType)
            {
                diagnostics.Report(
                    DiagnosticDescriptors.BS0041, span, null,
                    arg.ToDisplayString(), "class", param.Name, typeSymbol.Name);
                allValid = false;
            }

            // 基底型・インターフェース制約
            foreach (var constraintType in param.ConstraintTypes)
            {
                if (!IsTypeAssignableTo(arg, constraintType))
                {
                    diagnostics.Report(
                        DiagnosticDescriptors.BS0041, span, null,
                        arg.ToDisplayString(), constraintType.ToDisplayString(),
                        param.Name, typeSymbol.Name);
                    allValid = false;
                }
            }
        }

        return allValid;
    }

    /// <summary>
    /// 型がオープンジェネリック型（型引数が未指定）であるかを検証する。
    ///
    /// オープンジェネリック型はコンテキスト型として使用できないため、
    /// BS0043 診断を報告する。
    /// </summary>
    /// <param name="typeSymbol">検証対象の型シンボル。</param>
    /// <param name="diagnostics">診断メッセージを蓄積するバッグ。</param>
    /// <param name="span">DSL 上のソース位置。</param>
    /// <returns>オープンジェネリック型の場合 <c>true</c>（エラーあり）。</returns>
    public static bool IsOpenGenericType(
        INamedTypeSymbol typeSymbol,
        DiagnosticBag diagnostics,
        TextSpan span)
    {
        if (typeSymbol.IsGenericType && typeSymbol.IsUnboundGenericType)
        {
            diagnostics.Report(
                DiagnosticDescriptors.BS0043, span, null,
                typeSymbol.ToDisplayString());
            return true;
        }

        // 型引数に型パラメータが残っている場合もオープンジェネリック型とみなす
        if (typeSymbol.IsGenericType)
        {
            foreach (var typeArg in typeSymbol.TypeArguments)
            {
                if (typeArg.TypeKind == TypeKind.TypeParameter)
                {
                    diagnostics.Report(
                        DiagnosticDescriptors.BS0043, span, null,
                        typeSymbol.ToDisplayString());
                    return true;
                }
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 型引数文字列をカンマで分割し、ネストされた角括弧を考慮して個別の型引数に分離する。
    ///
    /// 例: <c>"string, List&lt;int&gt;"</c> → <c>["string", "List&lt;int&gt;"]</c>
    /// </summary>
    /// <param name="argsString">カンマ区切りの型引数文字列。</param>
    /// <returns>個別の型引数の不変配列。</returns>
    private static ImmutableArray<string> SplitTypeArguments(string argsString)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < argsString.Length; i++)
        {
            switch (argsString[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    var arg = argsString.Substring(start, i - start).Trim();
                    if (arg.Length > 0)
                    {
                        result.Add(arg);
                    }
                    start = i + 1;
                    break;
            }
        }

        // 最後の型引数
        var lastArg = argsString.Substring(start).Trim();
        if (lastArg.Length > 0)
        {
            result.Add(lastArg);
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// ソース型がターゲット型に代入可能かどうかを判定する。
    ///
    /// 以下の場合に代入可能と判定する:
    /// <list type="bullet">
    ///   <item><description>同一型（<see cref="SymbolEqualityComparer.Default"/> による比較）</description></item>
    ///   <item><description>ソース型の基底型チェーンにターゲット型が含まれる</description></item>
    ///   <item><description>ソース型がターゲット型のインターフェースを実装している</description></item>
    /// </list>
    /// </summary>
    /// <param name="source">代入元の型。</param>
    /// <param name="target">代入先の型（制約型）。</param>
    /// <returns>代入可能な場合 <c>true</c>。</returns>
    private static bool IsTypeAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        // 同一型の場合
        if (SymbolEqualityComparer.Default.Equals(source, target))
        {
            return true;
        }

        // 基底型チェーンを辿る
        var baseType = source.BaseType;
        while (baseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, target))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }

        // インターフェース実装チェック
        foreach (var iface in source.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, target))
            {
                return true;
            }
        }

        return false;
    }
}
