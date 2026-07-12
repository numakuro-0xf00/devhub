using System.Text.Json;

namespace Devhub.Tool;

/// <summary>
/// subagent 別トランスクリプト(<c>&lt;sessionId&gt;/subagents/agent-&lt;agentId&gt;.jsonl</c>)から
/// <c>agent_type</c> を補完するための純粋ロジック(doc/phase2.md「実装仕様」)。
///
/// 実ファイルの読み取り(sibling の <c>agent-&lt;agentId&gt;.meta.json</c> を開く)は呼び出し側
/// (Program.cs)の薄い IO 層が担う。このクラスは「パスの形からファイル種別を判定する」「meta.json の
/// 中身(JSON 文字列)から agentType を取り出す」という2つの純粋関数のみを提供する。
/// </summary>
public static class TranscriptAgentTypeResolver
{
    private const string SubagentsDirName = "subagents";

    /// <summary>親ディレクトリ名が <c>subagents</c> であれば subagent 別トランスクリプトとみなす。</summary>
    public static bool IsSubagentFile(string jsonlPath)
    {
        ArgumentNullException.ThrowIfNull(jsonlPath);
        var dir = Path.GetDirectoryName(jsonlPath);
        return dir is not null && string.Equals(Path.GetFileName(dir), SubagentsDirName, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>agent-&lt;agentId&gt;.jsonl</c> から、対応する meta ファイルのパス
    /// <c>agent-&lt;agentId&gt;.meta.json</c>(同じディレクトリ内)を組み立てる。
    /// </summary>
    public static string GetMetaFilePath(string jsonlPath)
    {
        ArgumentNullException.ThrowIfNull(jsonlPath);
        var dir = Path.GetDirectoryName(jsonlPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(jsonlPath);
        return Path.Combine(dir, $"{baseName}.meta.json");
    }

    /// <summary>
    /// meta.json の中身(JSON 文字列)から <c>agentType</c>(文字列)を取り出す。
    /// JSON として解釈できない、オブジェクトでない、フィールドが無い/文字列でない/空文字列の場合はすべて null。
    /// </summary>
    public static string? ExtractAgentType(string? metaJson)
    {
        if (string.IsNullOrWhiteSpace(metaJson)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(metaJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("agentType", out var value)) return null;
            if (value.ValueKind != JsonValueKind.String) return null;
            var s = value.GetString();
            return string.IsNullOrEmpty(s) ? null : s;
        }
    }
}
