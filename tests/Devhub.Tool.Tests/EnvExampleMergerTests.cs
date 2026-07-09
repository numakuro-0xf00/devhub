using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class EnvExampleMergerTests
{
    [Fact]
    public void 既存内容が無ければ全変数が追記対象になる()
    {
        var plan = EnvExampleMerger.BuildMergePlan(existingContent: null, new[] { "FOO", "BAR" });

        Assert.True(plan.HasChanges);
        Assert.Equal(new[] { "FOO", "BAR" }, plan.MissingVariables);
        Assert.Equal(new[] { "FOO=", "BAR=" }, plan.LinesToAppend);
    }

    [Fact]
    public void 既存に無い変数だけが追記対象になり既存にある変数は除かれる()
    {
        var existing = "FOO=already-set\n";

        var plan = EnvExampleMerger.BuildMergePlan(existing, new[] { "FOO", "BAR" });

        Assert.Equal(new[] { "BAR" }, plan.MissingVariables);
        Assert.Equal(new[] { "BAR=" }, plan.LinesToAppend);
    }

    [Fact]
    public void 不足が無ければHasChangesはfalseになる()
    {
        var existing = "FOO=x\nBAR=y\n";

        var plan = EnvExampleMerger.BuildMergePlan(existing, new[] { "FOO", "BAR" });

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.MissingVariables);
        Assert.Empty(plan.LinesToAppend);
    }

    [Fact]
    public void ApplyMergePlanは既存内容をそのまま保持し末尾に追記行だけを足す()
    {
        var existing = "# コメントは保持される\nFOO=already-set\n";
        var plan = EnvExampleMerger.BuildMergePlan(existing, new[] { "FOO", "BAR" });

        var result = EnvExampleMerger.ApplyMergePlan(existing, plan);

        Assert.Equal("# コメントは保持される\nFOO=already-set\nBAR=\n", result);
    }

    [Fact]
    public void 末尾改行が無い既存内容には改行を補ってから追記する()
    {
        var existing = "FOO=already-set";
        var plan = EnvExampleMerger.BuildMergePlan(existing, new[] { "FOO", "BAR" });

        var result = EnvExampleMerger.ApplyMergePlan(existing, plan);

        Assert.Equal("FOO=already-set\nBAR=\n", result);
    }

    [Fact]
    public void 追記が無い場合ApplyMergePlanは既存内容をそのまま返す()
    {
        var existing = "FOO=x\n";
        var plan = EnvExampleMerger.BuildMergePlan(existing, new[] { "FOO" });

        var result = EnvExampleMerger.ApplyMergePlan(existing, plan);

        Assert.Equal(existing, result);
    }

    [Fact]
    public void 必須変数が空なら追記対象も空になる()
    {
        var plan = EnvExampleMerger.BuildMergePlan("FOO=x\n", Array.Empty<string>());

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.MissingVariables);
    }

    [Fact]
    public void ApplyMergePlanの結果に対して再度マージ計画を立てても変更は発生しない()
    {
        var existing = "# コメントは保持される\nFOO=already-set\n";
        var required = new[] { "FOO", "BAR" };

        var firstResult = EnvExampleMerger.ApplyMergePlan(existing, EnvExampleMerger.BuildMergePlan(existing, required));
        var secondPlan = EnvExampleMerger.BuildMergePlan(firstResult, required);
        var secondResult = EnvExampleMerger.ApplyMergePlan(firstResult, secondPlan);

        Assert.False(secondPlan.HasChanges);
        Assert.Equal(firstResult, secondResult);
    }
}
