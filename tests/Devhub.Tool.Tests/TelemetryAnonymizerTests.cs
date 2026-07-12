using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

// 期待値は HMAC-SHA256 の既知ベクタとして、Python の hmac/hashlib で独立に計算したものを
// リテラルで固定している(自己完結。外部ツールへの依存や実行時計算に頼らない)。
//   python3 -c "import hmac,hashlib; print(hmac.new(b'test-salt', b'session-abc', hashlib.sha256).hexdigest())"
public class TelemetryAnonymizerTests
{
    private const string KnownSalt = "test-salt";
    private const string KnownMessage = "session-abc";

    [Fact]
    public void 既知のsaltと生値からのHMACSHA256の期待値と一致する()
    {
        var actual = TelemetryAnonymizer.Hash(KnownSalt, KnownMessage);

        Assert.Equal("e26d21a59e10d24c22e4a97300efb9a165d60140392f65d05222213bc99daf44", actual);
    }

    [Fact]
    public void 異なる生値でも既知のsaltでの期待値と一致する()
    {
        var actual = TelemetryAnonymizer.Hash(KnownSalt, "alice");

        Assert.Equal("b65aaf31e9e8f7d60cd4671c1bffb8b94580a8b176189ac9f31bae7c2e54cf37", actual);
    }

    [Fact]
    public void salt未設定相当の空文字列は空バイト列を鍵として計算した期待値と一致する()
    {
        var actual = TelemetryAnonymizer.Hash("", KnownMessage);

        Assert.Equal("2a896aaf032f486bfeaf18a4ab913219594d7e7c3b22de252f3828e1996a1694", actual);
    }

    [Fact]
    public void saltがnullでも空文字列と同じ結果になる()
    {
        var withNull = TelemetryAnonymizer.Hash(null, KnownMessage);
        var withEmpty = TelemetryAnonymizer.Hash("", KnownMessage);

        Assert.Equal(withEmpty, withNull);
        Assert.Equal("2a896aaf032f486bfeaf18a4ab913219594d7e7c3b22de252f3828e1996a1694", withNull);
    }

    [Fact]
    public void 空文字列の生値でも既知のsaltでの期待値と一致する()
    {
        var actual = TelemetryAnonymizer.Hash(KnownSalt, "");

        Assert.Equal("55d68a1498283e9875ab2041dd5d9429e915c04d756bdc789d02836f0df98bf1", actual);
    }

    [Fact]
    public void 同一のsaltと生値の組は常に同一のダイジェストを返す()
    {
        var first = TelemetryAnonymizer.Hash(KnownSalt, KnownMessage);
        var second = TelemetryAnonymizer.Hash(KnownSalt, KnownMessage);

        Assert.Equal(first, second);
    }

    [Fact]
    public void saltが異なれば同じ生値でもダイジェストが変わる()
    {
        var withKnownSalt = TelemetryAnonymizer.Hash(KnownSalt, KnownMessage);
        var withAnotherSalt = TelemetryAnonymizer.Hash("another-salt", KnownMessage);

        Assert.NotEqual(withKnownSalt, withAnotherSalt);
        Assert.Equal("a62758e1607354dc35df19d4b7549db89b2a2f418d5ffb6ecaf1a10b0b1ffb7a", withAnotherSalt);
    }

    [Fact]
    public void ダイジェストは64文字の小文字16進文字列である()
    {
        var actual = TelemetryAnonymizer.Hash(KnownSalt, KnownMessage);

        Assert.Equal(64, actual.Length);
        Assert.Equal(actual.ToLowerInvariant(), actual);
        Assert.Matches("^[0-9a-f]{64}$", actual);
    }

    [Fact]
    public void 生値がnullの場合はArgumentNullExceptionを送出する()
    {
        Assert.Throws<ArgumentNullException>(() => TelemetryAnonymizer.Hash(KnownSalt, null!));
    }

    // 期待値は python3 -c "import hmac,hashlib; print(hmac.new('test-salt'.encode(), '山田太郎'.encode(),
    // hashlib.sha256).hexdigest())" で独立に計算したもの。WSL/Windows 混在環境では日本語 OS
    // ユーザー名が Environment.UserName 経由でハッシュ元になり得るため(NFR-4)、非ASCII入力を確認する。
    [Fact]
    public void 非ASCII日本語文字列でも期待値と一致する()
    {
        var actual = TelemetryAnonymizer.Hash(KnownSalt, "山田太郎");

        Assert.Equal("eb8b83deb35614ce706a483fcf48ed8500d2491e3f9c871ccc0eca5036240e79", actual);
    }
}
