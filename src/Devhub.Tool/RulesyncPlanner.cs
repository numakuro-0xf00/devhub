namespace Devhub.Tool;

/// <summary>
/// rulesync の 1 回分の呼び出し(対象エージェントと機能の組)。
/// </summary>
public sealed record RulesyncInvocation(IReadOnlyList<string> Targets, IReadOnlyList<string> Features)
{
    /// <summary>ログ・テスト表示用の可読な引数文字列(実行時は ArgumentList を使う)。</summary>
    public string ToArguments() =>
        $"generate --targets {string.Join(",", Targets)} --features {string.Join(",", Features)}";
}

/// <summary>apply の入力。既定は 4 エージェント・全機能・CLAUDE.md 保護あり。</summary>
public sealed class ApplyOptions
{
    public IReadOnlyList<string> Targets { get; init; } = DefaultTargets;
    public IReadOnlyList<string> Features { get; init; } = DefaultFeatures;

    /// <summary>
    /// true のとき、claudecode 向けの生成から <c>rules</c> を外し、Phase 0 で用意した
    /// <c>@AGENTS.md</c> リンク方式の CLAUDE.md を rulesync に上書きさせない(要件 R-6)。
    /// </summary>
    public bool ProtectClaudeMd { get; init; } = true;

    public static readonly IReadOnlyList<string> DefaultTargets =
        new[] { "claudecode", "cursor", "copilot", "codexcli" };

    // rulesync の --features に渡せる機能一覧(2026-07 時点)。
    public static readonly IReadOnlyList<string> DefaultFeatures =
        new[] { "rules", "mcp", "hooks", "permissions", "subagents", "commands", "skills", "ignore" };
}

/// <summary>
/// apply を rulesync の呼び出し列へ展開する純粋ロジック。
/// CLAUDE.md 保護のため、claudecode だけ rules を外した別パスに分ける。
/// </summary>
public static class RulesyncPlanner
{
    public const string ClaudeCodeTarget = "claudecode";
    public const string RulesFeature = "rules";

    /// <summary>
    /// apply の実行計画を返す。保護が有効で claudecode と rules の両方が対象のときだけ
    /// 2 パス(その他エージェント / claudecode-rules除外)に分割する。それ以外は 1 パス。
    /// </summary>
    public static IReadOnlyList<RulesyncInvocation> BuildApplyPlan(ApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var protecting = options.ProtectClaudeMd
            && options.Targets.Contains(ClaudeCodeTarget)
            && options.Features.Contains(RulesFeature);

        if (!protecting)
        {
            // 保護不要:そのまま 1 パス。
            return new[] { new RulesyncInvocation(options.Targets.ToArray(), options.Features.ToArray()) };
        }

        var passes = new List<RulesyncInvocation>();

        // パス 1:claudecode 以外は全機能(rules を含む)。
        var others = options.Targets.Where(t => t != ClaudeCodeTarget).ToArray();
        if (others.Length > 0)
        {
            passes.Add(new RulesyncInvocation(others, options.Features.ToArray()));
        }

        // パス 2:claudecode は rules を外す(CLAUDE.md を上書きさせない)。
        var claudeFeatures = options.Features.Where(f => f != RulesFeature).ToArray();
        passes.Add(new RulesyncInvocation(new[] { ClaudeCodeTarget }, claudeFeatures));

        return passes;
    }
}
