using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptSkillScannerTests
{
    private static string SkillLine(string skillName) =>
        "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-09T00:00:00.000Z\",\"message\":{\"content\":[" +
        "{\"type\":\"tool_use\",\"name\":\"Skill\",\"input\":{\"skill\":\"" + skillName + "\"}}]}}";

    private static string TwoSkillsLine(string first, string second) =>
        "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-09T00:00:00.000Z\",\"message\":{\"content\":[" +
        "{\"type\":\"tool_use\",\"name\":\"Skill\",\"input\":{\"skill\":\"" + first + "\"}}," +
        "{\"type\":\"tool_use\",\"name\":\"Skill\",\"input\":{\"skill\":\"" + second + "\"}}]}}";

    private const string PlainLine = """{"type":"assistant","sessionId":"s1","message":{"content":[{"type":"text","text":"hi"}]}}""";

    [Fact]
    public void 上限内であればすべての行を消費してイベントを返す()
    {
        var lines = new[]
        {
            new TranscriptLine(SkillLine("a"), 100),
            new TranscriptLine(SkillLine("b"), 200),
        };

        var actual = TranscriptSkillScanner.Scan(lines, remainingBudget: 1000);

        Assert.Equal(2, actual.Events.Count);
        Assert.Equal("a", actual.Events[0].SkillName);
        Assert.Equal("b", actual.Events[1].SkillName);
        Assert.Equal(300, actual.ConsumedBytes);
    }

    [Fact]
    public void 上限に達する行の手前で停止し以降は消費しない()
    {
        var lines = new[]
        {
            new TranscriptLine(SkillLine("a"), 100),
            new TranscriptLine(SkillLine("b"), 200),
            new TranscriptLine(SkillLine("c"), 300),
        };

        var actual = TranscriptSkillScanner.Scan(lines, remainingBudget: 2);

        Assert.Equal(2, actual.Events.Count);
        Assert.Equal(new[] { "a", "b" }, actual.Events.Select(e => e.SkillName));
        Assert.Equal(300, actual.ConsumedBytes); // a(100) + b(200) のみ。c は次回に持ち越し。
    }

    [Fact]
    public void 上限0だと最初の行から一切消費しない()
    {
        var lines = new[] { new TranscriptLine(SkillLine("a"), 100) };

        var actual = TranscriptSkillScanner.Scan(lines, remainingBudget: 0);

        Assert.Empty(actual.Events);
        Assert.Equal(0, actual.ConsumedBytes);
    }

    [Fact]
    public void イベントを含まない行は上限到達後でも消費される()
    {
        var lines = new[]
        {
            new TranscriptLine(SkillLine("a"), 100),
            new TranscriptLine(PlainLine, 50),
            new TranscriptLine(SkillLine("b"), 200), // これは上限超過で停止
        };

        var actual = TranscriptSkillScanner.Scan(lines, remainingBudget: 1);

        Assert.Equal(new[] { "a" }, actual.Events.Select(e => e.SkillName));
        // a(100) + イベント無しの plain 行(50) は消費されるが、b(200) は持ち越されるため含まれない。
        Assert.Equal(150, actual.ConsumedBytes);
    }

    [Fact]
    public void 一行に複数イベントがあり上限を超える場合はその行ごと持ち越す()
    {
        var lines = new[] { new TranscriptLine(TwoSkillsLine("a", "b"), 100) };

        var actual = TranscriptSkillScanner.Scan(lines, remainingBudget: 1);

        Assert.Empty(actual.Events);
        Assert.Equal(0, actual.ConsumedBytes);
    }

    [Fact]
    public void 行が空リストなら何も消費しない()
    {
        var actual = TranscriptSkillScanner.Scan(Array.Empty<TranscriptLine>(), remainingBudget: 1000);

        Assert.Empty(actual.Events);
        Assert.Equal(0, actual.ConsumedBytes);
    }
}
