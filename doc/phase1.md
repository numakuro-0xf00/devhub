# Phase 1 — 配布ツール(dotnet tool スケルトン)

> `devhub` dotnet tool が rulesync をラップし、単一ソース `.rulesync/` から4エージェント(Claude Code / Cursor / Copilot / Codex)のネイティブ設定を生成する。
> 位置づけ:`doc/requirements.md` の Phase 1 / D-1。変換ロジックは内製せず rulesync に委譲(NFR-7)。ツール選定の根拠は `doc/phase1-tool-eval.md`。

## 現状(スケルトン)

実装済み・検証済みの範囲:

- `devhub apply` — `.rulesync/` から各エージェント設定を生成。
- `devhub check` — 生成物がソースと一致するか検査(rulesync `--check`、ドリフト時 exit 1)。CI 向け。
- `devhub --version` / `--help`。
- **CLAUDE.md 保護**(既定 ON)— 後述。実バイナリで 2 パス動作を確認済み。
- Node/npx 不在時の明示エラー(exit 3)。

未実装(今後):秘匿情報の混入チェック統合、`.rulesync/` に存在する機能だけを features に絞る最適化、プライベート NuGet フィードへの publish パイプライン。

## プロジェクト構成

```
devhub.sln
src/Devhub.Tool/
    Devhub.Tool.csproj      # PackAsTool, ToolCommandName=devhub, net8.0
    Program.cs              # CLI ディスパッチ(apply/check/--version/--help)
    RulesyncPlanner.cs      # apply を rulesync 呼び出し列へ展開する純粋ロジック(テスト対象)
    RulesyncRunner.cs       # rulesync を子プロセス実行する薄いラッパー
tests/Devhub.Tool.Tests/    # RulesyncPlanner のユニットテスト(xUnit)
```

## CLAUDE.md 保護(要件 R-6)

rulesync も Ruler も、rules 生成時に **CLAUDE.md を全文で上書き**する。それでは Phase 0 で用意した `@AGENTS.md` リンク方式の CLAUDE.md(開発ガイド + ブリッジ)が消える。

そこで `devhub apply` は rulesync を **2 パス**に分けて呼ぶ:

1. `--targets cursor,copilot,codexcli --features <全機能>` — rules を含め生成。
2. `--targets claudecode --features <rules を除く>` — CLAUDE.md には触れない。

これにより Claude Code 向けの skill/MCP/hooks 等は生成しつつ、CLAUDE.md は Phase 0 の内容を正として保つ。`--no-protect-claude` で無効化できる(claudecode にも rules を生成)。

判断ロジックは `RulesyncPlanner.BuildApplyPlan` に純粋関数として実装し、ユニットテストで境界(claudecode 単独 / rules 非生成 / 保護 OFF 等)を固定している。

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

- rulesync は対象が未対応の機能(例:Copilot の `ignore`/`permissions`、Codex の `commands`)を情報表示してスキップする。害はないが、`.rulesync/permissions.json` 等が無いと `Failed to load ...` が出る。将来、存在する機能だけ features に渡す最適化で抑制する。
- リモート MCP の `type` 値ドリフト(R-4)は rulesync 出力への後処理アダプタで補正する余地を残す。
