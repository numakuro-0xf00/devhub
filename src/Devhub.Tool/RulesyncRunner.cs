using System.Diagnostics;

namespace Devhub.Tool;

/// <summary>
/// rulesync を子プロセスとして実行する薄いラッパー。npx 経由でバージョン固定して呼ぶ。
/// devhub は変換ロジックを内製せず、この既存 OSS に委譲する(NFR-7・D-1)。
/// </summary>
public sealed class RulesyncRunner
{
    // 実地比較(doc/phase1-tool-eval.md)で検証したバージョンに固定。
    public const string PinnedVersion = "rulesync@9.2.0";

    private readonly string _launcher;
    private readonly IReadOnlyList<string> _launcherArgs;
    private readonly TextWriter _out;

    /// <param name="launcher">rulesync を起動するコマンド。既定は npx。</param>
    /// <param name="launcherArgs">launcher に渡す前置引数。既定は <c>-y rulesync@&lt;pinned&gt;</c>。</param>
    public RulesyncRunner(string? launcher = null, IReadOnlyList<string>? launcherArgs = null, TextWriter? output = null)
    {
        _launcher = launcher ?? "npx";
        _launcherArgs = launcherArgs ?? new[] { "-y", PinnedVersion };
        _out = output ?? Console.Out;
    }

    /// <summary>node/npx が PATH にあるか確認する。無ければ実行不能。</summary>
    public bool IsLauncherAvailable() => Which(_launcher) is not null;

    /// <summary>
    /// 1 パスを実行する。check なら <c>--check</c>(ドリフト時 exit 1)、dryRun なら <c>--dry-run</c> を付ける。
    /// 戻り値は rulesync の終了コード。
    /// </summary>
    public int Run(RulesyncInvocation invocation, bool check, bool dryRun, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _launcher,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };

        foreach (var a in _launcherArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("generate");
        psi.ArgumentList.Add("--targets");
        psi.ArgumentList.Add(string.Join(",", invocation.Targets));
        psi.ArgumentList.Add("--features");
        psi.ArgumentList.Add(string.Join(",", invocation.Features));
        if (check) psi.ArgumentList.Add("--check");
        if (dryRun) psi.ArgumentList.Add("--dry-run");

        _out.WriteLine($"[devhub] rulesync {invocation.ToArguments()}{(check ? " --check" : "")}{(dryRun ? " --dry-run" : "")}");

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
