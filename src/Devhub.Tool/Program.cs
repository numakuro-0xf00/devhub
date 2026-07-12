using System.Text.Json;
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
                "telemetry" => Telemetry(args.Skip(1).ToArray()),
                _ => Unknown(args[0]),
            };
        }

        /// <summary>
        /// check の実行本体。まず .rulesync/hooks.json の hooks lint(<see cref="HooksLint"/>)を rulesync
        /// ドリフト検査より前に実行し(壊れた設定のまま rulesync に生成させないため。doc/phase2.md Step 2c)、
        /// 続けてドリフト検査、最後に秘匿情報スキャン(devhub secrets)を実行する。
        /// いずれかが非ゼロで終わればその時点で打ち切る。--no-secrets 指定時は秘匿情報スキャンをスキップする
        /// (apply には影響しない。スキャンは検査系コマンドのみが対象)。
        /// </summary>
        private static int Check(string[] args)
        {
            var noSecrets = args.Any(a => a == "--no-secrets");
            var remaining = args.Where(a => a != "--no-secrets").ToArray();

            var lintCode = HooksLint(Directory.GetCurrentDirectory());
            if (lintCode != 0) return lintCode;

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
        /// devhub check の一部:.rulesync/hooks.json に rulesync@9.2.0 のバグ対象である
        /// <c>type:"http"</c> が混入していないかを検査する(<see cref="HooksLintChecker"/>。doc/phase2.md
        /// 「リスク・注意点」)。hooks.json が存在しない場合は検査対象が無いため何もせず exit 0。
        /// JSON パースに失敗した場合も lint 失敗にはせず、警告を表示するだけで exit 0 とする
        /// (rulesync 自身が別途パースエラーを出すため devhub 側で二重報告しない)。
        /// 混入を検出した場合はドリフト検査と同じ意味論で exit 1 にする。
        /// </summary>
        private static int HooksLint(string repoRoot)
        {
            var result = HooksLintChecker.CheckRepository(repoRoot);
            if (result is null) return 0;

            if (result.ParseError is not null)
            {
                Console.WriteLine(
                    "[devhub] check: .rulesync/hooks.json の JSON 解析に失敗したため hooks lint をスキップします" +
                    $"(rulesync 実行時のエラーメッセージを確認してください: {result.ParseError})。");
                return 0;
            }

            if (!result.HasViolations) return 0;

            Console.Error.WriteLine(
                "[devhub] check: .rulesync/hooks.json に type:\"http\" のエントリが含まれています。" +
                "rulesync@9.2.0 は Claude Code / Cursor 向けに url が欠落した壊れたエントリを生成するバグがあるため、" +
                "devhub では type:\"command\" のみを許可します。");
            foreach (var violation in result.Violations)
            {
                Console.Error.WriteLine($"[devhub]   該当エントリ: {violation.Path}");
            }
            return 1;
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

        /// <summary>
        /// devhub telemetry: hooks から呼ばれる利用状況送信(FR-4)。サブコマンドは send / scan-transcripts。
        /// telemetry 単体・未知サブコマンドは通常コマンドと同様に exit 2 でヘルプ誘導する
        /// (「hook から呼ばれる send / scan-transcripts だけ」が沈黙契約の対象。
        /// doc/phase2.md「送信コマンド devhub telemetry send の契約と構成」)。
        /// </summary>
        private static int Telemetry(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine(
                    "[devhub] telemetry: サブコマンドが必要です(send/scan-transcripts)。`devhub --help` を参照してください。");
                return 2;
            }

            return args[0] switch
            {
                "send" => TelemetrySend(args.Skip(1).ToArray()),
                "scan-transcripts" => TelemetryScanTranscripts(args.Skip(1).ToArray()),
                _ => TelemetryUnknown(args[0]),
            };
        }

        private static int TelemetryUnknown(string cmd)
        {
            Console.Error.WriteLine($"[devhub] telemetry: 未知のサブコマンド: {cmd}");
            return 2;
        }

        private const int DefaultTelemetryTimeoutMs = 1000;

        /// <summary>
        /// devhub telemetry send: hook ペイロード(stdin)を正規化・匿名化して DEVHUB_TELEMETRY_ENDPOINT へ POST する。
        ///
        /// hook から同期実行される(Claude Code の PostToolUse 等はツール応答をブロックする)ため、以下を
        /// 契約として固定する(doc/phase2.md 該当節):
        ///   - 常に exit 0 で終了する(引数エラー・未知 --agent・パース失敗・送信失敗を含め、例外は一切伝播させない)。
        ///   - 既定では stdout / stderr に何も出力しない(沈黙の原則)。診断は DEVHUB_TELEMETRY_DEBUG=1 のときだけ stderr へ。
        ///   - DEVHUB_TELEMETRY_ENDPOINT が未設定なら stdin も読まず即 exit 0(D-P2-4 の off スイッチ)。
        ///
        /// DEVHUB_TELEMETRY_* の環境変数は「OS 環境変数 → リポジトリルート(カレントディレクトリ)の .env」の
        /// 2段フォールバックで解決する(<see cref="TelemetryEnvVarResolver"/>)。hook プロセスの環境には
        /// .env が載らない(devhub env --fill は .env に書くのみ)ための穴埋め。
        /// </summary>
        private static int TelemetrySend(string[] args)
        {
            // hook プロセスの環境には .env が載らない(devhub env --fill は .env に書き込むだけで OS 環境変数は
            // 変更しない)ため、DEVHUB_TELEMETRY_* は「OS 環境変数 → リポジトリルート(カレントディレクトリ)の
            // .env」の2段フォールバックで解決する(TelemetryEnvVarResolver。純粋関数)。
            // .env の読み取り失敗(存在しない・権限エラー等)は沈黙契約どおり無視する
            // (ReadDotEnvSnapshotSilently が例外を飲み込む)。
            var osEnv = ReadOsEnvironmentSnapshot();
            var dotEnv = ReadDotEnvSnapshotSilently(Directory.GetCurrentDirectory());
            string? ResolveVar(string name) => TelemetryEnvVarResolver.Resolve(name, osEnv, dotEnv);

            var debug = ResolveVar("DEVHUB_TELEMETRY_DEBUG") == "1";
            void DebugLog(string message)
            {
                if (debug) Console.Error.WriteLine($"[devhub] telemetry send: {message}");
            }

            try
            {
                // DEVHUB_TELEMETRY_ENDPOINT 未設定なら、--agent の妥当性やペイロードを見るまでもなく
                // stdin すら読まずに即終了する(D-P2-4。テレメトリを使わないメンバー/チームには何も起きない)。
                var endpoint = ResolveVar("DEVHUB_TELEMETRY_ENDPOINT");
                if (string.IsNullOrEmpty(endpoint))
                {
                    DebugLog("DEVHUB_TELEMETRY_ENDPOINT が未設定のため送信をスキップします。");
                    return 0;
                }

                var agentRaw = ParseAgentOption(args, out var argError);
                if (argError is not null)
                {
                    DebugLog(argError);
                    return 0;
                }

                if (agentRaw is null || !Enum.TryParse<TelemetryAgentKind>(agentRaw, ignoreCase: true, out var agent))
                {
                    DebugLog($"--agent が不明または未指定です: {agentRaw ?? "(未指定)"}");
                    return 0;
                }

                var payload = Console.In.ReadToEnd();
                var normalized = TelemetryEventNormalizer.Normalize(agent, payload);
                if (normalized is null)
                {
                    DebugLog("hook ペイロードの正規化に失敗しました(JSON として解釈できないか、オブジェクトではありません)。");
                    return 0;
                }

                var salt = ResolveVar("DEVHUB_TELEMETRY_SALT");
                if (string.IsNullOrEmpty(salt))
                {
                    DebugLog("DEVHUB_TELEMETRY_SALT が未設定です。空キーで HMAC を計算します(可用性優先)。");
                }

                // user_id のハッシュ元の優先順位:
                //   1. DEVHUB_TELEMETRY_USER_SEED (明示上書き。WSL/Windows 混在環境で一致させたい場合の手段)
                //   2. normalized.RawUserSeed (Cursor の user_email。エージェントが自ら渡してくる識別子)
                //   3. Environment.UserName (既定のフォールバック)
                var userSeed = ResolveVar("DEVHUB_TELEMETRY_USER_SEED")
                    ?? normalized.RawUserSeed
                    ?? Environment.UserName;

                var outbound = new TelemetryOutboundEvent(
                    Schema: 1,
                    Event: normalized.Event,
                    Agent: normalized.Agent,
                    ToolName: normalized.ToolName,
                    AgentType: normalized.AgentType,
                    SessionId: normalized.RawSessionId is not null
                        ? TelemetryAnonymizer.Hash(salt, normalized.RawSessionId)
                        : null,
                    UserId: TelemetryAnonymizer.Hash(salt, userSeed),
                    Timestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

                var json = JsonSerializer.Serialize(outbound);

                var timeoutMs = ParseTelemetryTimeoutMs(ResolveVar("DEVHUB_TELEMETRY_TIMEOUT_MS"));
                var token = ResolveVar("DEVHUB_TELEMETRY_TOKEN");

                var sender = new TelemetrySender(TimeSpan.FromMilliseconds(timeoutMs));
                var sent = sender.Send(endpoint, json, token);
                DebugLog(sent ? "送信しました。" : "送信に失敗しました(無視します)。");
                return 0;
            }
            catch (Exception ex)
            {
                // 契約上、送信コマンドはどんな異常があっても hook の動作をブロックしてはならない。
                DebugLog($"予期しないエラーを無視します: {ex.Message}");
                return 0;
            }
        }

        private static int ParseTelemetryTimeoutMs(string? raw) =>
            int.TryParse(raw, out var ms) && ms > 0 ? ms : DefaultTelemetryTimeoutMs;

        /// <summary>
        /// telemetry send の引数から --agent の値を取り出す。--agent 以外の未知オプション・値欠落は
        /// すべて error に日本語メッセージを設定して呼び出し元へ返す(呼び出し元は debug 時のみ stderr へ出し
        /// 契約どおり exit 0 を維持する。通常コマンドの引数エラー(exit 2)とは異なる)。
        /// </summary>
        private static string? ParseAgentOption(string[] args, out string? error)
        {
            error = null;
            string? agent = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--agent")
                {
                    if (++i >= args.Length)
                    {
                        error = "--agent に値がありません。";
                        return null;
                    }
                    agent = args[i];
                }
                else
                {
                    error = $"未知のオプション: {args[i]}";
                    return null;
                }
            }
            return agent;
        }

        /// <summary>1回の実行(devhub telemetry scan-transcripts)で送信する skill_use イベントの上限件数。</summary>
        private const int MaxTranscriptEventsPerRun = 1000;

        /// <summary>
        /// devhub telemetry scan-transcripts: Claude Code トランスクリプト(<c>~/.claude/projects/&lt;encoded-cwd&gt;/</c>)
        /// を増分走査し、skill 利用(hooks からは取得不可。D-P2-7)を <c>devhub telemetry send</c> と同じ
        /// エンドポイントへ送信する(doc/phase2.md「実装仕様」Step 4)。
        ///
        /// telemetry send と同一の契約:常に exit 0・既定沈黙(DEVHUB_TELEMETRY_DEBUG=1 で診断)・
        /// DEVHUB_TELEMETRY_ENDPOINT 未設定なら即 no-op。対象ディレクトリが無い場合も no-op。
        /// 状態ファイル(<c>~/.devhub/telemetry-scan-state/&lt;encoded-cwd&gt;.json</c>)が無い(=本当の初回実行)場合は
        /// 走査せず現在の EOF オフセットを記録するだけにする(過去分の大量送信を避ける。--backfill 指定時のみ
        /// 全履歴を走査する)。1回の実行での送信上限は 1000 件(超過分は次回に持ち越す)。
        /// </summary>
        private static int TelemetryScanTranscripts(string[] args)
        {
            var backfill = ParseScanTranscriptsOptions(args, out var argError);
            var osEnv = ReadOsEnvironmentSnapshot();
            var dotEnv = ReadDotEnvSnapshotSilently(Directory.GetCurrentDirectory());
            string? ResolveVar(string name) => TelemetryEnvVarResolver.Resolve(name, osEnv, dotEnv);

            var debug = ResolveVar("DEVHUB_TELEMETRY_DEBUG") == "1";
            void DebugLog(string message)
            {
                if (debug) Console.Error.WriteLine($"[devhub] telemetry scan-transcripts: {message}");
            }

            if (argError is not null)
            {
                DebugLog(argError);
                return 0;
            }

            try
            {
                // DEVHUB_TELEMETRY_ENDPOINT 未設定ならファイルシステムに一切触れずに即終了する
                // (D-P2-4。telemetry send と同じオフスイッチ契約)。
                var endpoint = ResolveVar("DEVHUB_TELEMETRY_ENDPOINT");
                if (string.IsNullOrEmpty(endpoint))
                {
                    DebugLog("DEVHUB_TELEMETRY_ENDPOINT が未設定のためスキャンをスキップします。");
                    return 0;
                }

                var cwd = Directory.GetCurrentDirectory();
                var encoded = TranscriptProjectPathEncoder.Encode(cwd);
                var projectDir = Path.Combine(GetHomeDirectory(), ".claude", "projects", encoded);
                if (!Directory.Exists(projectDir))
                {
                    DebugLog($"対象のプロジェクトディレクトリが見つかりません: {projectDir}");
                    return 0;
                }

                var files = Directory.EnumerateFiles(projectDir, "*.jsonl", SearchOption.AllDirectories).ToArray();
                var lengths = files.ToDictionary(f => f, f => new FileInfo(f).Length);

                var stateDir = Path.Combine(GetHomeDirectory(), ".devhub", "telemetry-scan-state");
                var statePath = Path.Combine(stateDir, $"{encoded}.json");
                var hasPriorState = File.Exists(statePath);
                var previousOffsets = hasPriorState ? ReadScanStateSilently(statePath) : new Dictionary<string, long>();

                var plan = TranscriptScanPlanner.BuildPlan(lengths, hasPriorState, previousOffsets, backfill);

                var salt = ResolveVar("DEVHUB_TELEMETRY_SALT");
                var userSeed = ResolveVar("DEVHUB_TELEMETRY_USER_SEED") ?? Environment.UserName;
                var timeoutMs = ParseTelemetryTimeoutMs(ResolveVar("DEVHUB_TELEMETRY_TIMEOUT_MS"));
                var token = ResolveVar("DEVHUB_TELEMETRY_TOKEN");
                var sender = new TelemetrySender(TimeSpan.FromMilliseconds(timeoutMs));

                var newOffsets = new Dictionary<string, long>();
                var remainingBudget = MaxTranscriptEventsPerRun;
                var capNotified = false;

                foreach (var task in plan.OrderBy(t => t.Path, StringComparer.Ordinal))
                {
                    if (!task.ShouldRead)
                    {
                        // 状態ファイル無し(本当の初回実行)。走査せず現在の EOF を記録するだけ。
                        newOffsets[task.Path] = task.CurrentLength;
                        continue;
                    }

                    if (remainingBudget <= 0)
                    {
                        // 上限到達済み。このファイルは今回一切読まず、前回までの進捗をそのまま維持して次回に回す。
                        if (!capNotified)
                        {
                            DebugLog($"1回の実行での送信上限({MaxTranscriptEventsPerRun}件)に達したため、残りは次回に回します。");
                            capNotified = true;
                        }
                        newOffsets[task.Path] = task.StartOffset;
                        continue;
                    }

                    var chunk = ReadChunk(task.Path, task.StartOffset);
                    var completeLines = TranscriptChunkParser.ParseCompleteLines(chunk);
                    var outcome = TranscriptSkillScanner.Scan(completeLines, remainingBudget);

                    var agentType = ResolveAgentType(task.Path);
                    foreach (var record in outcome.Events)
                    {
                        var outbound = new TelemetryOutboundEvent(
                            Schema: 1,
                            Event: "skill_use",
                            Agent: TelemetryEventNormalizer.WireName(TelemetryAgentKind.ClaudeCode),
                            ToolName: record.SkillName,
                            AgentType: agentType,
                            SessionId: record.RawSessionId is not null
                                ? TelemetryAnonymizer.Hash(salt, record.RawSessionId)
                                : null,
                            UserId: TelemetryAnonymizer.Hash(salt, userSeed),
                            Timestamp: record.Timestamp ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

                        var json = JsonSerializer.Serialize(outbound);
                        var sent = sender.Send(endpoint, json, token);
                        DebugLog(sent
                            ? $"skill_use を送信しました({record.SkillName})。"
                            : $"送信に失敗しました(無視します): {record.SkillName}");
                    }

                    remainingBudget -= outcome.Events.Count;
                    newOffsets[task.Path] = task.StartOffset + outcome.ConsumedBytes;
                }

                WriteScanStateSilently(statePath, newOffsets);
                return 0;
            }
            catch (Exception ex)
            {
                // 契約上、このコマンドもどんな異常があっても hook の動作をブロックしてはならない。
                DebugLog($"予期しないエラーを無視します: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// scan-transcripts の引数から --backfill を取り出す。--backfill 以外の未知オプションは
        /// error に日本語メッセージを設定して呼び出し元へ返す(telemetry send の ParseAgentOption と同じ流儀。
        /// 呼び出し元は debug 時のみ stderr へ出し契約どおり exit 0 を維持する)。
        /// </summary>
        private static bool ParseScanTranscriptsOptions(string[] args, out string? error)
        {
            error = null;
            var backfill = false;
            foreach (var arg in args)
            {
                if (arg == "--backfill")
                {
                    backfill = true;
                }
                else
                {
                    error = $"未知のオプション: {arg}";
                    return false;
                }
            }
            return backfill;
        }

        /// <summary>
        /// ファイルの <paramref name="startOffset"/> から EOF までを読み取る薄い IO ラッパー。
        /// FileShare.ReadWrite を指定し、Claude Code が書き込み中でも読み取れるようにする(追記専用ファイル)。
        /// </summary>
        private static byte[] ReadChunk(string path, long startOffset)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(startOffset, SeekOrigin.Begin);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>
        /// subagent 別トランスクリプトなら sibling の meta.json から agentType を読む薄い IO ラッパー
        /// (<see cref="TranscriptAgentTypeResolver"/>)。メインのセッション transcript は常に null。
        /// メタファイルが無い・読み取れない場合も null(致命的ではないため沈黙して無視)。
        /// </summary>
        private static string? ResolveAgentType(string jsonlPath)
        {
            if (!TranscriptAgentTypeResolver.IsSubagentFile(jsonlPath)) return null;

            var metaPath = TranscriptAgentTypeResolver.GetMetaFilePath(jsonlPath);
            if (!File.Exists(metaPath)) return null;

            try
            {
                return TranscriptAgentTypeResolver.ExtractAgentType(File.ReadAllText(metaPath));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// scan-transcripts の状態ファイル(path→offset の JSON)を読み取る。存在しない・パース不能な場合は
        /// 空の辞書を返す(沈黙契約。呼び出し側で hasPriorState の有無と組み合わせて解釈する)。
        /// </summary>
        private static IReadOnlyDictionary<string, long> ReadScanStateSilently(string statePath)
        {
            try
            {
                var text = File.ReadAllText(statePath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, long>>(text);
                return parsed ?? new Dictionary<string, long>();
            }
            catch
            {
                return new Dictionary<string, long>();
            }
        }

        /// <summary>scan-transcripts の状態ファイルを書き込む。失敗しても沈黙契約どおり無視する。</summary>
        private static void WriteScanStateSilently(string statePath, IReadOnlyDictionary<string, long> state)
        {
            try
            {
                var dir = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(statePath, JsonSerializer.Serialize(state));
            }
            catch
            {
                // 次回もう一度差分計算が走るだけなので致命的ではない。
            }
        }

        /// <summary>
        /// ホームディレクトリを解決する。HOME 環境変数を優先し(E2E テストで一時ディレクトリへ差し替え可能に
        /// するため。WSL/Linux/macOS の通常利用でも一致する)、無ければ OS 標準の解決(Windows は USERPROFILE)に
        /// フォールバックする。
        /// </summary>
        private static string GetHomeDirectory() =>
            Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
                ? home
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
        /// telemetry send 専用の .env 読み取り。<see cref="ReadDotEnvSnapshot"/> と異なり、読み取り失敗
        /// (権限エラー等の予期しない IO 例外)を沈黙契約どおり無視して空の辞書を返す
        /// (hook から同期実行される telemetry send は、常に exit 0 で hook 本体の動作をブロックしてはならない)。
        /// </summary>
        private static IReadOnlyDictionary<string, string> ReadDotEnvSnapshotSilently(string repoRoot)
        {
            try
            {
                return ReadDotEnvSnapshot(repoRoot);
            }
            catch
            {
                return new Dictionary<string, string>();
            }
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
                  devhub check [options]    hooks lint + 生成物がソースと一致するか検査(ドリフト時 exit 1) + 秘匿情報スキャン
                  devhub secrets            設定ソース・生成物に秘匿情報の実値が混入していないか検査(検出時 exit 1)
                  devhub env [options]      .rulesync/ の ${VAR} プレースホルダから必要な環境変数を検出・案内
                  devhub telemetry send --agent <claudecode|cursor|codexcli>
                                            hook ペイロード(stdin)を正規化・匿名化して送信(常に exit 0)
                  devhub telemetry scan-transcripts [--backfill]
                                            Claude Code トランスクリプトを増分走査し skill 利用を送信(常に exit 0)
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

                hooks lint(check の一部):.rulesync/hooks.json に type:"http" が混入していないか検査する
                (rulesync@9.2.0 が url 欠落の壊れたエントリを生成するバグの回避。devhub では type:"command" のみ許可)。
                hooks.json が無ければスキップし、JSON パース不能な場合は警告表示のみで exit 0(rulesync 側の
                エラーと二重報告しない)。混入を検出した場合はドリフト検査より前に exit 1 で打ち切る。

                秘匿情報スキャン(devhub secrets / check の一部):secretlint に委譲し(NFR-7)、
                リポジトリの .secretlintrc.json があればそれを、無ければ devhub 内蔵の既定設定
                (preset-recommend)を使う。対象は .rulesync/ AGENTS.md CLAUDE.md .claude/ .cursor/
                .codex/ .github/ .mcp.json .agents/ telemetry/ のうち実在するもの。

                devhub env(フラグは併用不可。指定しなければ一覧表示):
                  devhub env                必要な環境変数を一覧表示(設定済み/未設定。常に exit 0)
                  devhub env --check        同上の検査。未設定が1件でもあれば exit 1(オンボーディング/CI 用)
                  devhub env --write-example  .env.example を生成(既存があれば不足分だけ追記マージ)
                  devhub env --fill         未設定変数を対話的に質問し、入力値を .env に追記(非対話環境では exit 2)

                devhub env は .rulesync/ 配下のテキストファイルから ${VAR} 形式のプレースホルダを抽出し、
                OS 環境変数または .env(リポジトリルート、gitignore 済み)に非空値があれば「設定済み」と判定する。
                値そのものは表示・ログに出さない(NFR-1)。実値は .env か OS 環境変数に置き、設定ファイルに書かないこと。

                devhub telemetry send --agent <claudecode|cursor|codexcli>:
                  hooks(type: command)から呼ばれる想定の送信コマンド。stdin の hook ペイロード JSON を
                  正規化・匿名化(HMAC-SHA256 擬似ID化)し、DEVHUB_TELEMETRY_ENDPOINT へ HTTP POST する。
                  hook 本体の動作を絶対にブロックしないため、常に exit 0 で終了し、既定では stdout/stderr に
                  何も出力しない(沈黙の原則)。DEVHUB_TELEMETRY_DEBUG=1 のときだけ診断を stderr に出す。
                  DEVHUB_TELEMETRY_ENDPOINT が未設定なら stdin も読まず即 exit 0(オフスイッチ。NFR-5)。

                  環境変数:
                    DEVHUB_TELEMETRY_ENDPOINT   送信先 URL(未設定なら no-op)
                    DEVHUB_TELEMETRY_TOKEN      Authorization: Bearer ヘッダ(設定時のみ付与)
                    DEVHUB_TELEMETRY_SALT       HMAC-SHA256 の鍵(未設定時は空キーで計算。可用性優先)
                    DEVHUB_TELEMETRY_USER_SEED  user_id のハッシュ元を明示上書き(既定は OS ユーザー名)
                    DEVHUB_TELEMETRY_TIMEOUT_MS HTTP POST のタイムアウト ms(既定 1000)
                    DEVHUB_TELEMETRY_DEBUG      1 を指定すると診断メッセージを stderr に出力

                  上記の環境変数はいずれも「OS 環境変数 → リポジトリルート(カレントディレクトリ)の .env」の
                  2段フォールバックで解決する。hook プロセスの環境には .env が載らないため(devhub env --fill は
                  .env に書くのみ)、OS 環境変数が無い場合は .env を読みに行く(読み取り失敗は沈黙して無視)。

                devhub telemetry scan-transcripts [--backfill]:
                  Claude Code トランスクリプト(~/.claude/projects/<encoded-cwd>/ 配下。カレントディレクトリに
                  対応するプロジェクトのみ)から skill 利用(hooks では取得不可な唯一の情報源)を抽出し、
                  telemetry send と同じ DEVHUB_TELEMETRY_ENDPOINT へ送信する。sessionEnd hook からの自動起動を
                  想定(devhub apply が配布する hooks テンプレートに含まれる)。

                  send と同一の契約:常に exit 0、既定沈黙(DEVHUB_TELEMETRY_DEBUG=1 で診断)、
                  DEVHUB_TELEMETRY_ENDPOINT 未設定・対象ディレクトリ無しは即 no-op。

                  ファイル別バイトオフセットを ~/.devhub/telemetry-scan-state/<encoded-cwd>.json に記録し
                  増分走査する。状態ファイルが無い(本当の初回実行)場合は走査せず現在の EOF を記録するだけに
                  留める(過去分の大量送信を避ける)。--backfill を指定すると全履歴を走査する。
                  1回の実行での送信上限は 1000 件(超過分は次回に持ち越す)。

                  devhub telemetry(サブコマンド省略)・未知サブコマンドは通常コマンドと同様に exit 2 で
                  ヘルプ誘導する(沈黙・exit 0 契約の対象は hook から呼ばれる send / scan-transcripts のみ)。
                """);
        }
    }
}
