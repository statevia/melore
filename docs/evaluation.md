# 評価方法と受け入れ基準

| 項目 | 値 |
| --- | --- |
| 種別 | Evaluation |
| Version | 0.3 |
| 更新日 | 2026-11-08 |

コーパスは Statevia のローカルクローンを正とする。評価クエリは仕様のファイルパスを期待出典に含める。

## 1. 原則

- ヒットにパスと行範囲が無いものは不合格。
- 行範囲は実ファイルを開いて目視またはテストで検証する。
- LLM の文章の上手さより、根拠の正しさと不足の明示を優先する。
- ゴールデンセットは少数でよい。最初は 15 件前後。検索漏れが出たら足す。

## 2. ゴールデンクエリ（初期案）

期待出典は「少なくとも 1 件含まれること」。完全一致リストではない。

| ID | クエリ | 期待 kind | 期待パスの例 |
| --- | --- | --- | --- |
| Q1 | Fork Join の仕様 | document | `docs/specifications/execution/fork-join.md` |
| Q2 | ExecutionEngine は何を依存するか | symbol | engine 配下の `ExecutionEngine` |
| Q3 | IExecutionService の実装 | symbol | application 配下 |
| Q4 | シナリオテストは Docker が無いとどうなるか | document | `AGENTS.md` または getting-started / guidelines |
| Q5 | Wait と Cancel の契約 | document | `docs/specifications/execution/wait-cancel.md` |
| Q6 | HTTP ログインとテナントヘッダ | document | `docs/specifications/api-http.md` |
| Q7 | Module の zip レイアウト | document | `docs/specifications/actions/module-zip-layout.md` |
| Q8 | CoreDbContext の所在 | symbol | Service API / infrastructure |
| Q9 | Architecture tests の場所 | document または symbol | `dotnet/tests/Statevia.Architecture.Tests/` |
| Q10 | Definition を publish する流れ | document | `docs/concepts/` または `docs/specifications/definition.md` |
| Q11 | ログマスキング | document | `docs/specifications/platform/io-log-masking.md` |
| Q12 | Studio の feature-first | document | `docs/architecture/ui-studio-structure.md` |
| Q13 | `Guid.NewGuid` 禁止 | document | `docs/development-guidelines.md` |
| Q14 | Hosted Fork の親子 | document | fork-join 仕様 |
| Q15 | xUnit シナリオテストの Category | test または document | Scenario テストプロジェクト |

Phase 1 時点でソリューションが開けないシンボルは、ファイル名検索のフォールバックでも AC を部分合格にしてよい。その場合は既知制限として記録する。

## 3. 指標

| 指標 | 定義 | Phase 1 目標 | Phase 4 目標 |
| --- | --- | --- | --- |
| Hit@8（dev） | ゴールデン 15 件で、上位 8 件に期待パスが含まれる率 | ≥ 0.7 | ≥ 0.9 |
| Hit@1（test） | 検証用 27 件で、1 位に期待パスが含まれる率 | — | ≥ 0.65 |
| Hit@1 / Hit@8（history） | 経緯の質問 14 件を `history` スコープで測り、1 位・上位 8 件に期待する履歴文書が含まれる率 | — | 参考値（目標は未定） |
| Hit@8（test） | 検証用 27 件で、上位 8 件に期待パスが含まれる率 | — | ≥ 0.93 |
| Citation validity | 返した行範囲にクエリ関連の語句またはシンボルがある率 | ≥ 0.9 | ≥ 0.95 |
| Stale rate | HEAD と indexed_revision が一致しているのに内容が古いヒット。未追跡を含む差分は 32 件まで検索前に再索引し、超えたら `dirty` を返す | 0（意図的遅延なし） | 0 |
| Explain grounded | explain の各主張に出典 ID が付く（Phase 3） | — | ≥ 0.95 |
| Hallucination | インデックスに無い型名・パスを断定 | — | 0 件（サンプル上） |
| Token budget | 1 search 応答の概算文字数。既定は snippet。null の本文は出さない | snippet 中心 | 改善前より 3 割以上減 |

自動計算が難しい項目はチェックリストで人手確認する。

## 4. フェーズ別受け入れ基準

### AC-P1 インデックス・検索

- [ ] `melore index` が Statevia の `docs/**/*.md` と C# を対象に完走する
- [ ] SQLite に document_chunks と symbols が入り、各行に path と start/end line がある
- [ ] 同一 revision での再実行が差分になり、無変更ファイルを再解析しない（ログまたはカウンタで確認）
- [ ] ファイル 1 つを変更すると、そのファイルのチャンクだけが更新される
- [ ] FTS とベクトルのそれぞれで Q1 が期待パスを返す
- [ ] 統合検索でゴールデン 15 件の Hit@8 ≥ 0.7
- [ ] Citation validity ≥ 0.9（ランダム 20 ヒットを行範囲検証）
- [ ] `.env` が索引に含まれない
- [ ] sqlite-vec の Windows ロード結果が ADR に実測として追記されている

### AC-P2 MCP

- [ ] Cursor から stdio で起動できるサンプル設定がある
- [ ] `search` がヒットにパスと行範囲を付け、隣接して `dirty`、差分件数、今回更新した件数を返す
- [ ] `references` がクラスに対し、メンバーまたはドキュメントリンクまたはテストのいずれかを `basis` 付きで返す
- [ ] `trace` が解決済み `calls` または `implements` を depth・件数・およそ 8KB で返す。ノードは名前と行範囲だけ。未解決は空配列 + 不足理由
- [ ] 既定応答にファイル全文を含めない
- [ ] stdout をプロトコル以外で汚さない
- [x] ツール説明に、使う場面と日本語のクエリ例がある（2026-11-06）

### AC-P3 LLM

- [ ] 実機で `qwen3.5:9b-q4_K_M` と `qwen3-embedding:0.6b` の VRAM 実測を ADR に追記
- [ ] `explain` が search 上位のみをプロンプトに使う（デバッグログで確認可）
- [ ] 根拠が無いとき「インデックスに無い」と述べ、存在しないパスを作らない
- [ ] 仕様と実装が食い違うフィクスチャ（または既知の乖離）で矛盾を明示する
- [ ] 専用 Reranker が無くても AC-P3 は合格できる

### AC-P4 精度

- [x] ゴールデン Hit@8 ≥ 0.9（2026-11-03 `melore eval` = 1.000）
- [x] `specifies` / `tested_by` が explicit と inferred に分かれて格納される（フィクスチャ）
- [x] 未コミット変更が dirty として再インデックスされる（作業ツリー SHA）
- [x] analyzer version を上げると再埋め込みが走る
- [x] 検索漏れの一覧と、対応（シノニム、n-gram、関係）がドキュメントに残る（[search-gaps.md](guides/search-gaps.md)。n-gram は未導入）
- [x] `melore eval` が dev（ゴールデン 15 件）と test（自然文 27 件、H12 を除く）の Hit@1 / Hit@8 を分ける。test が §3 の閾値を下回ると終了コード 1（2026-11-06。セット分割はテストで固定）
- [x] `melore eval` が経緯の質問（history、14 件）を `history` スコープで別に測る。終了コードには影響しない（2026-11-08）
- [x] `history` を設定した Statevia で、dev / test が履歴文書を索引しない場合を下回らない（2026-11-08。結果は §8）
- [ ] 再索引後の Statevia で、test の Hit@8 ≥ 0.93 かつ Hit@1 ≥ 0.65。検証用の失敗はシノニム表へ足さない
- [x] 未追跡の `.cs` を 1 つ足した直後、その型名の `search` が 1 位（フィクスチャ。差分が 32 件を超えるときは再索引せず `dirty: true`）（2026-11-06）
- [x] 文書チャンクの `endLine` がファイル行数以下（パーサテスト）（2026-11-06）
- [x] `explain` がクライアントへ返す evidence は ID・パス・行範囲・snippet。本文は LLM プロンプト内だけ（2026-11-06）
- [x] MCP の JSON が日本語を `\u` エスケープしない（2026-11-06）
- [ ] `trace ExecutionEngine` を depth=2 で呼んでもおよそ 8KB 以下で、`calls` が 1 件以上返る
- [ ] 検証用ヒットの行範囲がすべてファイル内で、`search` の平均応答文字数が改善前より 3 割以上減っている
- [ ] 調査を含む Statevia セッションの過半が Melore を 1 回以上呼ぶ（`mcp_invocations` の件数で数える）
- [ ] 検証用から約 10 件を文章で答えさせ、Melore ありとなしで正答率・ツール呼び出し回数・入力トークンを人手で比べる

## 5. 回帰の回し方

1. 固定 revision の Statevia で `melore eval` を実行し、dev と test の Hit@1 / Hit@8 を SHA とともに残す
2. インデックスを消し、フル再構築しても Hit@8 が同等
3. 1 ファイルだけ編集し、そのクエリの出典行が新しい内容を指す
4. Ollama 停止時、Phase 1–2 の FTS / シンボル検索は動く（ベクトル検索だけ失敗して明示する）

## 6. 実機確認チェック（Windows / RTX 5070 Ti 16GB）

計画時点の公開サイズであり、受け入れには実測が必要。

| 項目 | 確認コマンド例 | 記録する値 |
| --- | --- | --- |
| モデル存在 | `ollama pull qwen3-embedding:0.6b` / `ollama pull qwen3.5:9b-q4_K_M` | タグ、実サイズ |
| 次元 | embed API の配列長 | 1024 以外ならスキーマ修正 |
| VRAM | タスクマネージャまたは `nvidia-smi` | 単独 / 同時ロード |
| コンテキスト | explain を 8 チャンクで実行 | OOM の有無 |

## 7. 合格判定者

機能 AC は自動化テスト + ゴールデン実行。VRAM と Cursor 接続は開発者が一度手で確認し、`docs/decisions` か評価ログに数値を残す。

## 8. 評価ログ: 履歴文書のスコープ（2026-11-08）

Statevia `3e1d70b1`（作業ツリー、`.spec/archive/` と `.spec/reject/` を含む clone）で測った。どちらも `--limit 8`。

| 指標 | 履歴文書なし（exclude） | `history` あり（既定スコープ） |
| --- | --- | --- |
| dev Hit@1 / Hit@8 | 0.400 / 0.867 | 0.400 / 0.867 |
| test Hit@1 / Hit@8 | 0.556 / 0.926 | 0.556 / 0.926 |
| history Hit@1 / Hit@8 | 0.000 / 0.000 | 0.714 / 1.000 |
| 文書チャンク | 2,226 | 10,724 |
| 索引 DB | 61MB（同日の事前計測、新規作成時） | 118MB |
| フル索引の時間 | — | 11 分 15 秒 |

- 既定のスコープの dev / test は、履歴文書を索引しない場合と同じ値になった。test が §3 の閾値を下回るのは以前からで、この変更によるものではない。
- 比較として、スコープで分けずに archive を混ぜた場合（同日の事前計測）は、test が Hit@1 0.333 / Hit@8 0.852 まで下がり、dev と test の 42 問のうち 20 問で 1 位を archive が占めた。
- `history` の glob を外して `index` し直すと、`relabeled=514` で区分だけが変わり、archive のファイルは解析し直さなかった。
- history セットの 14 問は、archive の Spec 名と語が重ならないよう言い換えてある。目標値はまだ決めていない。
