using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class SecretScanPlannerTests
{
    // 全候補が実在するスナップショット。
    private static SecretScanTargetSnapshot AllPresent() => new(
        HasRulesyncDir: true,
        HasAgentsMd: true,
        HasClaudeMd: true,
        HasClaudeDir: true,
        HasCursorDir: true,
        HasCodexDir: true,
        HasGithubDir: true,
        HasMcpJson: true,
        HasAgentsDir: true,
        HasTelemetryDir: true);

    // 何も実在しないスナップショット。
    private static SecretScanTargetSnapshot NonePresent() => new(
        HasRulesyncDir: false,
        HasAgentsMd: false,
        HasClaudeMd: false,
        HasClaudeDir: false,
        HasCursorDir: false,
        HasCodexDir: false,
        HasGithubDir: false,
        HasMcpJson: false,
        HasAgentsDir: false,
        HasTelemetryDir: false);

    [Fact]
    public void 全候補が実在するスナップショットからは全対象を決まった順序で返す()
    {
        var targets = SecretScanPlanner.BuildScanTargets(AllPresent());

        Assert.Equal(
            new[] { ".rulesync", "AGENTS.md", "CLAUDE.md", ".claude", ".cursor", ".codex", ".github", ".mcp.json", ".agents", "telemetry" },
            targets);
    }

    [Fact]
    public void 何も実在しないスナップショットからは空を返す()
    {
        var targets = SecretScanPlanner.BuildScanTargets(NonePresent());

        Assert.Empty(targets);
    }

    [Fact]
    public void 一部だけ実在するスナップショットからは実在するものだけを順序を保って返す()
    {
        var snapshot = NonePresent() with { HasAgentsMd = true, HasClaudeMd = true, HasGithubDir = true };

        var targets = SecretScanPlanner.BuildScanTargets(snapshot);

        Assert.Equal(new[] { "AGENTS.md", "CLAUDE.md", ".github" }, targets);
    }

    [Fact]
    public void telemetryディレクトリが実在する場合は末尾の対象として加わる()
    {
        var snapshot = NonePresent() with { HasAgentsMd = true, HasTelemetryDir = true };

        var targets = SecretScanPlanner.BuildScanTargets(snapshot);

        Assert.Equal(new[] { "AGENTS.md", "telemetry" }, targets);
    }

    [Fact]
    public void ReadSnapshotは実ファイルシステムのファイルとディレクトリの実在を反映する()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-secrets-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            // ファイル系: AGENTS.md と .mcp.json だけ実在させる。
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# agents");
            File.WriteAllText(Path.Combine(root, ".mcp.json"), "{}");

            // ディレクトリ系: .claude/ と telemetry/ だけ実在させる(中身が空でも実在扱いでよい。rulesync 側の
            // 「非空でなければ実在扱いしない」制約とは異なり、秘匿情報スキャンでは空ディレクトリを
            // スキャン対象に含めても実害が無いため単純にディレクトリの実在有無だけを見る)。
            Directory.CreateDirectory(Path.Combine(root, ".claude"));
            Directory.CreateDirectory(Path.Combine(root, "telemetry"));

            var snapshot = SecretScanPlanner.ReadSnapshot(root);

            Assert.False(snapshot.HasRulesyncDir);
            Assert.True(snapshot.HasAgentsMd);
            Assert.False(snapshot.HasClaudeMd);
            Assert.True(snapshot.HasClaudeDir);
            Assert.False(snapshot.HasCursorDir);
            Assert.False(snapshot.HasCodexDir);
            Assert.False(snapshot.HasGithubDir);
            Assert.True(snapshot.HasMcpJson);
            Assert.False(snapshot.HasAgentsDir);
            Assert.True(snapshot.HasTelemetryDir);

            var targets = SecretScanPlanner.BuildScanTargets(snapshot);
            Assert.Equal(new[] { "AGENTS.md", ".claude", ".mcp.json", "telemetry" }, targets);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultIgnorePatternsはnode_modulesとbinとobjとgitを除外する()
    {
        // 改行コードのOS依存を避けるため正規化して比較する。
        Assert.Equal(
            "**/node_modules\n**/bin\n**/obj\n**/.git",
            SecretScanPlanner.DefaultIgnorePatterns.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ToArgumentsはsecretlintの引数を組み立てる()
    {
        var invocation = new SecretlintInvocation(
            "/tmp/.secretlintrc.json",
            ".devhub-secretlint-ignore.tmp",
            new[] { "AGENTS.md", ".claude" });

        Assert.Equal(
            "--secretlintrc /tmp/.secretlintrc.json --secretlintignore .devhub-secretlint-ignore.tmp AGENTS.md .claude",
            invocation.ToArguments());
    }
}
