using System.Text;

namespace Crisp.Syntax.Resolution;

/// <summary>
/// DSL 名と C# 名の相互変換ユーティリティ。
///
/// Crisp DSL ではケバブケース（<c>is-enemy-visible</c>）やそのまま PascalCase（<c>IsEnemyVisible</c>）
/// で記述されたメンバー名を、C# の命名規則に従った名前に変換する。
///
/// 変換候補の優先順位:
/// <list type="number">
///   <item><description>完全一致（大文字小文字無視）: <c>Health</c> → <c>Health</c></description></item>
///   <item><description>ケバブ → PascalCase: <c>is-enemy-visible</c> → <c>IsEnemyVisible</c></description></item>
///   <item><description>ケバブ → camelCase: <c>is-enemy-visible</c> → <c>isEnemyVisible</c></description></item>
///   <item><description>ケバブ → _camelCase: <c>is-enemy-visible</c> → <c>_isEnemyVisible</c></description></item>
///   <item><description>ケバブ → snake_case: <c>is-enemy-visible</c> → <c>is_enemy_visible</c></description></item>
/// </list>
///
/// 名前解決（<see cref="Resolution"/> フェーズ）では、これらの候補を優先順位順に
/// コンテキスト型のメンバーと照合し、最初にマッチしたものを採用する。
/// </summary>
public static class NameConversion
{
    /// <summary>
    /// DSL 名から C# メンバー名の候補を優先順位順に生成する。
    ///
    /// 候補は以下の順序で生成される:
    /// <list type="number">
    ///   <item><description>そのまま（大文字小文字無視で比較用）</description></item>
    ///   <item><description>ケバブケース → PascalCase</description></item>
    ///   <item><description>ケバブケース → camelCase</description></item>
    ///   <item><description>ケバブケース → _camelCase（アンダースコア付きプライベートフィールド）</description></item>
    ///   <item><description>ケバブケース → snake_case</description></item>
    /// </list>
    ///
    /// ケバブケースでない名前（ハイフンを含まない）の場合、変換候補 2〜5 は
    /// 元の名前と同一になる可能性がある。重複は生成側では排除しない。
    /// </summary>
    /// <param name="dslName">DSL で記述されたメンバー名。</param>
    /// <returns>C# メンバー名の候補を優先順位順に返す列挙。</returns>
    public static IEnumerable<string> GenerateCandidates(string dslName)
    {
        // 1. そのまま（大文字小文字無視で比較）
        yield return dslName;

        // 2. ケバブケース → PascalCase
        //    "is-enemy-visible" → "IsEnemyVisible"
        yield return ToPascalCase(dslName);

        // 3. ケバブケース → camelCase
        //    "is-enemy-visible" → "isEnemyVisible"
        yield return ToCamelCase(dslName);

        // 4. ケバブケース → _camelCase
        //    "is-enemy-visible" → "_isEnemyVisible"
        yield return "_" + ToCamelCase(dslName);

        // 5. ケバブケース → snake_case
        //    "is-enemy-visible" → "is_enemy_visible"
        yield return ToSnakeCase(dslName);
    }

    /// <summary>
    /// ケバブケースまたは任意の文字列を PascalCase に変換する。
    ///
    /// ハイフン区切りの各セグメントの先頭文字を大文字にして結合する。
    /// ハイフンを含まない場合は、先頭文字のみを大文字にする。
    ///
    /// 例:
    /// <list type="bullet">
    ///   <item><description><c>"is-enemy-visible"</c> → <c>"IsEnemyVisible"</c></description></item>
    ///   <item><description><c>"health"</c> → <c>"Health"</c></description></item>
    ///   <item><description><c>"Health"</c> → <c>"Health"</c></description></item>
    ///   <item><description><c>"a"</c> → <c>"A"</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="kebab">変換対象の文字列。ケバブケースまたは任意の形式。</param>
    /// <returns>PascalCase に変換された文字列。</returns>
    public static string ToPascalCase(string kebab)
    {
        if (string.IsNullOrEmpty(kebab)) return kebab;

        var sb = new StringBuilder(kebab.Length);
        var capitalizeNext = true;

        foreach (var ch in kebab)
        {
            if (ch == '-')
            {
                capitalizeNext = true;
                continue;
            }

            sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
            capitalizeNext = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// ケバブケースまたは任意の文字列を camelCase に変換する。
    ///
    /// PascalCase に変換した後、先頭文字のみを小文字にする。
    ///
    /// 例:
    /// <list type="bullet">
    ///   <item><description><c>"is-enemy-visible"</c> → <c>"isEnemyVisible"</c></description></item>
    ///   <item><description><c>"health"</c> → <c>"health"</c></description></item>
    ///   <item><description><c>"Health"</c> → <c>"health"</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="kebab">変換対象の文字列。</param>
    /// <returns>camelCase に変換された文字列。</returns>
    public static string ToCamelCase(string kebab)
    {
        var pascal = ToPascalCase(kebab);
        if (pascal.Length == 0) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>
    /// PascalCase または camelCase の C# メンバー名をケバブケースに変換する。
    ///
    /// 大文字の境界にハイフンを挿入し、全て小文字にする。
    /// これは <see cref="ToPascalCase"/> の逆変換に相当する。
    ///
    /// 補完ハンドラーで C# メンバー名からケバブケースのフィルタリング候補を
    /// 生成する際に使用する（アーキテクチャ仕様 5.4.2）。
    ///
    /// 例:
    /// <list type="bullet">
    ///   <item><description><c>"IsEnemyVisible"</c> → <c>"is-enemy-visible"</c></description></item>
    ///   <item><description><c>"Health"</c> → <c>"health"</c></description></item>
    ///   <item><description><c>"health"</c> → <c>"health"</c></description></item>
    ///   <item><description><c>"BTStatus"</c> → <c>"b-t-status"</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="name">変換対象の C# メンバー名。PascalCase または camelCase。</param>
    /// <returns>ケバブケースに変換された文字列。</returns>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1]))
            {
                sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// ケバブケースまたは任意の文字列を snake_case に変換する。
    ///
    /// ハイフンをアンダースコアに置換し、全て小文字にする。
    /// PascalCase の入力に対しては、大文字の前にアンダースコアを挿入する。
    ///
    /// 例:
    /// <list type="bullet">
    ///   <item><description><c>"is-enemy-visible"</c> → <c>"is_enemy_visible"</c></description></item>
    ///   <item><description><c>"IsEnemyVisible"</c> → <c>"is_enemy_visible"</c></description></item>
    ///   <item><description><c>"health"</c> → <c>"health"</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="name">変換対象の文字列。</param>
    /// <returns>snake_case に変換された文字列。</returns>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // まずケバブケースのハイフンをアンダースコアに変換
        if (name.Contains('-'))
        {
            return name.Replace('-', '_').ToLowerInvariant();
        }

        // PascalCase / camelCase → snake_case
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
