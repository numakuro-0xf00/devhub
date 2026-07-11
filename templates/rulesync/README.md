# templates/rulesync/hooks.json

Phase 2(利用状況可視化。`doc/phase2.md`)向けの hooks テンプレート。`devhub telemetry send` を
Claude Code / Cursor / Codex CLI の `postToolUse` から呼び出す。

## 使い方

1. このファイルを消費側リポジトリの `.rulesync/hooks.json` にコピーする。
   ```bash
   cp templates/rulesync/hooks.json .rulesync/hooks.json
   ```
   既に `.rulesync/hooks.json` がある場合は手動でマージすること(devhub は上書きしない)。
2. `devhub apply`(または `devhub apply --features hooks`)を実行し、各エージェント向け設定を生成する。
   - Claude Code → `.claude/settings.json`
   - Cursor → `.cursor/hooks.json`
   - Codex CLI → `.codex/hooks.json`
3. `devhub env --fill`(または `--check` / `--write-example`)で `DEVHUB_TELEMETRY_ENDPOINT` /
   `DEVHUB_TELEMETRY_TOKEN` / `DEVHUB_TELEMETRY_SALT` を `.env` に入力する。
   3変数とも未設定のままなら `devhub telemetry send` は no-op になる(送信オフスイッチ。NFR-5)。

## 設計メモ

- **`--agent` はターゲットごとに固定**: rulesync の hooks スキーマは共通 `hooks` に加え、
  `claudecode:` / `cursor:` / `codexcli:` の各ブロックで `postToolUse` を丸ごと上書きできる。
  本テンプレートは共通 `hooks` を空にし、3ブロックそれぞれに `--agent <claudecode|cursor|codexcli>` の
  異なる command を書く方式を採る(1本のイベント定義を全ターゲット共有すると `--agent` を出し分けられないため)。
- **`matcher: ".*"`**: 全ツールを対象にする(正規表現の全マッチ)。
- **`timeout: 5`**: hook 実行のタイムアウト(秒)。`devhub telemetry send` 自体の HTTP タイムアウトは
  別途 `DEVHUB_TELEMETRY_TIMEOUT_MS`(既定 1000ms)で制御される。
- **`env` フィールドの役割は「`devhub env` の検出源」のみ**: rulesync@9.2.0 で実測した結果、
  `env` フィールドは Claude Code / Cursor / Codex CLI いずれの生成物にも引き継がれない
  (3ターゲットとも変換時に silently drop される)。つまり hook プロセスの環境変数注入としては機能しない。
  ここに `${DEVHUB_TELEMETRY_*}` を書いているのは、`devhub env` が `.rulesync/` 配下のテキストを
  走査して `${VAR}` プレースホルダを検出する仕組み(`EnvPlaceholderScanner`)に乗せるためだけであり、
  実際の値解決は `devhub telemetry send` 側の「OS 環境変数 → リポジトリルートの `.env`」フォールバック
  (`TelemetryEnvVarResolver`)が担う。

## Codex CLI の初回信頼登録

Codex CLI は非管理(`.codex/hooks.json` に配布された)hook を初回実行前に `/hooks` コマンドで
明示的に信頼登録する必要がある(`doc/phase2.md` リスク節参照)。`devhub apply` で配布しただけでは
動作しないため、オンボーディング手順に含めること。
