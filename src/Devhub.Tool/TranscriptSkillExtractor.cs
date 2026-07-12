using System.Text.Json;

namespace Devhub.Tool;

/// <summary>
/// トランスクリプト1行から検出した skill 利用イベント(HMAC 擬似ID化・timestamp 付与より前。doc/phase2.md Step 4)。
/// </summary>
/// <param name="SkillName">利用された skill 名(<c>input.skill</c>)。</param>
/// <param name="RawSessionId">HMAC 化前の生セッションID(行の <c>sessionId</c>)。欠落時 null。</param>
/// <param name="Timestamp">行の <c>timestamp</c>(ISO-8601 UTC ミリ秒)。トランスクリプトの原時刻を送信時にそのまま使う。欠落時 null。</param>
public sealed record TranscriptSkillUseRecord(string SkillName, string? RawSessionId, string? Timestamp);

/// <summary>
/// Claude Code トランスクリプト(<c>~/.claude/projects/&lt;encoded-cwd&gt;/*.jsonl</c>)の1行(JSON オブジェクト)から
/// skill 利用イベントを抽出する純粋ロジック(doc/phase2.md「トランスクリプト実フォーマット」)。
///
/// 確実なシグナル: <c>type=="assistant"</c> 行の <c>message.content[]</c> 内、
/// <c>type=="tool_use" &amp;&amp; name=="Skill"</c> のブロック。skill 名は <c>input.skill</c>。
/// <c>input.args</c> は会話内容そのものであるため、このクラスは絶対に読まない(<c>skill</c> 以外の
/// <c>input</c> プロパティには一切触れない)。
///
/// 除外必須:
///   - <c>attachment.type=="skill_listing"</c>(利用可能スキル一覧の表示であり利用イベントではない)。
///   - <c>attributionSkill</c> フィールド(活性中の全行に付与され重複計数の原因になる。本クラスは
///     このフィールドを一切参照しない。除外というより「そもそも読まない」ことで安全にする)。
///
/// hook から同期実行される <c>devhub telemetry scan-transcripts</c> の契約(常に exit 0・沈黙)を上位層が
/// 守れるよう、このクラスは一切例外を投げない。JSON として解釈できない・オブジェクトでない・
/// 必須フィールド欠落等はすべて空リストを返すだけにする。
/// </summary>
public static class TranscriptSkillExtractor
{
    private const string AssistantRowType = "assistant";
    private const string ToolUseBlockType = "tool_use";
    private const string SkillToolName = "Skill";
    private const string SkillListingAttachmentType = "skill_listing";

    public static IReadOnlyList<TranscriptSkillUseRecord> ExtractFromLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Array.Empty<TranscriptSkillUseRecord>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Array.Empty<TranscriptSkillUseRecord>();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Array.Empty<TranscriptSkillUseRecord>();

            // skill 利用の確実なシグナルは type=="assistant" 行に限られる(doc/phase2.md)。
            if (GetString(root, "type") != AssistantRowType) return Array.Empty<TranscriptSkillUseRecord>();

            // 除外必須: attachment.type=="skill_listing"(type=="assistant" 行に混入するケースへの防御。
            // 実際の主戦場は type!="assistant" のはずだが、上の早期リターンだけに頼らず明示的にも弾く)。
            if (root.TryGetProperty("attachment", out var attachment)
                && attachment.ValueKind == JsonValueKind.Object
                && GetString(attachment, "type") == SkillListingAttachmentType)
            {
                return Array.Empty<TranscriptSkillUseRecord>();
            }

            // attributionSkill は意図的に一切参照しない(重複計数の原因になるため。doc/phase2.md)。

            var sessionId = GetString(root, "sessionId");
            var timestamp = GetString(root, "timestamp");

            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<TranscriptSkillUseRecord>();
            }
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<TranscriptSkillUseRecord>();
            }

            List<TranscriptSkillUseRecord>? results = null;
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (GetString(block, "type") != ToolUseBlockType) continue;
                if (GetString(block, "name") != SkillToolName) continue;
                if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object) continue;

                // input.skill 以外(args 含む)は一切読まない。
                var skillName = GetString(input, "skill");
                if (skillName is null) continue;

                (results ??= new List<TranscriptSkillUseRecord>()).Add(
                    new TranscriptSkillUseRecord(skillName, sessionId, timestamp));
            }

            return results ?? (IReadOnlyList<TranscriptSkillUseRecord>)Array.Empty<TranscriptSkillUseRecord>();
        }
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var s = value.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
