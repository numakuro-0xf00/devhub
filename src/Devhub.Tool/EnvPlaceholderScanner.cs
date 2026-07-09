using System.Text.RegularExpressions;

namespace Devhub.Tool;

/// <summary>
/// テキスト内容から <c>${VAR}</c> 形式の環境変数プレースホルダを抽出する純粋ロジック(要件 FR-2.4)。
/// rulesync/Claude Code が扱う <c>${VAR}</c> / <c>${VAR:-default}</c> 記法(doc/requirements.md 3.4)を対象とする。
/// <c>$VAR</c>(ブレース無し)は対象外。
/// </summary>
public static class EnvPlaceholderScanner
{
    // ${VAR} / ${VAR:-default} を拾う。変数名は [A-Za-z_][A-Za-z0-9_]* に限定するため、
    // ${1BAD} のように先頭が数字の名前はそもそもマッチせず、無視される。
    // ":-" 以降(既定値部分)は変数名の対象外として読み捨てる(次の "}" までを既定値として扱う)。
    private static readonly Regex PlaceholderPattern =
        new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>
    /// テキスト内容から <c>${VAR}</c> 形式のプレースホルダの変数名を抽出する。
    /// 重複は排除し、初出順を保って返す。
    /// </summary>
    public static IReadOnlyList<string> ExtractVariableNames(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var seen = new HashSet<string>();
        var names = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(content))
        {
            var name = match.Groups[1].Value;
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }
        return names;
    }
}
