# AGENTS.md

エージェント向けの薄い索引。設計の正本は [`docs/README.md`](docs/README.md)。用語は [`docs/glossary.md`](docs/glossary.md)。

## 構成

| 場所 | 内容 |
| --- | --- |
| `docs/` | 要件・設計 |
| `dotnet/` | C# |
| `global.json` | SDK 10 系（`rollForward: latestFeature`） |
| `melore.json.example` | 対象リポジトリ設定の例 |
| `.cursor/` | ルール・コマンド・スキル・フックの正本 |
| `.claude/` | Claude Code 用のラッパーとサブエージェント |
| `.spec/templates/` | Spec のテンプレート（`.spec/specs/` はローカルで git 管理外） |

## ビルド / テスト

```powershell
cd dotnet
dotnet test Melore.slnx
```

CLI を `artifacts/melore` に出す:

```powershell
cd dotnet
dotnet publish src/Melore.Cli -c Release
```

実行ファイルはリポジトリルートの `artifacts/melore/melore.exe`。

MCP を `artifacts/melore-mcp` に出す:

```powershell
cd dotnet
dotnet publish src/Melore.Mcp -c Release
```

実行ファイルはリポジトリルートの `artifacts/melore-mcp/melore-mcp.exe`。

## CLI

全コマンド・オプション・出力は [`docs/guides/cli.md`](docs/guides/cli.md)。

```powershell
cd dotnet
..\artifacts\melore\melore.exe index --config ..\melore.json
..\artifacts\melore\melore.exe status --config ..\melore.json
..\artifacts\melore\melore.exe search "Fork Join" --config ..\melore.json
..\artifacts\melore\melore.exe references ExecutionEngine --config ..\melore.json
..\artifacts\melore\melore.exe trace ExecutionEngine --config ..\melore.json
..\artifacts\melore\melore.exe explain "Fork Join の親子" --config ..\melore.json
..\artifacts\melore\melore.exe eval --config ..\melore.json
```

MCP（stdio、ログは stderr）:

```powershell
cd dotnet
dotnet run --project src/Melore.Mcp -- --config ..\melore.json
```

Melore MCP は対象リポジトリ側の MCP 設定から起動する（Statevia の `.cursor/mcp.json` / `.mcp.json` は、同じ親フォルダの `../Melore` を相対パスで参照する）。先に `dotnet publish src/Melore.Mcp -c Release` で発行する。`melore.json.example` を `melore.json` にコピーし、`repositoryPath` を対象クローンの絶対パスにする。ツールは `search` / `references` / `trace` / `explain`。インデックスが無い場合は先に `index` を実行する。

このリポジトリ自身の MCP 設定は [`.cursor/mcp.json`](.cursor/mcp.json)（Cursor。`serena-melore` / `sonarqube` / `github`）と [`.mcp.json`](.mcp.json)（Claude Code。`serena-melore` / `sonarqube`）。どちらも git 管理する。絶対パスと秘密情報を書かない。`sonarqube` は常駐コンテナ（9124 番）に接続し、環境変数 `SONARQUBE_TOKEN` を送る。`github` は `GITHUB_PERSONAL_ACCESS_TOKEN` を使う。

インデックス DB の既定場所は `%LOCALAPPDATA%\Melore\<repositoryName>.db`。

## 実装メモ（エージェント向け）

- **Serena MCP**: Melore のコードは `serena-melore`（`dotnet/`）で調べて編集する。Claude Code はログオン時に常駐する HTTP サーバー（9123 番）を全セッションで共有する。worktree からは編集しない。手順は [`.cursor/skills/serena-workflow-research-impl/SKILL.md`](.cursor/skills/serena-workflow-research-impl/SKILL.md)。
- **stdout**: MCP プロトコル専用。ログは stderr。
- **実機確認**: `artifacts/` の実行ファイルは古いことがある。確認は `dotnet run` で行う。手順は [`.cursor/rules/runtime-verification.mdc`](.cursor/rules/runtime-verification.mdc)。
