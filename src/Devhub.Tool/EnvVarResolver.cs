namespace Devhub.Tool;

/// <summary>
/// 変数 1 件ごとの充足状態。値そのものは保持しない(NFR-1:値を表示・ログに出さない方針を型でも徹底する)。
/// </summary>
public sealed record EnvVarStatus(string Name, bool IsSet);

/// <summary>
/// 必要な環境変数一覧と、OS 環境変数 / .env のスナップショットから、変数ごとの充足状態を解決する純粋ロジック
/// (要件 FR-2.4)。「設定済み」= OS 環境変数に非空値がある、または .env に非空値があるのいずれか
/// (どちらかにあれば充足。優先順位はない)。
/// </summary>
public static class EnvVarResolver
{
    /// <summary>
    /// 必要変数一覧の順序を保ったまま、各変数の充足状態を解決する。
    /// </summary>
    public static IReadOnlyList<EnvVarStatus> Resolve(
        IReadOnlyList<string> requiredVariables,
        IReadOnlyDictionary<string, string> osEnvironment,
        IReadOnlyDictionary<string, string> dotEnvFile)
    {
        ArgumentNullException.ThrowIfNull(requiredVariables);
        ArgumentNullException.ThrowIfNull(osEnvironment);
        ArgumentNullException.ThrowIfNull(dotEnvFile);

        return requiredVariables
            .Select(name => new EnvVarStatus(name, IsSet(name, osEnvironment, dotEnvFile)))
            .ToArray();
    }

    private static bool IsSet(
        string name,
        IReadOnlyDictionary<string, string> osEnvironment,
        IReadOnlyDictionary<string, string> dotEnvFile) =>
        HasNonEmptyValue(osEnvironment, name) || HasNonEmptyValue(dotEnvFile, name);

    private static bool HasNonEmptyValue(IReadOnlyDictionary<string, string> source, string name) =>
        source.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value);
}
