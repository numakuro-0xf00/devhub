# Phase 1 — 配布ツール(dotnet tool)

> `devhub` dotnet tool が rulesync をラップし、単一ソース `.rulesync/` から4エージェント(Claude Code / Cursor / Copilot / Codex)のネイティブ設定を生成する。
> 位置づけ:`doc/requirements.md` の Phase 1 / D-1。変換ロジックは内製せず rulesync に委譲(NFR-7)。ツール選定の根拠は `doc/phase1-tool-eval.md`。

## 現状

実装済み・検証済みの範囲:

- `devhub apply` — `.rulesync/` から各エージェント設定を生成。
- `devhub check` — 生成物がソースと一致するか検査(rulesync `--check`、ドリフト時 exit 1)+ 秘匿情報スキャン(`--no-secrets` でスキップ可)。CI 向け。
- `devhub secrets` — 設定ソース・生成物への秘匿情報の実値混入を検査(後述)。
- `devhub --version` / `--help`。
- **CLAUDE.md 保護**(既定 ON)— 後述。実バイナリで 2 パス動作を確認済み。
- **features 絞り込み**(後述)— `.rulesync/` に実在する機能だけを rulesync に渡す。
- Node/npx 不在時の明示エラー(exit 3)。
- **publish パイプライン** — CI(build+test)と `v*` タグでの GitHub Packages への publish(後述)。

未実装(今後):初期化スキル(FR-2.4、必要な環境変数の一覧化と穴埋め支援)、リモート MCP `type` 値ドリフト(R-4)への後処理アダプタ。

## プロジェクト構成

```
devhub.sln
.github/workflows/
    ci.yml                        # push/PR で build + test(Release)
    release.yml                   # v* タグで pack → GitHub Packages へ push
src/Devhub.Tool/
    Devhub.Tool.csproj            # PackAsTool, ToolCommandName=devhub, net8.0, pack メタデータ
    Program.cs                    # CLI ディスパッチ(apply/check/secrets/--version/--help)
    RulesyncPlanner.cs            # apply を rulesync 呼び出し列へ展開する純粋ロジック(テスト対象)
    RulesyncFeatureDetector.cs    # .rulesync/ の実在機能を検出し features を絞る純粋ロジック(テスト対象)
    RulesyncRunner.cs             # rulesync を子プロセス実行する薄いラッパー
    SecretScanPlanner.cs          # 秘匿情報スキャンの対象決定(純粋ロジック・テスト対象)
    SecretlintConfigResolver.cs   # .secretlintrc.json の解決(リポジトリ優先、無ければ内蔵既定)
    SecretlintRunner.cs           # secretlint を子プロセス実行する薄いラッパー
tests/Devhub.Tool.Tests/          # 純粋ロジックのユニットテスト(xUnit)
```

## CLAUDE.md 保護(要件 R-6)

rulesync も Ruler も、rules 生成時に **CLAUDE.md を全文で上書き**する。それでは Phase 0 で用意した `@AGENTS.md` リンク方式の CLAUDE.md(開発ガイド + ブリッジ)が消える。

そこで `devhub apply` は rulesync を **2 パス**に分けて呼ぶ:

1. `--targets cursor,copilot,codexcli --features <全機能>` — rules を含め生成。
2. `--targets claudecode --features <rules を除く>` — CLAUDE.md には触れない。

これにより Claude Code 向けの skill/MCP/hooks 等は生成しつつ、CLAUDE.md は Phase 0 の内容を正として保つ。`--no-protect-claude` で無効化できる(claudecode にも rules を生成)。

判断ロジックは `RulesyncPlanner.BuildApplyPlan` に純粋関数として実装し、ユニットテストで境界(claudecode 単独 / rules 非生成 / 保護 OFF 等)を固定している。

## features 絞り込み

rulesync は `--features` に渡された機能のソースが `.rulesync/` に無いと `Failed to load ...` という無害だが紛らわしい警告を出す。そこで `--features` を省略した既定実行時は、`.rulesync/` に**実在する機能だけ**へ自動的に絞り込む(`--features` を明示指定した場合は絞り込まない — 明示要求を黙って落とさない)。

feature → ソースの対応(rulesync@9.2.0 で実測):

| feature | ソース | 実在条件 |
|---|---|---|
| rules / subagents / commands / skills | `rules/` `subagents/` `commands/` `skills/` | ディレクトリが存在し中身が 1 つ以上 |
| mcp / hooks / permissions | `mcp.json` `hooks.json` `permissions.json` | ファイルが存在 |
| ignore | `.aiignore` | ファイルが存在 |

`.rulesync/` 自体が無い、または実在機能が 0 件のときは rulesync を呼ばず exit 2。絞り込みが働いたときは除外した機能を情報表示する。検出は `RulesyncFeatureDetector` に純粋関数として実装し、ユニットテストで境界を固定している。

## 秘匿情報スキャン(要件 FR-3.1 / FR-3.2)

`devhub secrets`(単体)と `devhub check`(ドリフト検査後に自動実行、`--no-secrets` でスキップ)が、設定ソース・生成物への秘匿情報の実値混入を検査する。検出時 exit 1。

- スキャナは内製せず **secretlint**(npm 製 OSS)に委譲(NFR-7)。rulesync と同じ npx 起動パターンでバージョン固定(secretlint@13.0.2 + preset-recommend)しており、Node 以外のランタイム依存を増やさない。gitleaks(Go バイナリ)は別途インストールが要るため見送り。
- 設定はリポジトリの `.secretlintrc.json` を優先し、無ければ devhub 内蔵の既定設定(preset-recommend)を使う。
- スキャン対象は設定ソースと生成物のうち実在するもの:`.rulesync/` `AGENTS.md` `CLAUDE.md` `.claude/` `.cursor/` `.codex/` `.github/` `.mcp.json` `.agents/`。`node_modules/` `bin/` `obj/` `.git/` は除外。
- 対象決定は `SecretScanPlanner`、設定解決は `SecretlintConfigResolver` に純粋関数として実装し、ユニットテスト対象。

## publish パイプライン

- `.github/workflows/ci.yml` — main / `phase*` ブランチへの push と PR で `dotnet build` + `dotnet test`(Release)。
- `.github/workflows/release.yml` — `v*` タグ push で pack → GitHub Packages(`https://nuget.pkg.github.com/<owner>/index.json`)へ push。認証はワークフロー組込みの `GITHUB_TOKEN` のみで、PAT は commit しない(FR-2.3)。タグと csproj の `<Version>` の不一致は fail。

リリース手順:`Devhub.Tool.csproj` の `<Version>` と `Program.cs` の `Version` 定数を上げる → commit → `git tag vX.Y.Z && git push origin vX.Y.Z`。

## 開発

```bash
dotnet build                 # ソリューション全体をビルド
dotnet test                  # 全テスト
dotnet run --project src/Devhub.Tool -- --help    # ローカル実行
```

## 配布・利用(想定運用)

ツールをプライベート NuGet フィード(GitHub Packages / Azure Artifacts)へ publish し、消費側リポジトリはローカルツールマニフェストでピン留めする(FR-2.2):

```bash
# 消費側リポジトリ
dotnet new tool-manifest                 # .config/dotnet-tools.json
dotnet tool install --local Devhub.Tool  # フィードから
# オンボーディング
dotnet tool restore && devhub apply
# CI
devhub check
```

フィード認証は env-var マクロ(`%GITHUB_TOKEN%` 等)で参照し、実値は commit しない(FR-2.3)。

## 既知の注意点

- rulesync は対象が未対応の機能(例:Copilot の `ignore`/`permissions`、Codex の `commands`)を情報表示してスキップする。ソース不在による `Failed to load ...` は features 絞り込みで抑制済みだが、ソースが存在して**中身のスキーマが不正**な場合の rulesync エラーはそのまま伝播する(存在有無のみが絞り込みの対象)。
- リモート MCP の `type` 値ドリフト(R-4)は rulesync 出力への後処理アダプタで補正する余地を残す。
- secretlint の AWS ルールは既定で Access Key ID 単体(`AKIA...`)の検出が無効(`AWS_SECRET_ACCESS_KEY=<値>` のような文脈パターンが中心)。検出強度を上げたい場合はリポジトリ側 `.secretlintrc.json` で調整する。
