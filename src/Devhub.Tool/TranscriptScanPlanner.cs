namespace Devhub.Tool;

/// <summary>
/// 1ファイル分のスキャン計画(doc/phase2.md「実装仕様」の増分走査)。
/// </summary>
/// <param name="Path">対象ファイルの絶対パス(状態ファイルのキーと同じ表記)。</param>
/// <param name="ShouldRead">
/// false の場合、このファイルは読み込まず <see cref="CurrentLength"/> をそのまま新しいオフセットとして
/// 記録するだけでよい(状態ファイル無し=初回実行時のみ発生。過去分の大量送信を避けるための挙動)。
/// </param>
/// <param name="StartOffset">読み込みを開始するバイトオフセット(<see cref="ShouldRead"/> が true のときのみ意味を持つ)。</param>
/// <param name="CurrentLength">計画時点でのファイルサイズ(EOF オフセット)。</param>
public sealed record TranscriptScanTask(string Path, bool ShouldRead, long StartOffset, long CurrentLength);

/// <summary>
/// ファイル別バイトオフセットの状態(前回実行時の記録)にもとづき、今回実行すべき増分走査の計画を立てる
/// 純粋ロジック(doc/phase2.md「実装仕様」)。実ファイル IO(列挙・サイズ取得・状態ファイルの読み書き)は
/// 呼び出し側(Program.cs)の薄い IO 層が担う。
///
/// 規則:
///   - <paramref name="backfill"/> が true の場合、状態の有無に関わらず全ファイルをオフセット 0 から読む
///     (「--backfill 指定時のみ全履歴を走査」)。
///   - backfill が false かつ <paramref name="hasPriorState"/> が false(状態ファイルが一度も存在しない
///     = 本当に初回の実行)の場合、どのファイルも読まず、現在のファイルサイズをそのまま新オフセットとして
///     記録するだけにする(過去分の大量送信と古いデータの取り込み時刻計上を避ける)。
///   - それ以外(状態ファイルは存在する通常の増分実行)の場合、各ファイルは前回記録したオフセットから読む。
///     前回の状態に無いファイル(新規に現れたファイル。例: セッション中に生成された subagent の
///     トランスクリプト)はオフセット 0 から読む(この時点で「初回スキップ」の対象はもう過ぎているため、
///     過去分ではなく素直に全部読んでよい)。
///   - <paramref name="discoveredFiles"/> に含まれないファイル(前回は存在したが今回消失したファイル)は
///     計画に含まれない(呼び出し側が新しい状態を書き戻す際、自然に状態から消える)。
/// </summary>
public static class TranscriptScanPlanner
{
    public static IReadOnlyList<TranscriptScanTask> BuildPlan(
        IReadOnlyDictionary<string, long> discoveredFiles,
        bool hasPriorState,
        IReadOnlyDictionary<string, long> previousOffsets,
        bool backfill)
    {
        ArgumentNullException.ThrowIfNull(discoveredFiles);
        ArgumentNullException.ThrowIfNull(previousOffsets);

        var tasks = new List<TranscriptScanTask>();
        foreach (var (path, length) in discoveredFiles)
        {
            if (backfill)
            {
                tasks.Add(new TranscriptScanTask(path, ShouldRead: true, StartOffset: 0, CurrentLength: length));
                continue;
            }

            if (!hasPriorState)
            {
                tasks.Add(new TranscriptScanTask(path, ShouldRead: false, StartOffset: length, CurrentLength: length));
                continue;
            }

            var start = previousOffsets.TryGetValue(path, out var previous) ? previous : 0;
            if (start > length) start = length; // ファイルが縮小/ローテートされた等の異常系への防御的クランプ
            tasks.Add(new TranscriptScanTask(path, ShouldRead: true, StartOffset: start, CurrentLength: length));
        }

        return tasks;
    }
}
