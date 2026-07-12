using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptScanPlannerTests
{
    [Fact]
    public void 状態ファイル無しの初回実行はどのファイルも読まずEOFだけを記録する()
    {
        var discovered = new Dictionary<string, long>
        {
            ["/proj/main.jsonl"] = 1000,
            ["/proj/s1/subagents/agent-a.jsonl"] = 500,
        };

        var actual = TranscriptScanPlanner.BuildPlan(
            discovered, hasPriorState: false, previousOffsets: new Dictionary<string, long>(), backfill: false);

        Assert.Equal(2, actual.Count);
        foreach (var task in actual)
        {
            Assert.False(task.ShouldRead);
            Assert.Equal(discovered[task.Path], task.StartOffset);
            Assert.Equal(discovered[task.Path], task.CurrentLength);
        }
    }

    [Fact]
    public void 状態ファイルありの通常実行では既存ファイルは記録済みオフセットから読む()
    {
        var discovered = new Dictionary<string, long> { ["/proj/main.jsonl"] = 1000 };
        var previous = new Dictionary<string, long> { ["/proj/main.jsonl"] = 400 };

        var actual = TranscriptScanPlanner.BuildPlan(discovered, hasPriorState: true, previous, backfill: false);

        var task = Assert.Single(actual);
        Assert.True(task.ShouldRead);
        Assert.Equal(400, task.StartOffset);
        Assert.Equal(1000, task.CurrentLength);
    }

    [Fact]
    public void 状態ファイルありでも前回未記録の新規ファイルはオフセット0から読む()
    {
        var discovered = new Dictionary<string, long>
        {
            ["/proj/main.jsonl"] = 1000,
            ["/proj/s1/subagents/agent-new.jsonl"] = 300, // 前回の状態には無い新規ファイル
        };
        var previous = new Dictionary<string, long> { ["/proj/main.jsonl"] = 1000 };

        var actual = TranscriptScanPlanner.BuildPlan(discovered, hasPriorState: true, previous, backfill: false);

        var newFileTask = actual.Single(t => t.Path == "/proj/s1/subagents/agent-new.jsonl");
        Assert.True(newFileTask.ShouldRead);
        Assert.Equal(0, newFileTask.StartOffset);
        Assert.Equal(300, newFileTask.CurrentLength);

        var existingTask = actual.Single(t => t.Path == "/proj/main.jsonl");
        Assert.True(existingTask.ShouldRead);
        Assert.Equal(1000, existingTask.StartOffset); // 差分無し
    }

    [Fact]
    public void backfill指定時は状態の有無に関わらず全ファイルをオフセット0から読む()
    {
        var discovered = new Dictionary<string, long> { ["/proj/main.jsonl"] = 1000 };
        var previous = new Dictionary<string, long> { ["/proj/main.jsonl"] = 900 };

        var actual = TranscriptScanPlanner.BuildPlan(discovered, hasPriorState: true, previous, backfill: true);

        var task = Assert.Single(actual);
        Assert.True(task.ShouldRead);
        Assert.Equal(0, task.StartOffset);
    }

    [Fact]
    public void backfillは状態ファイル無しの初回実行でも全ファイルを読む()
    {
        var discovered = new Dictionary<string, long> { ["/proj/main.jsonl"] = 1000 };

        var actual = TranscriptScanPlanner.BuildPlan(
            discovered, hasPriorState: false, previousOffsets: new Dictionary<string, long>(), backfill: true);

        var task = Assert.Single(actual);
        Assert.True(task.ShouldRead);
        Assert.Equal(0, task.StartOffset);
    }

    [Fact]
    public void discoveredFilesに存在しない前回追跡ファイルは計画に含まれない()
    {
        var discovered = new Dictionary<string, long> { ["/proj/main.jsonl"] = 1000 };
        var previous = new Dictionary<string, long>
        {
            ["/proj/main.jsonl"] = 500,
            ["/proj/s1/subagents/agent-deleted.jsonl"] = 200, // もう存在しない(消失)
        };

        var actual = TranscriptScanPlanner.BuildPlan(discovered, hasPriorState: true, previous, backfill: false);

        Assert.Single(actual);
        Assert.DoesNotContain(actual, t => t.Path == "/proj/s1/subagents/agent-deleted.jsonl");
    }

    [Fact]
    public void 記録済みオフセットが現在のファイルサイズを超える場合は現在サイズにクランプする()
    {
        var discovered = new Dictionary<string, long> { ["/proj/main.jsonl"] = 100 };
        var previous = new Dictionary<string, long> { ["/proj/main.jsonl"] = 9999 };

        var actual = TranscriptScanPlanner.BuildPlan(discovered, hasPriorState: true, previous, backfill: false);

        var task = Assert.Single(actual);
        Assert.Equal(100, task.StartOffset);
    }

    [Fact]
    public void discoveredFilesが空なら計画も空になる()
    {
        var actual = TranscriptScanPlanner.BuildPlan(
            new Dictionary<string, long>(), hasPriorState: false, new Dictionary<string, long>(), backfill: false);

        Assert.Empty(actual);
    }
}
