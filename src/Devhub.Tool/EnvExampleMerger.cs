using System.Text;

namespace Devhub.Tool;

/// <summary>
/// <c>.env.example</c> のマージ計画。<see cref="MissingVariables"/> は既存内容にまだ無い必須変数名
/// (要求順を保持)、<see cref="LinesToAppend"/> はそれに対応する追記行(<c>VAR=</c>)。
/// </summary>
public sealed record EnvExampleMergePlan(IReadOnlyList<string> MissingVariables, IReadOnlyList<string> LinesToAppend)
{
    /// <summary>追記すべき行が1つでもあれば true。</summary>
    public bool HasChanges => LinesToAppend.Count > 0;
}

/// <summary>
/// <c>.env.example</c> の既存内容を壊さず、不足している必須変数の <c>VAR=</c> 行だけを末尾に追記するための
/// マージ計画を立てる純粋ロジック(要件 FR-2.4)。既存行(コメントを含む)には一切手を加えない。
/// </summary>
public static class EnvExampleMerger
{
    /// <summary>
    /// 既存内容(無ければ null)と必須変数一覧から、マージ計画を組み立てる。
    /// 「既存に無い」の判定は <see cref="EnvFileParser"/> と同じ素朴な KEY=VALUE パースで
    /// 検出したキー集合を用いる(コメント行・空行のキーは無視される)。
    /// </summary>
    public static EnvExampleMergePlan BuildMergePlan(string? existingContent, IReadOnlyList<string> requiredVariables)
    {
        ArgumentNullException.ThrowIfNull(requiredVariables);

        var existingKeys = EnvFileParser.Parse(existingContent ?? "").Keys;
        var missing = requiredVariables.Where(v => !existingKeys.Contains(v)).ToArray();
        var lines = missing.Select(v => $"{v}=").ToArray();
        return new EnvExampleMergePlan(missing, lines);
    }

    /// <summary>
    /// 計画を既存内容へ適用した新しい内容を返す(実ファイルの読み書きは呼び出し側の責務)。
    /// 既存内容が末尾改行で終わっていない場合は、追記前に改行を1つ補う。
    /// 追記が無ければ既存内容をそのまま返す。
    /// </summary>
    public static string ApplyMergePlan(string? existingContent, EnvExampleMergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var content = existingContent ?? "";
        if (!plan.HasChanges) return content;

        var builder = new StringBuilder(content);
        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.Append('\n');
        }
        foreach (var line in plan.LinesToAppend)
        {
            builder.Append(line).Append('\n');
        }
        return builder.ToString();
    }
}
