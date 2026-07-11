using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TelemetryEventNormalizerTests
{
    [Fact]
    public void ClaudeCodeのPostToolUseイベントを正規化する()
    {
        var payload = """
            {
              "hook_event_name": "PostToolUse",
              "tool_name": "Bash",
              "session_id": "session-1",
              "cwd": "/repo"
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.Equal(
            new NormalizedTelemetryEvent(
                Event: "post_tool_use",
                Agent: "claudecode",
                ToolName: "Bash",
                AgentType: null,
                RawSessionId: "session-1",
                RawUserSeed: null),
            actual);
    }

    [Fact]
    public void ClaudeCodeのsubagent系イベントはagent_typeを取得する()
    {
        var payload = """
            {
              "hook_event_name": "SubagentStop",
              "session_id": "session-2",
              "agent_type": "code-reviewer"
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.Equal(
            new NormalizedTelemetryEvent(
                Event: "subagent_stop",
                Agent: "claudecode",
                ToolName: null,
                AgentType: "code-reviewer",
                RawSessionId: "session-2",
                RawUserSeed: null),
            actual);
    }

    [Fact]
    public void Cursorのイベントを正規化しuser_emailをRawUserSeedとして渡す()
    {
        var payload = """
            {
              "hook_event_name": "afterFileEdit",
              "tool_name": "edit_file",
              "conversation_id": "conv-1",
              "user_email": "alice@example.com"
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.Cursor, payload);

        Assert.Equal(
            new NormalizedTelemetryEvent(
                Event: "after_file_edit",
                Agent: "cursor",
                ToolName: "edit_file",
                AgentType: null,
                RawSessionId: "conv-1",
                RawUserSeed: "alice@example.com"),
            actual);
    }

    [Fact]
    public void Cursorのsubagent_typeはagent_typeとして取得する()
    {
        var payload = """
            {
              "hook_event_name": "beforeSubmitPrompt",
              "conversation_id": "conv-2",
              "subagent_type": "planner"
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.Cursor, payload);

        Assert.NotNull(actual);
        Assert.Equal("planner", actual!.AgentType);
        Assert.Equal("before_submit_prompt", actual.Event);
    }

    [Fact]
    public void CursorはuserEmailが無ければRawUserSeedがnullになる()
    {
        var payload = """{ "hook_event_name": "stop", "conversation_id": "conv-3" }""";

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.Cursor, payload);

        Assert.NotNull(actual);
        Assert.Null(actual!.RawUserSeed);
    }

    [Fact]
    public void CodexCliのイベントを正規化しEventは常にnullになる()
    {
        var payload = """
            {
              "tool_name": "shell",
              "session_id": "sess-9",
              "agent_type": "reviewer"
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.CodexCli, payload);

        Assert.Equal(
            new NormalizedTelemetryEvent(
                Event: null,
                Agent: "codexcli",
                ToolName: "shell",
                AgentType: "reviewer",
                RawSessionId: "sess-9",
                RawUserSeed: null),
            actual);
    }

    [Fact]
    public void 欠落フィールドはnullとして頑健に扱う()
    {
        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, "{}");

        Assert.Equal(
            new NormalizedTelemetryEvent(
                Event: null,
                Agent: "claudecode",
                ToolName: null,
                AgentType: null,
                RawSessionId: null,
                RawUserSeed: null),
            actual);
    }

    [Fact]
    public void 未知のフィールドが含まれていても無視して処理する()
    {
        var payload = """
            {
              "hook_event_name": "PostToolUse",
              "tool_name": "Bash",
              "session_id": "session-1",
              "some_unknown_field": { "nested": true },
              "another_unknown": [1, 2, 3]
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.NotNull(actual);
        Assert.Equal("post_tool_use", actual!.Event);
        Assert.Equal("Bash", actual.ToolName);
    }

    [Fact]
    public void 値が空文字列のフィールドは欠落と同様にnullとして扱う()
    {
        var payload = """
            {
              "hook_event_name": "PostToolUse",
              "tool_name": "",
              "session_id": ""
            }
            """;

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.NotNull(actual);
        Assert.Null(actual!.ToolName);
        Assert.Null(actual.RawSessionId);
    }

    [Fact]
    public void 文字列型でないフィールドはnullとして扱う()
    {
        var payload = """{ "hook_event_name": "PostToolUse", "tool_name": 123, "session_id": null }""";

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.NotNull(actual);
        Assert.Null(actual!.ToolName);
        Assert.Null(actual.RawSessionId);
    }

    [Fact]
    public void 不正なJSONは例外を投げずnullを返す()
    {
        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, "{ not valid json");

        Assert.Null(actual);
    }

    [Fact]
    public void JSON配列はオブジェクトでないためnullを返す()
    {
        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, "[1, 2, 3]");

        Assert.Null(actual);
    }

    [Fact]
    public void JSONスカラー値はオブジェクトでないためnullを返す()
    {
        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, "\"hello\"");

        Assert.Null(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空またはnullのペイロードはnullを返す(string? payload)
    {
        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.Null(actual);
    }

    [Fact]
    public void Cursorの実在イベント名beforeMCPExecutionは頭字語を分断せず変換する()
    {
        // beforeMCPExecution は doc/phase2.md の元調査で確認済みの Cursor 実在イベント名。
        var payload = """{ "hook_event_name": "beforeMCPExecution", "conversation_id": "conv-4" }""";

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.Cursor, payload);

        Assert.NotNull(actual);
        Assert.Equal("before_mcp_execution", actual!.Event);
    }

    [Fact]
    public void 全て大文字のイベント名STOPは頭字語として1単語に変換する()
    {
        var payload = """{ "hook_event_name": "STOP", "session_id": "session-5" }""";

        var actual = TelemetryEventNormalizer.Normalize(TelemetryAgentKind.ClaudeCode, payload);

        Assert.NotNull(actual);
        Assert.Equal("stop", actual!.Event);
    }

    [Theory]
    [InlineData(TelemetryAgentKind.ClaudeCode, "claudecode")]
    [InlineData(TelemetryAgentKind.Cursor, "cursor")]
    [InlineData(TelemetryAgentKind.CodexCli, "codexcli")]
    public void WireNameはCLIの引数表記と一致する(TelemetryAgentKind agent, string expected)
    {
        Assert.Equal(expected, TelemetryEventNormalizer.WireName(agent));
    }
}
