# Phase 1 準備:変換エンジンの実地比較(Ruler vs rulesync)

> 目的:D-1(変換エンジンは既存 OSS を dotnet tool でラップ)の対象を、実際に両ツールを動かして決める。
> 方法:2026-07-07 に同等サンプル(指示 + MCP、加えて hooks/skills/subagents/commands)を食わせ、4エージェント(Claude Code / Cursor / Copilot / Codex)向けの生成物を比較。
> 実行環境:Node v22.17.0。`@intellectronica/ruler@latest`、`rulesync@9.2.0`。

## 結論:**rulesync を採用**

要件 FR-1 が変換対象に挙げる **MCP・hooks・permissions を4エージェント全てでカバー**できるのは rulesync。Ruler は rules と MCP は良質だが **hooks / permissions を生成せず、Copilot 向け MCP も出力されなかった**。FR-1 の要求範囲に対して Ruler は穴が大きい。

## カバレッジ比較(実地確認)

| 設定種別 | Ruler | rulesync | 備考 |
|---|---|---|---|
| 指示 → AGENTS.md | ✅ | ✅ | 両者とも Copilot/Cursor/Codex 共通で AGENTS.md |
| 指示 → CLAUDE.md | ✅(全文複製) | ✅(全文複製) | **両者とも既存 CLAUDE.md を上書き**(下記 caveat) |
| 指示 → .cursor/rules/*.mdc | ✖(AGENTS.md に集約) | ✅ | Ruler は Cursor がネイティブに読む AGENTS.md に寄せる方針 |
| 指示 → .github/copilot-instructions.md | ✖(AGENTS.md に集約) | ✅ | 同上 |
| MCP → .mcp.json (Claude) | ✅ | ✅ | 両者とも `type: http`/`stdio` 正しく |
| MCP → .cursor/mcp.json | ✅ | ✅ | remote の `type` 値に差(下記) |
| MCP → .vscode/mcp.json (Copilot) | **✖ 未生成** | ✅(`servers` キー) | Ruler は Copilot MCP を出さなかった |
| MCP → .codex/config.toml (TOML) | ✅ | ✅ | JSON→TOML 変換とも良好 |
| skills → .agents/skills + .claude/skills 他 | △(experimental) | ✅(4ロケーション) | rulesync は Claude ブリッジ(.claude/skills)も生成 |
| subagents | △(experimental・既定 off) | ✅(Codex は TOML 化) | |
| commands / prompts | ✖ | ✅(.claude/commands, .github/prompts 等) | |
| **hooks** | **✖** | ✅(**4形式へ実変換**) | 決定的な差 |
| **permissions** | **✖** | ✅(ignore→deny 等) | |
| CI ドリフト検出 | dry-run のみ | ✅ `--check`(差分あれば exit 1) | |
| 既存設定の取り込み | revert | ✅ `import` / `convert` | 段階移行に有利 |
| 対応エージェント数 | 33 | 20+ | Ruler の方が広いが4対象は両者カバー |
| 実装言語 | Node/TS | Node/TS | どちらも dotnet tool からはプロセス呼び出しでラップ |

### hooks 変換の実例(rulesync のみ可能)

単一ソース `.rulesync/hooks.json`(`postToolUse` / matcher `Write|Edit`)から:
- Claude Code `.claude/settings.json` → `PostToolUse` + `"$CLAUDE_PROJECT_DIR"` 展開
- Codex `.codex/hooks.json` → PascalCase `PostToolUse`
- Cursor `.cursor/hooks.json` / Copilot `.github/hooks/copilot-hooks.json`

これが §3.2 で「変換の主戦場」とした hooks の実変換。Ruler にはこの機能がない。

## 変換品質の注意点(両ツール共通・要監視)

1. **リモート MCP の `type` 値が割れる**:Ruler は Cursor/Codex に `type = "remote"`、rulesync は全て `type = "http"` を出力。どちらが各エージェントで確実に効くかは要検証で、仕様ドリフト(R-3)の典型。→ **ラッパー側で後処理・上書きできる抽象化**を持つこと。
2. **CLAUDE.md 全文上書き**:両ツールとも CLAUDE.md を生成物として全文で上書きする。devhub の CLAUDE.md には開発ガイド + `@AGENTS.md` ブリッジがあるため、そのまま任せると消える。
   → **緩和策**:rulesync は `--features` と `--targets` で機能を絞れる。**claudecode に対しては `rules` を生成対象から外し**、Phase 0 で作った `@AGENTS.md` リンク方式の CLAUDE.md を維持する。skill/mcp/hooks/subagents だけ生成させる運用が可能。

## dotnet tool でのラップ方針(Phase 1)

- **中核**:`devhub apply` は内部で `rulesync generate --targets claudecode,cursor,copilot,codexcli --features <選択>` を呼ぶ。ソースは `.rulesync/`(または devhub 独自ソースを `.rulesync/` に射影)。
- **CLAUDE.md 保護**:claudecode の `rules` 生成は除外し、Phase 0 の `@AGENTS.md` リンク CLAUDE.md を正とする。または devhub 管理ブロックのみ差し替える方式を検討。
- **CI**:`rulesync generate --check` を CI に組み込み、ソースと生成物のドリフトを検出。
- **秘匿情報**:MCP の `env` は `${VAR}` 参照のまま素通しすること(実値混入チェックは別途 gitleaks 等)。
- **ドリフト吸収**:`type` 値など各エージェント固有の癖は、rulesync 出力に対する後処理アダプタで補正できる形にする(R-4)。差し替え(将来 Ruler や自前生成へ)も可能な抽象境界を保つ。

## 再現手順

```bash
# Ruler
mkdir -p r/.ruler && cd r
printf '# ...\n' > .ruler/instructions.md
# .ruler/ruler.toml に [mcp_servers.*]
npx -y @intellectronica/ruler@latest apply --agents claude,cursor,copilot,codex

# rulesync
mkdir rs && cd rs && npx -y rulesync@latest init
# .rulesync/ を編集
npx -y rulesync@latest generate --targets claudecode,cursor,copilot,codexcli --features '*'
```
