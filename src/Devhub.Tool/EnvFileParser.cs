namespace Devhub.Tool;

/// <summary>
/// <c>.env</c> / <c>.env.example</c> 形式(<c>KEY=VALUE</c> 行)の素朴なパースを行う純粋ロジック(要件 FR-2.4)。
/// コメント行(<c>#</c> 開始)・空行は無視する。値の前後空白はトリムするが、クォートは剥がさない
/// (`.env` ローダとしての厳密な互換性までは devhub の責務ではない)。
/// </summary>
public static class EnvFileParser
{
    /// <summary>
    /// 内容をパースし、変数名 → 値 の辞書を返す。
    /// 同じキーが複数行に現れた場合は、後(下)の行の値で上書きする。
    /// 値が空(<c>KEY=</c>)の場合もキー自体は辞書に空文字列として登録される
    /// (「未設定」扱いにするかどうかは呼び出し側 <see cref="EnvVarResolver"/> の責務)。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new Dictionary<string, string>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0) continue;

            var key = line[..separatorIndex].Trim();
            if (key.Length == 0) continue;

            var value = line[(separatorIndex + 1)..].Trim();
            result[key] = value;
        }
        return result;
    }
}
