#!/usr/bin/env pwsh
# devhub Phase 0 bootstrap (Windows / PowerShell)
#
# 共有スキル(.agents/skills/)を Claude Code が読む .claude/skills/ へリンクする。
# Cursor / Copilot / Codex は .agents/skills/ と AGENTS.md を直接読むため設定不要。
# Claude Code のみこのブリッジが必要。
#
# シンボリックリンクを試み、権限(開発者モード/管理者)が無い環境では複製にフォールバックする。
# 冪等: 何度実行しても同じ結果。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $repoRoot '.agents/skills'
$dstDir = Join-Path $repoRoot '.claude/skills'

if (-not (Test-Path $srcDir)) {
    Write-Warning "$srcDir が無い。共有スキルはまだ無し。処理をスキップ。"
    exit 0
}

New-Item -ItemType Directory -Force -Path $dstDir | Out-Null

$linked = 0
$copiedFallback = $false
foreach ($skill in Get-ChildItem -Path $srcDir -Directory) {
    $name = $skill.Name
    $target = Join-Path $dstDir $name
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    try {
        # 相対リンク先: .claude/skills/<name> から見て ../../.agents/skills/<name>
        New-Item -ItemType SymbolicLink -Path $target -Value "..\..\.agents\skills\$name" -ErrorAction Stop | Out-Null
        Write-Host "linked: .claude/skills/$name -> ..\..\.agents\skills\$name"
    }
    catch {
        # 権限不足など: 複製にフォールバック(ドリフト注意 — 更新のたびに再実行が必要)
        Copy-Item -Recurse -Force -Path $skill.FullName -Destination $target
        Write-Host "copied (symlink 不可のため複製): .claude/skills/$name"
        $copiedFallback = $true
    }
    $linked++
}

# stale なリンク/複製の掃除(.agents/skills に元が無いもの)
foreach ($entry in Get-ChildItem -Path $dstDir -ErrorAction SilentlyContinue) {
    if (-not (Test-Path (Join-Path $srcDir $entry.Name))) {
        Remove-Item -Recurse -Force $entry.FullName
        Write-Host "removed stale: $($entry.Name)"
    }
}

Write-Host "done: $linked skill(s) をブリッジ。CLAUDE.md は @AGENTS.md で指示を取り込み済み。"
if ($copiedFallback) {
    Write-Warning "一部を複製で対応。ドリフト回避のため、Windows の開発者モードを有効化すると symlink が使えます。"
}
