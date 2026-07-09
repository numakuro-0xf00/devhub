using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class EnvPlaceholderScannerTests
{
    [Fact]
    public void 複数のプレースホルダを重複排除して初出順で抽出する()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("${A} と ${B} と再び ${A}");

        Assert.Equal(new[] { "A", "B" }, names);
    }

    [Fact]
    public void デフォルト値付きプレースホルダは変数名だけを抽出し既定値部分は無視する()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("${HOST:-localhost:8080}");

        Assert.Equal(new[] { "HOST" }, names);
    }

    [Fact]
    public void ブレース無しのドル変数記法は対象外()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("$VAR は無視され ${OK} だけ拾う");

        Assert.Equal(new[] { "OK" }, names);
    }

    [Fact]
    public void 先頭が数字の不正な変数名は無視する()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("${1BAD} ${_ok2} ${2ALSO_BAD}");

        Assert.Equal(new[] { "_ok2" }, names);
    }

    [Fact]
    public void プレースホルダが1つも無ければ空を返す()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("プレースホルダを含まない普通のテキスト");

        Assert.Empty(names);
    }

    [Fact]
    public void 変数名はアンダースコアと数字を含んでよい()
    {
        var names = EnvPlaceholderScanner.ExtractVariableNames("${MY_VAR_2}");

        Assert.Equal(new[] { "MY_VAR_2" }, names);
    }

    [Fact]
    public void デフォルト値の中にネストしたプレースホルダがあっても内側の変数名は抽出されない()
    {
        // 既知の制約: 既定値部分は最初の "}" までしか読まないため、
        // ネストした ${DEFAULT_HOST} は展開されず HOST のみが抽出される。
        var names = EnvPlaceholderScanner.ExtractVariableNames("${HOST:-${DEFAULT_HOST}}");

        Assert.Equal(new[] { "HOST" }, names);
    }
}
