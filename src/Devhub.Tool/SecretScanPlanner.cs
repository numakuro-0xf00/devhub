namespace Devhub.Tool;

/// <summary>
/// スキャン対象候補ごとの実在フラグのスナップショット。
/// devhub が扱う「設定ソースと生成物」のうち、実際にリポジトリに存在するものだけをスキャン対象にする
/// (要件 FR-3.1/FR-3.2、doc/phase1.md 未実装項目「秘匿情報の混入チェック統合」)。
/// </summary>
public sealed record SecretScanTargetSnapshot(
    bool HasRulesyncDir,
    bool HasAgentsMd,
    bool HasClaudeMd,
    bool HasClaudeDir,
    bool HasCursorDir,
    bool HasCodexDir,
    bool HasGithubDir,
    bool HasMcpJson,
    bool HasAgentsDir);

/// <summary>
/// secretlint の 1 回分の呼び出し(設定・ignore ファイル・対象パスの組)。
/// </summary>
/// <param name="ConfigPath">--secretlintrc に渡す設定ファイルパス。任意の絶対/相対パスで良い(実地検証済み)。</param>
/// <param name="IgnoreFileName">
/// --secretlintignore に渡すファイル名。secretlint はこの値を各ディレクトリへ相対結合して
/// カスケード的に(.gitignore と同様に)探すため、必ず作業ディレクトリ(リポジトリルート)直下からの
/// 「相対パス」で渡すこと。絶対パスを渡すと <c>path.join(dir, absolutePath)</c> の Node.js の挙動により
/// 意図したファイルを指さず、除外が効かなくなることを実地検証で確認済み。
/// </param>
/// <param name="Targets">スキャン対象のパス(リポジトリルートからの相対パス)一覧。</param>
public sealed record SecretlintInvocation(string ConfigPath, string IgnoreFileName, IReadOnlyList<string> Targets)
{
    /// <summary>ログ・テスト表示用の可読な引数文字列(実行時は ArgumentList を使う)。</summary>
    public string ToArguments() =>
        $"--secretlintrc {ConfigPath} --secretlintignore {IgnoreFileName} {string.Join(" ", Targets)}";
}

/// <summary>
/// リポジトリ内に実在する「設定ソースと生成物」だけへスキャン対象を絞り込む純粋ロジック。
/// rulesync 側の RulesyncFeatureDetector と同じ設計方針:スナップショットから対象を決める純粋関数
/// (<see cref="BuildScanTargets"/>)と、実ファイルシステムを読む薄い IO ラッパー(<see cref="ReadSnapshot"/>)を分離する。
/// </summary>
public static class SecretScanPlanner
{
    /// <summary>
    /// secretlint に渡す --secretlintignore の既定内容。
    /// secretlint 自身が既定で除外するのは <c>**/node_modules</c> と <c>**/.git</c> のみ(実地検証済み)で、
    /// .NET のビルド成果物 <c>bin/</c> <c>obj/</c> は対象に含まれないため明示的に追加する。
    /// 末尾に <c>/**</c> を付けない書き方は secretlint 本体の既定除外パターンと同じ流儀
    /// (ディレクトリ自体を剪定でき、配下を1つずつ読みに行かずに済む)。
    /// </summary>
    public const string DefaultIgnorePatterns =
        """
        **/node_modules
        **/bin
        **/obj
        **/.git
        """;

    /// <summary>
    /// スナップショットから、実在するスキャン対象(リポジトリルートからの相対パス)一覧を返す。
    /// </summary>
    public static IReadOnlyList<string> BuildScanTargets(SecretScanTargetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var targets = new List<string>();
        if (snapshot.HasRulesyncDir) targets.Add(".rulesync");
        if (snapshot.HasAgentsMd) targets.Add("AGENTS.md");
        if (snapshot.HasClaudeMd) targets.Add("CLAUDE.md");
        if (snapshot.HasClaudeDir) targets.Add(".claude");
        if (snapshot.HasCursorDir) targets.Add(".cursor");
        if (snapshot.HasCodexDir) targets.Add(".codex");
        if (snapshot.HasGithubDir) targets.Add(".github");
        if (snapshot.HasMcpJson) targets.Add(".mcp.json");
        if (snapshot.HasAgentsDir) targets.Add(".agents");
        return targets;
    }

    /// <summary>
    /// 実ファイルシステムを読み取り、スキャン対象候補の実在スナップショットを作る薄い IO ラッパー。
    /// </summary>
    public static SecretScanTargetSnapshot ReadSnapshot(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        bool Exists(string relativePath)
        {
            var full = Path.Combine(repositoryRoot, relativePath);
            return Directory.Exists(full) || File.Exists(full);
        }

        return new SecretScanTargetSnapshot(
            HasRulesyncDir: Exists(".rulesync"),
            HasAgentsMd: Exists("AGENTS.md"),
            HasClaudeMd: Exists("CLAUDE.md"),
            HasClaudeDir: Exists(".claude"),
            HasCursorDir: Exists(".cursor"),
            HasCodexDir: Exists(".codex"),
            HasGithubDir: Exists(".github"),
            HasMcpJson: Exists(".mcp.json"),
            HasAgentsDir: Exists(".agents"));
    }
}
