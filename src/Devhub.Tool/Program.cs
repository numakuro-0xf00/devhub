using Devhub.Tool;

return Cli.Run(args);

namespace Devhub.Tool
{
    /// <summary>devhub CLI のエントリ。apply / check / --version / --help を最小構成で捌く。</summary>
    internal static class Cli
    {
        private const string Version = "0.1.0";

        public static int Run(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            return args[0] switch
            {
                "--version" or "-v" => PrintVersion(),
                "apply" => Apply(args.Skip(1).ToArray(), check: false),
                "check" => Apply(args.Skip(1).ToArray(), check: true),
                _ => Unknown(args[0]),
            };
        }

        /// <summary>
        /// apply / check の共通処理。計画(RulesyncPlanner)を立て、各パスを rulesync に流す。
        /// いずれかのパスが非ゼロで終わればその時点で打ち切り、そのコードを返す。
        /// </summary>
        private static int Apply(string[] args, bool check)
        {
            var options = ParseApplyOptions(args, out var dryRun, out var error);
            if (error is not null)
            {
                Console.Error.WriteLine($"[devhub] 引数エラー: {error}");
                return 2;
            }

            var runner = new RulesyncRunner();
            if (!runner.IsLauncherAvailable())
            {
                Console.Error.WriteLine(
                    "[devhub] npx (Node.js) が見つかりません。rulesync の実行に Node.js が必要です。" +
                    "Node をインストールするか、PATH を確認してください。");
                return 3;
            }

            var plan = RulesyncPlanner.BuildApplyPlan(options);
            Console.WriteLine($"[devhub] {(check ? "check" : "apply")}: {plan.Count} パスを実行します" +
                              $"{(options.ProtectClaudeMd ? "(CLAUDE.md 保護: 有効)" : "")}。");

            foreach (var invocation in plan)
            {
                var code = runner.Run(invocation, check: check, dryRun: dryRun);
                if (code != 0)
                {
                    Console.Error.WriteLine($"[devhub] rulesync が exit {code} で終了しました。中断します。");
                    return code;
                }
            }

            Console.WriteLine($"[devhub] 完了。");
            return 0;
        }

        private static ApplyOptions ParseApplyOptions(string[] args, out bool dryRun, out string? error)
        {
            dryRun = false;
            error = null;
            var protectClaude = true;
            IReadOnlyList<string> features = ApplyOptions.DefaultFeatures;
            IReadOnlyList<string> targets = ApplyOptions.DefaultTargets;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--no-protect-claude":
                        protectClaude = false;
                        break;
                    case "--features":
                        if (++i >= args.Length) { error = "--features に値がありません。"; return new ApplyOptions(); }
                        features = SplitCsv(args[i]);
                        break;
                    case "--targets":
                        if (++i >= args.Length) { error = "--targets に値がありません。"; return new ApplyOptions(); }
                        targets = SplitCsv(args[i]);
                        break;
                    default:
                        error = $"未知のオプション: {args[i]}";
                        return new ApplyOptions();
                }
            }

            return new ApplyOptions { Targets = targets, Features = features, ProtectClaudeMd = protectClaude };
        }

        private static string[] SplitCsv(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";

        private static int PrintVersion()
        {
            Console.WriteLine($"devhub {Version} (rulesync {RulesyncRunner.PinnedVersion})");
            return 0;
        }

        private static int Unknown(string cmd)
        {
            Console.Error.WriteLine($"[devhub] 未知のコマンド: {cmd}");
            PrintHelp();
            return 2;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
                """
                devhub — チーム共有 agent 設定を各エージェントへ配置する

                使い方:
                  devhub apply [options]    単一ソース(.rulesync/)から各エージェント設定を生成
                  devhub check [options]    生成物がソースと一致するか検査(ドリフト時 exit 1)
                  devhub --version          バージョン表示
                  devhub --help             このヘルプ

                options:
                  --features <csv>          生成する機能(既定: rules,mcp,hooks,permissions,subagents,commands,skills,ignore)
                  --targets  <csv>          対象エージェント(既定: claudecode,cursor,copilot,codexcli)
                  --no-protect-claude       CLAUDE.md 保護を無効化(claudecode にも rules を生成)
                  --dry-run                 書き込まず変更内容だけ表示

                CLAUDE.md 保護(既定 ON):claudecode 向けの rules 生成を外し、Phase 0 の
                @AGENTS.md リンク方式 CLAUDE.md を rulesync に上書きさせません(要件 R-6)。
                """);
        }
    }
}
