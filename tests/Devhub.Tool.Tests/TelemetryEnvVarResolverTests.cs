using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TelemetryEnvVarResolverTests
{
    private static IReadOnlyDictionary<string, string> Env(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void OS環境変数に非空値があればそれを返す()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(("FOO", "os-value")),
            dotEnvFile: Env());

        Assert.Equal("os-value", actual);
    }

    [Fact]
    public void OS環境変数に無ければdotEnvの値を返す()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(),
            dotEnvFile: Env(("FOO", "dot-value")));

        Assert.Equal("dot-value", actual);
    }

    [Fact]
    public void OS環境変数がdotEnvより優先される()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(("FOO", "os-value")),
            dotEnvFile: Env(("FOO", "dot-value")));

        Assert.Equal("os-value", actual);
    }

    [Fact]
    public void どちらにも無ければnullを返す()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(),
            dotEnvFile: Env());

        Assert.Null(actual);
    }

    [Fact]
    public void OS環境変数が空文字列ならdotEnvにフォールバックする()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(("FOO", "")),
            dotEnvFile: Env(("FOO", "dot-value")));

        Assert.Equal("dot-value", actual);
    }

    [Fact]
    public void 両方とも空文字列ならnullを返す()
    {
        var actual = TelemetryEnvVarResolver.Resolve(
            "FOO",
            osEnvironment: Env(("FOO", "")),
            dotEnvFile: Env(("FOO", "")));

        Assert.Null(actual);
    }
}
