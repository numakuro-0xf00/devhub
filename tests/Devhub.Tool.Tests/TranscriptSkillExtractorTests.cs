using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptSkillExtractorTests
{
    [Fact]
    public void assistant行のSkillツール利用を抽出する()
    {
        var line = """
            {
              "type": "assistant",
              "timestamp": "2026-07-09T01:02:03.456Z",
              "sessionId": "session-1",
              "uuid": "uuid-1",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        var record = Assert.Single(actual);
        Assert.Equal(
            new TranscriptSkillUseRecord("roslyn-query", "session-1", "2026-07-09T01:02:03.456Z"),
            record);
    }

    [Fact]
    public void Skillツール以外のtool_useブロックは無視する()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Bash", "input": { "command": "ls" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void tool_useブロックが無いassistant行は空を返す()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": { "content": [ { "type": "text", "text": "hello" } ] }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void 複数のSkill呼び出しブロックをすべて抽出する()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-2",
              "timestamp": "2026-07-09T02:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } },
                  { "type": "text", "text": "..." },
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "worktree" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Equal(2, actual.Count);
        Assert.Equal("roslyn-query", actual[0].SkillName);
        Assert.Equal("worktree", actual[1].SkillName);
    }

    [Fact]
    public void assistant以外のtypeは除外する()
    {
        var line = """
            {
              "type": "user",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void attachmentのskill_listing行は利用イベントとして数えない()
    {
        var line = """
            {
              "type": "attachment",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "attachment": { "type": "skill_listing", "skills": ["roslyn-query", "worktree"] }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void assistant行にskill_listing添付が混入していても除外する()
    {
        // 実際のスキーマでは想定しにくいが、type=="assistant" フィルタだけに頼らない防御を確認する。
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "attachment": { "type": "skill_listing" },
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void attributionSkillフィールドは計数に使わずtool_useが無ければ空を返す()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "attributionSkill": "roslyn-query",
              "message": { "content": [ { "type": "text", "text": "..." } ] }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void attributionSkillが併存していてもtool_use由来のskill名だけを使う()
    {
        // attributionSkill と input.skill にあえて異なる値を与え、抽出結果が
        // tool_use(input.skill)由来であって attributionSkill 由来ではないことを区別できるようにする。
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "attributionSkill": "worktree",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        var record = Assert.Single(actual);
        Assert.Equal("roslyn-query", record.SkillName);
    }

    [Fact]
    public void input_argsが併存していても会話内容を読まずskill名だけを抽出する()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": {
                      "skill": "roslyn-query",
                      "args": "ここにコード断片や会話内容など秘匿すべき情報が入る想定"
                  } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        var record = Assert.Single(actual);
        Assert.Equal(
            new TranscriptSkillUseRecord("roslyn-query", "session-1", "2026-07-09T01:00:00.000Z"),
            record);
    }

    [Fact]
    public void input_skillが欠落しているブロックは除外する()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "args": "some conversational content" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void input_skillが空文字列のブロックは除外する()
    {
        var line = """
            {
              "type": "assistant",
              "sessionId": "session-1",
              "timestamp": "2026-07-09T01:00:00.000Z",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void sessionIdやtimestampが欠落していてもnullとして頑健に扱う()
    {
        var line = """
            {
              "type": "assistant",
              "message": {
                "content": [
                  { "type": "tool_use", "name": "Skill", "input": { "skill": "roslyn-query" } }
                ]
              }
            }
            """;

        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        var record = Assert.Single(actual);
        Assert.Equal(new TranscriptSkillUseRecord("roslyn-query", null, null), record);
    }

    [Fact]
    public void 不正なJSONは例外を投げず空を返す()
    {
        var actual = TranscriptSkillExtractor.ExtractFromLine("{ not valid json");

        Assert.Empty(actual);
    }

    [Fact]
    public void JSON配列はオブジェクトでないため空を返す()
    {
        var actual = TranscriptSkillExtractor.ExtractFromLine("[1, 2, 3]");

        Assert.Empty(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空またはnullの行は空を返す(string? line)
    {
        var actual = TranscriptSkillExtractor.ExtractFromLine(line);

        Assert.Empty(actual);
    }

    [Fact]
    public void messageやcontentが欠落したassistant行は空を返す()
    {
        var actual = TranscriptSkillExtractor.ExtractFromLine("""{ "type": "assistant", "sessionId": "s1" }""");

        Assert.Empty(actual);
    }
}
