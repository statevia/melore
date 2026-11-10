# Melore — Specification Service

ローカルで動作する仕様・実装の知識サービスです。AI エージェントが大量のソースやドキュメントを直接読まずに、構造化された検索・説明・追跡を行えるようにします。

最初の利用対象は [Statevia](https://github.com/statevia/statevia) です。Melore 自体は特定リポジトリに依存しない独立サービスとして設計します。

## 文書

| 文書 | 内容 |
| --- | --- |
| [docs/README.md](docs/README.md) | 文書索引 |
| [docs/glossary.md](docs/glossary.md) | 用語集 |
| [docs/requirements.md](docs/requirements.md) | 要件 |
| [docs/architecture/overview.md](docs/architecture/overview.md) | アーキテクチャ |
| [docs/architecture/data-model.md](docs/architecture/data-model.md) | データモデル |
| [docs/decisions/README.md](docs/decisions/README.md) | 技術選定 ADR |
| [docs/guides/implementation-plan.md](docs/guides/implementation-plan.md) | Phase 1–4 実装計画と現状 |
| [docs/guides/cli.md](docs/guides/cli.md) | CLI のコマンドと出力 |
| [docs/guides/mcp-tool-candidates.md](docs/guides/mcp-tool-candidates.md) | MCP ツール追加候補 |
| [docs/guides/search-gaps.md](docs/guides/search-gaps.md) | 検索漏れと対応 |
| [docs/guides/agent-task-accuracy-plan.md](docs/guides/agent-task-accuracy-plan.md) | エージェントのタスク精度の計測と改善計画 |
| [docs/evaluation.md](docs/evaluation.md) | 評価方法と受け入れ基準 |

## MCP

Melore MCP は対象リポジトリ側の MCP 設定から起動します。Statevia は同じ親フォルダの `../Melore` を相対パスで参照するので、Melore を隣に置いて発行してください。[`melore.json.example`](melore.json.example) を `melore.json` にコピーし、`repositoryPath` を対象リポジトリ（最初は [Statevia](https://github.com/statevia/statevia)）のクローン絶対パスにします。ツールは `search` / `references` / `trace` / `explain`。ログは stderr のみです。

## 開発

.NET 10 SDK が必要です。CLI は `dotnet publish src/Melore.Cli -c Release`（`dotnet/` から）で `artifacts/melore/melore.exe` に、MCP は `dotnet publish src/Melore.Mcp -c Release` で `artifacts/melore-mcp/melore-mcp.exe` に出ます。詳細は [AGENTS.md](AGENTS.md) を参照してください。

## ライセンス

[MIT License](LICENSE) で公開しています。
