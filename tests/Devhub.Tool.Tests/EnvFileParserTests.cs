using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class EnvFileParserTests
{
    [Fact]
    public void コメント行と空行を無視する()
    {
        var content = """
            # これはコメント
            FOO=bar

            # 空行の後もコメント
            BAZ=qux
            """;

        var result = EnvFileParser.Parse(content);

        Assert.Equal(2, result.Count);
        Assert.Equal("bar", result["FOO"]);
        Assert.Equal("qux", result["BAZ"]);
    }

    [Fact]
    public void 空値のキーは空文字列として登録される()
    {
        var result = EnvFileParser.Parse("KEY=");

        Assert.Contains("KEY", result.Keys);
        Assert.Equal("", result["KEY"]);
    }

    [Fact]
    public void キーと値の前後空白はトリムされる()
    {
        var result = EnvFileParser.Parse("  KEY  =   value with spaces  ");

        Assert.Equal("value with spaces", result["KEY"]);
    }

    [Fact]
    public void 同じキーが複数行にある場合は後の行が優先される()
    {
        var content = """
            KEY=first
            KEY=second
            """;

        var result = EnvFileParser.Parse(content);

        Assert.Equal("second", result["KEY"]);
    }

    [Fact]
    public void クォートは剥がさずそのまま値に含める()
    {
        var result = EnvFileParser.Parse("KEY=\"quoted value\"");

        Assert.Equal("\"quoted value\"", result["KEY"]);
    }

    [Fact]
    public void 等号を含まない行は無視する()
    {
        var content = """
            この行は等号を含まない
            KEY=value
            """;

        var result = EnvFileParser.Parse(content);

        Assert.Single(result);
        Assert.Equal("value", result["KEY"]);
    }

    [Fact]
    public void 空文字列を渡すと空の辞書を返す()
    {
        var result = EnvFileParser.Parse("");

        Assert.Empty(result);
    }

    [Fact]
    public void 値に等号を複数含む場合は最初の等号だけを区切りとして扱う()
    {
        var result = EnvFileParser.Parse("KEY=a=b=c");

        Assert.Equal("a=b=c", result["KEY"]);
    }

    [Fact]
    public void CRLF改行の内容も正しくパースする()
    {
        var result = EnvFileParser.Parse("FOO=bar\r\nBAZ=qux\r\n");

        Assert.Equal(2, result.Count);
        Assert.Equal("bar", result["FOO"]);
        Assert.Equal("qux", result["BAZ"]);
    }
}
