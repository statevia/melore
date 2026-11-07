---
description: 次に着手する計画項目 / Spec を根拠つきで提案し、ユーザーと合意する
---

# spec-pick-next（Claude Code 用ラッパー）

正本は `.cursor/commands/spec-pick-next.md`。このファイルは参照用の薄いラッパーで、手順を重複記載しない。

## 手順

1. `.cursor/commands/spec-pick-next.md` を Read で読み込む。
2. 読み込んだ手順に従って作業する。ユーザーの追加指示: $ARGUMENTS

## Claude Code での読み替え

- `.cursor/skills/<name>/SKILL.md` の参照は、そのまま正本を Read すればよい（`.claude/skills/` のラッパーも同じ正本を指す）。
