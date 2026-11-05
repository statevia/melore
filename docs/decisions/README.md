# 技術選定（ADR）

| 項目 | 値 |
| --- | --- |
| 種別 | Decision |
| Version | 0.1 |
| 更新日 | 2026-11-02 |

各判断は採用・代替・制約をセットで書く。事実は 2026-11-02 時点の公開情報。

## ADR-001: 独立リポジトリとして Melore に置く

**採用:** Specification Service は独立した Git リポジトリ（Melore）に置く。Statevia にサブプロジェクトとして埋め込まない。

**理由:** 対象リポジトリを汚染しない。将来ほかの Git リポジトリもインデックスする。Statevia の Docker / PostgreSQL / テナントモデルと結合しない。

**代替:** Statevia の `dotnet/tools/` 配下。開発は速いが、独立サービスという目的に反する。

**制約:** 対象パスは設定が必要。CI で Statevia をサブモジュール化しない（ローカルパスを指す）。

## ADR-002: .NET 10（C#）

**採用:** TargetFramework `net10.0`。SDK は `global.json` で 10 系、`rollForward: latestFeature`。

**理由:** 2026-11-02 の現行安定版は .NET 10 LTS（SDK 10.0.403、Runtime 10.0.14、2026-10-25）。.NET 11 は RC3。Statevia も net10.0 のため、Roslyn 解析ホストと対象のランタイムが揃う。

**代替:** .NET 8（まだメンテナンスだが 2026-11-10 EOS）。対象が 10 のため解析が面倒。

**制約:** 開発機に .NET 10 SDK が必要。

## ADR-003: 単一プロセス + SQLite

**採用:** Generic Host 1 つ。メタデータ・FTS・ベクトルを同一 SQLite ファイル。

**理由:** 方針 8（過剰な分散をしない）。Windows ローカル、単一利用者。バックアップはファイルコピー。

**代替:** PostgreSQL + pgvector（Statevia と同じ）。運用と Docker が前提になり、Melore の「ローカル完結」が重い。Qdrant 等は別プロセスが増える。

**制約:** 同時ライターは SQLite のロックに従う。インデックス中の検索は読み取り一貫性を世代番号で表す。大規模化（数千万ベクトル）では再検討。

## ADR-004: 全文検索は FTS5

**採用:** SQLite FTS5。

**理由:** 追加サーバー不要。出典メタデータと同じトランザクションで更新できる。

**代替:** Lucene.NET。機能は多いが依存と運用が増える。初期コーパス規模には過剰。

**制約:** 日本語の形態素がない。仕様書の英語識別子・見出し・コードは強い。日本語本文の再現率は Phase 4 で測り、必要なら n-gram 補助を足す。

## ADR-005: ベクトルは sqlite-vec。sqlite-vss は使わない

**採用:** `sqlite-vec`（NuGet `sqlite-vec`、2026-11-02 時点 0.1.7-alpha.2.1 プレリリース）。必要なら `CommunityToolkit.VectorData.SqliteVec` を検討するが、初期は `Microsoft.Data.Sqlite` + 拡張ロードを直接扱う。

**理由:** `sqlite-vss` は開発停止、Faiss 依存、Windows 非対応（作者ブログおよびリポジトリ警告）。`sqlite-vec` は純 C で Windows 対応。

**代替:** メモリ内ブルートフォース（コーパスが小さい間）。実装は単純だが差分更新と永続化を別途書くことになる。

**制約:** プレリリース。`LoadExtension` はネイティブ DLL の配置に注意（RID 付き実行が必要になる場合がある）。Phase 1 の最初のスパイクで Windows ロードを検証し、失敗ならメモリ指数にフォールバックする判断点を置く。

## ADR-006: C# 解析は Roslyn。Tree-sitter は今やらない

**採用:** `Microsoft.CodeAnalysis.Workspaces.MSBuild` で `dotnet/statevia.sln` を開く。

**理由:** シンボル・参照・実装関係を正確に取る。Statevia は C# が中核。方針どおり複数言語は後回し。

**代替:** 正規表現 / テキスト分割。速いが参照関係が取れず、要件の trace を満たせない。

**制約:** ソリューション全体のコンパイルは重い。差分更新では変更ファイルと依存プロジェクトに限定する。生成コード・Source Generator 出力は除外設定を持つ。解析には MSBuild と対象 SDK が要る。

## ADR-007: Embedding は Ollama `qwen3-embedding:0.6b`

**採用:** 公式タグ `qwen3-embedding:0.6b`（639MB、32K コンテキスト、`/api/embed`）。論理モデルは HF `Qwen/Qwen3-Embedding-0.6B`（次元最大 1024、Instruction Aware）。

**理由:** 公式 Ollama に存在する。コード検索を謳っている。VRAM が小さく、16GB 上で生成モデルと共存しやすい。

**代替:** `qwen3-embedding:4b`（2.5GB）は精度向上の余地があるが、インデックス時間と VRAM が増える。初期は 0.6B。`latest` は 8b（4.7GB）を指すため、タグを `0.6b` に固定する。

**制約:** クエリ側は Instruction 付き、ドキュメント側は無し、が推奨。Ollama の embed API が instruction をどう渡すかは実装スパイクで確認し、未対応ならテキスト前に `Instruct: ...\nQuery:` を前置する。次元変更は全件再 embed。

## ADR-008: 生成は Ollama `qwen3.5:9b-q4_K_M`（Phase 3）

**採用:** 明示タグ `qwen3.5:9b-q4_K_M`（6.6GB、Q4_K_M、8.95B）。Phase 1–2 ではロードしない。

**理由:** ユーザー指定の Q4 初期候補と一致し、公式タグが存在する。`qwen3.5:9b` は 6.6–7.6GB でビジョン projector を含む。テキスト explain には Q4_K_M 明示が無駄が少ない。

**代替:** `qwen3.5:9b-q8_0`（10GB）は品質余地があるが KV と合算で 16GB が苦しい。`27b-q4_K_M`（17GB）は載らない。

**制約:** 表示コンテキスト 256K をフル使用しない。explain は取得チャンク合計を数千トークンに制限する。思考モードがある場合はエージェント向けに短文・非思考を既定にする（実装時に Ollama のオプションを実機確認）。

**実装（Phase 3）:** `OllamaChatClient` は `Think=false`、`num_ctx=8192`、`KeepAlive=0`（explain の間だけ chat をロードし、直後に unload）。プロンプトは検索上位最大 8 チャンク・合計約 8000 文字。

**VRAM 実測（2026-11-03、Windows、RTX 5070 Ti）:** `nvidia-smi` 合計 16303 MiB。未ロード時 約 2443–2497 MiB。`qwen3-embedding:0.6b`（`/api/embed`、次元 1024）と `qwen3.5:9b-q4_K_M`（`num_ctx=8192`、`think=false`）を同時ロードしたとき 14810 MiB。`ollama ps` は両モデルとも GPU 100%。embedding 側のコンテキスト表示は 32768 で、Ollama の SIZE 表示はいずれも 5.8 GB だった。16GB には収まる。Melore の explain は chat を `KeepAlive=0` で unload する。

**クライアント:** `OllamaSharp` + `Microsoft.Extensions.AI`。`Microsoft.Extensions.AI.Ollama` は非推奨。

## ADR-009: Reranker はポートだけ先に置き、Ollama では初期実装しない

**採用:** `IReranker`。既定は Reciprocal Rank Fusion（FTS とベクトルとシンボル一致の融合）のあと `NoOpReranker`（`IsModelBacked=false`）。`Qwen3-Reranker-0.6B` はゲート付きで未接続。

**理由:** モデルは Hugging Face に存在する。Ollama 公式には rerank API も公式モデルもない（issue #3368、#10989、#12601 のメンテナコメント）。コミュニティモデルを chat API で使うのはランキング用途として不正確。

**代替:** llama.cpp `--rerank` を別ポートで立てる。正確だが運用が二系統になるため Phase 4 候補。

**制約:** Phase 2 の品質は RRF に依存する。受け入れは「出典付きで関連が返る」を先に満たし、nDCG 改善は Phase 4。

## ADR-010: MCP は公式 C# SDK、stdio

**採用:** NuGet `ModelContextProtocol`（2.3.0 を計画時点の現行安定として記録）。Cursor からは stdio で起動。

**理由:** Microsoft / Anthropic / MCP 組織の公式 SDK。Statevia のエージェント利用形態（Cursor）と一致。HTTP は Web 公開になり、初期非目標に近い。

**代替:** JSON-RPC 自前実装。プロトコル追従コストが高い。

**制約:** stdio ではログを stdout に出さない（SDK 案内どおり stderr）。Cursor の `mcp.json` は Melore リポジトリ側にサンプルを置く。Statevia の `.cursor/mcp.json` を Melore 実装時まで変更しない。

## ADR-011: Git 操作

**採用:** 差分判定は `git rev-parse HEAD`、`git ls-files -s`、必要なら `git diff --name-only` をプロセス呼び出し。オブジェクト読みはワークツリーのファイル I/O。

**理由:** 追加ネイティブ依存を増やさない。Windows で Git は既にある。

**代替:** LibGit2Sharp。依存とバージョン固定が増える。初期は不要。

**制約:** 対象に Git が必要。未コミット変更はワークツリーのハッシュで dirty として再インデックスする。

## ADR-012: Markdown パーサ

**採用:** 見出し（ATX）でセクション分割し、行番号を自前で数える。CommonMark の完全実装より「行番号が正しいこと」を優先。必要なら Markdig で見出しとリンクを補助取得する。

**理由:** 要件の中心は出典行。AST 美より範囲の正確さ。

**代替:** ファイル全体を 1 チャンク。Statevia の仕様書は長く、LLM とベクトルの両方に不適。

**制約:** Setext 見出し、YAML front matter、HTML 混在は Phase 1 で簡易対応（front matter はスキップ、HTML はテキスト扱い）。

## ADR-013: テスト検出

**採用:** プロジェクト名 / パスに `Tests`、xUnit の `[Fact]` / `[Theory]`。

**理由:** Statevia のテストはこの形が中心（`dotnet/tests/`、各コンポーネントの `*.Tests`）。

**代替:** カバレッジデータ。初期は重い。

**制約:** 対象シンボルの推定は誤る。`basis=inferred` で保存し、explain では推定と明示する。
