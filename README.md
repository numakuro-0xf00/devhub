# devhub

プライベートな開発チーム内で coding agent の設定(指示ファイル・skill・MCP・hooks・permissions)を**単一ソースから一元管理・配布**するための仕組み。単一ソース `.rulesync/` から、Claude Code / Cursor / GitHub Copilot / OpenAI Codex のネイティブ設定を `devhub` dotnet tool で生成・配置する(変換は [rulesync](https://github.com/dyoshikawa/rulesync) に委譲)。

## devhub コマンド

| コマンド | 動作 |
|---|---|
| `devhub apply` | `.rulesync/` から各エージェント設定を生成(CLAUDE.md 保護つき) |
| `devhub check` | 生成物のドリフト検査 + 秘匿情報スキャン(CI 向け、失敗時 exit 1) |
| `devhub secrets` | 設定ソース・生成物への秘匿情報の実値混入を検査([secretlint](https://github.com/secretlint/secretlint) に委譲) |
| `devhub env` | 必要な環境変数の一覧化・未設定検出・`.env.example` 生成・対話的穴埋め |

秘匿情報は実値を commit せず `${VAR}` プレースホルダで参照し、実値は `.env`(gitignore 済み)か OS 環境変数に置く。

## オンボーディング(消費側リポジトリ)

```bash
git clone <team-repo> && cd <team-repo>
dotnet tool restore      # .config/dotnet-tools.json でピン留めされた devhub を取得
devhub apply             # 各エージェントのネイティブ設定を生成
devhub env --fill        # 未設定の環境変数を対話的に .env へ穴埋め
```

実行には .NET 8 と Node.js(npx。rulesync / secretlint の起動に使用)が必要。

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

## ライセンス

Apache-2.0
