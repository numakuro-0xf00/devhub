using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class CopilotSeatDiffPlannerTests
{
    [Fact]
    public void 初回実行はアクティブなseat全件を送信対象にする()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-01T00:00:00Z", "vscode"),
            new CopilotSeatSendItem("hash-bob", "2026-07-02T00:00:00Z", null),
        };

        var actual = CopilotSeatDiffPlanner.Plan(current, previousState: new Dictionary<string, string>());

        Assert.Equal(
            new[]
            {
                new CopilotSeatSendItem("hash-alice", "2026-07-01T00:00:00Z", "vscode"),
                new CopilotSeatSendItem("hash-bob", "2026-07-02T00:00:00Z", null),
            },
            actual.ToSend);
        Assert.Equal("2026-07-01T00:00:00Z", actual.NewState["hash-alice"]);
        Assert.Equal("2026-07-02T00:00:00Z", actual.NewState["hash-bob"]);
    }

    [Fact]
    public void 前回と同じlast_activity_atのユーザーはスキップする()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-01T00:00:00Z", "vscode"),
        };
        var previous = new Dictionary<string, string> { ["hash-alice"] = "2026-07-01T00:00:00Z" };

        var actual = CopilotSeatDiffPlanner.Plan(current, previous);

        Assert.Empty(actual.ToSend);
        Assert.Equal("2026-07-01T00:00:00Z", actual.NewState["hash-alice"]);
    }

    [Fact]
    public void 一部だけlast_activity_atが変化した場合はその分だけ送信対象にする()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-05T00:00:00Z", "vscode"), // 変化した
            new CopilotSeatSendItem("hash-bob", "2026-07-02T00:00:00Z", null),       // 変化なし
        };
        var previous = new Dictionary<string, string>
        {
            ["hash-alice"] = "2026-07-01T00:00:00Z",
            ["hash-bob"] = "2026-07-02T00:00:00Z",
        };

        var actual = CopilotSeatDiffPlanner.Plan(current, previous);

        Assert.Equal(
            new[] { new CopilotSeatSendItem("hash-alice", "2026-07-05T00:00:00Z", "vscode") },
            actual.ToSend);
        Assert.Equal("2026-07-05T00:00:00Z", actual.NewState["hash-alice"]);
        Assert.Equal("2026-07-02T00:00:00Z", actual.NewState["hash-bob"]);
    }

    [Fact]
    public void 状態に無い新規ユーザーは送信対象にする()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-bob", "2026-07-02T00:00:00Z", null),   // 既知
            new CopilotSeatSendItem("hash-carol", "2026-07-03T00:00:00Z", "jetbrains-ide"), // 新規
        };
        var previous = new Dictionary<string, string> { ["hash-bob"] = "2026-07-02T00:00:00Z" };

        var actual = CopilotSeatDiffPlanner.Plan(current, previous);

        Assert.Equal(
            new[] { new CopilotSeatSendItem("hash-carol", "2026-07-03T00:00:00Z", "jetbrains-ide") },
            actual.ToSend);
    }

    [Fact]
    public void currentに存在しなくなったユーザーは状態から掃除される()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-01T00:00:00Z", "vscode"),
        };
        var previous = new Dictionary<string, string>
        {
            ["hash-alice"] = "2026-07-01T00:00:00Z",
            ["hash-departed"] = "2026-06-01T00:00:00Z", // もう current に無い(退職/seat解除等)
        };

        var actual = CopilotSeatDiffPlanner.Plan(current, previous);

        Assert.Empty(actual.ToSend); // alice は変化なし
        Assert.DoesNotContain("hash-departed", actual.NewState.Keys);
        Assert.Single(actual.NewState);
    }

    [Fact]
    public void currentが空なら送信対象も新状態も空になる()
    {
        var actual = CopilotSeatDiffPlanner.Plan(
            Array.Empty<CopilotSeatSendItem>(),
            previousState: new Dictionary<string, string> { ["hash-alice"] = "2026-07-01T00:00:00Z" });

        Assert.Empty(actual.ToSend);
        Assert.Empty(actual.NewState);
    }

    [Fact]
    public void 送信に失敗した既存ユーザーは前回値に戻る()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-05T00:00:00Z", "vscode"), // 変化した
        };
        var previous = new Dictionary<string, string> { ["hash-alice"] = "2026-07-01T00:00:00Z" };

        var plan = CopilotSeatDiffPlanner.Plan(current, previous);
        var actual = CopilotSeatDiffPlanner.ReconcileAfterSend(plan, previous, failedUserIds: new[] { "hash-alice" });

        Assert.Equal("2026-07-01T00:00:00Z", actual["hash-alice"]);
    }

    [Fact]
    public void 送信に失敗した新規ユーザーは記録されない()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-carol", "2026-07-03T00:00:00Z", "jetbrains-ide"), // 新規
        };
        var previous = new Dictionary<string, string>();

        var plan = CopilotSeatDiffPlanner.Plan(current, previous);
        var actual = CopilotSeatDiffPlanner.ReconcileAfterSend(plan, previous, failedUserIds: new[] { "hash-carol" });

        Assert.DoesNotContain("hash-carol", actual.Keys);
    }

    [Fact]
    public void 一部成功一部失敗の混在では成功分のみ更新される()
    {
        var current = new[]
        {
            new CopilotSeatSendItem("hash-alice", "2026-07-05T00:00:00Z", "vscode"), // 送信成功想定
            new CopilotSeatSendItem("hash-bob", "2026-07-06T00:00:00Z", null),       // 送信失敗想定
        };
        var previous = new Dictionary<string, string>
        {
            ["hash-alice"] = "2026-07-01T00:00:00Z",
            ["hash-bob"] = "2026-07-02T00:00:00Z",
        };

        var plan = CopilotSeatDiffPlanner.Plan(current, previous);
        var actual = CopilotSeatDiffPlanner.ReconcileAfterSend(plan, previous, failedUserIds: new[] { "hash-bob" });

        Assert.Equal("2026-07-05T00:00:00Z", actual["hash-alice"]); // 成功分は新しい値のまま
        Assert.Equal("2026-07-02T00:00:00Z", actual["hash-bob"]);   // 失敗分は前回値に戻る
    }
}
