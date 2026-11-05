# 検索漏れと対応

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.1 |
| 更新日 | 2026-11-03 |

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

## 2. まだ入れないもの

| ギャップ | 理由 | 次の候補 |
| --- | --- | --- |
| 日本語本文の部分一致（形態素なし） | 見出し・識別子中心で Hit@8 を先に満たす | FTS n-gram 列 |
| 長文クエリとゴールデン表の文言差 | eval は短いクエリ（`GoldenQueries`）を使う | 表の原文をシノニム展開 |
| 専用 Reranker | VRAM と運用コスト | `IReranker` 実装の差し替え |
| LLM による specifies | 根拠の再現性が低い | inferred のみ、規則の後 |

## 3. ゴールデンとの対応

`docs/evaluation.md` の Q1–Q15 を、検索文字列だけ短くしたものが `GoldenQueries`。期待パスの OR は表と同じ。Hit@8 ≥ 0.9 は `melore eval` の終了コードで見る。

2026-11-03、既存 Statevia 索引に対する `melore eval` は Hit@8 = 1.000（15/15）。`specifies` / `tested_by` の再構築は analyzer `rel:1` のため、次の `melore index` で入る。
