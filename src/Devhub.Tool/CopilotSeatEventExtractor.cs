using System.Text.Json;

namespace Devhub.Tool;

/// <summary>
/// GitHub Copilot Billing Seats API(<c>GET /orgs/{org}/copilot/billing/seats</c>)の1ページ分の
/// レスポンスボディ(JSON)から、送信候補となる seat 情報を抽出した結果(doc/phase2.md D-P2-8 / Step 5)。
/// </summary>
/// <param name="RawLogin">HMAC 化前の生ユーザー名(<c>assignee.login</c>)。</param>
/// <param name="LastActivityAt">seat の <c>last_activity_at</c>(ISO 8601)。この型に入る時点で非 null であることが保証される。</param>
/// <param name="AgentType">
/// <c>last_activity_editor</c> の先頭セグメント(最初の <c>/</c> まで。例 <c>"vscode/1.85.1"</c> → <c>"vscode"</c>)。
/// <c>last_activity_editor</c> が欠落・空文字列の場合は null。
/// </param>
public sealed record CopilotSeatCandidate(string RawLogin, string LastActivityAt, string? AgentType);

/// <summary>
/// Copilot Billing Seats API のレスポンスボディ(JSON 文字列)を <see cref="CopilotSeatCandidate"/> の列へ
/// 変換する純粋ロジック(doc/phase2.md Step 5)。実際の HTTP 呼び出し・ページネーションは
/// <see cref="CopilotSeatsClient"/>(薄い IO ラッパー)の責務とし、このクラスは1ページ分のレスポンスボディを
/// 受け取って変換するだけにする。
///
/// 頑健性: JSON として解釈できない、オブジェクトでない、<c>seats</c> 配列が無い等はすべて空リストを返す。
/// 個々の seat エントリについても、パース失敗・必須フィールド欠落(<c>assignee.login</c> が無い等)は
/// その seat だけをスキップし、他の seat の抽出は継続する(1件の壊れたデータで全体を落とさない)。
///
/// <c>last_activity_at</c> が null(=未利用)の seat はそもそも送信対象ではないため、この時点で除外する
/// (「last_activity_at が null(未利用)の seat は送信しない」)。
/// </summary>
public static class CopilotSeatEventExtractor
{
    public static IReadOnlyList<CopilotSeatCandidate> ExtractFromResponseJson(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return Array.Empty<CopilotSeatCandidate>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch (JsonException)
        {
            return Array.Empty<CopilotSeatCandidate>();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Array.Empty<CopilotSeatCandidate>();
            if (!root.TryGetProperty("seats", out var seats) || seats.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<CopilotSeatCandidate>();
            }

            List<CopilotSeatCandidate>? results = null;
            foreach (var seat in seats.EnumerateArray())
            {
                if (seat.ValueKind != JsonValueKind.Object) continue;

                // last_activity_at が無い(未利用)seat は送信対象外。
                var lastActivityAt = GetString(seat, "last_activity_at");
                if (lastActivityAt is null) continue;

                if (!seat.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var login = GetString(assignee, "login");
                if (login is null) continue;

                var agentType = ExtractAgentType(GetString(seat, "last_activity_editor"));

                (results ??= new List<CopilotSeatCandidate>()).Add(
                    new CopilotSeatCandidate(login, lastActivityAt, agentType));
            }

            return results ?? (IReadOnlyList<CopilotSeatCandidate>)Array.Empty<CopilotSeatCandidate>();
        }
    }

    /// <summary>
    /// <c>last_activity_editor</c>(例 <c>"vscode/1.85.1"</c>)の先頭セグメント(最初の <c>/</c> まで)を取り出す。
    /// <c>/</c> が無ければ文字列全体をそのまま使う。値そのものが欠落・空文字列の場合、または先頭セグメントが
    /// 空文字列になる場合(例 <c>"/foo"</c>)は null を返す。
    /// </summary>
    private static string? ExtractAgentType(string? lastActivityEditor)
    {
        if (string.IsNullOrEmpty(lastActivityEditor)) return null;

        var segment = lastActivityEditor.Split('/')[0];
        return string.IsNullOrEmpty(segment) ? null : segment;
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var s = value.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
