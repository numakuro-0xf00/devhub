using Devhub.Tool;
using Xunit;

namespace Devhub.Tool.Tests;

public class TranscriptProjectPathEncoderTests
{
    [Fact]
    public void 実機調査済みの例をそのまま変換できる()
    {
        var actual = TranscriptProjectPathEncoder.Encode("/mnt/c/Users/home/source/repos/devhub");

        Assert.Equal("-mnt-c-Users-home-source-repos-devhub", actual);
    }

    // ネイティブ Windows の Claude Code のエンコード実例は未確認(このリポジトリの開発環境は WSL)。
    // このマシンの Windows 側には ~/.claude/projects/ が存在せず実機検証は不可能だった。
    // コロンは Windows のディレクトリ名として不正のため、実機で異なる場合はエンコーダごと修正が必要。
    // 現状の実装挙動の回帰検知としてのみ固定する。
    [Fact]
    public void 実機未検証の現状仕様_バックスラッシュはダッシュへ変換されドライブレターのコロンは素通しする()
    {
        var actual = TranscriptProjectPathEncoder.Encode("""C:\Users\home\repos\devhub""");

        Assert.Equal("C:-Users-home-repos-devhub", actual);
    }

    [Fact]
    public void 区切り文字が無ければそのまま返す()
    {
        var actual = TranscriptProjectPathEncoder.Encode("devhub");

        Assert.Equal("devhub", actual);
    }
}
