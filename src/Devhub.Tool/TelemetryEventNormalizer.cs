using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devhub.Tool;

/// <summary>
/// devhub telemetry send が対応するエージェント種別。CLI の --agent 引数(claudecode/cursor/codexcli)と
/// 大文字小文字を無視した 1:1 対応にする(<c>Enum.TryParse(..., ignoreCase: true, ...)</c> で変換できる名前にしてある)。
/// </summary>
public enum TelemetryAgentKind
{
    ClaudeCode,
    Cursor,
    CodexCli,
}

/// <summary>
/// 正規化後(匿名化・timestamp 付与より前)のイベント(doc/phase2.md「イベントスキーマ」)。
/// session_id / user_id の元になる生値はここではまだ HMAC 擬似ID化しない
/// (擬似ID化は <see cref="TelemetryAnonymizer"/> の責務。生値を持ち回るのはこの型までで、
/// 呼び出し側は必ず HMAC 化してから送信イベントを組み立てること。NFR-2)。
/// </summary>
/// <param name="Event">snake_case に正規化したイベント名(例 post_tool_use)。取得できなければ null。</param>
/// <param name="Agent">devhub の agent 識別子(claudecode/cursor/codexcli)。</param>
/// <param name="ToolName">ツール名(Bash, mcp__server__tool 等)。欠落時 null。</param>
/// <param name="AgentType">subagent 種別。欠落時 null(Cursor は固定カテゴリのみ。doc/phase2.md 実測制約表)。</param>
/// <param name="RawSessionId">HMAC 化前の生セッション識別子。欠落時 null。</param>
/// <param name="RawUserSeed">
/// user_id のハッシュ元を明示上書きする生値。現状 Cursor の user_email のみがここに入る
/// (正規化段階で破棄せず、匿名化対象として上位層へ渡す)。null の場合、呼び出し側は
/// DEVHUB_TELEMETRY_USER_SEED / Environment.UserName へフォールバックする。
/// </param>
public sealed record NormalizedTelemetryEvent(
    string? Event,
    string Agent,
    string? ToolName,
    string? AgentType,
    string? RawSessionId,
    string? RawUserSeed);

/// <summary>
/// 送信直前の最終イベント(doc/phase2.md「イベントスキーマ」に一致する JSON を生成する)。
/// session_id / user_id はこの型に入る時点ですでに HMAC 擬似ID化済みであること(生値はここに来ない)。
/// timestamp は hook ペイロードに含まれないため、送信側(IO層)が付与する(実測で3エージェントとも欠落)。
/// </summary>
public sealed record TelemetryOutboundEvent(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("tool_name")] string? ToolName,
    [property: JsonPropertyName("agent_type")] string? AgentType,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("timestamp")] string Timestamp);

/// <summary>
/// エージェント固有の hook ペイロード(JSON 文字列)を共通スキーマへ正規化する純粋ロジック
/// (doc/phase2.md「イベントスキーマ」「各エージェントの実測制約」)。
///
/// hook から同期実行される <c>devhub telemetry send</c> の契約(常に exit 0・沈黙)を上位層が守れるよう、
/// このクラスは一切例外を投げない。JSON として解釈できない、オブジェクトでない等はすべて null を返すだけにする。
///
/// 各エージェントのフィールド対応(doc/phase2.md より):
///   claudecode: hook_event_name, tool_name, session_id, (subagent 系イベントのみ) agent_type
///   cursor:     hook_event_name(値は camelCase), tool_name, conversation_id(→session_id),
///               subagent_type(→agent_type), user_email(→RawUserSeed。正規化段階では破棄しない)
///   codexcli:   tool_name, session_id, agent_type
///               (実測でイベント名フィールドは含まれないため Event は常に null)
/// </summary>
public static class TelemetryEventNormalizer
{
    public static NormalizedTelemetryEvent? Normalize(TelemetryAgentKind agent, string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawPayload);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            return agent switch
            {
                TelemetryAgentKind.ClaudeCode => NormalizeClaudeCode(root),
                TelemetryAgentKind.Cursor => NormalizeCursor(root),
                TelemetryAgentKind.CodexCli => NormalizeCodexCli(root),
                _ => null,
            };
        }
    }

    private static NormalizedTelemetryEvent NormalizeClaudeCode(JsonElement root) => new(
        Event: ToSnakeCaseEventName(GetString(root, "hook_event_name")),
        Agent: WireName(TelemetryAgentKind.ClaudeCode),
        ToolName: GetString(root, "tool_name"),
        AgentType: GetString(root, "agent_type"),
        RawSessionId: GetString(root, "session_id"),
        RawUserSeed: null);

    private static NormalizedTelemetryEvent NormalizeCursor(JsonElement root) => new(
        Event: ToSnakeCaseEventName(GetString(root, "hook_event_name")),
        Agent: WireName(TelemetryAgentKind.Cursor),
        ToolName: GetString(root, "tool_name"),
        AgentType: GetString(root, "subagent_type"),
        RawSessionId: GetString(root, "conversation_id"),
        RawUserSeed: GetString(root, "user_email"));

    private static NormalizedTelemetryEvent NormalizeCodexCli(JsonElement root) => new(
        // doc/phase2.md「各エージェントの実測制約」表のとおり、Codex CLI の hook ペイロードには
        // イベント名を示すフィールドが実測で含まれない(hooks.json 側で1イベント1エントリのため
        // ペイロード内に discriminator を持つ必要が無い)。よって常に null。
        Event: null,
        Agent: WireName(TelemetryAgentKind.CodexCli),
        ToolName: GetString(root, "tool_name"),
        AgentType: GetString(root, "agent_type"),
        RawSessionId: GetString(root, "session_id"),
        RawUserSeed: null);

    /// <summary>CLI --agent 引数と同じ表記(claudecode/cursor/codexcli)に変換する。</summary>
    public static string WireName(TelemetryAgentKind agent) => agent.ToString().ToLowerInvariant();

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var s = value.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>
    /// PascalCase/camelCase のイベント名(例 PostToolUse, afterFileEdit)を snake_case(post_tool_use,
    /// after_file_edit)へ変換する。すでに snake_case のものはそのまま(大文字が無ければ変化しない)。
    /// 連続する大文字は1つの頭字語として保持し分断しない(例 beforeMCPExecution → before_mcp_execution、
    /// STOP → stop。Cursor の実測イベント名 beforeMCPExecution を参照)。単語境界は「小文字→大文字」の
    /// 遷移、および「大文字連続の末尾+直後の小文字」の位置にのみ置く。
    /// </summary>
    private static string? ToSnakeCaseEventName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prevIsLower = char.IsLower(value[i - 1]);
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (prevIsLower || nextIsLower)
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
