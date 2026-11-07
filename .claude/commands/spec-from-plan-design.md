---
description: Plan モードで得た設計・合意内容を .spec/specs/ 配下に仕様セットとして書き起こす
---

# spec-from-plan-design（Claude Code 用ラッパー）

正本は `.cursor/commands/spec-from-plan-design.md`。このファイルは参照用の薄いラッパーで、手順を重複記載しない。

## 手順

1. `.cursor/commands/spec-from-plan-design.md` を Read で読み込む。
2. 読み込んだ手順に従って作業する。ユーザーの追加指示: $ARGUMENTS

## Claude Code での読み替え

- Cursor の Plan 出力・`@` 参照は、同じ会話内のプラン（ExitPlanMode で承認した内容）や、ユーザーが示したファイルとして扱う。
- `.cursor/skills/<name>/SKILL.md` の参照は、そのまま正本を Read すればよい（`.claude/skills/` のラッパーも同じ正本を指す）。
