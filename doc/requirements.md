# devhub 要件定義

> チーム内で coding agent の設定(skill / subagent / MCP / AGENTS.md / hooks / permissions)を共有・配布・可視化する仕組み。
> 本書は 2026-07-07 時点の各エージェント公式ドキュメント調査(3スレッド)にもとづく要件整理。設計・実装の前段。

---

## 1. 背景と目的

チームメンバーが各自の環境に散在させている coding agent の設定を、一元管理して配布・運用できるようにする。狙いは3つ。

1. **共有** — 良い設定(skill・subagent・MCP・ルール・hooks・permissions)を1箇所で管理し、全員・全リポジトリに行き渡らせる。
2. **省力化** — 新メンバーのオンボーディングと日々の同期を「ほぼゼロ手間」にする。秘匿情報は共有せず、初期化スクリプト/スキルで穴埋めの手間を最小化する。
3. **最適化** — 実際の利用状況を可視化し、使われていない共有設定を検出して削除を推奨する。さらにチャット内容を集積し、新しいツール(skill 等)作成のレコメンドを自動化する。

チームの技術スタックは **C# / .NET**。新規に作るツールは C# を積極採用する。

---

## 2. スコープ

### 対象エージェント

| エージェント | 位置づけ |
|---|---|
| **Claude Code** (Anthropic) | 主要。プラグイン機構・hooks・OTel が最も充実。ただし AGENTS.md 非対応・CLI のプラグイン自動インストール非対応という癖あり |
| **Cursor** | 主要。Team Rules(ダッシュボード集中管理)・hooks・プラグインあり |
| **GitHub Copilot** | 対応。ただし**利用状況の設定単位での可視化は不可**(後述)。BYOK/completions に制約 |
| **OpenAI Codex** | 対応。`requirements.toml` による組織強制が4者中最も強力 |

### 共有対象の設定種別

skill / subagent(agent) / MCP サーバー設定 / 指示ファイル(AGENTS.md・CLAUDE.md・rules)/ hooks / permissions。

### スコープ外(現時点)

- 秘匿情報(API キー・トークン)そのものの配布 — **明示的に共有しない**。プレースホルダと初期化で代替。
- 各エージェント本体のインストール・ライセンス管理。

---

## 3. 調査で判明した重要な前提(設計を縛る事実)

要件の実現可能性を左右する、確定した事実。詳細な出典は付録参照。

### 3.1 skill と AGENTS.md は既に業界標準化済み

- **Agent Skills 標準**(agentskills.io、Anthropic 発、2025-12 標準化)を4エージェント全てが採用済み。`SKILL.md` は共通フォーマット。
- Codex / Copilot / Cursor は共通ディレクトリ **`.agents/skills/`(プロジェクト)/ `~/.agents/skills/`(ユーザー)** を読む。**skill は「1回書けば3エージェントで動く」状態**。
- **AGENTS.md** も標準化済み(Linux Foundation 傘下 AAIF がステュワード)。Cursor / Copilot / Codex はネイティブ対応。
- **例外は Claude Code のみ**。skill は自前の `.claude/skills/` しか読まず、指示は `CLAUDE.md` のみで **AGENTS.md 非対応**。
  → **ブリッジが必須**:`CLAUDE.md` 内で `@AGENTS.md` をインポート、または `ln -s AGENTS.md CLAUDE.md`。skill は `.claude/skills/` へ複製 or シンボリックリンク。

### 3.2 MCP / hooks / permissions は4者4様 → ここが変換ツールの主戦場

| 種別 | Claude Code | Cursor | Copilot | Codex |
|---|---|---|---|---|
| MCP | `.mcp.json`(`mcpServers`) | `.cursor/mcp.json`(`mcpServers`) | `.vscode/mcp.json`(`servers`!) | `config.toml`(`[mcp_servers.*]`) |
| hooks | `settings.json` の `hooks`(JSON) | `.cursor/hooks.json` | `.agent.md`/CLI | `config.toml`/`hooks.json`(TOML) |
| permissions | `settings.json` の `permissions` | `.cursor/permissions.json` 等 | `settings.json` | `config.toml`(`approval_policy`等) |

キー名も配置もフォーマットも異なる。**単一ソース → 各形式へ生成**する変換層が必要。

### 3.3 全カバーは「単一ソース + ジェネレータ」だけ

4エージェント全てを1入力からカバーできる方式は「共通ソースを持ち各エージェントのネイティブファイルを生成する」型のみ。先行 OSS に **Ruler**(`.ruler/` → 30形式、MCP対応)と **rulesync**(`.rulesync/` → 20+形式、4エージェント+MCP対応)があり、いずれも4対象を実証済み。ただし両者とも Node/TS 製。

### 3.4 秘匿情報はプレースホルダで分離可能

全エージェントが何らかの環境変数展開に対応。commit するのは参照だけ、実値は commit しない。

| エージェント | 展開構文 | 備考 |
|---|---|---|
| Claude Code | `${VAR}` / `${VAR:-default}` | `.env` ローダなし。`claude mcp add` が実値を書き戻すバグあり → **要 CI チェック** |
| Cursor | `${env:VAR}` | 第一級の `envFile`(stdio 限定)あり |
| Copilot(VS Code) | `${input:id}` / `${env:VAR}` | **`inputs` で初回プロンプト → OS キーチェーン保存**(最強) |
| Codex | 展開なし。`env_vars=[...]` / `bearer_token_env_var` で env 転送 | `[env]` テーブルは展開されない(秘匿を置かない) |

### 3.5 利用状況テレメトリ:Copilot だけ設定単位の可視化が不可能

- **Claude Code / Cursor / Codex**:hooks(`PostToolUse` 等、`type: http` で直接 POST 可能)が最もクリーンなシグナル。`tool_name` に MCP(`mcp__server__tool`)・skill(`Skill`)、`agent_type` に subagent 名が乗る。
- **Copilot**:公式 API(Metrics API / Billing Seats API)は**組織/チーム集計・日次**まで。どの skill / instructions / MCP が使われたかの**設定単位の内訳は取得不可**。→ Copilot の「未使用設定検出」要件は**公式には実現不可**。ローカルの `state.vscdb` を掘る非公式手段のみ(脆弱)。

### 3.6 チャットログ集積は法務ゲートを伴う

従業員のチャット内容を集積・保存する行為は、EU 圏では労使協議会(独 BetrVG §87)の同意や GDPR の DPIA を要する。チャットログには**コード断片・認証情報が混入する**(Samsung の事例)。→ **保存前のレダクション(Presidio / gitleaks / TruffleHog)と法務承認が前提条件**。要件2(秘匿情報は共有しない)と直接緊張するため、方針の明文化が必須。

---

## 4. 機能要件

### FR-1 設定の共有(単一ソース管理)

- **FR-1.1** チーム共有設定を単一のプライベート git リポジトリ(source of truth)で管理する。
- **FR-1.2** ソースから各エージェントのネイティブ設定を生成する:
  - skill → `.agents/skills/`(共通)+ Claude Code 用 `.claude/skills/` ブリッジ
  - 指示 → `AGENTS.md`(共通)+ Claude Code 用 `CLAUDE.md` は **`AGENTS.md` へのリンク方式で一時対応**(`CLAUDE.md` から `@AGENTS.md` をインポート、またはシンボリックリンク)。Claude Code が AGENTS.md をネイティブ対応したら解消する暫定策。
  - MCP → 各エージェントの MCP 設定ファイル(§3.2 の4形式)
  - hooks / permissions → 各エージェント形式へ変換(対応可能な範囲で)
  - subagent → `.claude/agents/`・各エージェント相当先
- **FR-1.3** 生成は決定的(LLM 非依存)で、再実行しても差分が出ない冪等性を持つ。
- **FR-1.4** 共有スコープを「全員必須」「推奨(任意導入)」「チーム/リポジトリ限定」で区別できる。

### FR-2 配布と初期化(省力オンボーディング)

- **FR-2.1** 配布ツールを **C# / dotnet tool** として提供する。プライベート NuGet フィード(GitHub Packages or Azure Artifacts)経由で配布。変換・生成の中核ロジックは**既存 OSS(Ruler もしくは rulesync)を dotnet tool からラップ**して用い、内製は「チーム固有の接着(設定配置・ブリッジ・初期化・呼び出し)」に限定する(NFR-7)。
- **FR-2.2** ローカルツールマニフェスト(`.config/dotnet-tools.json`)にバージョンをピン留めし、オンボーディングを **`git clone && dotnet tool restore && <tool> apply`** に集約する。
- **FR-2.3** フィード認証情報は env-var マクロ(`%GITHUB_TOKEN%` 等)で参照し、生の PAT を commit しない。
- **FR-2.4** **初期化スキル/スクリプト**を提供し、秘匿情報の穴埋めを支援する:
  - 必要な環境変数(`.env` / OS 環境)を一覧化し、未設定を検出して対話的に案内。
  - `.env.example` を配布、実 `.env` は gitignore。
  - `${VAR}` プレースホルダを含む MCP 設定を各エージェント向けに配置。
- **FR-2.5** 更新伝播:ソース更新 → メンバーは `git pull && dotnet tool restore && <tool> apply` で収束。差分プレビューを提供。

### FR-3 秘匿情報の非共有

- **FR-3.1** 生成・配布される設定ファイルには**秘匿情報の実値を一切含めない**。参照(プレースホルダ)のみ。
- **FR-3.2** commit 前フックで実値混入を検出(§3.4 の Claude Code 書き戻しバグ対策を含む)。
- **FR-3.3** Copilot/VS Code 利用者には `inputs`+OS キーチェーン方式を推奨として提示。

### FR-4 利用状況の可視化と未使用設定の整理

- **FR-4.1** 各エージェントの利用イベントを収集する:
  - Claude Code / Cursor / Codex:**`type: http` hook** で `{tool_name, agent_type, skill 名, session_id, timestamp}` を収集エンドポイントへ POST。ユーザー識別子は**匿名化(擬似ID化)**して送る(NFR-2)。
  - 補助:Claude Code の `~/.claude/projects/**/*.jsonl`(既定30日で消えるため期限内に回収)、OTel logs チャネル。
  - 収集基盤は既存 OSS を優先(OTel + Prometheus/Grafana、`claude-code-otel` 等)。内製は集計・未使用検出ロジックに限定(NFR-7)。
- **FR-4.2** 収集イベントを集計し、skill / subagent / MCP サーバー / hooks 単位の**利用回数・最終利用日時**をダッシュボード化する。
- **FR-4.3** 一定期間(閾値は設定可能)利用実績のない共有設定を**未使用として検出し、共有から外す/削除を推奨**する。削除は自動実行せず提案にとどめる。
- **FR-4.4** **制約の明示**:Copilot は設定単位の利用データが公式に取得できない。Copilot 分は「利用有無(Billing Seats API の `last_activity_at`)」までしか可視化できないことを要件・UI 上で明示する。

### FR-5 チャットログ集積と自動レコメンド 【後回し / 将来フェーズ】

> 本機能は初期スコープから外し、Phase 3(将来)で扱う。要件は残すが MVP では実装しない。

- **FR-5.1** チャット/会話内容をクローズドなサーバー/DB に集積する。取得元:
  - ローカルトランスクリプト(Claude Code `.jsonl`、Codex rollout JSONL、Cursor `state.vscdb`)、
  - または自前 LLM ゲートウェイ(LiteLLM 等、base-URL override)経由のリクエスト/レスポンス。集積・分析基盤は既存 OSS(LiteLLM 等)を優先(NFR-7)。
- **FR-5.2** **保存前に必ずレダクション+匿名化を通す**:秘匿情報(gitleaks / TruffleHog)・PII(Presidio)を除去し、ユーザー識別子を匿名化する。生ログを無加工で保存しない(NFR-2)。
- **FR-5.3** 集積データを分析し、繰り返し現れるタスク/課題パターンから**新しい skill / subagent / MCP の作成候補を自動レコメンド**する。
- **FR-5.4** プライベートチームのため重い法務ゲートは想定しないが、**匿名化とレダクションは必須**とする。本機能はコア(FR-1〜4)から独立した、無効化可能なモジュールとする(NFR-5)。

---

## 5. 非機能要件

- **NFR-1 セキュリティ**:秘匿情報は配布経路・生成物・ログのいずれにも実値を残さない。収集エンドポイントは認証必須・チーム内クローズド。
- **NFR-2 プライバシー/匿名化**:プライベートチームのため重い法務ゲート(DPIA・労使協議会同意)は前提としないが、**利用状況(FR-4)・チャットログ(FR-5)ともユーザー識別子を匿名化(擬似ID化)**して扱う。FR-5 は加えて保存前レダクション・保存期間制限(60〜90日目安)・アクセス制御を必須とする。
- **NFR-3 冪等性/決定性**:生成は再実行安全。手編集との衝突を検出できる。
- **NFR-4 可搬性**:Windows / WSL / Linux / macOS で動作(WSL からの Windows 側 Cursor 参照など混在環境を想定)。
- **NFR-5 段階的無効化**:テレメトリ(FR-4)・ログ集積(FR-5)は共有・配布(FR-1〜3)と独立して on/off できる。プライバシー機能を切っても中核が動く。
- **NFR-6 追従性**:各エージェント仕様は月単位で変化する(§7)。変換層はエージェント別アダプタとして分離し、差分追従を局所化する。
- **NFR-7 既存 OSS 優先・内製最小化**:無料で利用できる既存ツール/OSS を優先採用する。一般的な分野(変換エンジン・テレメトリ収集・ログ集積・レダクション)は内製せず既存を流用し、内製は**チーム固有の接着**(dotnet tool ラッパー、設定配置・ブリッジ・初期化、集計・未使用検出、レコメンド)に限定する。

---

## 6. アーキテクチャ方針(推奨)

調査結論から、以下の構成を推奨する。設計フェーズで確定する。

```
[単一ソース git リポジトリ]  ソース: skills / AGENTS.md / MCP / hooks / permissions(${VAR} 参照のみ)
        │
        │  dotnet tool "<devhub> apply"  ← 既存 OSS(Ruler/rulesync)をラップ + チーム固有の接着
        ▼
[各エージェントのネイティブ設定]  .agents/skills, .claude/*, .cursor/*, .vscode/mcp.json, config.toml, AGENTS.md/CLAUDE.md(→AGENTS.md リンク)
        │
        ├─ 初期化スキル(env 穴埋め・.env ブートストラップ)
        │
        ├─ hooks(type:http)──▶ [OTel/収集基盤(既存 OSS)] ──▶ [利用状況ダッシュボード] ──▶ 未使用検出・削除推奨(内製)
        │                                                        ※ユーザーID は匿名化
        │
        └─【後回し/Phase 3】ローカルトランスクリプト or LLM ゲートウェイ(LiteLLM 等)
                                    │
                              [レダクション+匿名化: gitleaks/TruffleHog/Presidio]
                                    ▼
                          [クローズド DB] ──▶ レコメンドエンジン(新 skill 候補提示)
```

- **中核(FR-1〜3)**:変換・生成は既存 OSS(Ruler もしくは rulesync)を dotnet tool から**ラップ**して用いる。内製はチーム固有の接着(ラッパー・設定配置・Claude Code ブリッジ・初期化)に限定(NFR-7)。エージェント別の差分はアダプタで吸収(NFR-6)。
- **強制が必要なガードレール**は各エージェントのネイティブ集中管理を併用:Codex `requirements.toml`、Cursor Team Rules。Claude Code は managed-settings で値を強制できるが CLI はプラグイン自動インストール非対応のため初回のみ手動 or entrypoint 自動化。
- **テレメトリ(FR-4)**:hooks(http)中心。収集/ダッシュボードは既存 OSS(OTel + Grafana、`claude-code-otel` 等)を流用し、集計・未使用検出のみ内製。ユーザーID は匿名化。
- **ログ集積+レコメンド(FR-5)**:後回し(Phase 3)。独立モジュール。集積基盤は LiteLLM 等を流用。レダクション+匿名化必須。

---

## 7. 確定した設計判断 / 残リスク

### 確定した判断(2026-07-07)

| # | 判断 | 内容 |
|---|---|---|
| D-1 | 変換エンジンは**既存 OSS をラップ**(**rulesync** 採用) | C# 内製せず、**rulesync** を dotnet tool からラップ。実地比較(`doc/phase1-tool-eval.md`)で、FR-1 が要求する MCP・hooks・permissions を4エージェント全てでカバーできるのは rulesync と確認(Ruler は hooks/permissions 非対応・Copilot MCP 未生成)。内製はチーム固有の接着に限定(NFR-7) |
| D-2 | **既存 OSS を最優先・内製最小化** | 一般分野(変換・テレメトリ収集・ログ集積・レダクション)は既存/無料 OSS を流用(NFR-7) |
| D-3 | FR-5(ログ集積・レコメンド)は**後回し** | 初期スコープ外。Phase 3 で扱う。要件は残す |
| D-4 | Claude Code の AGENTS.md 対応は**リンク方式で一時解決** | `CLAUDE.md` → `AGENTS.md` の import/シンボリックリンク。ネイティブ対応まで暫定 |
| D-5 | Copilot 可視化制約は**割り切る** | 設定単位の利用データは取れない。利用有無まで(FR-4.4) |
| D-6 | プライバシーは**匿名化で対応** | プライベートチームのため重い法務ゲートは不要。利用状況・ログとも匿名化(NFR-2) |

### 残リスク

| # | 項目 | 内容 | 対応方針 |
|---|---|---|---|
| R-1 | Copilot 可視化不可 | 設定単位の利用データが公式 API にない | 利用有無まで割り切る(D-5・FR-4.4) |
| R-2 | 秘匿混入 | ログ/利用イベントにコード/鍵が混入し得る | 保存前レダクション+匿名化必須(FR-5.2・NFR-2) |
| R-3 | 仕様追従コスト | 4エージェントとも月単位で変化(プラグイン自動更新バグ、Cursor Organizations 成熟途上、Copilot BYOK 制約 等) | アダプタ分離(NFR-6)。付録 in-flux 項目を定期再検証 |
| R-4 | ラップ対象 OSS(rulesync)の追従性 | rulesync の対応形式・更新に依存。リモート MCP の `type` 値など各エージェント固有の癖で出力がずれ得る(Ruler は `remote`、rulesync は `http`) | 出力に対する後処理アダプタで補正。差し替え可能な抽象境界を保つ(`doc/phase1-tool-eval.md`) |
| R-6 | 生成物が CLAUDE.md を上書き | rulesync/Ruler とも CLAUDE.md を全文で上書きし、Phase 0 の `@AGENTS.md` ブリッジ+開発ガイドを消す | claudecode の `rules` 生成を除外し Phase 0 の CLAUDE.md を正とする(`doc/phase1-tool-eval.md`) |
| R-5 | Claude Code の癖 | CLI プラグイン自動インストール非対応・`mcp add` 書き戻しバグ | entrypoint 自動化・CI チェックで吸収 |

---

## 8. 段階的実装計画(案)

- **Phase 0 — 共有の最小形**:単一ソース + skill(`.agents/skills`)+ AGENTS.md + Claude Code リンク方式ブリッジ。直接 git commit で配布。変換ツールなしでも回る土台。
- **Phase 1 — 配布ツール**:既存 OSS(Ruler/rulesync)を dotnet tool でラップ。MCP/hooks/permissions の変換。初期化スキル(秘匿情報の穴埋め)。`dotnet tool restore && apply` のオンボーディング確立。
- **Phase 2 — 利用状況可視化**:http hooks → 既存収集基盤(OTel/OSS)→ ダッシュボード。未使用設定の検出・削除推奨(内製)。ユーザーID 匿名化。Copilot は利用有無まで。
- **Phase 3 — ログ集積・レコメンド【後回し】**:レダクション+匿名化 → クローズド DB → レコメンドエンジン。独立モジュール。初期スコープ外。

---

## 付録:主要な出典

- Agent Skills 標準:agentskills.io / code.claude.com/docs/en/skills
- AGENTS.md 標準:agents.md(AAIF / Linux Foundation)
- Claude Code:code.claude.com/docs/en/{skills,sub-agents,mcp,hooks,settings,plugins-reference,plugin-marketplaces,monitoring-usage,llm-gateway}
- Cursor:cursor.com/docs/{context/rules,context/mcp,hooks,skills} / cursor.com/docs/account/teams/{admin-api,analytics-api}
- Copilot:docs.github.com/copilot(custom-instructions, custom-agents, mcp)/ Copilot Metrics API・Billing Seats API
- Codex:developers.openai.com/codex/{skills,mcp,config-reference,enterprise/managed-configuration}
- 配布:dotnet tool(learn.microsoft.com)/ Ruler(github.com/intellectronica/ruler)/ rulesync(github.com/dyoshikawa/rulesync)
- テレメトリ/レダクション:ccusage / claude-code-otel / LiteLLM / Microsoft Presidio / gitleaks / TruffleHog
- プライバシー:BetrVG §87、GDPR(DPIA)

> in-flux(要定期再検証):Claude Code プラグイン自動更新の不具合、managed-settings の CLI 自動インストール非対応(#45323 not-planned)、Cursor Organizations(2026-06 出荷・成熟途上)、Copilot org instructions の IDE 非適用・MCP allowlist が soft、Copilot BYOK が completions 非対応、Codex hooks/OTel の網羅範囲。
