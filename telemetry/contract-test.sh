#!/usr/bin/env bash
#
# devhub telemetry 収集基盤の契約テスト(doc/phase2.md Step 3「契約テスト」)。
#
# 前提:
#   - Docker と Docker Compose v2(`docker compose` サブコマンド)が使えること。
#   - 初回はイメージ pull が発生するためネットワークアクセスが必要(数分かかることがある)。
#   - このスクリプトは telemetry/ を丸ごと起動・停止する(既存の telemetry コンテナがあれば巻き込む)。
#     CI や検証専用環境での実行を想定し、実行中は他の用途に telemetry/ の compose プロジェクトを使わないこと。
#
# 何を検証するか:
#   1. Bearer トークン付きでサンプルイベント(doc/phase2.md のイベントスキーマ形式)を
#      webhookevent receiver (POST /events) へ送ると 2xx が返る。
#   2. OTel Collector の prometheus exporter (/metrics) に devhub_tool_use_total が
#      期待ラベル(tool_name/agent/agent_type)付きでカウントされている。
#   3. Bearer トークンなしの POST が拒否される(認証が効いている)。
#
# 使い方:
#   telemetry/.env が無ければテスト専用の一時値で自動生成し、終了時に削除する。
#   既に telemetry/.env がある場合はその DEVHUB_TELEMETRY_TOKEN を使う(内容は変更しない)。
#
#   ./telemetry/contract-test.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

COLLECTOR_EVENTS_URL="http://localhost:8088/events"
COLLECTOR_METRICS_URL="http://localhost:8889/metrics"
READY_TIMEOUT_SECS=120

ENV_FILE=".env"
ENV_FILE_CREATED=0

log() { echo "[contract-test] $*"; }
fail() { echo "[contract-test] FAIL: $*" >&2; exit 1; }

cleanup() {
  local exit_code=$?
  log "後片付け: docker compose down -v"
  docker compose down -v --remove-orphans >/dev/null 2>&1 || true
  if [ "$ENV_FILE_CREATED" = "1" ] && [ -f "$ENV_FILE" ]; then
    log "テスト用に生成した $ENV_FILE を削除します"
    rm -f "$ENV_FILE"
  fi
  exit "$exit_code"
}
trap cleanup EXIT INT TERM

command -v docker >/dev/null 2>&1 || fail "docker コマンドが見つかりません"
docker compose version >/dev/null 2>&1 || fail "docker compose (v2 プラグイン) が見つかりません"

if [ ! -f "$ENV_FILE" ]; then
  log "$ENV_FILE が無いためテスト専用の一時値で生成します(終了時に削除)"
  {
    echo "DEVHUB_TELEMETRY_TOKEN=contract-test-token-$(date +%s)"
    echo "DEVHUB_TELEMETRY_GRAFANA_ADMIN_PASSWORD=contract-test-admin-password"
  } > "$ENV_FILE"
  ENV_FILE_CREATED=1
fi

# shellcheck disable=SC1090
TOKEN="$(grep -E '^DEVHUB_TELEMETRY_TOKEN=' "$ENV_FILE" | head -n1 | cut -d= -f2-)"
[ -n "$TOKEN" ] || fail "$ENV_FILE に DEVHUB_TELEMETRY_TOKEN が設定されていません"

log "docker compose up -d"
docker compose up -d

log "otel-collector の起動待ち(最大 ${READY_TIMEOUT_SECS}s)"
waited=0
until curl -s -o /dev/null "$COLLECTOR_METRICS_URL"; do
  waited=$((waited + 2))
  if [ "$waited" -ge "$READY_TIMEOUT_SECS" ]; then
    docker compose logs otel-collector || true
    fail "otel-collector の /metrics が ${READY_TIMEOUT_SECS}s 経っても応答しません"
  fi
  sleep 2
done
log "otel-collector 起動確認 OK"

SAMPLE_EVENT='{"schema":1,"event":"post_tool_use","agent":"claudecode","tool_name":"Bash","agent_type":null,"session_id":"contract-test-session-hash","user_id":"contract-test-user-hash","timestamp":"2026-07-11T00:00:00.000Z"}'

log "検証1: Bearer トークン付き POST -> 2xx を期待"
status=$(curl -s -o /dev/null -w '%{http_code}' \
  -X POST "$COLLECTOR_EVENTS_URL" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d "$SAMPLE_EVENT")
[ "$status" -ge 200 ] && [ "$status" -lt 300 ] || fail "認証ありPOSTが${status}を返しました(2xx を期待)"
log "  -> ${status} OK"

log "検証2: devhub_tool_use_total{agent=\"claudecode\",agent_type=\"none\",tool_name=\"Bash\"} が /metrics に出るまで待機"
waited=0
found=0
until [ "$found" = "1" ]; do
  if curl -s "$COLLECTOR_METRICS_URL" | grep -E '^devhub_tool_use_total\{[^}]*agent="claudecode"[^}]*agent_type="none"[^}]*tool_name="Bash"[^}]*\} [0-9]' >/dev/null; then
    found=1
    break
  fi
  waited=$((waited + 2))
  if [ "$waited" -ge "$READY_TIMEOUT_SECS" ]; then
    curl -s "$COLLECTOR_METRICS_URL" | grep '^devhub_' || true
    fail "devhub_tool_use_total が期待ラベルで /metrics に現れませんでした"
  fi
  sleep 2
done
log "  -> OK(期待ラベルでカウントされていることを確認)"

log "検証3: Bearer トークンなし POST -> 拒否(2xx 以外)を期待"
status=$(curl -s -o /dev/null -w '%{http_code}' \
  -X POST "$COLLECTOR_EVENTS_URL" \
  -H "Content-Type: application/json" \
  -d "$SAMPLE_EVENT")
[ "$status" -lt 200 ] || [ "$status" -ge 300 ] || fail "認証なしPOSTが${status}(2xx)を返しました。認証が効いていません"
log "  -> ${status} OK(拒否を確認)"

log "検証4: 誤ったトークンでの POST -> 拒否(2xx 以外)を期待"
status=$(curl -s -o /dev/null -w '%{http_code}' \
  -X POST "$COLLECTOR_EVENTS_URL" \
  -H "Authorization: Bearer wrong-token-xxx" \
  -H "Content-Type: application/json" \
  -d "$SAMPLE_EVENT")
[ "$status" -lt 200 ] || [ "$status" -ge 300 ] || fail "誤ったトークンでのPOSTが${status}(2xx)を返しました"
log "  -> ${status} OK(拒否を確認)"

log "契約テスト PASS"
