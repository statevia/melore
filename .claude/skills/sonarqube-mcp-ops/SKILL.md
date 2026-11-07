---
name: sonarqube-mcp-ops
description: SonarQube MCP での分析作業手順を実行する。SonarQube の issue 調査、プロジェクトキー確認、自動解析の on/off 管理が必要なときに使う。
disable-model-invocation: true
---

# sonarqube-mcp-ops（Claude Code 用ラッパー）

正本は `.cursor/skills/sonarqube-mcp-ops/SKILL.md`。このファイルは参照用の薄いラッパーで、手順を重複記載しない。

## 手順

1. `.cursor/skills/sonarqube-mcp-ops/SKILL.md` を Read で読み込む。
2. 読み込んだ手順に従って作業する。

## Claude Code での読み替え

- MCP サーバー名は `sonarqube`（`.mcp.json` のプロジェクトスコープ）。ツール名は `mcp__sonarqube__*`。
