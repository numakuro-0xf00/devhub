using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class CopilotSeatEventExtractorTests
{
    [Fact]
    public void 正常なseatを抽出しeditorの先頭セグメントをagent_typeにする()
    {
        var body = """
            {
              "total_seats": 1,
              "seats": [
                {
                  "created_at": "2026-01-01T00:00:00Z",
                  "last_activity_at": "2026-07-09T01:02:03Z",
                  "last_activity_editor": "vscode/1.85.1",
                  "assignee": { "login": "alice", "id": 1 }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Equal(
            new CopilotSeatCandidate("alice", "2026-07-09T01:02:03Z", "vscode"),
            seat);
    }

    [Fact]
    public void last_activity_atがnullのseatは送信対象から除外する()
    {
        var body = """
            {
              "total_seats": 1,
              "seats": [
                {
                  "created_at": "2026-01-01T00:00:00Z",
                  "last_activity_at": null,
                  "last_activity_editor": null,
                  "assignee": { "login": "bob", "id": 2 }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }

    [Fact]
    public void last_activity_editorが欠落していればagent_typeはnullになる()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "assignee": { "login": "carol" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Null(seat.AgentType);
    }

    [Fact]
    public void last_activity_editorがnullフィールドとして存在してもagent_typeはnullになる()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "last_activity_editor": null,
                  "assignee": { "login": "carol" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Null(seat.AgentType);
    }

    [Fact]
    public void last_activity_editorが数値の場合はagent_typeがnullになる()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "last_activity_editor": 123,
                  "assignee": { "login": "frank" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Equal("frank", seat.RawLogin);
        Assert.Equal("2026-07-09T01:00:00Z", seat.LastActivityAt);
        Assert.Null(seat.AgentType);
    }

    [Theory]
    [InlineData("jetbrains-ide/2024.1", "jetbrains-ide")]
    [InlineData("vscode", "vscode")]
    [InlineData("vscode/1.85.1/win32-x64", "vscode")]
    public void editorのセグメント切り出しを検証する(string editor, string expectedAgentType)
    {
        var body = $$"""
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "last_activity_editor": "{{editor}}",
                  "assignee": { "login": "dave" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Equal(expectedAgentType, seat.AgentType);
    }

    [Fact]
    public void editorの先頭セグメントが空文字列になる場合はnullとして扱う()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "last_activity_editor": "/1.0.0",
                  "assignee": { "login": "erin" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        var seat = Assert.Single(actual);
        Assert.Null(seat.AgentType);
    }

    [Fact]
    public void assigneeが欠落しているseatはスキップする()
    {
        var body = """
            {
              "seats": [
                { "last_activity_at": "2026-07-09T01:00:00Z" }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }

    [Fact]
    public void assignee_loginが空文字列のseatはスキップする()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "assignee": { "login": "" }
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }

    [Fact]
    public void assigneeがnullのseatはスキップする()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "assignee": null
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }

    [Fact]
    public void assigneeが文字列のseatはスキップする()
    {
        var body = """
            {
              "seats": [
                {
                  "last_activity_at": "2026-07-09T01:00:00Z",
                  "assignee": "alice"
                }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }

    [Fact]
    public void 壊れたseatエントリだけをスキップし他は抽出を継続する()
    {
        var body = """
            {
              "seats": [
                { "last_activity_at": "2026-07-09T01:00:00Z", "assignee": { "login": "ok-user-1" } },
                { "last_activity_at": null, "assignee": { "login": "no-activity" } },
                { "last_activity_at": "2026-07-09T02:00:00Z" },
                "not-an-object",
                { "last_activity_at": "2026-07-09T03:00:00Z", "assignee": { "login": "ok-user-2" } }
              ]
            }
            """;

        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Equal(2, actual.Count);
        Assert.Equal("ok-user-1", actual[0].RawLogin);
        Assert.Equal("ok-user-2", actual[1].RawLogin);
    }

    [Fact]
    public void seats配列が無いレスポンスは空を返す()
    {
        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson("""{ "total_seats": 0 }""");

        Assert.Empty(actual);
    }

    [Fact]
    public void seatsが配列以外の場合は空を返す()
    {
        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson("""{ "seats": {} }""");

        Assert.Empty(actual);
    }

    [Fact]
    public void 不正なJSONは例外を投げず空を返す()
    {
        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson("{ not valid json");

        Assert.Empty(actual);
    }

    [Fact]
    public void JSON配列はオブジェクトでないため空を返す()
    {
        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson("[1, 2, 3]");

        Assert.Empty(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空またはnullのレスポンスは空を返す(string? body)
    {
        var actual = CopilotSeatEventExtractor.ExtractFromResponseJson(body);

        Assert.Empty(actual);
    }
}
