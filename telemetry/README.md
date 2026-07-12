# devhub telemetry 収集基盤

`devhub telemetry send`(および将来の `devhub telemetry scan-transcripts`)が送るイベントを受信し、
Prometheus + Loki + Grafana で可視化する基盤。設計は `doc/phase2.md` を参照(本ディレクトリはその Step 3)。

構成: OTel Collector(webhookeventreceiver + bearertokenauth 認証)→ transform processor(OTTL)で
JSON body をパース → count connector でメトリクス化 → Prometheus。生イベント全文(session_id / user_id を
含む)は otlphttp exporter で Loki へ。

## 起動手順

1. `.env` を用意する(コミットしない。`.gitignore` の `.env` パターンで無視される)。

   ```bash
   cp telemetry/.env.example telemetry/.env
   # telemetry/.env を編集し、DEVHUB_TELEMETRY_TOKEN と
   # DEVHUB_TELEMETRY_GRAFANA_ADMIN_PASSWORD に実値を設定する
   ```

2. 起動する。

   ```bash
   cd telemetry
   docker compose up -d
   ```

3. 確認する。

   - Grafana: http://localhost:3000 (admin / `.env` に設定したパスワード) — ダッシュボード `devhub` フォルダ配下の
     「devhub telemetry — 利用状況可視化」
   - Prometheus: http://localhost:9090
   - Collector の生メトリクス: http://localhost:8889/metrics
   - Loki API: http://localhost:3100

4. 停止する。

   ```bash
   docker compose down       # データを残す(named volume)
   docker compose down -v    # データも含めて完全に削除
   ```

## 送信側(devhub telemetry send)の設定

チームメンバー(消費側リポジトリ)は環境変数で送信先を指定する(`doc/phase2.md` 参照)。

| 変数 | 値 |
|---|---|
| `DEVHUB_TELEMETRY_ENDPOINT` | `http://<この基盤をホストするマシン>:8088/events` |
| `DEVHUB_TELEMETRY_TOKEN` | この基盤の `.env` の `DEVHUB_TELEMETRY_TOKEN` と同じ値 |
| `DEVHUB_TELEMETRY_SALT` | チーム共有の匿名化ソルト(任意の値。全員で揃える) |

`DEVHUB_TELEMETRY_ENDPOINT` が未設定なら `devhub telemetry send` は何もせず即終了する(オフスイッチ。
`doc/phase2.md` D-P2-4)。テレメトリを使わないメンバー/チームはこの変数を設定しなければ何も送信されない。

受信側の URL パスは固定で `/events`(`telemetry/otel-collector/config.yaml` の `receivers.webhookevent.path`)。
認証は `Authorization: Bearer <DEVHUB_TELEMETRY_TOKEN>` ヘッダ(`devhub telemetry send` が自動的に付与する)。

## 契約テストの実行方法

```bash
./telemetry/contract-test.sh
```

Docker / Docker Compose v2 が必要。`telemetry/.env` が無ければテスト専用の一時値で自動生成し、終了時に削除する
(既存の `.env` があればそれを使い、内容は変更しない)。`docker compose up -d` → サンプルイベントを
Bearer 付きで POST → `/metrics` に `devhub_tool_use_total` が期待ラベルで現れることを確認 →
認証なし/誤トークンの POST が拒否されることを確認 → `docker compose down -v` まで自動で行う。

## 既存の OTel 基盤があるチーム向けの差し替えポイント

自前の OTel Collector / Prometheus / Loki / Grafana(または Grafana Cloud 等の SaaS)が既にある場合、
本ディレクトリ一式を丸ごと使う必要はない。差し替えるべき最小の点は以下の3つ:

1. **受信エンドポイント**: `telemetry/otel-collector/config.yaml` の `receivers.webhookevent` 設定
   (`webhookeventreceiver` + `path: /events` + `auth: authenticator: bearertokenauth`)を、既存の
   Collector パイプラインに1レシーバーとして追加するだけでよい。認証拡張(`bearertokenauth`)と
   トークンの env 展開(`${env:DEVHUB_TELEMETRY_TOKEN}`)もそのまま流用できる。
2. **メトリクス変換**: `processors.transform/devhub`(OTTL)と `connectors.count` の
   `devhub_tool_use_total` 定義(`doc/phase2.md` の「メトリクススキーマ」が単一の正)をそのまま
   既存パイプラインの `logs` パイプラインに差し込める。
3. **チームメンバー側の設定**: 各メンバーは `DEVHUB_TELEMETRY_ENDPOINT` を既存基盤の受信 URL に、
   `DEVHUB_TELEMETRY_TOKEN` を既存基盤が要求するトークンに差し替えるだけでよい(`devhub telemetry send`
   側のコードは変更不要)。

Grafana ダッシュボード JSON (`telemetry/grafana/dashboards/devhub-telemetry.json`) は Prometheus /
Loki の datasource uid (`devhub-prometheus` / `devhub-loki`) にのみ依存するため、既存 Grafana に
同じ uid の datasource を用意すればそのままインポートできる(uid が異なる場合は JSON 内の
`datasource.uid` を書き換える)。

## メトリクス実現手段と設計からの逸脱(doc/phase2.md Step 3)

- `devhub_tool_use_total`(Counter、ラベル `tool_name` / `agent` / `agent_type`)は設計どおり
  **count connector** で実現できた(`transform` processor で JSON body を属性へ昇格 → `count`
  connector でカウンタ化 → `prometheus` exporter)。
- `devhub_last_used_timestamp`(Gauge)は **Collector 内で素直に作れなかった**(count connector は
  カウンタ生成に特化しており、`last_over_time` 相当のゲージを直接作る OTel 標準コンポーネントが
  見当たらなかった)。設計文書が許容する代替方針のとおり、
  - ダッシュボードの「最終利用日時」パネルは **Loki(LogQL)側**で表現した。Collector 側で
    `log.attributes["last_used_unix_ms"] = UnixMilli(log.observed_time)` を付与し、Grafana から
    `max by (tool_name, agent) (last_over_time({service_name="devhub-telemetry"} | tool_name != "" | unwrap last_used_unix_ms [$__range]))`
    で「ツール別・最終受信時刻」を表として出す。
  - 未使用アラート/検出は **PromQL `increase(devhub_tool_use_total[${unused_threshold_days}d]) == 0`**
    系(ダッシュボード変数 `unused_threshold_days`、既定 30)で代替した。
  - この逸脱は `doc/phase2.md` の記述変更を伴う可能性があるが、本タスクの指示により doc の更新は
    行っていない(親タスク側で反映予定)。

## Loki への OTLP エクスポートについて

`exporter/lokiexporter` は 2024-07(v0.131.0)に contrib から削除済みのため使用していない。
Loki 3.x のネイティブ OTLP ログ取り込み(`http://loki:3100/otlp`)へ `otlphttp` exporter で送る構成にした。
