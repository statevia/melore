# アーキテクチャ概要

| 項目 | 値 |
| --- | --- |
| 種別 | Architecture |
| Version | 0.4 |
| 更新日 | 2026-11-10 |

## 1. 位置づけ

```text
[Cursor 等のエージェント]
        │ MCP stdio
        ▼
[Melore]  ← 起動ごとに単一プロセス
   ├ CLI (index / status / search / references / trace / explain / eval)
   ├ MCP tools (search / references / trace / explain)
   ├ Query services (search / references / trace / explain)
   ├ Indexers (Markdown / Roslyn / Git)
   ├ Providers (Embedding / Chat。Rerank は NoOp)
   └ SQLite (metadata + FTS5 + ベクトル BLOB)
        │ 読み取りのみ
        ▼
[対象 Git リポジトリ]  例: melore.json の repositoryPath
```

Melore は対象リポジトリの外で動く。対象は設定の `RepositoryPath` で指す。

## 2. プロセス構成

実行ファイルは 2 つ。どちらも `Melore.Host` の `MeloreRuntime` で同じ部品を組み立て、同じ索引 DB を開く。常駐するサーバーは持たない。

| モード | 起動 | 役割 |
| --- | --- | --- |
| CLI | `melore index` / `melore search` / `melore eval` 等。コマンドごとに起動して終わる | 人が操作する |
| MCP | `melore-mcp --config <file>`（stdio、.NET Generic Host）。エージェントのクライアントが子プロセスで起動する | エージェントが操作する |

HTTP サーバーは置かない。MCP の HTTP トランスポートは将来オプション。

Ollama は別プロセス（既存のローカルデーモン、既定 `http://localhost:11434`）。Melore はクライアントに徹する。

## 3. 論理レイヤー

過剰な分散はしない。プロジェクト分割は責務の境界をコードで固定するためだけに行う。

| プロジェクト | 責務 | 依存してよいもの |
| --- | --- | --- |
| `Melore.Contracts` | DTO、ヒット、出典、関係の種別、設定。MCP/CLI 共通 | なし |
| `Melore.Core` | 索引の手順、検索統合、関係の再構築、鮮度、回答原則、eval | Contracts |
| `Melore.Infrastructure` | SQLite、FTS5、ベクトル BLOB、Git、ファイル I/O | Core のポート |
| `Melore.Parsing.Markdown` | 見出し・行番号付きチャンク | Core のポート |
| `Melore.Parsing.Roslyn` | シンボル（構文解析）と、`calls` / `implements`（意味解析） | Core のポート |
| `Melore.Providers.Ollama` | Embedding / Chat（OllamaSharp） | Core のポート |
| `Melore.Host` | 設定の読み込みと検証（`SettingsLoader`）、部品の組み立て（`MeloreRuntime`） | 上記すべて |
| `Melore.Cli` | コマンド | Core + Host |
| `Melore.Mcp` | MCP ツール | Core + Host |
| `Melore.Tests` | 単体・契約テスト | 各層 |

依存方向: Cli/Mcp → Core → Contracts。Infrastructure / Parsing / Providers は Core が定義するインターフェースの実装。Core は SQLite や Ollama の型を参照しない。実装を選んでつなぐのは `Melore.Host` だけで、Cli と Mcp は Infrastructure / Parsing / Providers を直接参照しない。

## 4. 交換可能なポート

`Melore.Core` の `Ports` にある。

| ポート | 役割 | 実装 |
| --- | --- | --- |
| `IDocumentParser` | Markdown を見出し単位のチャンクにする | `MarkdownDocumentParser` |
| `ICodeAnalyzer` | C# からシンボルとテストを取る（構文解析） | `RoslynSyntaxAnalyzer` |
| `ISemanticGraphExtractor` | ソリューションから `calls` / `implements` を取る（意味解析） | `RoslynSemanticGraph` |
| `IGitWorkspace` | ファイル列挙、リビジョン、作業ツリーの差分 | `GitWorkspace` |
| `IMeloreStore` | メタデータ・全文検索・ベクトル・関係・MCP の利用記録 | `SqliteMeloreStore` |
| `IEmbeddingClient` | テキストのベクトル化 | `OllamaEmbeddingClient`。使えないときは `NullEmbeddingClient` |
| `IMeloreChatClient` | `explain` の文章生成 | `OllamaChatClient`。使えないときは `NullChatClient` |
| `IReranker` | 統合後のヒットの並べ替え | `NoOpReranker`（並べ替えない） |

`IEmbeddingClient` と `IMeloreChatClient` は Melore 独自のインターフェースで、Core は `Microsoft.Extensions.AI` を参照しない。`Melore.Providers.Ollama` の中で OllamaSharp（`Microsoft.Extensions.AI` の抽象を実装している）を呼ぶ。

メタデータ・全文検索・ベクトル検索は `IMeloreStore` 1 つにまとめてある。全文検索やベクトル検索だけを別のエンジンに替えるポートは無く、替えるときは `IMeloreStore` を分ける改修が先に要る。テストの検出は `ICodeAnalyzer` が兼ねる。

## 5. インデックスパイプライン

`IndexService.IndexAsync` の順。

```text
1. Git の HEAD と、include / exclude に合うファイル一覧（作業ツリーの blob SHA 付き）を取得
2. analyzer versions が前回と違えば、埋め込みを消して全ファイルを解析対象にする
3. リネーム（git diff -M）で内容が同じものは、解析し直さずパスだけ付け替える
4. 無くなったファイルの行・シンボル・ベクトルを除去
5. blob SHA が変わったファイルだけをパーサへ。Documents / Symbols / Tests と FTS5 を同じ手順で更新
6. 全ファイルの区分（lifecycle）を付け直す
7. specifies / tested_by / contradicts を全体で再構築（RelationshipIndexer）
8. solutionPath を開けたら calls / implements を入れ替える（開けなければ飛ばして続行）
9. Ollama が使えれば、埋め込みの無いチャンクとシンボルをベクトル化
10. インデックス世代（commit SHA + analyzer versions）を記録
```

`declares` / `inherits` / `doc_link` は 5 でファイルごとに入る。途中で失敗すると 10 に進まず、世代は前回のまま残る。

`search` / `references` / `explain`（MCP は `trace` も）の前には、作業ツリーの差分（未追跡を含む）が 32 件以内ならそのファイルだけ 5 と 9 を行う（`IndexService.EnsureFreshAsync`）。このときは 7 と 8 を行わない。

全文を LLM に渡さない。Embedding はチャンク単位。生成モデルを使うのは `explain` だけ。

## 6. クエリパイプライン

```text
クエリ
  ├ 正規化（助詞・機能語を落とす）とシノニム展開（最大 3 通り）
  ├ FTS5（文書チャンクとシンボル。トークンの AND）
  ├ シンボル名・FQN の部分一致
  └ ベクトル（クエリを埋め込み、全件のコサイン類似度。Ollama が使えるときだけ）
        │  どの経路もスコープ（区分）で候補を絞る
        ▼
ヒット統合（RRF）
        │
        ▼
Reranker（既定は NoOp）
        │
        ▼
テストの順位下げ（クエリにテストの語が無いとき）
        │
        ▼
出典付き DTO を返却（search）
        │
        ▼
根拠チャンクのみを LLM へ（explain）
```

kind・パス接頭辞・見出しパスで絞るフィルタは未実装（[mcp-tool-candidates.md](../guides/mcp-tool-candidates.md)）。`references` と `trace` は検索の後段ではなく、ID か名前を受けて関係テーブルをたどる別のツール。

explain に渡すコンテキストは、統合後の上位チャンク（最大 8 件、合計約 8000 文字）に限定する。関係エッジは渡していない。リポジトリ全体や「関連ファイルの全文」は渡さない。

## 7. 出典

すべてのヒットと説明文は次を持つ。

- `repositoryId`
- リポジトリ相対パス
- `startLine` / `endLine`（1-origin、両端含む）
- `revision`（Git SHA）
- `kind`（document / symbol / test）
- `basis`（`explicit` | `inferred`）。関係をたどったヒット（`references` / `trace`）だけ
- `lifecycle`（`current` | `archived` | `rejected`）。出典ファイルが現行か、完了・却下した履歴文書か

検索は既定で `current` だけを候補にする。履歴文書は `scope=history`（または `all`）を指定したときだけ返す。区分はパスと `melore.json` の `history` の glob で決め、文書のヘッダーは見ない。

行番号はパーサがファイル読み取り時に確定する。再計算でずれる場合はインデックス再構築対象とする。

## 8. Statevia を読むときの解析入力

| 入力 | 使い方 |
| --- | --- |
| `dotnet/statevia.sln` | `calls` / `implements` を取るときに Roslyn `MSBuildWorkspace` で開く。コンポーネント別 sln は使わない。シンボルの抽出はファイル単位の構文解析で、ソリューションを使わない |
| `docs/` | Markdown コーパス。仕様正本は `docs/specifications/` |
| `dotnet/**/*Tests*` | テストプロジェクト |
| `.git` | 差分とリビジョン |

`MSBuildWorkspace` は対象の `global.json`（.NET 10）と SDK がホストに必要。Melore 自身も net10.0 とし、同じ SDK で解析する。ソリューションを開けないときは `calls` / `implements` が空になるだけで、`index` は続く。

## 9. セキュリティ境界

- 対象リポジトリは読み取り専用で開く。
- 読むのは `repositoryPath` の配下だけ。ただし、`repositoryPath` の配下に利用者が置いたリンク（ジャンクション・シンボリックリンク）は、その先も読む。Statevia の worktree（例: `statevia-main`）では、Git の管理外の `.spec/specs` などが本体の clone へのジャンクションになっているためである。glob（include / exclude / `history`）に絶対パスや `..` を書いて外を指すことはできない。
- `.gitignore` の対象ファイルは `git status --porcelain` に出ないので、検索前の自動の再索引では拾われない。編集や移動は次の `index` で反映される。
- インデックス DB は Melore のデータディレクトリ（例: `%LOCALAPPDATA%\Melore\` または設定パス）。対象 Git 配下に書き込まない。
- `.env`、`*credentials*`、秘密鍵は除外リストでスキップする。
- MCP はローカル stdio。認証・テナントは持たない（単一利用者前提）。

## 10. 将来拡張を閉じないが今は作らないもの

- Tree-sitter パーサ（`ICodeAnalyzer` の別実装）
- HTTP MCP
- 複数リポジトリの同時インデックス（データモデル上 `repository_id` は持つが、CLI は単一パス）
- 専用 rerank プロセス
