# データモデル

| 項目 | 値 |
| --- | --- |
| 種別 | Architecture |
| Version | 0.1 |
| 更新日 | 2026-11-06 |

永続化は SQLite 1 ファイル。ベクトルは `sqlite-vec` の仮想テーブルに分離する。

## 1. 識別子

| 対象 | 形式 |
| --- | --- |
| 主キー | `TEXT` の ULID（時刻順、分散不要だがソートしやすい） |
| リポジトリ | 設定名 + 正規化絶対パスのハッシュを `repository_id` として安定化 |
| シンボル | リポジトリ内の Roslyn `ISymbol.ToDisplayString` を `fqn` として保持。主キーは ULID（リネームで FQN が変わるため） |
| 行 | 1-origin の整数。ファイル末尾の空行は含めない範囲でパーサが決める |

## 2. テーブル

### repositories

対象 Git リポジトリ。

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| name | TEXT | 表示名（例: `statevia`） |
| root_path | TEXT | インデックス時の絶対パス |
| indexed_revision | TEXT | 解析済み HEAD SHA |
| analyzer_versions | TEXT | JSON。markdown/roslyn/embedding の版 |
| updated_at | TEXT | ISO-8601 |

### files

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| repository_id | TEXT FK | |
| path | TEXT | リポジトリ相対、`/` 区切り |
| language | TEXT | `markdown` / `csharp` / `other` |
| git_blob_sha | TEXT | |
| last_write_utc | TEXT | |
| is_deleted | INTEGER | 論理削除（差分の監査用。通常クエリから除外） |

UNIQUE(repository_id, path)

### documents

Markdown ファイル単位のメタ。

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| file_id | TEXT FK | |
| title | TEXT | 先頭見出しまたはファイル名 |
| doc_kind | TEXT | `specification` / `concept` / `guide` / `architecture` / `other`（パス規則で付与） |

### document_chunks

検索の基本単位。見出しセクション。長すぎれば分割。

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| document_id | TEXT FK | |
| heading_path | TEXT | 例: `Specifications / execution / Fork Join` |
| spec_id | TEXT NULL | 見出しやメタデータから取れる識別子 |
| text | TEXT | チャンク本文 |
| start_line | INTEGER | |
| end_line | INTEGER | |
| ordinal | INTEGER | 文書内順序 |
| token_estimate | INTEGER | 概算。分割判定用 |

### symbols

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| file_id | TEXT FK | |
| kind | TEXT | `namespace` / `class` / `interface` / `struct` / `record` / `method` / `property` / `field` / `enum` / `enum_member` / `event` |
| fqn | TEXT | |
| name | TEXT | |
| containing_type_fqn | TEXT NULL | |
| signature | TEXT NULL | メソッド引数含む |
| accessibility | TEXT | |
| start_line | INTEGER | |
| end_line | INTEGER | |
| is_generated | INTEGER | |
| documentation_comment | TEXT NULL | XML doc 要約 |

### tests

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| symbol_id | TEXT FK | テストメソッドの symbol |
| framework | TEXT | `xunit` / `unknown` |
| fact_or_theory | TEXT NULL | |
| display_name | TEXT NULL | |
| inferred_target_fqn | TEXT NULL | Phase 1 は命名規則。Phase 4 で強化 |

### relationships

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| from_kind | TEXT | `document_chunk` / `symbol` / `test` |
| from_id | TEXT | |
| to_kind | TEXT | |
| to_id | TEXT | |
| rel_type | TEXT | 下記 |
| basis | TEXT | `explicit` / `inferred` |
| confidence | REAL | explicit は 1.0 |
| evidence | TEXT NULL | 短い根拠（リンク文字列、属性名など） |

`rel_type` の初期集合:

| rel_type | basis | 作り方 |
| --- | --- | --- |
| `doc_link` | explicit | Markdown の相対リンク |
| `implements` | explicit | Roslyn インターフェース実装 |
| `overrides` | explicit | Roslyn |
| `inherits` | explicit | Roslyn |
| `calls` | explicit | Roslyn 呼び出し（同一ソリューション内、解決できたもの） |
| `declares` | explicit | 型とメンバー |
| `tested_by` | explicit または inferred | `[Fact]` メソッドと対象。属性や完全名一致なら explicit |
| `specifies` | explicit または inferred | 見出し末尾が型名なら explicit。パス語幹の一致は inferred |
| `contradicts` | inferred | 仕様チャンク内のバッククォート識別子が対象型のメンバーに無いとき |

### git_commits / file_revisions

鮮度と差分更新のため、全履歴は持たない。

| テーブル | 内容 |
| --- | --- |
| git_index_state | 1 行。indexed_revision、前回成功時刻、dirty ファイル数 |
| file_revisions | path, blob_sha, indexed_at。差分判定の正本 |

コミットメッセージ全文の永久保存はしない。必要なら `git log` をその場で読む。

### embeddings / vec_chunks

| 列 | 内容 |
| --- | --- |
| chunk_ref | `document_chunk:{id}` または `symbol:{id}` |
| model | 例: `qwen3-embedding:0.6b` |
| dim | 1024 |
| embedding | `vec0` 仮想テーブル側 |

シンボルのベクトル化テキストは型名と `<summary>` / `<remarks>` とする。メソッド本体全文は埋め込まない。テストメソッドは埋め込み対象にしない。

### mcp_invocations

MCP ツール（`search` / `references` / `trace` / `explain`）の引数と応答。知識の検索対象にはしない。CLI は書かない。

| 列 | 内容 |
| --- | --- |
| id | TEXT PK |
| created_at | ISO-8601 |
| tool | ツール名 |
| arguments_json | エージェントが渡した引数 |
| response_json | クライアントへ返した JSON。256 KiB で切る |
| response_chars | 切る前の文字数 |
| duration_ms | 所要時間 |
| success | 1 または 0 |
| error | 失敗時のメッセージ |

### fts

FTS5 仮想テーブル。本文 + 見出し + FQN。

トークナイザは初期 `unicode61`。日本語は形態素解析を入れない（外部依存を増やさない）。クエリ側で文字 n-gram の補助が必要なら Phase 4。

## 3. 検索結果 DTO（論理）

```text
SearchHit
  id
  kind                 document_chunk | symbol | test
  title
  snippet              短い抜粋（既定）
  score
  sources[]            path, startLine, endLine, revision
  related[]            任意。関係を畳み込みすぎない
```

MCP 応答は配列を小さく保つ。既定 `limit=8`、snippet は数百文字。`includeBody=true` のときだけチャンク全文。

## 4. スキーマ進化

SQLite の `PRAGMA user_version` と、アプリケーションのマイグレーションスクリプト。EF Core は初期採用しない（埋め込み DB と仮想テーブルに対して過剰）。
