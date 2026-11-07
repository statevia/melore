# 検索漏れと対応

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.1 |
| 更新日 | 2026-11-06 |

Phase 4 で測った FTS / 統合検索のギャップ。コーパスは Statevia。ゴールデンの実行は `melore eval`。

## 1. いま入れたもの

| ギャップ | 対応 |
| --- | --- |
| 日本語クエリの助詞が FTS AND を殺す（「Fork Join の仕様」） | `QueryNormalizer` が助詞・機能語を落とす |
| 用語の揺れ（シナリオテスト、テナントヘッダ、zip レイアウト、ログマスキング、Guid 禁止） | 短いシノニムを Expand し RRF に載せる |
| `$` がトークンにならない | FTS `tokenchars '$'`（スキーマ v2） |
| 仕様↔型、テスト↔型が関係テーブルに無い | `specifies` / `tested_by` を explicit と inferred で再構築 |
| 仕様のバッククォート識別子が型に無い | `contradicts`（inferred、信頼度低） |
| 未コミット編集が skip される | `git hash-object --stdin-paths` で作業ツリー SHA |
| リネームで再解析が走る | `git diff -M` + `RelocateFile` |
| analyzer 変更後に古いベクトルが残る | 版不一致で `ClearEmbeddings` と全再解析 |
| 型の XML 全文が埋め込みを薄める | 型名 + summary + remarks だけを埋める |
| テストが実装より上に来る | クエリにテスト語が無いときはテストパスを後ろへ下げる |
| 未追跡ファイルが HEAD と同じだと古い | `search` の前に porcelain 差分を見て、32 件まで再索引する |

## 2. まだ入れないもの

| ギャップ | 理由 | 次の候補 |
| --- | --- | --- |
| 日本語本文の部分一致（形態素なし） | 見出し・識別子中心で Hit@8 を先に満たす | FTS n-gram 列 |
| 長文クエリとゴールデン表の文言差 | eval は短いクエリ（`GoldenQueries`）を使う | 表の原文をシノニム展開 |
| 専用 Reranker | VRAM と運用コスト | `IReranker` 実装の差し替え |
| LLM による specifies | 根拠の再現性が低い | inferred のみ、規則の後 |

## 3. ゴールデンとの対応

`docs/evaluation.md` の Q1–Q15 を、検索文字列だけ短くしたものが `GoldenQueries`（dev）。検証用の自然文は `HoldoutQueries`（test、H12 を除く）。`melore eval` は両方を出し、test の Hit@8 が 0.93 未満、または Hit@1 が 0.65 未満なら終了コード 1。検証用の失敗をシノニム表へは足さない。

2026-11-03、既存 Statevia 索引に対する `melore eval` は Hit@8 = 1.000（15/15）。`specifies` / `tested_by` の再構築は analyzer `rel:1` のため、次の `melore index` で入る。
