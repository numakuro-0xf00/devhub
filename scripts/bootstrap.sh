#!/usr/bin/env bash
# devhub Phase 0 bootstrap (WSL / Linux / macOS)
#
# 共有スキル(.agents/skills/)を Claude Code が読む .claude/skills/ へリンクする。
# Cursor / Copilot / Codex は .agents/skills/ と AGENTS.md を直接読むため設定不要。
# Claude Code のみこのブリッジが必要(AGENTS.md 非対応・skill は .claude/skills/ を参照)。
#
# 冪等: 何度実行しても同じ結果。既存のリンクは張り直す。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src_dir="$repo_root/.agents/skills"
dst_dir="$repo_root/.claude/skills"

if [[ ! -d "$src_dir" ]]; then
  echo "warn: $src_dir が無い。共有スキルはまだ無し。処理をスキップ。" >&2
  exit 0
fi

mkdir -p "$dst_dir"

linked=0
for skill_path in "$src_dir"/*/; do
  [[ -d "$skill_path" ]] || continue
  name="$(basename "$skill_path")"
  target="$dst_dir/$name"
  # 既存のリンク/ディレクトリを除去して張り直す(冪等)
  if [[ -L "$target" || -e "$target" ]]; then
    rm -rf "$target"
  fi
  ln -s "../../.agents/skills/$name" "$target"
  echo "linked: .claude/skills/$name -> ../../.agents/skills/$name"
  linked=$((linked + 1))
done

# .agents/skills から消えたスキルの stale なリンクを掃除
for link_path in "$dst_dir"/*; do
  [[ -L "$link_path" ]] || continue
  if [[ ! -e "$link_path" ]]; then
    echo "removed stale link: $(basename "$link_path")"
    rm -f "$link_path"
  fi
done

echo "done: $linked skill(s) をブリッジ。CLAUDE.md は @AGENTS.md で指示を取り込み済み。"
