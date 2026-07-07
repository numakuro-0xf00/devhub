# Phase 0 — 共有の最小形

> ゴール:**変換ツールなしで回る共有の土台**を作る。skill と指示(AGENTS.md)を標準ロケーションに置き、Claude Code だけリンク方式でブリッジする。配布は「直接 git commit」。
> 位置づけ:`doc/requirements.md` の Phase 0。Phase 1 でこの手動ブリッジを dotnet tool が自動化する。

## 何がどう効くか

| 設定 | 置き場所(単一ソース) | Cursor / Copilot / Codex | Claude Code |
|---|---|---|---|
| skill | `.agents/skills/<name>/SKILL.md` | ✅ ネイティブに読む(設定不要) | ⚙️ `bootstrap` で `.claude/skills/` にリンク |
| 指示 | `AGENTS.md`(ルート) | ✅ ネイティブに読む(設定不要) | ⚙️ `CLAUDE.md` が `@AGENTS.md` でインポート |

- **Cursor / Copilot / Codex**:`.agents/skills/` と `AGENTS.md` をコミットするだけで即座に効く。追加設定ゼロ。
- **Claude Code**:AGENTS.md 非対応・skill は `.claude/skills/` を参照するため、2つのブリッジを張る:
  1. **指示** → `CLAUDE.md` に `@AGENTS.md` の import 行(コミット済み・常時有効)。
  2. **skill** → `bootstrap` スクリプトで `.claude/skills/<name>` を `.agents/skills/<name>` にシンボリックリンク。

## リポジトリ構成(Phase 0 該当分)

```
AGENTS.md                                  # 共有指示(単一ソース)。3エージェントがネイティブに読む
CLAUDE.md                                  # Claude Code 用。@AGENTS.md を import + devhub 開発ガイド
.agents/skills/
    authoring-shared-skills/SKILL.md       # 共有スキルの例(全エージェント対応)
.claude/skills/                            # ブリッジ生成物(gitignore)。bootstrap が作る
scripts/
    bootstrap.sh                           # Claude Code ブリッジ(WSL/Linux/macOS)
    bootstrap.ps1                          # Claude Code ブリッジ(Windows/PowerShell)
doc/
    requirements.md                        # 全体要件・設計判断
    phase0.md                              # 本書
```

`.claude/skills/` はブートストラップで再生成できるため gitignore している(実体は `.agents/skills/` が単一ソース)。

## オンボーディング(メンバーがやること)

```bash
git clone <repo>
cd <repo>

# Claude Code を使う場合のみブリッジを実行
./scripts/bootstrap.sh          # WSL / Linux / macOS
pwsh ./scripts/bootstrap.ps1    # Windows
```

- Cursor / Copilot / Codex しか使わないなら、clone だけで完了(ブートストラップ不要)。
- Windows で symlink 権限が無い場合、`bootstrap.ps1` は複製にフォールバックする(開発者モード有効化を推奨)。

## 共有スキルを増やす

`.agents/skills/<name>/SKILL.md` を追加し、`bootstrap` を再実行するだけ。書き方は共有スキル `authoring-shared-skills`(このリポジトリに同梱)が案内する。

## Phase 0 の割り切り(既知の制約)

- 対象は **skill と指示(AGENTS.md)** のみ。MCP / hooks / permissions の共有は **Phase 1**(変換ツール)で扱う。
- Claude Code のブリッジは手動(`bootstrap` 実行)。Phase 1 で dotnet tool が自動化する。
- 複製フォールバック時はドリフトし得るため、更新後は `bootstrap` 再実行が必要。

## Phase 1 への接続

Phase 1 では、この `bootstrap` が行う「配置・ブリッジ」を C# 製 dotnet tool が引き継ぐ。tool は既存 OSS(Ruler / rulesync)をラップして MCP/hooks/permissions の変換まで担い、オンボーディングを `dotnet tool restore && <tool> apply` に一本化する(`doc/requirements.md` D-1 / §8)。Phase 0 のディレクトリ規約(`.agents/skills/`・`AGENTS.md` 単一ソース)はそのまま Phase 1 の入力になる。
