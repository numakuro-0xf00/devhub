using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class SecretlintConfigResolverTests
{
    [Fact]
    public void ResolveConfigPathはリポジトリ設定が実在すればそれを優先する()
    {
        var resolved = SecretlintConfigResolver.ResolveConfigPath(
            repoConfigPath: "/repo/.secretlintrc.json",
            fallbackConfigPath: "/tmp/devhub-default.json");

        Assert.Equal("/repo/.secretlintrc.json", resolved);
    }

    [Fact]
    public void ResolveConfigPathはリポジトリ設定が無ければ既定設定のパスを返す()
    {
        var resolved = SecretlintConfigResolver.ResolveConfigPath(
            repoConfigPath: null,
            fallbackConfigPath: "/tmp/devhub-default.json");

        Assert.Equal("/tmp/devhub-default.json", resolved);
    }

    [Fact]
    public void FindRepoConfigPathはsecretlintrcJsonが実在すればそのパスを返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-secretlint-cfg-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var expected = Path.Combine(root, SecretlintConfigResolver.RepoConfigFileName);
            File.WriteAllText(expected, "{}");

            var found = SecretlintConfigResolver.FindRepoConfigPath(root);

            Assert.Equal(expected, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindRepoConfigPathは実在しなければnullを返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "devhub-secretlint-cfg-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var found = SecretlintConfigResolver.FindRepoConfigPath(root);

            Assert.Null(found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteDefaultConfigToTempFileはpresetRecommendを有効化したJSONを書き出す()
    {
        var path = SecretlintConfigResolver.WriteDefaultConfigToTempFile();
        try
        {
            Assert.True(File.Exists(path));
            var content = File.ReadAllText(path);
            Assert.Contains("@secretlint/secretlint-rule-preset-recommend", content);
            Assert.Equal(SecretlintConfigResolver.DefaultConfigJson, content);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
