namespace Devhub.Tool;

/// <summary>
/// カレントディレクトリの絶対パスから、Claude Code が <c>~/.claude/projects/&lt;encoded-cwd&gt;/</c> に
/// 使う「encoded-cwd」表記へ変換する純粋ロジック(doc/phase2.md「トランスクリプト実フォーマット」実機調査)。
///
/// 絶対パスの区切り文字(<c>/</c>。Windows ネイティブ環境向けに <c>\</c> も同様に扱う。NFR-4)をすべて
/// <c>-</c> に置換するだけ(例 <c>/mnt/c/Users/home/source/repos/devhub</c> →
/// <c>-mnt-c-Users-home-source-repos-devhub</c>)。
/// </summary>
public static class TranscriptProjectPathEncoder
{
    public static string Encode(string absoluteCwd)
    {
        ArgumentNullException.ThrowIfNull(absoluteCwd);
        return absoluteCwd.Replace('\\', '-').Replace('/', '-');
    }
}
