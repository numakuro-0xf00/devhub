namespace Devhub.Tool;

/// <summary>
/// <see cref="TranscriptSkillScanner.Scan"/> の結果。<see cref="ConsumedBytes"/> は
/// <paramref name="lines"/>(呼び出し側が渡した候補行)のうち、実際に処理した(= イベントとして採用済みの)
/// 行の <see cref="TranscriptLine.ByteLength"/> の合計。呼び出し側はこれを開始オフセットに加算して
/// 次回のオフセットとする。
/// </summary>
public sealed record TranscriptScanOutcome(IReadOnlyList<TranscriptSkillUseRecord> Events, long ConsumedBytes);

/// <summary>
/// <see cref="TranscriptChunkParser"/> が返す「書き込み完了済みの行」列から skill 利用イベントを抽出しつつ、
/// 1回の実行あたりの送信件数上限(既定 1000。doc/phase2.md「実装仕様」)を適用する純粋ロジック。
///
/// 上限に達しそうな行(=その行のイベントを加えると <paramref name="remainingBudget"/> を超える行)の
/// 手前で処理を打ち切り、それ以降の行は消費済みバイト数に含めない(次回の実行に持ち越す。「超過分は次回」)。
/// イベントを含まない行(通常の会話行など)は、上限超過後であっても打ち切りの原因にはならず、そのまま
/// 消費済みとして扱う(実際に送信を見送ったイベントだけを次回に回すという契約を素直に実装したもの)。
/// </summary>
public static class TranscriptSkillScanner
{
    public static TranscriptScanOutcome Scan(IReadOnlyList<TranscriptLine> lines, int remainingBudget)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var events = new List<TranscriptSkillUseRecord>();
        var consumed = 0L;

        foreach (var line in lines)
        {
            var lineEvents = TranscriptSkillExtractor.ExtractFromLine(line.Content);
            if (events.Count + lineEvents.Count > remainingBudget)
            {
                break;
            }
            events.AddRange(lineEvents);
            consumed += line.ByteLength;
        }

        return new TranscriptScanOutcome(events, consumed);
    }
}
