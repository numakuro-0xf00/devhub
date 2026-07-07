---
name: authoring-shared-skills
description: devhub のチーム共有スキルを新規作成・編集するときに使う。Agent Skills 標準に沿った SKILL.md の書き方、devhub の配置規約（.agents/skills/）、4エージェント（Claude Code / Cursor / Copilot / Codex）で動かすための命名・frontmatter ルール、Claude Code へのブリッジ手順を案内する。「共有スキルを作りたい」「skill を追加したい」「SKILL.md の書き方」といったときにトリガーする。
---

# 共有スキルの作り方（devhub / Phase 0）

チームで共有する skill を Agent Skills 標準に沿って作成するためのガイド。ここで作る skill は `.agents/skills/` に置くことで Cursor / Copilot / Codex がネイティブに読み込み、ブートストラップ後は Claude Code でも動く。

## 1. 置き場所と命名

- ディレクトリ:`.agents/skills/<skill-name>/SKILL.md`
- `<skill-name>` は **小文字とハイフンのみ**(例:`csharp-test-runner`)。`SKILL.md` の `name` frontmatter と**完全一致**させる。
- 補助ファイルは同じディレクトリ配下に置く(`scripts/`、`references/`、`assets/`)。

## 2. SKILL.md の frontmatter（必須 2 項目）

```yaml
---
name: <ディレクトリ名と一致・小文字ハイフン・1〜64文字>
description: <このスキルを「いつ・何のために」使うか。1024文字以内。トリガー精度を左右する最重要項目>
---
```

- `description` は**発火条件**そのもの。「〜するとき」「〜したいとき」を具体的な語で書く。曖昧だと呼ばれない。
- `name` と `description` 以外の frontmatter(`license`・`metadata` 等)は任意。`allowed-tools` はエージェント間で挙動が揺れるため Phase 0 では使わない。

## 3. 本文の書き方

- 本文は **500 行以内**を目安に、手順・判断基準・具体例を簡潔に。
- 長い参考資料・テンプレートは本文に埋めず `references/` に分け、本文から参照する(段階的開示)。
- スクリプトは `scripts/` に置き、本文から呼び出す。

## 4. 追加したら:Claude Code へブリッジ

`.agents/skills/` は Cursor / Copilot / Codex には即座に効く。Claude Code は `.claude/skills/` を読むため、リポジトリルートで次を実行してリンクを張り直す:

```bash
# WSL / Linux / macOS
./scripts/bootstrap.sh

# Windows (PowerShell)
pwsh ./scripts/bootstrap.ps1
```

これで `.claude/skills/<skill-name>` が `.agents/skills/<skill-name>` にリンクされ、Claude Code でも使えるようになる。

## 5. チェックリスト

- [ ] ディレクトリ名 == `name` frontmatter(小文字ハイフン)
- [ ] `description` に「いつ使うか」を具体的に書いた
- [ ] 本文は簡潔(長い資料は `references/` へ）
- [ ] `bootstrap` を実行して Claude Code に反映した
- [ ] 4エージェントのうち手元で使うものでトリガーを確認した

> 将来（Phase 1）は dotnet tool がこのブリッジと配置を自動化する。それまでは本スキルと bootstrap スクリプトが手動の受け皿。
