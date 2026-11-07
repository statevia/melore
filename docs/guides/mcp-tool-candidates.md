# MCP ツール追加候補

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.1 |
| 更新日 | 2026-11-06 |

いま公開しているのは `search` / `references` / `trace` / `explain`。契約を壊さず足す候補。優先は **索引にあるのに MCP に出ていないもの**。エージェントの流れは `status` → `search`（snippet）→ `get`（必要な ID だけ）→ 関係の種別指定、が説明を増やすより方針に合う。

## 1. 現行

| ツール | 役割 |
| --- | --- |
| `search` | 横断検索。出典必須。既定は snippet |
| `references` | まわりの文書・実装・テスト。`basis` 付き |
| `trace` | 所属・継承。呼び出しは limitation |
| `explain` | 検索上位だけを根拠に説明 |

## 2. すぐ足せる（薄いラッパ）

索引または CLI にすでにある。

| 候補 | 入力（案） | 出すもの | なぜ |
| --- | --- | --- | --- |
| `status` | （なし） | リビジョン、件数、embeddings の有無 | 古い索引で調べるのを止める |
| `get` | id、`includeBody` | 1 ヒット（snippet または本文） | `search` は短く、全文は ID 指定だけ |
| `contradicts` | チャンクまたは型、limit | `contradicts` 関係だけ | 仕様と実装の食い違いを明示する要件 |
| `specifies` | チャンクまたは型、limit | `specifies` 関係だけ | `references` から仕様↔型だけ取る |
| `tested_by` | 型、limit | `tested_by` 関係だけ | 型↔テストだけ取る |

`specifies` と `tested_by` は、1 ツール `related` に `relType` を付ける案でもよい。

## 3. 次点（検索の絞り）

| 候補 | 内容 |
| --- | --- |
| `search` のフィルタ | 計画にあった `kinds[]`、パス接頭辞、見出しパス。未実装 |
| 見出しアウトライン | `heading_path` の木。仕様書の構造を先に掴む |

## 4. 索引側の仕事が増えるもの

| 候補 | 前提 |
| --- | --- |
| `trace` の呼び出しグラフ（`calls`） | **新ツールではない。** 既存 `trace` の辺を埋める。データモデルの `calls`。いまは構文のみのため limitation。同一ソリューション内で解決できた呼び出しだけ `explicit`、循環は depth で切断、未解決は空＋ limitation |
| Git 差分 / dirty 一覧 | 索引以降に変わったファイル |
| YAML Definition の索引 | 要件では意図的に後回し |

## 5. 足さない方がよい

| 候補 | 理由 |
| --- | --- |
| MCP からの `index` | 長い、DB ロック、stdio を汚すリスク。`search` / `references` / `explain` は差分が 32 件以内のとき、ツールを増やさずそのファイルだけ内部で再索引する |
| ファイル全文読み | IDE と重複。短い抜粋が既定という方針に逆行 |
| `eval` | 人向け CLI のまま |

## 6. 足すときの約束

- Phase 2 の JSON 契約（`search` / `references` / `trace`）は壊さない。追加のみ。
- ヒットはパスと行範囲を持つ。
- 既定本文は snippet。全文は明示したときだけ。
- ログは stderr。stdout はプロトコルのみ。
