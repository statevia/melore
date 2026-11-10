# データモデル

| 項目 | 値 |
| --- | --- |
| 種別 | Architecture |
| Version | 0.4 |
| 更新日 | 2026-11-10 |

永続化は SQLite 1 ファイル。ベクトルは `embeddings` テーブルの BLOB に置き、検索時に C# で全件のコサイン類似度を取る（ADR-005）。

## 1. 識別子

| 対象 | 形式 |
| --- | --- |
| 主キー | `TEXT` の UUID v7（ハイフンなし 32 文字。時刻順に近く、ソートしやすい）。`Ids.New()` |
| リポジトリ | 設定の `repositoryName` で 1 行を引く。無ければ主キーを新しく振る。パスは識別に使わない |
| シンボル | 構文から組み立てた完全修飾名を `fqn` として保持する（メソッドは引数の型を含む）。主キーは UUID v7（リネームで FQN が変わるため） |
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
| analyzer_versions | TEXT | `;` 区切りの文字列。例: `markdown:1;roslyn-syntax:3;embed:qwen3-embedding-0.6b;rel:1` |
| updated_at | TEXT | ISO-8601 |

### files

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| repository_id | TEXT FK | |
| path | TEXT | リポジトリ相対、`/` 区切り |
| language | TEXT | `markdown` / `csharp`。それ以外の拡張子は索引しない |
| git_blob_sha | TEXT | |
| last_write_utc | TEXT | |
| is_deleted | INTEGER | 論理削除の列。検索は 0 の行だけを見る。いまの削除は行ごと消すので、1 になる行は無い |
| lifecycle | TEXT | 区分。`current` / `archived` / `rejected`。既定 `current`。パスと `melore.json` の `history` で決め、`index` のたびに付け直す |

UNIQUE(repository_id, path)、INDEX(repository_id, lifecycle, is_deleted)

`lifecycle` はスキーマ版 3 で足した。版 2 の DB を開くと列を足し、既存の行は `current` になる（次の `index` で付け直す）。検索は全文・シンボル・ベクトルのそれぞれで、スコープに対応する区分だけを候補にする。

### documents

Markdown ファイル単位のメタ。

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| file_id | TEXT FK | |
| title | TEXT | 先頭見出しまたはファイル名 |
| doc_kind | TEXT | `specification` / `concept` / `guide` / `architecture` / `other`（パス規則で付与） |

### document_chunks

検索の基本単位。見出しセクション。8000 文字を超えるセクションは段落の境目で分割する。

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| document_id | TEXT FK | |
| heading_path | TEXT | 例: `Specifications / execution / Fork Join` |
| spec_id | TEXT NULL | 見出しやメタデータから取れる識別子。いまのパーサは入れていない（全行 NULL） |
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
| kind | TEXT | `class` / `interface` / `struct` / `record` / `enum` / `enum_member` / `method` / `property` / `event` |
| fqn | TEXT | |
| name | TEXT | |
| containing_type_fqn | TEXT NULL | |
| signature | TEXT NULL | メソッド引数含む |
| accessibility | TEXT | |
| start_line | INTEGER | |
| end_line | INTEGER | |
| is_generated | INTEGER | いまの解析器は常に 0 |
| documentation_comment | TEXT NULL | XML doc 要約 |

### tests

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| symbol_id | TEXT FK | テストメソッドの symbol |
| framework | TEXT | `xunit` |
| fact_or_theory | TEXT NULL | いまは入れていない |
| display_name | TEXT NULL | テストメソッドの名前 |
| inferred_target_fqn | TEXT NULL | いまは入れていない。対象の型は `relationships` の `tested_by` で持つ |

### relationships

| 列 | 型 | 内容 |
| --- | --- | --- |
| id | TEXT PK | |
| from_kind | TEXT | `document_chunk` / `symbol` |
| from_id | TEXT | |
| to_kind | TEXT | `symbol` / `file`（`doc_link`）/ `type_name`（`inherits`） |
| to_id | TEXT | `to_kind` が `symbol` なら ID、`file` ならリンク先のパス、`type_name` なら基底型の名前 |
| rel_type | TEXT | 下記 |
| basis | TEXT | `explicit` / `inferred` |
| confidence | REAL | explicit は 1.0 |
| evidence | TEXT NULL | 短い根拠（リンク文字列、属性名など） |

`rel_type` の集合:

| rel_type | basis | 作り方 |
| --- | --- | --- |
| `doc_link` | explicit | Markdown の相対リンク |
| `implements` | explicit | Roslyn の意味解析。インターフェース実装（同一ソリューション内、解決できたもの） |
| `inherits` | inferred（0.6） | 構文解析で読んだ基底型リストの名前。型を解決していないので、基底クラスとインターフェースを区別せず、相手は名前のまま持つ |
| `calls` | explicit | Roslyn の意味解析。呼び出し（同一ソリューション内、解決できたもの） |
| `declares` | explicit | 型とメンバー |
| `tested_by` | explicit または inferred | `[Fact]` メソッドと対象。属性や完全名一致なら explicit |
| `specifies` | explicit または inferred | 見出し末尾が型名なら explicit。パス語幹の一致は inferred |
| `contradicts` | inferred | 仕様チャンク内のバッククォート識別子が対象型のメンバーに無いとき |

`calls` と `implements` は、`solutionPath` を開けた `index` のたびに全件を入れ替える。`overrides` は抽出していない。

### 鮮度の情報

Git の履歴やコミットを入れるテーブルは持たない。差分の判定に使うのは次の 2 つだけ。

| 場所 | 内容 |
| --- | --- |
| `files.git_blob_sha` | 索引した時点のファイル内容のハッシュ。作業ツリーのファイルを `git hash-object` で計算する。差分判定の正本 |
| `repositories.indexed_revision` / `analyzer_versions` / `updated_at` | 最後に成功した索引の HEAD、解析器の版、時刻 |

コミットメッセージは保存しない。必要なら `git log` をその場で読む。

### embeddings

| 列 | 内容 |
| --- | --- |
| id | TEXT PK |
| chunk_id | 文書チャンクまたはシンボルの ID。UNIQUE |
| kind | `DocumentChunk` / `Symbol` |
| model | 例: `qwen3-embedding:0.6b` |
| dim | 1024 |
| vector | float32 の BLOB |

文書チャンクのベクトル化テキストは、見出しパスと本文（先頭 4000 文字まで）。シンボルは `class` / `interface` / `record` / `struct` / `method` だけが対象で、テキストは名前とドキュメントコメント（`<summary>` / `<remarks>`。先頭 2000 文字まで）。メソッド本体は埋め込まない。テストメソッドは埋め込み対象にしない。

Statevia（2026-11-09 の索引）では 16,802 件（文書チャンク 10,724、シンボル 6,078）。既定のスコープで走査するのは、そのうち現行の 8,304 件。

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

### document_fts / symbol_fts

FTS5 仮想テーブル。`document_fts` は見出しパス・`spec_id`・本文、`symbol_fts` は名前・FQN・シグネチャ・ドキュメントコメントを持つ。

トークナイザは `unicode61 tokenchars '$'`（`$` をトークンにする。スキーマ版 2）。日本語は形態素解析を入れない（外部依存を増やさない）。文字 n-gram の補助は未導入（[search-gaps.md](../guides/search-gaps.md)）。

### インデックス

主キーと UNIQUE 制約が作るもののほかに、次の 2 つを持つ。

| 名前 | 対象 | 用途 |
| --- | --- | --- |
| `idx_files_repo_lifecycle` | `files (repository_id, lifecycle, is_deleted)` | スコープ（区分）での絞り込み。スキーマ版 3 |
| `idx_symbols_file` | `symbols (file_id)` | 区分で絞ったファイルからシンボルを引く。`Initialize` のたびに `IF NOT EXISTS` で作るので、既存の DB にも版を上げずに入る |

`idx_symbols_file` が無いと、シンボル名の部分一致がファイルごとに `symbols` を全件走査する。スキーマ版 3 で区分の絞り込みを足したときに実行計画がこの形になり、Statevia（シンボル 10,560 件、現行の C# ファイル 1,068 件）で `search` 1 回に 3〜15 秒かかっていた。足した後は同じ SQL が 13 ミリ秒以下（2026-11-10 実測）。

## 3. 検索結果 DTO（論理）

```text
SearchHit
  id
  kind                 DocumentChunk | Symbol | Test
  title
  snippet              短い抜粋（280 文字まで）
  score
  source               path, startLine, endLine, revision
  lifecycle            current | archived | rejected
```

MCP と CLI が返すのは、これを平らにした `HitDetail`（`path` / `startLine` / `endLine` / `revision` を直接持ち、`body`・`relType`・`basis` は該当するときだけ付く）。`relType` と `basis` が付くのは `references` のヒット。

MCP 応答は配列を小さく保つ。既定 `limit=8`。`includeBody=true` のときだけチャンク全文。

## 4. スキーマ進化

SQLite の `PRAGMA user_version`（いまは 3）と、`SqliteMeloreStore` の中のマイグレーション手順。2 で FTS の列構成を作り直し、3 で `files.lifecycle` を足した。EF Core は採用しない（埋め込み DB と仮想テーブルに対して過剰）。
