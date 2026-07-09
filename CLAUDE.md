# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 共有チーム規約(AGENTS.md ブリッジ)

Claude Code は AGENTS.md をネイティブに読まないため、チーム共有の指示(単一ソース)をここでインポートする(Phase 0 の暫定策)。

@AGENTS.md

共有スキルは `.agents/skills/` が単一ソース。Claude Code で使うには `scripts/bootstrap.sh`(Windows は `scripts/bootstrap.ps1`)を実行して `.claude/skills/` にリンクする。詳細は `doc/phase0.md`。

## プロジェクトの目的

devhub は、プライベートな開発チーム内で coding agent の設定(skill、agent、MCP サーバー設定など)を共有するための仕組みを提供するプロジェクト。チームメンバーが各自の環境に散在させがちなエージェント設定を、一元管理・配布できるようにすることがゴール。

## 技術方針

- 開発メンバーの技術スタックは C# / .NET。ツールを新規に作る場合は C# を積極的に採用する。
- LICENSE は Apache 2.0。
- `.gitignore` は .NET プロジェクト用に整備済み(`bin/`、`obj/`、`*.nupkg`、テスト結果などを除外)。

## 現在の状態

Phase 0(共有基盤)と Phase 1 の dotnet tool スケルトンが実装済み。

- `src/Devhub.Tool/` — `devhub` dotnet tool。rulesync をラップして各エージェント設定を生成(`devhub apply` / `check`)。中核は `RulesyncPlanner`(純粋ロジック・テスト対象)。
- `tests/Devhub.Tool.Tests/` — xUnit テスト。
- `AGENTS.md` / `.agents/skills/` — 共有設定の単一ソース(Phase 0)。
- `doc/` — `requirements.md`(要件・設計判断)、`phase0.md`、`phase1.md`、`phase1-tool-eval.md`(Ruler vs rulesync 比較)。

要件は `doc/requirements.md` に整理済み(4エージェント調査+確定した設計判断にもとづく)。着手前に必ず参照すること。要点:
- 中核は「単一ソース git リポジトリ → dotnet tool でエージェント別ネイティブ設定を生成・配置」。変換エンジンは**既存 OSS(Ruler / rulesync)を dotnet tool でラップ**し、内製はチーム固有の接着に限定(既存 OSS 優先・内製最小化=NFR-7)。
- skill と AGENTS.md は業界標準化済みで共通化できるが、**Claude Code だけ AGENTS.md 非対応**。`CLAUDE.md` → `AGENTS.md` のリンク方式で一時対応(skill は `.claude/skills/` へ複製)。
- MCP / hooks / permissions は4エージェントで形式が全く異なり、ここが変換層の主戦場。
- 秘匿情報は実値を配布・生成物・ログに残さず `${VAR}` プレースホルダで分離。
- 利用状況は `type: http` hook で収集(既存 OTel/OSS 基盤を流用)。ユーザーID は匿名化。ただし **Copilot は設定単位の可視化が公式に不可能**(利用有無まで)。
- チャットログ集積+レコメンド(FR-5)は**後回し**(Phase 3)。保存前レダクション+匿名化が前提の独立モジュール。

## 開発コマンド

```bash
dotnet build                 # ソリューション全体をビルド
dotnet test                  # 全テスト実行
dotnet test --filter "FullyQualifiedName~<テスト名>"   # 単一テストの実行
dotnet run --project src/Devhub.Tool -- --help         # ツールをローカル実行
```

## C# コードナビゲーション

この環境では `roslyn-query` skill が利用できる。C# ソリューション内の定義ジャンプ、参照検索、コール階層、コンパイル診断が必要なときは grep より優先して使うこと。
