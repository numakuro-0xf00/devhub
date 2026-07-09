using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class RulesyncFeatureDetectorTests
{
    // 全 feature のソースが実在するスナップショット。
    private static RulesyncDirectorySnapshot AllPresent() => new(
        HasRules: true,
        HasMcp: true,
        HasHooks: true,
        HasPermissions: true,
        HasSubagents: true,
        HasCommands: true,
        HasSkills: true,
        HasIgnore: true);

    // 何もソースが無いスナップショット(.rulesync/ は存在するが中身が空)。
    private static RulesyncDirectorySnapshot NonePresent() => new(
        HasRules: false,
        HasMcp: false,
        HasHooks: false,
        HasPermissions: false,
        HasSubagents: false,
        HasCommands: false,
        HasSkills: false,
        HasIgnore: false);

    [Fact]
    public void 全機能存在するスナップショットからは全機能をDefaultFeaturesの順で検出する()
    {
        var detected = RulesyncFeatureDetector.DetectAvailableFeatures(AllPresent());

        Assert.Equal(
            new[] { "rules", "mcp", "hooks", "permissions", "subagents", "commands", "skills", "ignore" },
            detected);
        // 契約: 検出順は ApplyOptions.DefaultFeatures と揃える(RulesyncFeatureDetector の XML ドキュメントコメント参照)。
        Assert.Equal(ApplyOptions.DefaultFeatures, detected);
    }

    [Fact]
    public void 一部欠落したスナップショットからは実在する機能だけを順序を保って検出する()
    {
        // permissions.json と .aiignore が無い(実測どおり rulesync が Failed to load を出す組)。
        var snapshot = AllPresent() with { HasPermissions = false, HasIgnore = false };

        var detected = RulesyncFeatureDetector.DetectAvailableFeatures(snapshot);

        Assert.Equal(new[] { "rules", "mcp", "hooks", "subagents", "commands", "skills" }, detected);
    }

    [Fact]
    public void 全欠落したスナップショットからは空を検出する()
    {
        var detected = RulesyncFeatureDetector.DetectAvailableFeatures(NonePresent());

        Assert.Empty(detected);
    }

    [Fact]
    public void Narrowは既定features全機能存在なら絞り込まず全機能を返す()
    {
        var narrowed = RulesyncFeatureDetector.Narrow(
            ApplyOptions.DefaultFeatures, featuresExplicit: false, AllPresent());

        Assert.Equal(ApplyOptions.DefaultFeatures, narrowed.Features);
        Assert.Empty(narrowed.SkippedFeatures);
    }

    [Fact]
    public void Narrowは一部欠落なら実在する機能に絞り込みskippedに除外分を積む()
    {
        var snapshot = AllPresent() with { HasPermissions = false, HasIgnore = false };

        var narrowed = RulesyncFeatureDetector.Narrow(
            ApplyOptions.DefaultFeatures, featuresExplicit: false, snapshot);

        Assert.Equal(new[] { "rules", "mcp", "hooks", "subagents", "commands", "skills" }, narrowed.Features);
        Assert.Equal(new[] { "permissions", "ignore" }, narrowed.SkippedFeatures);
    }

    [Fact]
    public void Narrowは全欠落ならFeaturesが空になりSkippedFeaturesが要求分すべてになる()
    {
        var narrowed = RulesyncFeatureDetector.Narrow(
            ApplyOptions.DefaultFeatures, featuresExplicit: false, NonePresent());

        Assert.Empty(narrowed.Features);
        Assert.Equal(ApplyOptions.DefaultFeatures, narrowed.SkippedFeatures);
    }

    [Fact]
    public void Narrowはfeatures明示指定時はソースが全欠落していても絞り込まない()
    {
        var requested = new[] { "mcp", "hooks" };

        var narrowed = RulesyncFeatureDetector.Narrow(
            requested, featuresExplicit: true, NonePresent());

        Assert.Equal(requested, narrowed.Features);
        Assert.Empty(narrowed.SkippedFeatures);
    }

    [Fact]
    public void ReadSnapshotはrulesyncディレクトリが無ければnullを返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var snapshot = RulesyncFeatureDetector.ReadSnapshot(root);
            Assert.Null(snapshot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadSnapshotはファイルと非空ディレクトリだけをHasありと判定する()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        Directory.CreateDirectory(rulesyncDir);
        try
        {
            // ファイル系: mcp.json のみ実在させる。
            File.WriteAllText(Path.Combine(rulesyncDir, "mcp.json"), "{}");

            // ディレクトリ系: rules/ はファイルを1つ置く(実在扱い)。subagents/ は空ディレクトリ(実在扱いしない)。
            var rulesDir = Directory.CreateDirectory(Path.Combine(rulesyncDir, "rules"));
            File.WriteAllText(Path.Combine(rulesDir.FullName, "overview.md"), "# overview");
            Directory.CreateDirectory(Path.Combine(rulesyncDir, "subagents"));

            var snapshot = RulesyncFeatureDetector.ReadSnapshot(root);

            Assert.NotNull(snapshot);
            Assert.True(snapshot!.HasRules);
            Assert.True(snapshot.HasMcp);
            Assert.False(snapshot.HasHooks);
            Assert.False(snapshot.HasPermissions);
            Assert.False(snapshot.HasSubagents); // ディレクトリはあるが空なので実在扱いしない
            Assert.False(snapshot.HasCommands);
            Assert.False(snapshot.HasSkills);
            Assert.False(snapshot.HasIgnore);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // これは RulesyncFeatureDetector と RulesyncPlanner の配線を検証する統合テストである。
    // narrow で絞り込んだ結果が BuildApplyPlan に正しく流れ込み、2パス分割に反映されることを確認する。
    [Fact]
    public void Narrow後のfeaturesをBuildApplyPlanに渡すとCLAUDE_md保護の2パス分割が正しく動く()
    {
        // permissions と ignore のソースが無いケース。
        var snapshot = AllPresent() with { HasPermissions = false, HasIgnore = false };
        var narrowed = RulesyncFeatureDetector.Narrow(
            ApplyOptions.DefaultFeatures, featuresExplicit: false, snapshot);

        var options = new ApplyOptions { Features = narrowed.Features };
        var plan = RulesyncPlanner.BuildApplyPlan(options);

        Assert.Equal(2, plan.Count);

        // パス1: claudecode 以外、絞り込み後の全機能(rules を含む)。
        var others = plan[0];
        Assert.Equal(new[] { "cursor", "copilot", "codexcli" }, others.Targets);
        Assert.Contains("rules", others.Features);
        Assert.DoesNotContain("permissions", others.Features);
        Assert.DoesNotContain("ignore", others.Features);

        // パス2: claudecode のみ、rules は外れるが絞り込み後の他機能は残る。
        var claude = plan[1];
        Assert.Equal(new[] { "claudecode" }, claude.Targets);
        Assert.DoesNotContain("rules", claude.Features);
        Assert.Contains("mcp", claude.Features);
        Assert.Contains("hooks", claude.Features);
        Assert.Contains("subagents", claude.Features);
        Assert.DoesNotContain("permissions", claude.Features);
        Assert.DoesNotContain("ignore", claude.Features);
    }
}
