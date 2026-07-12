using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptAgentTypeResolverTests
{
    [Theory]
    [InlineData("/proj/s1/subagents/agent-a.jsonl", true)]
    [InlineData("/proj/main.jsonl", false)]
    [InlineData("/proj/s1/agent-a.jsonl", false)]
    public void 親ディレクトリ名でsubagentファイルかどうかを判定する(string path, bool expected)
    {
        Assert.Equal(expected, TranscriptAgentTypeResolver.IsSubagentFile(path));
    }

    [Fact]
    public void meta用ファイルパスを組み立てる()
    {
        var actual = TranscriptAgentTypeResolver.GetMetaFilePath("/proj/s1/subagents/agent-abc.jsonl");

        Assert.Equal(Path.Combine("/proj/s1/subagents", "agent-abc.meta.json"), actual);
    }

    [Fact]
    public void agentTypeを抽出する()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("""{"agentType":"code-reviewer"}""");

        Assert.Equal("code-reviewer", actual);
    }

    [Fact]
    public void agentTypeフィールドが無ければnullを返す()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("""{"other":"value"}""");

        Assert.Null(actual);
    }

    [Fact]
    public void agentTypeが文字列でなければnullを返す()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("""{"agentType":123}""");

        Assert.Null(actual);
    }

    [Fact]
    public void agentTypeが空文字列ならnullを返す()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("""{"agentType":""}""");

        Assert.Null(actual);
    }

    [Fact]
    public void 不正なJSONは例外を投げずnullを返す()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("{ not valid json");

        Assert.Null(actual);
    }

    [Fact]
    public void JSON配列はオブジェクトでないためnullを返す()
    {
        var actual = TranscriptAgentTypeResolver.ExtractAgentType("[1,2,3]");

        Assert.Null(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空またはnullのJSONはnullを返す(string? json)
    {
        Assert.Null(TranscriptAgentTypeResolver.ExtractAgentType(json));
    }
}
