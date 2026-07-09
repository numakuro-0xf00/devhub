using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class EnvVarResolverTests
{
    private static IReadOnlyDictionary<string, string> Env(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void OS環境変数に非空値があれば設定済みと判定する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "FOO" },
            osEnvironment: Env(("FOO", "value")),
            dotEnvFile: Env());

        Assert.Equal(new[] { new EnvVarStatus("FOO", true) }, statuses);
    }

    [Fact]
    public void dotEnvに非空値があれば設定済みと判定する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "FOO" },
            osEnvironment: Env(),
            dotEnvFile: Env(("FOO", "value")));

        Assert.Equal(new[] { new EnvVarStatus("FOO", true) }, statuses);
    }

    [Fact]
    public void どちらにも無ければ未設定と判定する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "FOO" },
            osEnvironment: Env(),
            dotEnvFile: Env());

        Assert.Equal(new[] { new EnvVarStatus("FOO", false) }, statuses);
    }

    [Fact]
    public void どちらにもあるが値が空文字列なら未設定と判定する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "FOO" },
            osEnvironment: Env(("FOO", "")),
            dotEnvFile: Env(("FOO", "")));

        Assert.Equal(new[] { new EnvVarStatus("FOO", false) }, statuses);
    }

    [Fact]
    public void 片方が空でもう片方に非空値があれば設定済みと判定する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "FOO" },
            osEnvironment: Env(("FOO", "")),
            dotEnvFile: Env(("FOO", "value")));

        Assert.Equal(new[] { new EnvVarStatus("FOO", true) }, statuses);
    }

    [Fact]
    public void 複数変数を要求順を保って解決する()
    {
        var statuses = EnvVarResolver.Resolve(
            new[] { "A", "B", "C" },
            osEnvironment: Env(("B", "value")),
            dotEnvFile: Env(("C", "value")));

        Assert.Equal(
            new[]
            {
                new EnvVarStatus("A", false),
                new EnvVarStatus("B", true),
                new EnvVarStatus("C", true),
            },
            statuses);
    }
}
