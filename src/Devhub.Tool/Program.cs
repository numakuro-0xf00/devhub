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
                "check" => Check(args.Skip(1).ToArray()),
                "secrets" => Secrets(args.Skip(1).ToArray()),
                "env" => Env(args.Skip(1).ToArray()),
                _ => Unknown(args[0]),
            };
        }

        /// <summary>
        /// check の実行本体。既存の rulesync ドリフト検査の後に、秘匿情報スキャン(devhub secrets)を実行する。
        /// ドリフト検査が非ゼロで終わればその時点で打ち切る。--no-secrets 指定時は秘匿情報スキャンをスキップする
        /// (apply には影響しない。スキャンは検査系コマンドのみが対象)。
        /// </summary>
        private static int Check(string[] args)
        {
            var noSecrets = args.Any(a => a == "--no-secrets");
            var remaining = args.Where(a => a != "--no-secrets").ToArray();

            var code = Apply(remaining, check: true);
            if (code != 0) return code;

            if (noSecrets)
            {
                Console.WriteLine("[devhub] check: --no-secrets が指定されたため秘匿情報スキャンをスキップします。");
                return 0;
            }

            return Secrets(Array.Empty<string>());
        }

        /// <summary>
        /// devhub secrets: リポジトリ内の設定ソース・生成物に秘匿情報の実値が混入していないか検査する
        /// (要件 FR-3.1/FR-3.2)。スキャナは内製せず secretlint(npm 製 OSS)に委譲する(NFR-7)。
        ///
        /// 設定は「リポジトリの .secretlintrc.json があればそれを優先し、無ければ devhub 内蔵の既定設定
        /// (preset-recommend)を使う」(<see cref="SecretlintConfigResolver"/>)。
        /// スキャン対象は「リポジトリ内に実在する設定ソース・生成物」に絞り込む(<see cref="SecretScanPlanner"/>)。
        /// 対象が1つも無ければスキャンする意味が無いため、何もせず exit 0 とする。
        /// </summary>
        private static int Secrets(string[] args)
        {
            if (args.Length > 0)
            {
                Console.Error.WriteLine($"[devhub] 未知のオプション: {args[0]}");
                return 2;
            }

            var repoRoot = Directory.GetCurrentDirectory();
            var targets = SecretScanPlanner.BuildScanTargets(SecretScanPlanner.ReadSnapshot(repoRoot));
            if (targets.Count == 0)
            {
                Console.WriteLine(
                    "[devhub] secrets: スキャン対象(.rulesync/ AGENTS.md CLAUDE.md .claude/ 等)が" +
                    "見つかりません。スキップします。");
                return 0;
            }

            var runner = new SecretlintRunner();
            if (!runner.IsLauncherAvailable())
            {
                Console.Error.WriteLine(
                    "[devhub] npx (Node.js) が見つかりません。secretlint の実行に Node.js が必要です。" +
                    "Node をインストールするか、PATH を確認してください。");
                return 3;
            }

            string? tempConfigFile = null;
            string? tempIgnoreFile = null;
            try
            {
                var repoConfigPath = SecretlintConfigResolver.FindRepoConfigPath(repoRoot);
                if (repoConfigPath is null)
                {
                    tempConfigFile = SecretlintConfigResolver.WriteDefaultConfigToTempFile();
                    Console.WriteLine("[devhub] secrets: 既定設定(preset-recommend)を使用します。");
                }
                else
                {
                    Console.WriteLine($"[devhub] secrets: リポジトリの設定を使用します: {SecretlintConfigResolver.RepoConfigFileName}");
                }
                var configPath = SecretlintConfigResolver.ResolveConfigPath(repoConfigPath, tempConfigFile ?? "");

                // --secretlintignore はワーキングディレクトリ(リポジトリルート)直下からの相対パスで
                // 渡す必要がある(SecretlintInvocation のコメント参照)ため、一時ファイルもそこに置く。
                var ignoreFileName = $".devhub-secretlint-ignore.{Guid.NewGuid():N}.tmp";
                tempIgnoreFile = Path.Combine(repoRoot, ignoreFileName);
                File.WriteAllText(tempIgnoreFile, SecretScanPlanner.DefaultIgnorePatterns);

                var invocation = new SecretlintInvocation(configPath, ignoreFileName, targets);
                Console.WriteLine($"[devhub] secrets: {targets.Count} 件の対象をスキャンします({string.Join(",", targets)})。");

                var code = runner.Run(invocation, workingDirectory: repoRoot);
                if (code == 0)
                {
                    Console.WriteLine("[devhub] secrets: 秘匿情報は検出されませんでした。");
                }
                else if (code == 1)
                {
                    Console.Error.WriteLine("[devhub] secrets: secretlint が秘匿情報の疑いを検出しました(exit 1)。");
                }
                else
                {
                    Console.Error.WriteLine($"[devhub] secrets: secretlint が exit {code} で終了しました。");
                }
                return code;
            }
            finally
            {
                if (tempConfigFile is not null) TryDelete(tempConfigFile);
                if (tempIgnoreFile is not null) TryDelete(tempIgnoreFile);
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch { /* 一時ファイルの削除失敗は致命的ではないためベストエフォートで無視する */ }
        }

        /// <summary>env サブコマンドの動作モード。</summary>
        private enum EnvMode
        {
            List,
            Check,
            WriteExample,
            Fill,
        }

        /// <summary>
        /// devhub env: .rulesync/ 配下の ${VAR} プレースホルダから必要な環境変数を検出し、
        /// 設定状況の一覧表示・検査・.env.example 生成・対話入力を行う(要件 FR-2.4)。
        /// フラグは併用不可(同時指定は引数エラー exit 2)。
        /// </summary>
        private static int Env(string[] args)
        {
            var mode = ParseEnvMode(args, out var argError);
            if (argError is not null)
            {
                Console.Error.WriteLine($"[devhub] 引数エラー: {argError}");
                return 2;
            }

            var repoRoot = Directory.GetCurrentDirectory();
            var required = EnvFileScanner.FindRequiredVariables(repoRoot);
            if (required is null)
            {
                Console.Error.WriteLine("[devhub] .rulesync/ が見つかりません。");
                return 2;
            }

            return mode switch
            {
                EnvMode.List => EnvList(required, repoRoot, check: false),
                EnvMode.Check => EnvList(required, repoRoot, check: true),
                EnvMode.WriteExample => EnvWriteExample(required, repoRoot),
                EnvMode.Fill => EnvFill(required, repoRoot),
                _ => throw new InvalidOperationException($"未知の EnvMode: {mode}"),
            };
        }

        private static EnvMode ParseEnvMode(string[] args, out string? error)
        {
            error = null;
            if (args.Length == 0) return EnvMode.List;
            if (args.Length > 1)
            {
                error = "env のフラグは同時に指定できません。";
                return EnvMode.List;
            }

            switch (args[0])
            {
                case "--check": return EnvMode.Check;
                case "--write-example": return EnvMode.WriteExample;
                case "--fill": return EnvMode.Fill;
                default:
                    error = $"未知のオプション: {args[0]}";
                    return EnvMode.List;
            }
        }

        /// <summary>
        /// 一覧表示(devhub env)/ 検査(devhub env --check)の共通実装。
        /// 値そのものは絶対に表示しない(設定済みかどうかのみ。NFR-1)。
        /// check モードでは未設定が1件でもあれば exit 1、それ以外は常に exit 0。
        /// </summary>
        private static int EnvList(IReadOnlyList<string> required, string repoRoot, bool check)
        {
            if (required.Count == 0)
            {
                Console.WriteLine("[devhub] env: .rulesync/ 内に ${VAR} 形式のプレースホルダを含む設定が見つかりません。");
                return 0;
            }

            var osEnv = ReadOsEnvironmentSnapshot();
            var dotEnv = ReadDotEnvSnapshot(repoRoot);
            var statuses = EnvVarResolver.Resolve(required, osEnv, dotEnv);

            var unsetCount = 0;
            foreach (var status in statuses)
            {
                Console.WriteLine($"  {status.Name}: {(status.IsSet ? "設定済み" : "未設定")}");
                if (!status.IsSet) unsetCount++;
            }

            Console.WriteLine();
            if (unsetCount > 0)
            {
                Console.WriteLine(
                    $"[devhub] env: 未設定の変数が {unsetCount} 件あります。実値は .env(gitignore 済み)か " +
                    "OS 環境変数に置いてください。設定ファイルに実値を書かないでください。");
                Console.WriteLine("[devhub] env: `devhub env --fill` で対話的に .env へ入力できます。");
            }
            else
            {
                Console.WriteLine("[devhub] env: 必要な環境変数はすべて設定済みです。");
            }

            return check && unsetCount > 0 ? 1 : 0;
        }

        /// <summary>
        /// devhub env --write-example: .env.example をリポジトリルートに生成する。
        /// 既存の .env.example がある場合は上書きせずマージする(既存行はコメント含め保持し、
        /// 不足している変数の VAR= 行だけを末尾に追記する)。変更が無ければその旨を表示する。
        /// </summary>
        private static int EnvWriteExample(IReadOnlyList<string> required, string repoRoot)
        {
            var examplePath = Path.Combine(repoRoot, ".env.example");
            var existing = File.Exists(examplePath) ? File.ReadAllText(examplePath) : null;

            var plan = EnvExampleMerger.BuildMergePlan(existing, required);
            if (!plan.HasChanges)
            {
                Console.WriteLine("[devhub] env: .env.example は最新です。");
                return 0;
            }

            var newContent = EnvExampleMerger.ApplyMergePlan(existing, plan);
            File.WriteAllText(examplePath, newContent);
            Console.WriteLine(
                $"[devhub] env: .env.example に {plan.MissingVariables.Count} 件の変数を追記しました" +
                $"({string.Join(",", plan.MissingVariables)})。");
            return 0;
        }

        /// <summary>
        /// devhub env --fill: 未設定変数を1つずつ対話的に質問し、入力値を .env に追記する。
        /// 標準入力がリダイレクトされている等、対話不能な環境では実行できない(exit 2)。
        /// 入力はエコーバックせずマスク表示し、書き込んだ値をログや標準出力に出さない(NFR-1)。
        /// </summary>
        private static int EnvFill(IReadOnlyList<string> required, string repoRoot)
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine(
                    "[devhub] env --fill は対話的な入力が必要です。標準入力がリダイレクトされているため実行できません。");
                return 2;
            }

            var osEnv = ReadOsEnvironmentSnapshot();
            var dotEnv = ReadDotEnvSnapshot(repoRoot);
            var unset = EnvVarResolver.Resolve(required, osEnv, dotEnv)
                .Where(s => !s.IsSet)
                .Select(s => s.Name)
                .ToArray();

            if (unset.Length == 0)
            {
                Console.WriteLine("[devhub] env: 未設定の変数はありません。");
                return 0;
            }

            var envPath = Path.Combine(repoRoot, ".env");
            var filled = 0;
            foreach (var name in unset)
            {
                Console.Write($"{name} の値を入力してください(Enter でスキップ): ");
                var value = ReadMaskedLine();
                Console.WriteLine();

                if (string.IsNullOrEmpty(value))
                {
                    Console.WriteLine($"  {name}: 空入力のためスキップしました。");
                    continue;
                }

                AppendToDotEnv(envPath, name, value);
                filled++;
                Console.WriteLine($"  {name}: .env に書き込みました。");
            }

            Console.WriteLine($"[devhub] env: {filled} / {unset.Length} 件の変数を .env に書き込みました。");
            return 0;
        }

        /// <summary>Console.ReadKey で1行分の入力を読み取り、画面には '*' でマスク表示する(値は表示しない)。</summary>
        private static string ReadMaskedLine()
        {
            var buffer = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                {
                    buffer.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
            return buffer.ToString();
        }

        /// <summary>
        /// .env に KEY=VALUE 行を追記する(既存内容は保持)。末尾改行が無ければ補ってから追記する。
        /// 値はここでも一切ログ・標準出力に書かない。
        /// </summary>
        private static void AppendToDotEnv(string envPath, string key, string value)
        {
            var existing = File.Exists(envPath) ? File.ReadAllText(envPath) : "";
            var needsNewline = existing.Length > 0 && existing[^1] != '\n';

            using var writer = new StreamWriter(envPath, append: true);
            if (needsNewline) writer.Write('\n');
            writer.Write($"{key}={value}\n");
        }

        /// <summary>OS 環境変数のスナップショットを読み取る薄い IO ラッパー。</summary>
        private static IReadOnlyDictionary<string, string> ReadOsEnvironmentSnapshot()
        {
            var result = new Dictionary<string, string>();
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key)
                {
                    result[key] = entry.Value?.ToString() ?? "";
                }
            }
            return result;
        }

        /// <summary>リポジトリルートの .env を読み取る薄い IO ラッパー。存在しなければ空。</summary>
        private static IReadOnlyDictionary<string, string> ReadDotEnvSnapshot(string repoRoot)
        {
            var envPath = Path.Combine(repoRoot, ".env");
            if (!File.Exists(envPath)) return new Dictionary<string, string>();
            return EnvFileParser.Parse(File.ReadAllText(envPath));
        }

        /// <summary>
        /// apply / check の共通処理。計画(RulesyncPlanner)を立て、各パスを rulesync に流す。
        /// いずれかのパスが非ゼロで終わればその時点で打ち切り、そのコードを返す。
        /// </summary>
        private static int Apply(string[] args, bool check)
        {
            var options = ParseApplyOptions(args, out var dryRun, out var featuresExplicit, out var error);
            if (error is not null)
            {
                Console.Error.WriteLine($"[devhub] 引数エラー: {error}");
                return 2;
            }

            // --features を明示指定していない(既定値=全機能の)ときだけ、.rulesync/ に実在する
            // 機能へ絞り込む。明示指定時は絞り込まずそのまま rulesync に渡す(明示要求を黙って落とさない)。
            if (!featuresExplicit)
            {
                var narrowed = TryNarrowFeatures(options, out var narrowError);
                if (narrowError is not null)
                {
                    Console.Error.WriteLine(narrowError);
                    return 2;
                }
                options = narrowed!;
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

        private static ApplyOptions ParseApplyOptions(string[] args, out bool dryRun, out bool featuresExplicit, out string? error)
        {
            dryRun = false;
            featuresExplicit = false;
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
                        featuresExplicit = true;
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

        /// <summary>
        /// .rulesync/ の実在状況(<see cref="RulesyncFeatureDetector"/>)にもとづき、既定 features を
        /// 実在する機能へ絞り込む。.rulesync/ 自体が無い、または絞り込んだ結果 0 件になる場合は
        /// null を返し、<paramref name="error"/> に stderr 用の日本語メッセージを設定する。
        /// 絞り込みが働いた場合は、除外した機能を情報行として標準出力へ書く。
        /// </summary>
        private static ApplyOptions? TryNarrowFeatures(ApplyOptions options, out string? error)
        {
            error = null;

            var snapshot = RulesyncFeatureDetector.ReadSnapshot(Directory.GetCurrentDirectory());
            if (snapshot is null)
            {
                error = "[devhub] .rulesync/ ディレクトリが見つかりません。`npx rulesync init` 等でソースを用意してから実行してください。";
                return null;
            }

            var narrowed = RulesyncFeatureDetector.Narrow(options.Features, featuresExplicit: false, snapshot);
            if (narrowed.Features.Count == 0)
            {
                error = "[devhub] .rulesync/ に生成対象となる機能のソース(rules/mcp.json/hooks.json 等)が1つも見つかりません。";
                return null;
            }

            if (narrowed.SkippedFeatures.Count > 0)
            {
                Console.WriteLine($"[devhub] ソースが無い機能をスキップ: {string.Join(",", narrowed.SkippedFeatures)}");
            }

            return new ApplyOptions
            {
                Targets = options.Targets,
                Features = narrowed.Features,
                ProtectClaudeMd = options.ProtectClaudeMd,
            };
        }

        private static string[] SplitCsv(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";

        private static int PrintVersion()
        {
            Console.WriteLine(
                $"devhub {Version} (rulesync {RulesyncRunner.PinnedVersion}, secretlint {SecretlintRunner.PinnedVersion})");
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
                  devhub check [options]    生成物がソースと一致するか検査(ドリフト時 exit 1) + 秘匿情報スキャン
                  devhub secrets            設定ソース・生成物に秘匿情報の実値が混入していないか検査(検出時 exit 1)
                  devhub env [options]      .rulesync/ の ${VAR} プレースホルダから必要な環境変数を検出・案内
                  devhub --version          バージョン表示
                  devhub --help             このヘルプ

                options (apply/check共通):
                  --features <csv>          生成する機能(既定: rules,mcp,hooks,permissions,subagents,commands,skills,ignore)
                  --targets  <csv>          対象エージェント(既定: claudecode,cursor,copilot,codexcli)
                  --no-protect-claude       CLAUDE.md 保護を無効化(claudecode にも rules を生成)
                  --dry-run                 書き込まず変更内容だけ表示

                options (checkのみ):
                  --no-secrets              check に含まれる秘匿情報スキャンをスキップする

                CLAUDE.md 保護(既定 ON):claudecode 向けの rules 生成を外し、Phase 0 の
                @AGENTS.md リンク方式 CLAUDE.md を rulesync に上書きさせません(要件 R-6)。

                --features を省略した既定実行時は、.rulesync/ に実在する機能だけへ自動的に絞り込みます
                (--features を明示指定した場合は絞り込みません)。

                秘匿情報スキャン(devhub secrets / check の一部):secretlint に委譲し(NFR-7)、
                リポジトリの .secretlintrc.json があればそれを、無ければ devhub 内蔵の既定設定
                (preset-recommend)を使う。対象は .rulesync/ AGENTS.md CLAUDE.md .claude/ .cursor/
                .codex/ .github/ .mcp.json .agents/ のうち実在するもの。

                devhub env(フラグは併用不可。指定しなければ一覧表示):
                  devhub env                必要な環境変数を一覧表示(設定済み/未設定。常に exit 0)
                  devhub env --check        同上の検査。未設定が1件でもあれば exit 1(オンボーディング/CI 用)
                  devhub env --write-example  .env.example を生成(既存があれば不足分だけ追記マージ)
                  devhub env --fill         未設定変数を対話的に質問し、入力値を .env に追記(非対話環境では exit 2)

                devhub env は .rulesync/ 配下のテキストファイルから ${VAR} 形式のプレースホルダを抽出し、
                OS 環境変数または .env(リポジトリルート、gitignore 済み)に非空値があれば「設定済み」と判定する。
                値そのものは表示・ログに出さない(NFR-1)。実値は .env か OS 環境変数に置き、設定ファイルに書かないこと。
                """);
        }
    }
}
