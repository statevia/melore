# アーキテクチャ概要

| 項目 | 値 |
| --- | --- |
| 種別 | Architecture |
| Version | 0.1 |
| 更新日 | 2026-11-02 |

## 1. 位置づけ

```text
[Cursor 等のエージェント]
        │ MCP stdio (Phase 2+)
        ▼
[Melore Host]  ← 単一プロセス
   ├ CLI (index / status / search)
   ├ MCP tools
   ├ Query services (search / references / trace / explain)
   ├ Indexers (Markdown / Roslyn / Git)
   ├ Providers (FTS / Vector / LLM / Embedding / Rerank)
   └ SQLite (metadata + FTS5 + sqlite-vec)
        │ 読み取りのみ
        ▼
[対象 Git リポジトリ]  例: melore.json の repositoryPath
```

Melore は対象リポジトリの外で動く。対象は設定の `RepositoryPath` で指す。

## 2. プロセス構成（初期）

単一の .NET Generic Host。

| モード | 起動 | 役割 |
| --- | --- | --- |
| CLI | `melore index` / `melore search` / `melore eval` 等 | 人が操作する |
| MCP | `melore mcp`（stdio） | エージェントが操作する |

HTTP サーバーは置かない。MCP の HTTP トランスポートは将来オプション。

Ollama は別プロセス（既存のローカルデーモン、既定 `http://localhost:11434`）。Melore はクライアントに徹する。

## 3. 論理レイヤー

過剰な分散はしない。プロジェクト分割は責務の境界をコードで固定するためだけに行う。

| プロジェクト（予定） | 責務 | 依存してよいもの |
| --- | --- | --- |
| `Melore.Contracts` | DTO、ヒット、出典、関係の種別。MCP/CLI 共通 | なし |
| `Melore.Core` | チャンク化、検索統合、関係解決、鮮度、回答原則 | Contracts |
| `Melore.Infrastructure` | SQLite、FTS5、sqlite-vec、Git、ファイル I/O | Core のポート |
| `Melore.Parsing.Markdown` | 見出し・行番号付きチャンク | Core のポート |
| `Melore.Parsing.Roslyn` | シンボル・参照グラフ | Core のポート |
| `Melore.Providers.Ollama` | Embedding / Chat（OllamaSharp） | Core のポート |
| `Melore.Cli` | コマンド | 上記 |
| `Melore.Mcp` | MCP ツール | Core + Hosting |
| `Melore.Tests` | 単体・契約テスト | 各層 |

依存方向: Cli/Mcp → Core → Contracts。Infrastructure / Parsing / Providers は Core が定義するインターフェースの実装。Core は SQLite や Ollama の型を参照しない。

## 4. 交換可能なポート

```text
IDocumentParser
ICodeAnalyzer
ITestAnalyzer          // 初期は Roslyn + 命名規則。xUnit 属性は Phase 1 で読む
IGitHistory
IMetadataStore
IFullTextIndex
IVectorIndex
IEmbeddingGenerator    // Microsoft.Extensions.AI
IChatClient            // Microsoft.Extensions.AI（Phase 3）
IReranker              // Phase 3。既定は NoOp またはスコア合成のみ
```

検索エンジンの差し替えは `IFullTextIndex` / `IVectorIndex` で行う。初期実装は SQLite のみ。

## 5. インデックスパイプライン

```text
1. Git 現状リビジョンとファイル一覧を取得
2. 前回インデックスの (path, blob hash, analyzer version) と比較
3. 追加・変更ファイルだけをパーサへ
4. 削除ファイルの行・シンボル・ベクトルを除去
5. Documents / Symbols / Tests を SQLite に upsert
6. 確定 Relationships を再計算（同一ファイル内 using、ドキュメント相対リンク、テスト属性）
7. Embedding 対象テキストを生成しベクトル化
8. FTS5 を更新
9. インデックス世代（commit SHA + analyzer versions）を記録
```

全文を LLM に渡さない。Embedding はチャンク単位。生成モデルは Phase 3 の explain のみ。

## 6. クエリパイプライン

```text
クエリ
  ├ キーワード正規化
  ├ FTS5
  ├ Vector KNN
  ├ シンボル名の完全一致 / 前方一致
  └ 構造フィルタ（kind, path prefix, heading path）
        │
        ▼
ヒット統合（RRF）
        │
        ▼
関係展開（任意: references / trace）
        │
        ▼
出典付き DTO を返却（Phase 2）
        │
        ▼
根拠チャンクのみを LLM へ（Phase 3 explain）
```

explain に渡すコンテキストは、統合後の上位 N チャンクと関係エッジに限定する。リポジトリ全体や「関連ファイルの全文」は渡さない。

## 7. 出典

すべてのヒットと説明文は次を持つ。

- `repositoryId`
- リポジトリ相対パス
- `startLine` / `endLine`（1-origin、両端含む）
- `revision`（Git SHA）
- `kind`（document / symbol / test）
- `relationshipBasis`（`explicit` | `inferred`）。該当する場合のみ

行番号はパーサがファイル読み取り時に確定する。再計算でずれる場合はインデックス再構築対象とする。

## 8. Statevia を読むときの解析入力

| 入力 | 使い方 |
| --- | --- |
| `dotnet/statevia.sln` | Roslyn `MSBuildWorkspace` の初期解。コンポーネント別 sln は使わない |
| `docs/` | Markdown コーパス。仕様正本は `docs/specifications/` |
| `dotnet/**/*Tests*` | テストプロジェクト |
| `.git` | 差分とリビジョン |

`MSBuildWorkspace` は対象の `global.json`（.NET 10）と SDK がホストに必要。Melore 自身も net10.0 とし、同じ SDK で解析する。

## 9. セキュリティ境界

- 対象リポジトリは読み取り専用で開く。
- インデックス DB は Melore のデータディレクトリ（例: `%LOCALAPPDATA%\Melore\` または設定パス）。対象 Git 配下に書き込まない。
- `.env`、`*credentials*`、秘密鍵は除外リストでスキップする。
- MCP はローカル stdio。認証・テナントは持たない（単一利用者前提）。

## 10. 将来拡張を閉じないが今は作らないもの

- Tree-sitter パーサ（`ICodeAnalyzer` の別実装）
- HTTP MCP
- 複数リポジトリの同時インデックス（データモデル上 `repository_id` は持つが、CLI は単一パス）
- 専用 rerank プロセス
