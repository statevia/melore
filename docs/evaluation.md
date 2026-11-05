# 評価方法と受け入れ基準

| 項目 | 値 |
| --- | --- |
| 種別 | Evaluation |
| Version | 0.1 |
| 更新日 | 2026-11-02 |

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
| Hit@8 | 上位 8 件に期待パスが含まれる率 | ≥ 0.7 | ≥ 0.9 |
| Citation validity | 返した行範囲にクエリ関連の語句またはシンボルがある率 | ≥ 0.9 | ≥ 0.95 |
| Stale rate | HEAD と indexed_revision が一致しているのに内容が古いヒット | 0（意図的遅延なし） | 0 |
| Explain grounded | explain の各主張に出典 ID が付く（Phase 3） | — | ≥ 0.95 |
| Hallucination | インデックスに無い型名・パスを断定 | — | 0 件（サンプル上） |
| Token budget | 1 search 応答の概算文字数 | snippet 中心 | 変更なし |

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
- [ ] `search` が AC-P1 と同等の JSON を返す（出典フィールド必須）
- [ ] `references` がクラスに対し、メンバーまたはドキュメントリンクまたはテストのいずれかを `basis` 付きで返す
- [ ] `trace` が解決済み `calls` または `implements` を depth 制限付きで返す。未解決は空配列 + 不足理由
- [ ] 既定応答にファイル全文を含めない
- [ ] stdout をプロトコル以外で汚さない

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

## 5. 回帰の回し方

1. 固定 revision の Statevia でゴールデンを実行（SHA を評価ログに記録）
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
