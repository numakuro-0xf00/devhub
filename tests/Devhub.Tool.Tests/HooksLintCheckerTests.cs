using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class HooksLintCheckerTests
{
    [Fact]
    public void typeがhttpのエントリが無ければ違反無しでパースエラーも無い()
    {
        var json = """
            {
              "version": 1,
              "hooks": {},
              "claudecode": {
                "hooks": {
                  "postToolUse": [
                    { "matcher": ".*", "type": "command", "command": "dotnet devhub telemetry send --agent claudecode" }
                  ]
                }
              }
            }
            """;

        var result = HooksLintChecker.Check(json);

        Assert.False(result.HasViolations);
        Assert.Empty(result.Violations);
        Assert.Null(result.ParseError);
    }

    [Fact]
    public void トップレベルのhooks配下のtypehttpを検出する()
    {
        var json = """
            {
              "version": 1,
              "hooks": {
                "postToolUse": [
                  { "matcher": ".*", "type": "http", "url": "https://example.test/hook" }
                ]
              }
            }
            """;

        var result = HooksLintChecker.Check(json);

        Assert.True(result.HasViolations);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("hooks.postToolUse[0]", violation.Path);
    }

    [Fact]
    public void ターゲット別オーバーライド内のtypehttpを検出する()
    {
        var json = """
            {
              "version": 1,
              "hooks": {},
              "claudecode": {
                "hooks": {
                  "postToolUse": [
                    { "matcher": ".*", "type": "command", "command": "echo ok" },
                    { "matcher": ".*", "type": "http", "url": "https://example.test/hook" }
                  ]
                }
              },
              "cursor": {
                "hooks": {
                  "beforeMCPExecution": [
                    { "matcher": ".*", "type": "http", "url": "https://example.test/hook" }
                  ]
                }
              }
            }
            """;

        var result = HooksLintChecker.Check(json);

        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.Path == "claudecode.hooks.postToolUse[1]");
        Assert.Contains(result.Violations, v => v.Path == "cursor.hooks.beforeMCPExecution[0]");
    }

    [Fact]
    public void typeがcommandとpromptは許容し違反にしない()
    {
        var json = """
            {
              "version": 1,
              "hooks": {},
              "claudecode": {
                "hooks": {
                  "postToolUse": [
                    { "matcher": ".*", "type": "command", "command": "echo ok" }
                  ],
                  "userPromptSubmit": [
                    { "matcher": ".*", "type": "prompt", "prompt": "confirm?" }
                  ]
                }
              }
            }
            """;

        var result = HooksLintChecker.Check(json);

        Assert.False(result.HasViolations);
        Assert.Null(result.ParseError);
    }

    [Fact]
    public void JSONとして解釈できない場合はlint失敗ではなくParseErrorに理由を入れる()
    {
        var json = "{ invalid json ";

        var result = HooksLintChecker.Check(json);

        Assert.Empty(result.Violations);
        Assert.False(result.HasViolations);
        Assert.NotNull(result.ParseError);
    }

    [Fact]
    public void CheckRepositoryはhooksJsonが存在しない場合nullを返しスキップ扱いにする()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-hookslint-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var result = HooksLintChecker.CheckRepository(root);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CheckRepositoryは実在するhooksJsonの中身を検査する()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-hookslint-test-" + Guid.NewGuid());
        var rulesyncDir = Path.Combine(root, ".rulesync");
        Directory.CreateDirectory(rulesyncDir);
        try
        {
            File.WriteAllText(
                Path.Combine(rulesyncDir, "hooks.json"),
                """
                {
                  "version": 1,
                  "hooks": {
                    "postToolUse": [
                      { "matcher": ".*", "type": "http", "url": "https://example.test/hook" }
                    ]
                  }
                }
                """);

            var result = HooksLintChecker.CheckRepository(root);

            Assert.NotNull(result);
            Assert.True(result!.HasViolations);
            Assert.Equal("hooks.postToolUse[0]", Assert.Single(result.Violations).Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
