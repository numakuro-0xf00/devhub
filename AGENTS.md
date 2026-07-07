# AGENTS.md

チーム共有の coding agent 指示ファイル。これは **単一ソース(source of truth)** であり、Cursor / GitHub Copilot / OpenAI Codex がネイティブに読み込む。Claude Code は AGENTS.md を直接読まないため、リポジトリの `CLAUDE.md` がこのファイルを `@AGENTS.md` でインポートしてブリッジしている(Phase 0 の暫定策)。

> 編集はこのファイルに対して行うこと。各エージェント固有ファイルは原則ここから派生させる。

## チームの技術スタック

- 言語 / ランタイム:**C# / .NET**。新規ツールは C# を積極採用する。
- ビルド・テストは標準の dotnet CLI(`dotnet build` / `dotnet test`)を用いる。
- 秘匿情報(API キー・トークン)は設定ファイルに実値で書かない。環境変数プレースホルダ(`${VAR}` など)で参照し、実値は `.env`(gitignore 済み)や OS 環境変数に置く。

## 共有設定の置き場所(Phase 0 規約)

チームで共有する agent 設定は、各エージェントがネイティブに読む標準ロケーションに置く。これにより変換ツールなしでも Cursor / Copilot / Codex には即座に効く。

| 種別 | 置き場所 | ネイティブに読むエージェント |
|---|---|---|
| skill | `.agents/skills/<name>/SKILL.md` | Cursor / Copilot / Codex(+ ブリッジ後 Claude Code) |
| 指示 | このファイル `AGENTS.md`(ルート) | Cursor / Copilot / Codex(+ `CLAUDE.md` 経由で Claude Code) |

Claude Code へは `scripts/bootstrap.sh`(WSL/Linux/macOS)または `scripts/bootstrap.ps1`(Windows)を実行して `.agents/skills/` → `.claude/skills/` をリンクする。詳細は `doc/phase0.md`。

## この仕組み自体について

本リポジトリ `devhub` は、この共有の仕組みを提供するプロジェクト。全体の要件と設計判断は `doc/requirements.md` に、Phase 0 の構成は `doc/phase0.md` にある。
