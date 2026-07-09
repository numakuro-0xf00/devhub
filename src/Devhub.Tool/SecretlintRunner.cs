using System.Diagnostics;

namespace Devhub.Tool;

/// <summary>
/// secretlint を子プロセスとして実行する薄いラッパー。npx 経由でバージョン固定して呼ぶ。
/// devhub は秘匿情報スキャンロジックを内製せず、この既存 OSS に委譲する(NFR-7・FR-3.1/FR-3.2)。
///
/// rulesync と異なり、secretlint はルール(preset)を別 npm パッケージとして解決する。
/// 実地検証(2026-07、secretlint@13.0.2)の結果、単純に <c>npx -y secretlint@&lt;pin&gt;</c> だけを
/// 起動すると <c>@secretlint/secretlint-rule-preset-recommend</c> が解決できず致命的エラー(exit 2)に
/// なることを確認したため、npx の複数 <c>-p</c> 指定で本体とプリセットの両方を明示的にインストールしてから
/// <c>secretlint</c> を起動する(<c>npx -y -p secretlint@&lt;pin&gt; -p @secretlint/secretlint-rule-preset-recommend@&lt;pin&gt; -- secretlint ...</c>)。
/// </summary>
public sealed class SecretlintRunner
{
    // 実地検証(npm view secretlint version, 2026-07 時点の最新安定版)で確認したバージョンに固定。
    // preset-recommend は secretlint 本体と同一バージョンで足並みを揃えて配布されているため同じ値を使う。
    public const string PinnedVersion = "secretlint@13.0.2";
    public const string PinnedPresetRecommendVersion = "@secretlint/secretlint-rule-preset-recommend@13.0.2";

    private readonly string _launcher;
    private readonly IReadOnlyList<string> _launcherArgs;
    private readonly TextWriter _out;

    /// <param name="launcher">secretlint を起動するコマンド。既定は npx。</param>
    /// <param name="launcherArgs">launcher に渡す前置引数。既定は本体・preset の同時インストール + `secretlint` 起動。</param>
    public SecretlintRunner(string? launcher = null, IReadOnlyList<string>? launcherArgs = null, TextWriter? output = null)
    {
        _launcher = launcher ?? "npx";
        _launcherArgs = launcherArgs ?? new[]
        {
            "-y", "-p", PinnedVersion, "-p", PinnedPresetRecommendVersion, "--", "secretlint",
        };
        _out = output ?? Console.Out;
    }

    /// <summary>node/npx が PATH にあるか確認する。無ければ実行不能。</summary>
    public bool IsLauncherAvailable() => Which(_launcher) is not null;

    /// <summary>
    /// スキャンを 1 回実行する。戻り値は secretlint の終了コード
    /// (実地検証で確認済み:0=検出なし、1=秘匿情報の疑いを検出、2=設定不備等の致命的エラー)。
    ///
    /// <paramref name="invocation"/>.IgnoreFileName は workingDirectory 直下からの相対パスであること
    /// (<see cref="SecretlintInvocation"/> のコメント参照)。
    /// </summary>
    public int Run(SecretlintInvocation invocation, string? workingDirectory = null)
    {
        var cwd = workingDirectory ?? Directory.GetCurrentDirectory();
        var psi = new ProcessStartInfo
        {
            FileName = _launcher,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            WorkingDirectory = cwd,
        };

        foreach (var a in _launcherArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--secretlintrc");
        psi.ArgumentList.Add(invocation.ConfigPath);
        psi.ArgumentList.Add("--secretlintignore");
        psi.ArgumentList.Add(invocation.IgnoreFileName);
        foreach (var t in invocation.Targets) psi.ArgumentList.Add(t);

        _out.WriteLine($"[devhub] secretlint {invocation.ToArguments()}");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"{_launcher} を起動できませんでした。");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static string? Which(string command)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        var candidates = OperatingSystem.IsWindows()
            ? new[] { command, command + ".cmd", command + ".exe" }
            : new[] { command };

        foreach (var dir in paths)
        {
            foreach (var c in candidates)
            {
                var full = Path.Combine(dir, c);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }
}
