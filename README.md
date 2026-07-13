# devhub

プライベートな開発チーム内で coding agent の設定(指示ファイル・skill・MCP・hooks・permissions)を**単一ソースから一元管理・配布**するための仕組み。単一ソース `.rulesync/` から、Claude Code / Cursor / GitHub Copilot / OpenAI Codex のネイティブ設定を `devhub` dotnet tool で生成・配置する(変換は [rulesync](https://github.com/dyoshikawa/rulesync) に委譲)。

## devhub コマンド

| コマンド | 動作 |
|---|---|
| `devhub apply` | `.rulesync/` から各エージェント設定を生成(CLAUDE.md 保護つき) |
| `devhub check` | 生成物のドリフト検査 + 秘匿情報スキャン(CI 向け、失敗時 exit 1) |
| `devhub secrets` | 設定ソース・生成物への秘匿情報の実値混入を検査([secretlint](https://github.com/secretlint/secretlint) に委譲) |
| `devhub env` | 必要な環境変数の一覧化・未設定検出・`.env.example` 生成・対話的穴埋め |
| `devhub telemetry send` / `scan-transcripts` | tool・skill 利用イベントの送信(通常は hooks から自動起動され、手動実行は不要) |
| `devhub telemetry copilot-seats` | GitHub Copilot の利用有無の取り込み(管理者向け) |

> ※ devhub はリポジトリごとの「ローカルツール」として導入するため、実際には `dotnet devhub <コマンド>` の形で実行する。導入手順は下の[セットアップガイド](#セットアップガイド実運用開始)を参照。

秘匿情報は実値を commit せず `${VAR}` プレースホルダで参照し、実値は `.env`(gitignore 済み)か OS 環境変数に置く。

## セットアップガイド(実運用開始)

devhub の導入は3つの役割で進める。**自分の役割の節だけ読めばよい。**

| 役割 | やること | 頻度 |
|---|---|---|
| [チーム管理者](#1-チーム管理者収集基盤を立てる) | 利用状況の収集基盤(telemetry サーバー)を立て、接続情報をチームに共有する | チームで最初に1回 |
| [リポジトリ管理者](#2-リポジトリ管理者開発リポジトリへ-devhub-を導入する) | 開発リポジトリに devhub を導入して commit する | リポジトリごとに1回 |
| [チームメンバー](#3-チームメンバー自分の-pc-をセットアップする) | 自分の PC で初回セットアップする | 1人1回 |

### 0. 前提ツール

| ツール | 何に使うか | 入っているかの確認 |
|---|---|---|
| Git | リポジトリの clone | `git --version` が表示される |
| .NET 8 SDK | devhub 本体の実行 | `dotnet --version` が `8.` で始まる |
| Node.js(npx) | rulesync / secretlint の内部起動 | `npx --version` が表示される |
| Docker + Compose v2 | 収集基盤の起動(**チーム管理者のみ**) | `docker compose version` が表示される |

> **メモ**: devhub はリポジトリごとにインストールする「ローカルツール」なので、コマンドは `dotnet devhub <コマンド>` の形で呼び出す。

### 1. チーム管理者:収集基盤を立てる

どの skill / tool がどれだけ使われているかを集計・可視化するサーバー(OTel Collector + Prometheus + Loki + Grafana)を、チーム内からアクセスできるマシン1台に立てる。

> **警告**: 手順 (1) の `contract-test.sh` は終了時に `docker compose down -v`(**収集データも含めた全削除**)を行う。本番起動前の疎通確認として**最初の1回だけ**実行し、本番起動後は絶対に再実行しないこと。

```bash
git clone https://github.com/numakuro-0xf00/devhub.git
cd devhub/telemetry

# (1) まず疎通確認。一時コンテナで起動→検証→全削除まで自動で行う
./contract-test.sh          # 最後に「契約テスト PASS」と出れば OK

# (2) 本番用の設定を作る
cp .env.example .env
# .env をエディタで開き、2つの値を設定する:
#   DEVHUB_TELEMETRY_TOKEN                  … 送信認証トークン。openssl rand -hex 32 などで生成
#   DEVHUB_TELEMETRY_GRAFANA_ADMIN_PASSWORD … Grafana の管理者パスワード

# (3) 起動
docker compose up -d
```

(トークンの生成は `openssl` でなくてもよい。推測されない十分長いランダム文字列なら手段は問わない)

**動作確認**: ブラウザで `http://<このマシン>:3000` を開き、`admin` と上記パスワードでログイン → 「devhub」フォルダのダッシュボード「devhub telemetry — 利用状況可視化」が表示されれば完了。

最後に、次の3つの値をチームメンバーへ安全な経路(パスワードマネージャ等)で共有する:

| 共有する値 | 内容 |
|---|---|
| `DEVHUB_TELEMETRY_ENDPOINT` | `http://<このマシン>:8088/events` |
| `DEVHUB_TELEMETRY_TOKEN` | 上の `.env` に設定したトークンと同じ値 |
| `DEVHUB_TELEMETRY_SALT` | 匿名化用ソルト。任意のランダム文字列を決めて**全員で同じ値**を使う(ユーザー ID のハッシュ化に使うため、揃っていないと同一人物が別人として集計される) |

**(任意)Copilot の利用状況も集めたい場合**: GitHub Copilot は hooks に対応していないため、org オーナー権限のトークン(`manage_billing:copilot` または `read:org`)を使って `dotnet devhub telemetry copilot-seats` を定期実行する(cron 等、チーム内の1箇所でよい)。必要な環境変数は `DEVHUB_TELEMETRY_COPILOT_ORG` / `DEVHUB_TELEMETRY_COPILOT_TOKEN`(+ 上記 ENDPOINT / TOKEN / SALT)。取得できるのは**利用有無まで**(どの設定を使ったかは GitHub の仕様上取得不可)。

### 2. リポジトリ管理者:開発リポジトリへ devhub を導入する

チームが日々開発しているリポジトリ(devhub リポジトリ自体ではない)で以下を実施し、commit する。

#### 2-1. GitHub Packages フィードを登録する

devhub は GitHub Packages(NuGet)で配布されている。リポジトリ直下に `nuget.config` を作成する:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github-devhub" value="https://nuget.pkg.github.com/numakuro-0xf00/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-devhub>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github-devhub>
  </packageSourceCredentials>
</configuration>
```

`%GITHUB_USER%` / `%GITHUB_TOKEN%` は実行時に各自の環境変数から読まれる書き方。**PAT の実値をこのファイルに書いて commit しないこと**(実値の設定方法はメンバー手順 3-1 参照)。

#### 2-2. devhub をローカルツールとして固定する

> **事前準備**: 次の `dotnet tool install` は GitHub Packages から取得するため、リポジトリ管理者自身にも PAT が必要。まだ設定していなければ先にメンバー手順 [3-1](#3-1-github-packages-用の-pat-を用意する) を済ませること。

```bash
dotnet new tool-manifest                              # .config/dotnet-tools.json を作成
dotnet tool install --local Devhub.Tool --version 0.1.0
dotnet devhub --version                               # 「devhub 0.1.0 ...」と出れば OK
```

#### 2-3. 設定の単一ソース `.rulesync/` を用意する

```bash
npx rulesync init
```

生成された `.rulesync/` 配下にチームの共有設定を書く(`rules/` に指示ファイル、必要に応じて `mcp.json` / `permissions.json` / `skills/` など)。ここが**単一ソース**で、各エージェントのネイティブ設定はすべてここから生成される。

#### 2-4. telemetry hooks を配置する

devhub リポジトリのテンプレートをコピーする:

```bash
curl -fsSL -o .rulesync/hooks.json \
  https://raw.githubusercontent.com/numakuro-0xf00/devhub/main/templates/rulesync/hooks.json
```

これで各エージェントの tool 実行イベントが `dotnet devhub telemetry send` 経由で収集基盤へ送られるようになる。既に独自の `.rulesync/hooks.json` がある場合は上書きせず手動でマージする(既存のエントリは残したまま、テンプレートの telemetry 用エントリを追記する)。

#### 2-5. 生成・検査して commit する

```bash
dotnet devhub apply           # 各エージェントのネイティブ設定を生成
dotnet devhub env --write-example   # 必要な環境変数の一覧を .env.example に出力
dotnet devhub check           # hooks lint + ドリフト検査 + 秘匿情報スキャン(exit 0 を確認)
```

`.gitignore` に `.env` が入っていることを確認したうえで、以下を commit する:

- `nuget.config`、`.config/dotnet-tools.json`
- `.rulesync/`(単一ソース)と生成物(`.claude/` `.cursor/` `.codex/` など)
- `.env.example`(**`.env` は絶対に commit しない**)

#### 2-6. CI に検査を組み込む(推奨)

生成物が単一ソースからズレていないか・秘匿情報が混入していないかを PR ごとに検査する:

```yaml
# GitHub Actions の例(.NET 8 と Node.js のセットアップ後)
# 注意: workflow の permissions に packages: read が必要
- run: dotnet tool restore
  env:
    GITHUB_USER: ${{ github.actor }}
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}   # nuget.config の %GITHUB_*% がここから解決される
- run: dotnet devhub check   # 失敗時 exit 1 で CI が落ちる
```

### 3. チームメンバー:自分の PC をセットアップする

#### 3-1. GitHub Packages 用の PAT を用意する

devhub パッケージの取得に GitHub の Personal Access Token が必要(GitHub Packages は public でも認証必須)。

1. GitHub → 右上アイコン → **Settings** → **Developer settings** → **Personal access tokens (classic)** → **Generate new token (classic)**
2. スコープは **`read:packages` だけ**にチェックして生成
3. 生成されたトークンを環境変数に設定する(シェルの起動ファイルに追記):

```bash
# ~/.bashrc など(Windows なら「環境変数の編集」で同名の変数を作る)
export GITHUB_USER=<自分の GitHub ユーザー名>
export GITHUB_TOKEN=<生成した PAT>
```

設定後、**新しいターミナルを開いて** `echo $GITHUB_TOKEN`(PowerShell なら `echo $env:GITHUB_TOKEN`)で値が出ることを確認。

> **注意**: `GITHUB_TOKEN` は `gh` CLI なども参照する環境変数。すでに `gh` を使っていて別のトークンを設定済みの場合は、上書きすると `gh` 側の権限が変わることに注意。

#### 3-2. clone して設定を生成する

```bash
git clone <開発リポジトリの URL> && cd <リポジトリ>
dotnet tool restore     # ピン留めされた devhub を取得(3-1 の PAT を使う)
dotnet devhub apply     # 自分の環境に各エージェントのネイティブ設定を生成
```

**確認**: 自分が使うエージェント用のディレクトリ(Claude Code なら `.claude/`、Cursor なら `.cursor/` など)が生成されていれば成功。

#### 3-3. 環境変数を穴埋めする

```bash
dotnet devhub env --fill
```

チーム管理者から共有された `DEVHUB_TELEMETRY_ENDPOINT` / `DEVHUB_TELEMETRY_TOKEN` / `DEVHUB_TELEMETRY_SALT` を聞かれるまま入力すると、`.env`(gitignore 済み)に保存される。

**確認**: `dotnet devhub env --check` が何も文句を言わず終われば OK。

#### 3-4. エージェント別の初回手順

| 使っているエージェント | 追加手順 |
|---|---|
| Claude Code / Cursor | なし(生成された設定が自動で読み込まれる) |
| Codex CLI | **初回に hook を信頼登録する**: `codex` を対話モードで起動し、セッション内で `/hooks` と入力して表示された hook を承認する(登録しないと hook が一切動かない) |
| GitHub Copilot | なし(hooks 非対応。利用状況は管理者の `copilot-seats` で別途収集) |

### 4. 日々の運用

チームの共有設定(`.rulesync/`)が更新されたら、各メンバーは次の1行で追従する:

```bash
git pull && dotnet tool restore && dotnet devhub apply
```

### 困ったとき

| 症状 | 原因と対処 |
|---|---|
| `dotnet tool restore` が 401 / Unauthorized | PAT 未設定か期限切れ。3-1 をやり直し、新しいターミナルで再実行 |
| `npx (Node.js) が見つかりません`(exit 3) | Node.js が未インストール。前提ツールの節を参照 |
| `.rulesync/ ディレクトリが見つかりません` | リポジトリ管理者の手順 2-3 が未実施のリポジトリで実行している |
| CI でだけ `dotnet tool restore` が 401 | ワークフローに `GITHUB_USER` / `GITHUB_TOKEN` の env 設定、または `permissions: packages: read` がない(2-6 参照) |
| テレメトリが届いているか分からない | hook は失敗しても沈黙する設計(開発の邪魔をしないため)。`DEVHUB_TELEMETRY_DEBUG=1` を設定してエージェントを使うと診断が stderr に出る |
| Grafana は開けるがデータが出ない | 送信側の ENDPOINT / TOKEN / SALT が管理者の共有値と一致しているか確認(`dotnet devhub env --check`)。その上で上記 DEBUG で送信側を診断 |
| `docker compose up -d` がポート競合で失敗 | 3000 / 3100 / 8088 / 8889 / 9090 番ポートの空きを確認(`telemetry/docker-compose.yml` 参照) |
| 収集基盤の動作確認をやり直したい | `contract-test.sh` は**再実行しない**(データ全削除)。Grafana のダッシュボードを見るか、`curl` で `/events` へテスト POST する |
| Codex で hook が動かない | `/hooks` の信頼登録を忘れていないか確認(3-4) |

## 開発

```bash
dotnet build                                       # ビルド
dotnet test                                        # 全テスト
dotnet run --project src/Devhub.Tool -- --help     # ローカル実行
```

リリースは `src/Devhub.Tool/Devhub.Tool.csproj` の `<Version>` を上げて `v<Version>` タグを push(GitHub Packages へ自動 publish)。

## ドキュメント

- `doc/requirements.md` — 要件と設計判断
- `doc/phase0.md` — 共有設定の置き場所と Claude Code ブリッジ
- `doc/phase1.md` — devhub tool の設計(CLAUDE.md 保護・features 絞り込み・秘匿情報スキャン・`devhub env`・publish パイプライン)
- `doc/phase1-tool-eval.md` — 変換エンジン選定(Ruler vs rulesync)の実地比較
- `doc/phase2.md` — 利用状況テレメトリの設計(hooks 送信・トランスクリプト補完・Copilot seats)
- `telemetry/README.md` — 収集基盤(OTel Collector + Prometheus + Loki + Grafana)の詳細と既存基盤への組み込み方

## ライセンス

Apache-2.0
