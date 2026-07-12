using System.Text;
using System.Text.Json;

namespace Devhub.Tool;

/// <summary>
/// トランスクリプトファイルの断片(あるオフセットから読み取った生バイト列)のうち、書き込みが完了した
/// 1行を表す。<see cref="ByteLength"/> は改行(および CRLF の場合の \r)まで含めた、この行がファイル内で
/// 占めるバイト数(呼び出し側はこれを積算してオフセットを進める)。
/// </summary>
public sealed record TranscriptLine(string Content, int ByteLength);

/// <summary>
/// ファイルのあるオフセットから EOF まで読み取った生バイト列を、確実に書き込みが完了した行のみへ分解する
/// 純粋ロジック(doc/phase2.md「トランスクリプト実フォーマット」)。
///
/// トランスクリプトは追記専用だが、読み取りタイミングによっては末尾行が書き込み途中で JSON として不完全な
/// ことがある。本クラスは次の2パターンを両方とも「未到達」とみなし、返す行リストからもバイト数からも
/// 除外する(呼び出し側はこの分を消費済みオフセットに含めないことで、次回の実行時に同じ行を再読する):
///   1. chunk が改行で終端されていない(末尾に書き込み途中の断片が続いている)。
///   2. chunk の最後の行は改行で終端されているが、JSON オブジェクトとして解釈できない
///      (改行だけが先に書き込まれ、本体がまだ完全でない極端なケースへの防御)。
///
/// 末尾以外の行は JSON として解釈できるかどうかを検証しない(改行で終端されている=書き込み完了とみなす。
/// 内容が壊れている場合の扱いは <see cref="TranscriptSkillExtractor"/> 側の責務であり、そちらは
/// パース不能な行を静かにスキップするだけでオフセットの前進自体は妨げない)。
/// </summary>
public static class TranscriptChunkParser
{
    public static IReadOnlyList<TranscriptLine> ParseCompleteLines(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0) return Array.Empty<TranscriptLine>();

        var newlineIndexes = new List<int>();
        for (var i = 0; i < chunk.Length; i++)
        {
            if (chunk[i] == (byte)'\n') newlineIndexes.Add(i);
        }
        if (newlineIndexes.Count == 0) return Array.Empty<TranscriptLine>();

        var lines = new List<TranscriptLine>();
        var start = 0;
        foreach (var newlineIndex in newlineIndexes)
        {
            var rawLength = newlineIndex - start;
            var contentLength = rawLength > 0 && chunk[start + rawLength - 1] == (byte)'\r'
                ? rawLength - 1
                : rawLength;
            var text = Encoding.UTF8.GetString(chunk, start, contentLength);
            var byteLength = newlineIndex + 1 - start; // 行内容 + 改行までの、この行がファイル内で占めるバイト数
            lines.Add(new TranscriptLine(text, byteLength));
            start = newlineIndex + 1;
        }

        if (lines.Count > 0 && !IsParseableJsonObject(lines[^1].Content))
        {
            // 末尾行が改行で終端されていても JSON として不完全なら「未到達」として除外する。
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool IsParseableJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
