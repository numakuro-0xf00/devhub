using System.Text.Json;

namespace Devhub.Tool;

/// <summary>
/// .rulesync/hooks.json 内で検出した type:"http" の 1 件(どこにあったかのパス表記)。
/// <see cref="Path"/> は JSON のプロパティ名・配列インデックスをドットで連結した表記
/// (例 <c>hooks.postToolUse[0]</c>、ターゲット別オーバーライドなら <c>claudecode.hooks.postToolUse[0]</c>)で、
/// 「どのイベントのエントリか」を人間が読める形にする。
/// </summary>
public sealed record HooksHttpViolation(string Path);

/// <summary>
/// .rulesync/hooks.json の lint 結果。
/// <see cref="ParseError"/> が非 null のときは JSON として解釈できなかったことを示し、<see cref="Violations"/> は
/// 常に空になる。これは lint の「検査失敗」ではなく警告に留める(rulesync 自身が別途パースエラーを出すため、
/// devhub 側で二重に報告しない。doc/phase2.md「Step 2c」)。
/// </summary>
public sealed record HooksLintResult(IReadOnlyList<HooksHttpViolation> Violations, string? ParseError)
{
    public bool HasViolations => Violations.Count > 0;
}

/// <summary>
/// .rulesync/hooks.json に rulesync@9.2.0 のバグ対象である <c>type:"http"</c> が混入していないかを
/// 検査する純粋ロジック(doc/phase2.md「リスク・注意点」の rulesync <c>type:"http"</c> バグ、D-P2-1)。
///
/// rulesync@9.2.0 は <c>type:"http"</c> を Claude Code / Cursor 向けに <c>url</c> が欠落した壊れたエントリとして
/// 出力する(Codex では生成すらされない)。devhub では hook は <c>type:"command"</c> のみを許可し、
/// 混入を検出したら <c>devhub check</c> を exit 1 で止める(壊れた設定のまま rulesync に生成させない)。
///
/// hooks.json の構造はトップレベルの <c>hooks:</c> と、ターゲット別オーバーライド(<c>claudecode:</c> /
/// <c>cursor:</c> / <c>codexcli:</c> 等の任意キー配下の <c>hooks:</c>)の両方があり得る(templates/rulesync/hooks.json
/// 参照)。将来の rulesync のスキーマ変更に対しても頑健であるよう、特定の階層を決め打ちせず JSON ツリー全体を
/// 再帰的に走査し、<c>"type": "http"</c> を持つオブジェクトをすべて検出する。
///
/// 「JSON 文字列 → 違反リスト」の純粋関数(<see cref="Check(string)"/>)と、実ファイルを読む薄い IO ラッパー
/// (<see cref="CheckRepository"/>)を分離する(既存の Planner 群と同じ設計方針)。
/// </summary>
public static class HooksLintChecker
{
    private const string TypePropertyName = "type";
    private const string HttpTypeValue = "http";

    /// <summary>
    /// hooks.json の中身(JSON 文字列)を検査する。JSON として解釈できない場合は
    /// <see cref="HooksLintResult.ParseError"/> にメッセージを入れて返す(lint 失敗ではなく警告扱い)。
    /// </summary>
    public static HooksLintResult Check(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new HooksLintResult(Array.Empty<HooksHttpViolation>(), ex.Message);
        }

        using (document)
        {
            var violations = new List<HooksHttpViolation>();
            Walk(document.RootElement, path: "", violations);
            return new HooksLintResult(violations, ParseError: null);
        }
    }

    /// <summary>
    /// リポジトリの .rulesync/hooks.json を読み取り検査する薄い IO ラッパー。
    /// ファイルが存在しない場合は検査対象が無いため null を返す(呼び出し側でスキップ扱いにする)。
    /// </summary>
    public static HooksLintResult? CheckRepository(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        var path = System.IO.Path.Combine(repositoryRoot, ".rulesync", "hooks.json");
        if (!File.Exists(path)) return null;

        return Check(File.ReadAllText(path));
    }

    /// <summary>
    /// JSON ツリーを再帰的に走査し、<c>"type": "http"</c> を持つオブジェクトを見つけるたびに
    /// <paramref name="path"/>(ここまでのプロパティ名・配列インデックスのドット/角括弧連結)を記録する。
    /// </summary>
    private static void Walk(JsonElement element, string path, List<HooksHttpViolation> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsHttpTypeEntry(element))
                {
                    violations.Add(new HooksHttpViolation(path.Length > 0 ? path : "(root)"));
                }
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    Walk(property.Value, childPath, violations);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{index}]", violations);
                    index++;
                }
                break;
        }
    }

    private static bool IsHttpTypeEntry(JsonElement obj) =>
        obj.TryGetProperty(TypePropertyName, out var typeProp)
        && typeProp.ValueKind == JsonValueKind.String
        && typeProp.GetString() == HttpTypeValue;
}
