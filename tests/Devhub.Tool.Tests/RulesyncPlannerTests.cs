using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class RulesyncPlannerTests
{
    [Fact]
    public void 既定では2パスに分かれ_claudecodeパスからrulesが外れる()
    {
        var plan = RulesyncPlanner.BuildApplyPlan(new ApplyOptions());

        Assert.Equal(2, plan.Count);

        // パス1: claudecode 以外、rules を含む全機能
        var others = plan[0];
        Assert.Equal(new[] { "cursor", "copilot", "codexcli" }, others.Targets);
        Assert.Contains("rules", others.Features);

        // パス2: claudecode のみ、rules を含まない
        var claude = plan[1];
        Assert.Equal(new[] { "claudecode" }, claude.Targets);
        Assert.DoesNotContain("rules", claude.Features);
        Assert.Contains("mcp", claude.Features);
        Assert.Contains("hooks", claude.Features);
    }

    [Fact]
    public void 保護を無効化すると1パスで全ターゲットに全機能を流す()
    {
        var plan = RulesyncPlanner.BuildApplyPlan(new ApplyOptions { ProtectClaudeMd = false });

        var pass = Assert.Single(plan);
        Assert.Equal(ApplyOptions.DefaultTargets, pass.Targets);
        Assert.Contains("rules", pass.Features);
        Assert.Contains("claudecode", pass.Targets);
    }

    [Fact]
    public void claudecodeが対象外なら保護ONでも1パス()
    {
        var plan = RulesyncPlanner.BuildApplyPlan(new ApplyOptions
        {
            Targets = new[] { "cursor", "copilot" },
        });

        var pass = Assert.Single(plan);
        Assert.Equal(new[] { "cursor", "copilot" }, pass.Targets);
        Assert.Contains("rules", pass.Features);
    }

    [Fact]
    public void rulesを生成しないなら保護は不要で1パス()
    {
        var plan = RulesyncPlanner.BuildApplyPlan(new ApplyOptions
        {
            Features = new[] { "mcp", "hooks" },
        });

        var pass = Assert.Single(plan);
        Assert.Equal(ApplyOptions.DefaultTargets, pass.Targets);
        Assert.DoesNotContain("rules", pass.Features);
    }

    [Fact]
    public void claudecodeのみが対象なら_othersパスは作らずrules除外の1パスになる()
    {
        var plan = RulesyncPlanner.BuildApplyPlan(new ApplyOptions
        {
            Targets = new[] { "claudecode" },
        });

        var pass = Assert.Single(plan);
        Assert.Equal(new[] { "claudecode" }, pass.Targets);
        Assert.DoesNotContain("rules", pass.Features);
    }

    [Fact]
    public void ToArgumentsはrulesyncのgenerate引数を組み立てる()
    {
        var invocation = new RulesyncInvocation(
            new[] { "claudecode", "cursor" },
            new[] { "mcp", "hooks" });

        Assert.Equal("generate --targets claudecode,cursor --features mcp,hooks", invocation.ToArguments());
    }
}
