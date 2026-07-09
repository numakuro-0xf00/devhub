namespace Devhub.Tool;

/// <summary>
/// 実ファイルシステムの <c>.rulesync/</c> 配下を再帰的に読み取り、<c>${VAR}</c> プレースホルダを集約する
/// 薄い IO ラッパー(要件 FR-2.4)。プレースホルダ抽出そのものの純粋ロジックは
/// <see cref="EnvPlaceholderScanner"/> に分離している。
/// </summary>
public static class EnvFileScanner
{
    /// <summary>
    /// バイナリファイルを読みに行くのを避けるため、走査対象とする拡張子
    /// (devhub/rulesync が実際に使うテキスト系の拡張子のみに限定)。
    /// </summary>
    private static readonly string[] TargetExtensions =
        { ".json", ".md", ".toml", ".yaml", ".yml", ".txt" };

    /// <summary>拡張子を持たないが対象に含める特別なファイル名。</summary>
    private static readonly string[] TargetFileNames = { ".aiignore" };

    /// <summary>
    /// <c>.rulesync/</c> 配下を走査し、必要な環境変数名一覧(重複排除・出現順保持)を返す。
    /// <c>.rulesync/</c> 自体が存在しない場合は null を返す(呼び出し側で明確なエラーにする)。
    /// </summary>
    public static IReadOnlyList<string>? FindRequiredVariables(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        var rulesyncDir = Path.Combine(repositoryRoot, ".rulesync");
        if (!Directory.Exists(rulesyncDir)) return null;

        var seen = new HashSet<string>();
        var names = new List<string>();

        foreach (var file in EnumerateTargetFiles(rulesyncDir))
        {
            var content = File.ReadAllText(file);
            foreach (var name in EnvPlaceholderScanner.ExtractVariableNames(content))
            {
                if (seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    // ファイル走査順を決定的にするため、フルパスの順序で列挙する
    // (プレースホルダ自体の重複排除・順序保持ロジックは EnvPlaceholderScanner 側で検証済み)。
    private static IEnumerable<string> EnumerateTargetFiles(string rulesyncDir) =>
        Directory.EnumerateFiles(rulesyncDir, "*", SearchOption.AllDirectories)
            .Where(IsTargetFile)
            .OrderBy(f => f, StringComparer.Ordinal);

    private static bool IsTargetFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (TargetFileNames.Contains(fileName)) return true;

        var extension = Path.GetExtension(path);
        return TargetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
