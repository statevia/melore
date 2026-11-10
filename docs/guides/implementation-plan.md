# 実装計画

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.2 |
| 更新日 | 2026-11-10 |

§0 から §7 は、実装を始める前（2026-11-02）に決めたリポジトリ構成案と順序で、当時の記述のまま残してある。Phase 1 から 4 の実装は済んでいる。計画から変わった点と残りは [§8](#8-現状2026-11-10)。現行の構成は [architecture/overview.md](../architecture/overview.md) を正とする。

## 0. Melore リポジトリの目標構成

Statevia に合わせ、言語ルートを分ける。初期に Node は置かない。

```text
Melore/
├─ README.md
├─ AGENTS.md                          # 実装開始時に追加。起動・テストの薄い索引
├─ global.json                        # SDK 10.x, rollForward: latestFeature
├─ docs/                              # 本設計一式
├─ dotnet/
│  ├─ Melore.slnx
│  ├─ Directory.Build.props           # net10.0, Nullable, ImplicitUsings
│  ├─ Directory.Packages.props
│  ├─ .editorconfig
│  ├─ src/
│  │  ├─ Melore.Contracts/
│  │  ├─ Melore.Core/
│  │  ├─ Melore.Infrastructure/
│  │  ├─ Melore.Parsing.Markdown/
│  │  ├─ Melore.Parsing.Roslyn/
│  │  ├─ Melore.Providers.Ollama/
│  │  ├─ Melore.Cli/
│  │  └─ Melore.Mcp/
│  └─ tests/
│     └─ Melore.Tests/
└─ samples/
   └─ cursor-mcp.json.example
```

コーディング規約は Statevia の C# 方針に合わせる（プライマリコンストラクタ、公開 API の XML ドキュメント、テスト AAA、SQL パラメータ化）。Statevia の EF / テナント / `Guid.NewGuid` 禁止は持ち込まない。

設定ファイル例（実装時）:

```json
{
  "repositoryName": "statevia",
  "repositoryPath": "/absolute/path/to/statevia",
  "include": ["docs/**/*.md", "dotnet/**/*.cs"],
  "exclude": ["**/bin/**", "**/obj/**", "artifacts/**", ".local/**", "nodejs/**/node_modules/**", "**/.env"],
  "solutionPath": "dotnet/statevia.sln",
  "ollama": {
    "endpoint": "http://localhost:11434",
    "embeddingModel": "qwen3-embedding:0.6b",
    "chatModel": "qwen3.5:9b-q4_K_M"
  }
}
```

## 1. Phase 1 — インデックスと検索

目的: エージェントなしで、CLI から出典付き検索ができる。

### 1.1 スパイク（最初の実装コミット群の前に必須）

実装本流に入る前に、失敗したら計画を更新する検証。

| ID | 検証 | 失敗時 |
| --- | --- | --- |
| S1 | Windows で `Microsoft.Data.Sqlite` から `sqlite-vec` を LoadExtension し、1024 次元で KNN | メモリ内コサイン検索にフォールバックし ADR-005 を更新 |
| S2 | Ollama `qwen3-embedding:0.6b` の `/api/embed` が返す次元と VRAM | 次元が 1024 でなければスキーマを実測に合わせる |
| S3 | `MSBuildWorkspace` で対象の `dotnet/statevia.sln` が開く | コンポーネント sln の逐次オープンに変更 |
| S4 | FTS5 で日本語仕様見出しがヒットするか | n-gram 列を Phase 1 に前倒し |

### 1.2 作業順

1. `dotnet/` スケルトン、CPM、`Melore.Contracts` のヒット型
2. SQLite スキーマとマイグレーション（FTS5 まで。vec は S1 後）
3. Git ファイル列挙と blob ハッシュ差分
4. Markdown パーサ（見出し、行範囲、相対リンク）
5. Roslyn シンボル抽出（定義位置）。参照グラフは同 Phase の後半
6. テスト `[Fact]` / `[Theory]` 検出
7. FTS5 投入と `melore search --fts`
8. Ollama embed とベクトル検索 `melore search --semantic`
9. 統合検索（RRF）`melore search`
10. `melore index` の差分更新と `melore status`（revision、件数、dirty）

### 1.3 Phase 1 完了の定義

[evaluation.md](../evaluation.md) の AC-P1 を満たす。MCP と LLM はまだない。

### 1.4 明示的にやらないこと

explain、MCP、関係の LLM 推定、YAML 定義解析、Studio / Node の索引。

## 2. Phase 2 — MCP

目的: Cursor が構造化 JSON をツール結果として受け取れる。

### 2.1 作業順

1. `Melore.Mcp` + `ModelContextProtocol` + stdio
2. ログは stderr
3. ツールは次表
4. `samples/cursor-mcp.json.example`
5. ヒット本文の既定は snippet。`detail=full` でチャンク全文

| ツール | 入力（概要） | 出力 |
| --- | --- | --- |
| `search` | query, limit, kinds[] | SearchHit[]（出典必須） |
| `references` | symbol または documentChunkId | 関連ヒット。basis を付ける |
| `trace` | fqn または symbolId, depth | 呼び出し・実装エッジ。循環は切断 |

追加候補は [mcp-tool-candidates.md](mcp-tool-candidates.md)。Phase 2 の契約は壊さない。

### 2.2 Phase 2 完了の定義

AC-P2。explain は未実装でよい。

## 3. Phase 3 — ローカル LLM

目的: 検索結果だけを根拠にした explain。

### 3.1 作業順

1. `OllamaSharp` で `IChatClient`（モデル `qwen3.5:9b-q4_K_M`）
2. 実機 VRAM: embed 常駐 + chat の同時ロードを計測。溢れたら explain 時だけ chat をロード
3. `explain` ツール: 内部で search → 上位チャンクのみプロンプト
4. システム指示: 根拠外の補完禁止、矛盾と不足の明示、出典列挙
5. `IReranker` インターフェース。既定 RRF。専用モデルはゲート付き

### 3.2 Phase 3 完了の定義

AC-P3。Reranker モデル本体は必須ではない。

## 4. Phase 4 — 精度

目的: 横断関係と鮮度、検索漏れの計測。

1. `specifies` 関係（規則ベースを先に: パス対応、型名の見出し一致。LLM 推定は `inferred`）
2. 仕様チャンクと実装の矛盾候補（名前は一致するが説明とシグネチャが食い違う）
3. ゴールデンセットによる Recall@k / 出典精度
4. Git 差分: リネーム検出、未コミット dirty、analyzer version 変更時の全再構築
5. 日本語 FTS のギャップを測定し、必要なら n-gram

実装メモ: 関係再構築は `RelationshipIndexer`。検索は `QueryNormalizer` の助詞除去と短いシノニム。漏れの記録は [search-gaps.md](search-gaps.md)。n-gram 列は未導入。

## 5. マイルストーンと依存

```text
S1–S4 スパイク
    → Phase 1 CLI 検索
        → Phase 2 MCP
            → Phase 3 explain
                → Phase 4 精度
```

Phase 2 は Phase 1 の統合検索に依存。Phase 3 は Phase 2 のツール契約を壊さない（explain 追加のみ）。

## 6. テスト方針（実装時）

| 層 | 内容 |
| --- | --- |
| パーサ | フィクスチャ Markdown / 小さな C# で行番号が安定 |
| ストア | 一時 SQLite。upsert と削除 |
| 検索 | ゴールデンクエリ（evaluation.md） |
| MCP | ツール JSON の契約テスト（ホスト起動は最小） |

Ollama 実機テストはカテゴリで分離し、CI が GPU を持たない場合は skip する。

## 7. 実装開始時の最初のコミット案

計画承認後:

1. `dotnet/` スケルトンとリポジトリルートの `global.json`
2. `AGENTS.md`（ビルドとテストコマンド）
3. スパイク S1 の小さなコンソール

それ以外のプロダクトコードはスパイク結果を見てから書く。

## 8. 現状（2026-11-10）

| Phase | 状態 | 計画から変わった点 |
| --- | --- | --- |
| 1 インデックスと検索 | 実装済み | ベクトルは `sqlite-vec` の `vec0` を使わず、BLOB の全件走査（S1 はロード成功。ADR-005）。`search` の `--fts` / `--semantic` は作らず、統合検索だけ。シンボルは構文解析で取り、ソリューションを開くのは `calls` / `implements` だけ（S3。ADR-006）。プロジェクトに `Melore.Host` を足した |
| 2 MCP | 実装済み | `search` の `kinds[]` は未実装。本文の指定は `detail=full` ではなく `includeBody=true`。`search` と `explain` に `scope` を足した |
| 3 ローカル LLM | 実装済み | Core のポートは `IChatClient` ではなく Melore 独自の `IMeloreChatClient`。chat は `explain` の間だけロードする |
| 4 精度 | 実装済み。受け入れは未達 | `specifies` は規則だけで作り、LLM の推定は使っていない。矛盾候補は `contradicts`（仕様のバッククォート名が型のメンバーに無い）。n-gram は未導入 |

受け入れ基準の達成状況は [evaluation.md](../evaluation.md) の §4、いまの数値は同 §9。これからの作業と優先度は [agent-task-accuracy-plan.md](agent-task-accuracy-plan.md) の §4 に置く。この文書には足さない。
