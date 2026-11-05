# AGENTS.md

Cursor エージェント向けの薄い索引。設計の正本は [`docs/README.md`](docs/README.md)。用語は [`docs/glossary.md`](docs/glossary.md)。

## 構成

| 場所 | 内容 |
| --- | --- |
| `docs/` | 要件・設計 |
| `dotnet/` | C# |
| `global.json` | SDK 10 系（`rollForward: latestFeature`） |
| `melore.json.example` | 対象リポジトリ設定の例 |

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

このリポジトリの Cursor MCP 設定は [`.cursor/mcp.json`](.cursor/mcp.json)。`melore.json.example` を `melore.json` にコピーし、`repositoryPath` を対象クローンの絶対パスにする。エージェントはツール `search` / `references` / `trace` / `explain` を使う。インデックスが無い場合は先に `index` を実行する。

インデックス DB の既定場所は `%LOCALAPPDATA%\Melore\<repositoryName>.db`。
