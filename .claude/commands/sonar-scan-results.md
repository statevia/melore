---
description: SonarQube のスキャン結果（Quality Gate / issue / カバレッジ）を確認し、不足を是正する
---

# sonar-scan-results（Claude Code 用ラッパー）

正本は `.cursor/commands/sonar-scan-results.md`。このファイルは参照用の薄いラッパーで、手順を重複記載しない。

## 手順

1. `.cursor/commands/sonar-scan-results.md` を Read で読み込む。
2. 読み込んだ手順に従って作業する。ユーザーの追加指示: $ARGUMENTS

## Claude Code での読み替え

- MCP `sonarqube` は `mcp__sonarqube__*`（`.mcp.json` のプロジェクトスコープ）を指す。
- 認可エラー時は Cursor の `mcp_auth` ではなく、環境変数 `SONARQUBE_TOKEN` が USER token かをユーザーに確認してもらう。
- `.cursor/skills/<name>/SKILL.md` の参照は、そのまま正本を Read すればよい（`.claude/skills/` のラッパーも同じ正本を指す）。
