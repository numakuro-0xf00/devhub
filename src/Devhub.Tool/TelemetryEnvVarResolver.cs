namespace Devhub.Tool;

/// <summary>
/// <c>devhub telemetry send</c> が読む <c>DEVHUB_TELEMETRY_*</c> 環境変数を、
/// 「OS 環境変数 → リポジトリルート(カレントディレクトリ)の <c>.env</c>」の2段フォールバックで解決する純粋ロジック。
///
/// 背景: <c>devhub env --fill</c> は値を <c>.env</c> に書き込むだけで OS 環境変数は変更しないため、
/// hook から同期実行される <c>telemetry send</c> のプロセス環境には <c>.env</c> の内容が載らない
/// (穴。doc/phase2.md Step 2b)。このクラスはその穴を埋めるための値解決のみを担い、
/// 実ファイル I/O(.env の読み取り)は呼び出し側(Program.cs)の責務とする
/// (<see cref="EnvVarResolver"/> と同じ流儀:辞書スナップショットを受け取るだけ)。
///
/// 優先順位は OS 環境変数が常に上位。OS 側に非空値があれば .env は見ない
/// (<see cref="EnvVarResolver"/> の「どちらかにあれば充足」とは異なり、こちらは値そのものを返すため
/// 一方に決め打つ必要がある。OS 環境変数を明示的に設定した場合はそちらの意図を優先するのが自然)。
/// </summary>
public static class TelemetryEnvVarResolver
{
    /// <summary>
    /// 変数名 <paramref name="name"/> の値を OS 環境変数 → .env の順で解決する。
    /// どちらにも非空値が無ければ null を返す(未設定として扱う。値が空文字列の場合も未設定扱い)。
    /// </summary>
    public static string? Resolve(
        string name,
        IReadOnlyDictionary<string, string> osEnvironment,
        IReadOnlyDictionary<string, string> dotEnvFile)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(osEnvironment);
        ArgumentNullException.ThrowIfNull(dotEnvFile);

        if (osEnvironment.TryGetValue(name, out var osValue) && !string.IsNullOrEmpty(osValue))
        {
            return osValue;
        }
        if (dotEnvFile.TryGetValue(name, out var dotValue) && !string.IsNullOrEmpty(dotValue))
        {
            return dotValue;
        }
        return null;
    }
}
