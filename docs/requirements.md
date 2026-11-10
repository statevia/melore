# 要件

| 項目 | 値 |
| --- | --- |
| 種別 | Requirements |
| Version | 0.2 |
| 更新日 | 2026-11-10 |

## 1. 目的

大規模なソースコードとドキュメントを持つ Git リポジトリについて、AI エージェントが少ないトークンで正確に仕様を調査・理解できるようにする。

Melore は既存の AI エージェントを置き換えない。エージェントに対して、構造化された知識と検索・説明機能を提供する。

最初のコーパスは [Statevia](https://github.com/statevia/statevia) とする。設計は特定リポジトリに依存させず、設定で対象パス・言語・除外規則を差し替えられるようにする。

## 2. 成功条件

エージェントが Statevia の仕様を調べるとき、リポジトリ全体や長大な Markdown をコンテキストに載せなくても、次ができる。

1. 自然言語またはキーワードで、仕様・実装・テストを横断検索できる。
2. 各ヒットにファイルパスと行番号がある。
3. 指定シンボルの呼び出し・実装関係を追跡できる。
4. 仕様項目に紐づく実装とテストを列挙できる。
5. 根拠のない推測で仕様を補完しない。不足と矛盾は明示する。

## 3. 利用者と利用形態

| 利用者 | 利用形態 |
| --- | --- |
| Cursor 等の AI エージェント | MCP ツール（search / references / trace / explain） |
| 開発者 | CLI（インデックス構築、検索、状態確認） |

Web UI、マルチテナント、クラウド SaaS は初期対象外。

## 4. 機能要件

### 4.1 知識の対象

独立した知識種別として扱う。

| 種別 | 内容 | 初期対応 |
| --- | --- | --- |
| Documents | Markdown のファイル、見出し階層、仕様項目、本文、行番号、更新情報 | Phase 1 |
| Code Symbols | C# の Namespace / Class / Interface / Method / Property / Enum 等。定義位置、所属ファイル、行番号、参照 | Phase 1 |
| Tests | テストクラス・メソッド、対象シンボル（判明時）、内容、ファイル・行番号 | Phase 1（検出）/ Phase 4（対象紐付け強化） |
| Relationships | 仕様↔実装、型階層、呼び出し、実装↔テスト、ドキュメント間参照。根拠が確定なものと推定を区別 | 確定関係は Phase 1–2、推定は Phase 4 |
| Git Information | ファイル変更、commit、差分、解析済みリビジョン。差分更新と鮮度管理 | Phase 1（基本）/ Phase 4（改善） |

見出しまたは仕様項目をドキュメントの基本検索単位とする。長すぎる場合は分割するが、分割チャンクにも元の見出しパスと行範囲を残す。

Statevia 初回コーパスの既定対象:

- ドキュメント: `docs/**/*.md`（特に `docs/specifications/`、`docs/concepts/`、`docs/architecture/`、`docs/guides/`）
- コード: `dotnet/**/*.cs`（生成物・`bin` / `obj` 除外）
- テスト: `dotnet/**/*Tests*/**/*.cs` および csproj 名に `Tests` を含むプロジェクト
- 除外: `artifacts/`、`.local/`、`.vs/`、`nodejs/**/node_modules/`、バイナリ、秘密情報ファイル

YAML 定義（Statevia の Definition）は初期は対象外。ドキュメントと C# 実装の横断を先に成立させる。

### 4.2 インデックス

- Markdown を見出し階層付きで解析する。
- C# を Roslyn で構造解析する。
- SQLite にメタデータと全文索引を保存する。
- Embedding によるベクトル検索を行う。
- Git のリビジョンとファイルハッシュで差分更新する。
- すべての検索単位がファイルパスと行範囲を持つ。

### 4.3 検索

次を組み合わせ、結果を統合する。出典は必須。

1. 全文検索（FTS5）
2. ベクトル検索
3. シンボル検索（名前、完全修飾名、種類）
4. 依存・参照検索
5. ドキュメント構造検索（見出しパス、仕様 ID）

単一コーパス内の文章類似だけでなく、関連する実装・テスト・仕様を横断して返す。

Reranker は Phase 3 以降。Ollama 公式の rerank API が無いため、初期は Reciprocal Rank Fusion（RRF）で統合し、専用 rerank は交換可能なポートとして残す。根拠は [decisions/README.md](decisions/README.md) ADR-009。

### 4.4 提供 API（MVP）

| 機能 | Phase | 振る舞い |
| --- | --- | --- |
| `search` | 2 | 自然言語 / キーワードで仕様・コードを検索し、構造化ヒットを返す |
| `references` | 2 | 仕様またはシンボルに関連するドキュメント・実装・テストを列挙する |
| `trace` | 2 | 機能・クラス・メソッドの呼び出し・依存を追跡する |
| `explain` | 3 | 検索結果のみを根拠に簡潔に説明する。ファイルと行番号を付ける |
| MCP | 2 | 上記を stdio MCP で公開する。Phase 2 では LLM 要約なし |

CLI はインデックス構築・更新・ステータス表示を Phase 1 から提供する。

### 4.5 回答生成の原則（Phase 3）

- インデックスから取得した情報だけを根拠にする。
- 根拠がない情報を推測で補完しない。
- 仕様書と実装が矛盾する場合は矛盾を明示する。
- 関連情報が不足している場合は不足を明示する。
- 推定関係と確定参照を区別して表示する。
- 回答は必要最小限とし、詳細は追加検索で段階取得できるようにする。

## 5. 非機能要件

| ID | 要件 |
| --- | --- |
| NFR-1 | Windows 上でローカル完結。既定で外部クラウド LLM / 埋め込み API を呼ばない |
| NFR-2 | 開発言語は C#。.NET の現行安定版は **.NET 10 LTS**（SDK 10.0.403 / Runtime 10.0.14、2026-10-25。2026-11-02 時点で .NET 11 は RC3） |
| NFR-3 | LLM / Embedding / 検索エンジンをインターフェースで交換できる。初期は過剰な分散化をしない（単一プロセス + SQLite） |
| NFR-4 | GPU は NVIDIA GeForce RTX 5070 Ti 16GB。モデル同時ロードは VRAM 予算内に収める |
| NFR-5 | エージェント向け応答はトークン量を抑える（ヒット本文の既定は短い抜粋。全文は明示要求時のみ） |
| NFR-6 | 対象リポジトリのソースを Melore リポジトリへコピーしない。パス参照とインデックスのみ |
| NFR-7 | 秘密情報（`.env`、接続文字列、トークン）をインデックスしない |

## 6. 制約（調査済みの事実）

推測ではなく、2026-11-02 時点の公開情報に基づく。

| 対象 | 事実 | 含意 |
| --- | --- | --- |
| 生成モデル | Ollama 公式 `qwen3.5:9b-q4_K_M` が存在する。重み 6.6GB、パラメータ 8.95B、量子化 Q4_K_M、コンテキスト表示 256K。`qwen3.5:9b` は latest と同一タグで 6.6–7.6GB（ビジョン projector を含む構成） | テキスト用途では明示タグ `qwen3.5:9b-q4_K_M` を初期採用。256K をフルに使うと KV キャッシュで 16GB を超えるため、explain は短いコンテキストに制限する |
| Embedding | Ollama 公式 `qwen3-embedding:0.6b`（639MB、32K、`/api/embed`）。HF `Qwen/Qwen3-Embedding-0.6B` は次元最大 1024、MRL 対応、Instruction Aware | Phase 1 から採用。次元は 1024 を既定とし、変更時はインデックス再構築 |
| Reranker | HF に `Qwen/Qwen3-Reranker-0.6B` はある。Ollama 公式ライブラリに reranker モデルはなく、`/api/rerank` も未提供（issue #3368 / #10989。メンテナコメントで API 未サポート） | Phase 3 で `IReranker` を置くが、既定実装は RRF。専用モデルは Ollama 外ランタイムが必要になった時点で追加 |
| ベクトル SQLite | `sqlite-vss` は開発停止で Windows 非対応。後継 `sqlite-vec` は純 C、Windows 対応。NuGet `sqlite-vec` 0.1.7-alpha.2.1（プレリリース）。CommunityToolkit.VectorData.SqliteVec もプレビュー | Phase 1 は `sqlite-vec` を採用し、ロード失敗時の検証を計画に含める。実装は `vec0` を使わず、BLOB の全件走査にした（ADR-005 の実測） |
| MCP | 公式 C# SDK `ModelContextProtocol` 2.3.0。stdio は本パッケージ、HTTP は `ModelContextProtocol.AspNetCore` | Phase 2 は stdio（Cursor 向け）。HTTP は初期対象外 |
| Ollama .NET | `Microsoft.Extensions.AI.Ollama` は非推奨。`OllamaSharp` が `IChatClient` / `IEmbeddingGenerator` を実装 | LLM・Embedding のアダプタは OllamaSharp + MEAI 抽象。MEAI の型は `Melore.Providers.Ollama` の中だけで使い、Core のポートは Melore 独自のインターフェースにした |

VRAM 概算（16GB、同時ロード時）:

| 構成 | 重み（公開サイズ） | 判定 |
| --- | --- | --- |
| Embedding のみ（インデックス） | 639MB | 余裕 |
| `qwen3.5:9b-q4_K_M` + Embedding | 6.6GB + 639MB | 短〜中コンテキストなら収まる |
| 上記 + 公式 Ollama rerank | 不可 | API が無い |
| `qwen3.5:9b-q8_0`（10GB）+ Embedding | 約 11GB+KV | 長コンテキストは厳しい。初期は採用しない |
| `qwen3.5:27b-q4_K_M`（17GB） | 17GB | 16GB に入らない |

## 7. 非目標（初期）

- 独自 AI エージェント / ワークフローエンジン
- Web UI
- クラウドサービス、複数ユーザー・テナント
- 自動コード修正
- LLM によるリポジトリ全体の完全理解
- 複数言語の本格対応（Tree-sitter は将来検討）
- Statevia 本体への機能埋め込み
- YAML Definition の意味解析（初期）

## 8. Statevia 側の規約との関係

Melore は独立リポジトリとする。Statevia のコードや `docs/` 契約を改変しない。

実装時に **合わせる** もの（Melore 自身のコード規約）:

- C# は `dotnet/` 配下。ルートに csproj を置かない
- `global.json` で SDK 10 系（`rollForward: latestFeature`）
- `Directory.Build.props` で `net10.0` / Nullable / ImplicitUsings
- Central Package Management（`Directory.Packages.props`）
- テストは AAA、1 テスト 1 シナリオ。公開 API に XML ドキュメント
- プライマリコンストラクタ優先、SQL はパラメータ化

Statevia 固有で **持ち込まない** もの:

- PostgreSQL / EF Core 実行基盤、テナント、Docker 本番構成
- `Guid.NewGuid` 禁止ルール（Statevia の ID 設計に依存）
- コンポーネント別 6 sln / Sonar コンポーネント分割（初期は 1 sln）
- Studio（Next.js）

Statevia をコーパスとして読むときの注意:

- 仕様の正本は `docs/specifications/`。実装は `dotnet/`
- エージェント索引は `AGENTS.md` だが、契約の正本は `docs/`
- IDE 用包括解は `dotnet/statevia.sln`。Roslyn の初期入力はこれを使う
