---
description: 完了した仕様セットを .spec/archive/specs/ へ、却下した仕様を .spec/reject/specs/ へ移す
---

# spec-archive（Claude Code 用ラッパー）

正本は `.cursor/commands/spec-archive.md`。このファイルは参照用の薄いラッパーで、手順を重複記載しない。

## 手順

1. `.cursor/commands/spec-archive.md` を Read で読み込む。
2. 読み込んだ手順に従って作業する。ユーザーの追加指示: $ARGUMENTS

## Claude Code での読み替え

- Cursor の `@` 参照は、ユーザーが引数や本文で示したフォルダ名・パスとして扱う。
