using System.Text;
using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptChunkParserTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void 改行で終端された複数行はすべて完了行として返す()
    {
        var line1 = """{"type":"assistant","sessionId":"s1"}""";
        var line2 = """{"type":"user","sessionId":"s1"}""";
        var chunk = Bytes($"{line1}\n{line2}\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        Assert.Equal(2, actual.Count);
        Assert.Equal(line1, actual[0].Content);
        Assert.Equal(line2, actual[1].Content);
        Assert.Equal(chunk.Length, actual.Sum(l => l.ByteLength));
    }

    [Fact]
    public void 末尾に改行が無い断片は未到達として除外する()
    {
        var complete = """{"type":"assistant","sessionId":"s1"}""";
        var incompleteTail = """{"type":"assistant","sessionId":"s2","message":{"content":[""";
        var chunk = Bytes($"{complete}\n{incompleteTail}");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        var line = Assert.Single(actual);
        Assert.Equal(complete, line.Content);
        // 消費バイト数は complete 行 + 改行のみ(末尾の未完成断片は含まない)。
        Assert.Equal(Bytes(complete + "\n").Length, line.ByteLength);
    }

    [Fact]
    public void 改行はあるが末尾行がJSONとして不完全なら除外する()
    {
        var complete = """{"type":"assistant","sessionId":"s1"}""";
        // 改行で終端されてはいるが、閉じ括弧が足りない壊れた JSON(書き込み途中の極端なケースを模擬)。
        var brokenButTerminated = """{"type":"assistant","sessionId":"s2""";
        var chunk = Bytes($"{complete}\n{brokenButTerminated}\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        var line = Assert.Single(actual);
        Assert.Equal(complete, line.Content);
        Assert.Equal(Bytes(complete + "\n").Length, line.ByteLength);
    }

    [Fact]
    public void 唯一の行が末尾でJSON不完全なら結果は空になる()
    {
        var brokenButTerminated = """{"type":"assistant""";
        var chunk = Bytes($"{brokenButTerminated}\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        Assert.Empty(actual);
    }

    [Fact]
    public void 途中の行が壊れたJSONでも末尾行が正常なら消費して先へ進む()
    {
        var brokenMiddle = """{"type":"assistant","sessionId":""";
        var validLast = """{"type":"user","sessionId":"s2"}""";
        var chunk = Bytes($"{brokenMiddle}\n{validLast}\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        Assert.Equal(2, actual.Count);
        Assert.Equal(brokenMiddle, actual[0].Content);
        Assert.Equal(validLast, actual[1].Content);
        Assert.Equal(chunk.Length, actual.Sum(l => l.ByteLength));
    }

    [Fact]
    public void 改行を1つも含まない断片は空を返す()
    {
        var chunk = Bytes("""{"type":"assistant","sessionId":"s1"}""");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        Assert.Empty(actual);
    }

    [Fact]
    public void 空の断片は空を返す()
    {
        var actual = TranscriptChunkParser.ParseCompleteLines(Array.Empty<byte>());

        Assert.Empty(actual);
    }

    [Fact]
    public void CRLF改行でも末尾のCRを取り除いて内容を返す()
    {
        var line1 = """{"type":"assistant","sessionId":"s1"}""";
        var line2 = """{"type":"user","sessionId":"s1"}""";
        var chunk = Bytes($"{line1}\r\n{line2}\r\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        Assert.Equal(2, actual.Count);
        Assert.Equal(line1, actual[0].Content);
        Assert.Equal(line2, actual[1].Content);
        Assert.Equal(chunk.Length, actual.Sum(l => l.ByteLength));
    }

    [Fact]
    public void ByteLengthは改行を含めたファイル内消費バイト数と一致する()
    {
        var line1 = """{"a":1}""";
        var chunk = Bytes($"{line1}\n");

        var actual = TranscriptChunkParser.ParseCompleteLines(chunk);

        var single = Assert.Single(actual);
        Assert.Equal(chunk.Length, single.ByteLength);
    }
}
