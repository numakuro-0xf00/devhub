namespace Devhub.Tool;

/// <summary>
/// .rulesync/ ディレクトリのスナップショット(feature ごとに、rulesync がソースとして読みに行くパスが
/// 実在するかどうかの真偽値)。
///
/// rulesync@9.2.0 を実地検証した結果、各 feature が読むパスは以下の通り
/// (`npx rulesync@9.2.0 generate --dry-run --verbose` で確認):
///
///   feature      | .rulesync/ 配下のパス | 種別
///   -------------|------------------------|--------
///   rules        | rules/                 | ディレクトリ
///   mcp          | mcp.json               | ファイル
///   hooks        | hooks.json             | ファイル
///   permissions  | permissions.json       | ファイル
///   subagents    | subagents/             | ディレクトリ
///   commands     | commands/              | ディレクトリ
///   skills       | skills/                | ディレクトリ
///   ignore       | .aiignore              | ファイル
///
/// ディレクトリ系(rules/subagents/commands/skills)は、ディレクトリ自体が存在しても中身が空だと
/// rulesync は該当機能を 0 件として黙って(または "not found" 系の情報ログを出しつつ)処理する。
/// 生成しても意味が無いため、本ツールでは「ディレクトリが存在し、かつ中身が 1 つ以上ある」ことを実在の条件とする。
/// ファイル系(mcp/hooks/permissions/ignore)は、ファイルが存在しないと
/// rulesync が `Failed to load ...` という無害だが紛らわしい警告を出す。ファイルの存在有無だけを見る。
/// </summary>
public sealed record RulesyncDirectorySnapshot(
    bool HasRules,
    bool HasMcp,
    bool HasHooks,
    bool HasPermissions,
    bool HasSubagents,
    bool HasCommands,
    bool HasSkills,
    bool HasIgnore);

/// <summary>
/// features 絞り込みの結果。Features は実際に rulesync へ渡す機能一覧、SkippedFeatures はソースが無く
/// 除外した機能一覧(情報表示用)。
/// </summary>
public sealed record NarrowedFeatures(IReadOnlyList<string> Features, IReadOnlyList<string> SkippedFeatures);

/// <summary>
/// .rulesync/ に実在する機能だけへ features を絞り込む純粋ロジック。
/// 既定値(全機能)で rulesync を呼ぶと、ソースが無い機能について `Failed to load ...` という
/// 無害だが紛らわしい警告が出るため、事前に存在確認して渡す機能を絞る(要件:phase1.md 未実装項目)。
///
/// 「スナップショットから feature 一覧を検出する純粋関数」と「実ファイルシステムを読む薄い IO ラッパー」を
/// 分離している。ラッパー(<see cref="ReadSnapshot"/>)以外はファイル I/O を行わない。
/// </summary>
public static class RulesyncFeatureDetector
{
    /// <summary>
    /// スナップショットから、ソースが実在する feature 一覧を返す。
    /// 順序は <see cref="ApplyOptions.DefaultFeatures"/> と揃える(rules,mcp,hooks,permissions,subagents,commands,skills,ignore)。
    /// </summary>
    public static IReadOnlyList<string> DetectAvailableFeatures(RulesyncDirectorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var features = new List<string>();
        if (snapshot.HasRules) features.Add("rules");
        if (snapshot.HasMcp) features.Add("mcp");
        if (snapshot.HasHooks) features.Add("hooks");
        if (snapshot.HasPermissions) features.Add("permissions");
        if (snapshot.HasSubagents) features.Add("subagents");
        if (snapshot.HasCommands) features.Add("commands");
        if (snapshot.HasSkills) features.Add("skills");
        if (snapshot.HasIgnore) features.Add("ignore");
        return features;
    }

    /// <summary>
    /// 要求された features をスナップショットに基づいて絞り込む。
    /// <paramref name="featuresExplicit"/> が true(ユーザーが --features を明示指定した場合)は
    /// 絞り込まず、<paramref name="requestedFeatures"/> をそのまま返す(明示要求を黙って落とさない)。
    /// false(既定値のまま)のときだけ、実在する機能との積を取って絞り込む。
    /// </summary>
    public static NarrowedFeatures Narrow(
        IReadOnlyList<string> requestedFeatures,
        bool featuresExplicit,
        RulesyncDirectorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(requestedFeatures);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (featuresExplicit)
        {
            return new NarrowedFeatures(requestedFeatures, Array.Empty<string>());
        }

        var available = DetectAvailableFeatures(snapshot);
        var kept = requestedFeatures.Where(available.Contains).ToArray();
        var skipped = requestedFeatures.Where(f => !available.Contains(f)).ToArray();
        return new NarrowedFeatures(kept, skipped);
    }

    /// <summary>
    /// 実ファイルシステムの .rulesync/ を読み取り、スナップショットを作る薄い IO ラッパー。
    /// .rulesync/ ディレクトリ自体が存在しない場合は null を返す(呼び出し側で明確なエラーにする)。
    /// </summary>
    public static RulesyncDirectorySnapshot? ReadSnapshot(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        var rulesyncDir = Path.Combine(repositoryRoot, ".rulesync");
        if (!Directory.Exists(rulesyncDir)) return null;

        return new RulesyncDirectorySnapshot(
            HasRules: DirectoryHasEntries(Path.Combine(rulesyncDir, "rules")),
            HasMcp: File.Exists(Path.Combine(rulesyncDir, "mcp.json")),
            HasHooks: File.Exists(Path.Combine(rulesyncDir, "hooks.json")),
            HasPermissions: File.Exists(Path.Combine(rulesyncDir, "permissions.json")),
            HasSubagents: DirectoryHasEntries(Path.Combine(rulesyncDir, "subagents")),
            HasCommands: DirectoryHasEntries(Path.Combine(rulesyncDir, "commands")),
            HasSkills: DirectoryHasEntries(Path.Combine(rulesyncDir, "skills")),
            HasIgnore: File.Exists(Path.Combine(rulesyncDir, ".aiignore")));
    }

    private static bool DirectoryHasEntries(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
}
