# Phase 2 — 利用状況可視化(設計)

> coding agent の hooks から利用イベントを HTTP POST で収集し、skill / subagent / MCP 単位の利用回数・最終利用日時をダッシュボード化、未使用設定の削除を推奨する。
> 位置づけ:`doc/requirements.md` の Phase 2 / FR-4。関連 NFR:NFR-1(実値を残さない)・NFR-2(匿名化)・NFR-5(独立して on/off)・NFR-7(既存 OSS 優先)。
> 本書は実装前の設計文書。調査は 2026-07 に実施(各エージェント公式ドキュメント+rulesync@9.2.0 実地検証+収集基盤 OSS 比較)。

## 全体像

```
[Claude Code]──┐ hooks(type: command)
[Cursor]───────┼──▶ devhub telemetry send ──HTTP POST──▶ [OTel Collector] ──▶ [Prometheus] ──▶ [Grafana]
[Codex CLI]────┘    (正規化・匿名化・no-op判定)   Bearer認証  (webhookevent    └─▶ [Loki]      (ダッシュボード+
                                                            receiver+OTTL変換) (生イベント)   未使用アラート)
[Claude Code transcripts]──▶ 事後解析(skill 名の補完。hooks では取得不可)──▶ 同上
[Copilot]──▶ 利用有無のみ(Billing Seats API)。設定単位は公式に不可(FR-4.4 / D-5)
```

- 送信側(センサー)は rulesync の hooks 変換に乗り、`devhub apply` で各エージェントへ配布する。
- 受信側(収集基盤)は既存 OSS の docker-compose 一式を本リポジトリに同梱し、チームの誰かが 1 箇所ホストする。既存の OTel 基盤があるチームはエンドポイント URL を差し替えるだけでよい。

## 設計判断

| # | 判断 | 根拠 |
|---|---|---|
| D-P2-1 | hooks は rulesync 変換に乗せ、**`type: "command"` のみ**使う | rulesync@9.2.0 の `type:"http"` は実測で3ターゲットとも機能しない(Codex は非生成、Claude Code / Cursor は `url` が欠落した壊れたエントリを出力するバグ)。`command` 文字列内の `${VAR}` プレースホルダは3ターゲットとも verbatim 通過を実測確認済み |
| D-P2-2 | hook command は curl ではなく **`devhub telemetry send`** を呼ぶ | Windows ネイティブ環境(NFR-4 の混在環境)で curl / openssl の存在は保証されない。正規化・匿名化・タイムアウト・失敗もみ消しを C# の 1 コマンドに閉じ込める。devhub は消費側リポジトリにローカルツールとして必ず入っている(FR-2.2) |
| D-P2-3 | 匿名化(擬似ID化)は**送信側で HMAC-SHA256** | 収集基盤側に匿名化機能は無い(調査で確認)。生の識別子をネットワークに流さない(NFR-2)。キーはチーム共有ソルト `${DEVHUB_TELEMETRY_SALT}` |
| D-P2-4 | `${DEVHUB_TELEMETRY_ENDPOINT}` **未設定なら送信せず即終了(no-op)** | NFR-5 のオフスイッチを環境変数の有無で実現。テレメトリを使わないメンバー/チームは何も設定しなければ何も起きない。hook 本体の動作は決してブロックしない |
| D-P2-5 | 収集基盤は **OTel Collector(webhookeventreceiver + bearertokenauth)→ Prometheus + Loki → Grafana** | 比較調査の第1候補。収集・保存・可視化がすべて既存 OSS で、内製コードゼロ(NFR-7)。次点だった ASP.NET 自前 API は「収集エンドポイント自体が内製」になり NFR-7 の文言に反するため見送り |
| D-P2-6 | 未使用検出は **PromQL + Grafana アラート**(設定のみ) | `increase(devhub_tool_use_total[閾値d]) == 0` で表現でき、内製はゼロ。削除は自動実行せず提案にとどめる(FR-4.3) |
| D-P2-7 | **skill 利用はトランスクリプト事後解析で補完**(Phase 2 後半) | 3エージェントとも hooks から skill 名は取得不可(Claude Code は該当 issue が not_planned でクローズ済み=恒久ギャップ)。要件書 FR-4.1 の「補助」を「skill については必須」に格上げする |
| D-P2-8 | Copilot は**利用有無のみ** | 設定単位の可視化は公式に不可能(FR-4.4 / D-5)。ダッシュボード上に制約を明示する |

## イベントスキーマ

hook から `devhub telemetry send` に stdin で渡るペイロード(各エージェント固有)を、送信側で以下に正規化して POST する:

```json
{
  "schema": 1,
  "event": "post_tool_use",
  "agent": "claudecode | cursor | codexcli",
  "tool_name": "Bash | mcp__<server>__<tool> | ...",
  "agent_type": "<subagent 名。Cursor は固定カテゴリのみ>",
  "session_id": "<HMAC-SHA256 擬似ID>",
  "user_id": "<HMAC-SHA256 擬似ID(OS ユーザー名等から生成)>",
  "timestamp": "<送信側で付与(ISO 8601 UTC)>"
}
```

- `session_id` / `user_id` は送信前に HMAC 擬似ID化する。生値(Cursor の `user_email` 含む)は送らない(NFR-2)。
- `user_id` のハッシュ元は **`Environment.UserName` を既定**とし、環境変数 `DEVHUB_TELEMETRY_USER_SEED` で明示上書きできる。WSL / Windows 混在環境(NFR-4)では OS ユーザー名が異なり同一人物が別 ID になり得るため、一致させたい場合は SEED を両環境で揃える(制約として明示)。
- `timestamp` は hook ペイロードに無いため送信側で付与する(実測で3エージェントとも欠落)。
- Prometheus のメトリクスラベルには `tool_name` / `agent` / `agent_type` のみ使い、**`session_id` / `user_id` はラベルにしない**(カーディナリティ事故防止)。生イベントは Loki のみに保持(保持期間 90 日目安。NFR-2 の保持期間義務は FR-5 のみが対象だが、本設計は FR-4 の生イベントにも自主的に同じ配慮を適用する)。

## メトリクススキーマ

Step 3 の全成果物(OTTL 変換・ダッシュボード・アラート)が参照する単一の正として先に確定する:

| メトリクス | 型 | ラベル |
|---|---|---|
| `devhub_tool_use_total` | Counter(count connector で生成) | `event`, `tool_name`, `agent`, `agent_type` |

`event` ラベル(`post_tool_use` / `skill_use` 等)で hooks 由来のツール利用とトランスクリプト由来の skill 利用を区別する。skill 利用時は `tool_name` に skill 名が入る。

当初予定していた `devhub_last_used_timestamp`(Gauge)は **Collector 内で生成できないことが実装時に判明**した(count 系コネクタはカウンタ専用)。最終利用日時は Loki(LogQL の `last_over_time`)のパネルで表現し、未使用検出は PromQL `increase(devhub_tool_use_total[閾値d]) == 0` で行う(閾値は Grafana 変数 `unused_threshold_days`、既定 30)。

## 送信コマンド `devhub telemetry send` の契約と構成

hook から同期実行されるため(Claude Code の PostToolUse はツール応答をブロックする)、以下を契約として固定する:

- **常に exit 0** で終了する(正常・異常を問わず)。hook 本体の動作に影響を与えない。
- **既定では stdout / stderr に一切出力しない**(沈黙の原則)。診断は `DEVHUB_TELEMETRY_DEBUG=1` のときだけ stderr へ出す(沈黙と可観測性の両立)。
- HTTP POST のタイムアウトは既定 **1 秒**(`DEVHUB_TELEMETRY_TIMEOUT_MS` で上書き可)。失敗・タイムアウトは黙って破棄し、リトライしない。
- `DEVHUB_TELEMETRY_ENDPOINT` 未設定なら stdin も読まず即 exit 0(D-P2-4 の no-op)。
- `DEVHUB_TELEMETRY_*` の解決は「**OS 環境変数 → リポジトリルートの `.env`**」の2段フォールバック(変数単位)。rulesync は hooks の `env` フィールドを3ターゲットとも変換時に**落とす**ことが実測で判明したため、`devhub env --fill` が書く `.env` を hook 実行時に読むこの経路が必須(テンプレートの `env` 宣言は `devhub env` の `${VAR}` 検出源としてのみ機能する)。

内部構成は Phase 1 の規約(純粋ロジック+薄い IO)に従い分割する:

- `TelemetryEventNormalizer` — エージェント固有 hook ペイロード → 共通スキーマへの正規化(純粋関数・テスト対象)
- `TelemetryAnonymizer` — HMAC-SHA256 擬似ID化(純粋関数・テスト対象)
- `TelemetrySender` — HTTP POST・タイムアウト・失敗の握りつぶし(`RulesyncRunner` 相当の薄い IO ラッパー)
- `Program.cs` の `telemetry send` はこれらを束ねるだけの薄い層とする

## 各エージェントの実測制約(2026-07)

| | Claude Code | Cursor | Codex CLI |
|---|---|---|---|
| hooks 生成先(rulesync) | `.claude/settings.json` | `.cursor/hooks.json` | `.codex/hooks.json` |
| tool_name(MCP 含む) | ○ `mcp__server__tool` | ○ | ○ |
| skill 名 | ✗(hook 非発火・恒久ギャップ) | ✗(仕様に記載なし) | ✗(提案段階) |
| subagent | ○ カスタム名 | △ 固定カテゴリのみ | ○ |
| 備考 | HTTP hook はネイティブ対応だが rulesync が変換できない(D-P2-1) | 既定 fail-open | **非管理 hook は初回に `/hooks` で信頼登録が必要**(オンボーディング手順に明記) |

## 実装計画

1. **Step 1(本書)**: 設計の確定とレビュー。
2. **Step 2a — 送信コマンド本体**: `devhub telemetry send`(上記契約と構成のとおり)。
3. **Step 2b — hooks テンプレートと env 連携**: `.rulesync/hooks.json` のテンプレート配布と `devhub env` 連携(`DEVHUB_TELEMETRY_ENDPOINT` / `_TOKEN` / `_SALT` の穴埋め)。
4. **Step 2c — lint**: `.rulesync/hooks.json` に `type:"http"` が混入していないかの検査を `devhub check` に追加(rulesync バグ回避)。
5. **Step 3 — 収集側**: `telemetry/` ディレクトリに docker-compose 一式(OTel Collector / Prometheus / Loki / Grafana、全イメージバージョンピン)、Collector 設定(webhookeventreceiver + bearertokenauth + OTTL 変換)、Grafana ダッシュボード JSON(利用回数・最終利用日時・未使用アラート、Copilot 制約の注記)。あわせて:
   - **トークンの秘匿**: Collector 設定は env 展開構文(`${env:DEVHUB_TELEMETRY_TOKEN}`)を使い、docker-compose は gitignore 済み `.env` から読む(`.env.example` に変数名のみ記載)。実値はいかなる同梱ファイルにも書かない(NFR-1)。`devhub secrets` のスキャン対象に `telemetry/` を追加し CI で機械検出する。
   - **契約テスト**: OTTL 変換は実質ロジックなので、「サンプルイベント JSON を POST → `/metrics` を scrape → 期待メトリクスを検証」する自動化スクリプトを同梱する(Phase 1 の「純粋ロジックは必ずテスト」の収集側版)。
   - **方針の外出し**: 未使用判定の閾値(既定 30 日)は Grafana アラートルールの変数として外出しし、Loki 保持期間(既定 90 日)も設定 1 箇所に集約する。
6. **Step 4 — skill 補完(実装済み)**: `devhub telemetry scan-transcripts` が Claude Code トランスクリプト(`~/.claude/projects/<encoded-cwd>/`。メインの `<sessionId>.jsonl` と `subagents/agent-*.jsonl` の両方)から skill 利用を抽出して `event: "skill_use"` で送信する。実装仕様(実機フォーマット調査にもとづく):
   - **確実なシグナルのみ計数**: `name=="Skill"` の tool_use ブロックの `input.skill`。`attachment.type=="skill_listing"`(メニュー表示)と `attributionSkill`(活性中の全行に付与)は計数に使わない。**`input.args` は会話内容のため読まない・送らない**(レダクション)。
   - **増分走査**: ファイル別バイトオフセットを `~/.devhub/telemetry-scan-state/<encoded-cwd>.json` に記録(トランスクリプトは追記専用であることを実証済み)。書き込み途中の末尾不完全行は据え置き。**初回は EOF を記録するのみで過去分を送らない**(`--backfill` で全履歴をオプトイン)。1回の実行で最大 1000 件。
   - **起動トリガー**: hooks テンプレートの claudecode オーバーライドに `sessionEnd` で統合(rulesync@9.2.0 で `SessionEnd` へ変換されることを実測)。`send` と同じ no-op スイッチ・沈黙契約。
   - **制約**: Prometheus のカウントは取り込み時刻で計上される(count connector の性質)。イベントの原時刻は Loki の本文にのみ保持されるため、`--backfill` した過去分はメトリクス上「実行時点の利用」として見える。
7. **Step 5 — Copilot(任意)**: Billing Seats API の `last_activity_at` による利用有無の取り込み。優先度低。

## リスク・注意点

- **rulesync `type:"http"` バグ**: 警告と実出力が矛盾する(上述)。将来 rulesync が修正しても、`command` 方式は動き続けるため移行は任意。R-4(rulesync 追従性)の具体事例として記録。
- **webhookeventreceiver は beta**(contrib)。Collector のイメージをバージョンピンし、更新時に設定互換を確認する(R-3 と同種)。なお contrib の lokiexporter は削除済みのため、Loki への出力は Loki 3.x ネイティブ OTLP 取り込み(`otlphttp` exporter → `/otlp`)を使う(実装済み)。
- **契約テスト(`telemetry/contract-test.sh`)は docker 必須**。本設計の作業環境には docker が無く未実行(YAML/JSON/シェルの静的検証+公式ドキュメント突き合わせのみ)。**初回デプロイ時に必ず契約テストを実行して受け口〜メトリクス生成を検証すること。**
- **Codex の信頼登録**: `devhub apply` で hooks を配布しても、各開発者が初回に `/hooks` で承認しないと動かない。オンボーディング手順に明記する。
- **認証**: bearertokenauth の静的トークンはローテーション運用をチームで決める(ファイルベース差し替え可)。収集エンドポイントはチーム内クローズドに置く(NFR-1)。
- **Cursor の subagent 粒度**: 固定カテゴリのみのため、カスタム subagent 別の集計は Claude Code / Codex に限られる。ダッシュボードに注記。
- **ネイティブ Windows のトランスクリプトパスは実機未検証**: `~/.claude/projects/` のプロジェクトパスエンコード(`/`→`-`)は WSL/Linux で実機確認済みだが、ネイティブ Windows(`C:\...`、特にドライブレターのコロンの扱い)は未検証(開発環境の Windows 側に Claude Code 利用実績が無く確認不能だった)。ネイティブ Windows 利用者が出た時点で `TranscriptProjectPathEncoder` の期待値を実機確認すること(R-3 の in-flux 項目)。
