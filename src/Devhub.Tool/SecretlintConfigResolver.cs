namespace Devhub.Tool;

/// <summary>
/// secretlint の設定ファイル(.secretlintrc.json)の解決ロジック。
/// リポジトリに .secretlintrc.json が存在すればそれを使い、無ければ devhub 内蔵の既定設定
/// (preset-recommend を有効化した最小構成)を使う(要件 FR-3.1/FR-3.2、doc/phase1.md 未実装項目)。
///
/// 「どちらの設定パスを使うか」という決定ロジックは純粋関数として切り出し(<see cref="ResolveConfigPath"/>)、
/// 実ファイルシステムを読む処理(<see cref="FindRepoConfigPath"/>)・既定設定を一時ファイルへ書き出す処理
/// (<see cref="WriteDefaultConfigToTempFile"/>)は薄い IO ラッパーとして分離している
/// (RulesyncFeatureDetector における「スナップショット取得(IO)」と「絞り込み(純粋関数)」の分離と同じ設計方針)。
/// </summary>
public static class SecretlintConfigResolver
{
    /// <summary>リポジトリ直下で優先される設定ファイル名。</summary>
    public const string RepoConfigFileName = ".secretlintrc.json";

    /// <summary>
    /// devhub 内蔵の既定設定。preset-recommend のみを有効化する最小構成。
    /// リポジトリに <see cref="RepoConfigFileName"/> が無い場合のフォールバックとして使う。
    ///
    /// csproj 側の EmbeddedResource は使わず、C# の文字列定数として保持する
    /// (Devhub.Tool.csproj は本タスクの対象外で変更しない方針のため)。
    /// </summary>
    public const string DefaultConfigJson =
        """
        {
          "rules": [
            {
              "id": "@secretlint/secretlint-rule-preset-recommend"
            }
          ]
        }
        """;

    /// <summary>
    /// 使用する設定ファイルのパスを決定する純粋関数。
    /// <paramref name="repoConfigPath"/> が非 null(リポジトリに設定が実在する)ならそれを優先し、
    /// null(実在しない)なら <paramref name="fallbackConfigPath"/>(既定設定を書き出した一時ファイル等)を返す。
    /// </summary>
    public static string ResolveConfigPath(string? repoConfigPath, string fallbackConfigPath)
    {
        ArgumentNullException.ThrowIfNull(fallbackConfigPath);
        return repoConfigPath ?? fallbackConfigPath;
    }

    /// <summary>
    /// リポジトリ直下に .secretlintrc.json が実在すればその絶対パスを、無ければ null を返す薄い IO ラッパー。
    /// </summary>
    public static string? FindRepoConfigPath(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var path = Path.Combine(repositoryRoot, RepoConfigFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 既定設定(<see cref="DefaultConfigJson"/>)を一時ファイルへ書き出し、そのパスを返す薄い IO ラッパー。
    /// secretlint の --secretlintrc は任意の場所のファイルをそのまま読める(実地検証済み。相対パス解決の
    /// 癖がある --secretlintignore とは異なる)ため、OS の一時ディレクトリで問題ない。
    /// </summary>
    public static string WriteDefaultConfigToTempFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, DefaultConfigJson);
        return path;
    }
}
