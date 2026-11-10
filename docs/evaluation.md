# 評価方法と受け入れ基準

| 項目 | 値 |
| --- | --- |
| 種別 | Evaluation |
| Version | 0.5 |
| 更新日 | 2026-11-10 |

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

- [x] `melore index` が Statevia の `docs/**/*.md` と C# を対象に完走する（2026-11-08。全索引 11 分 15 秒）
- [x] SQLite に document_chunks と symbols が入り、各行に path と start/end line がある（2026-11-09。行範囲が空の行は 0 件）
- [x] 同一 revision での再実行が差分になり、無変更ファイルを再解析しない（ログまたはカウンタで確認）（2026-11-09。`parsed=0 skipped=1213`）
- [ ] ファイル 1 つを変更すると、そのファイルのチャンクだけが更新される
- [ ] FTS とベクトルのそれぞれで Q1 が期待パスを返す
- [x] 統合検索でゴールデン 15 件の Hit@8 ≥ 0.7（2026-11-08。0.867）
- [x] Citation validity ≥ 0.9（ランダム 20 ヒットを行範囲検証）（2026-11-09。チャンク 10・シンボル 10 で 20/20）
- [x] `.env` が索引に含まれない（2026-11-09。`files` に該当 0 件）
- [x] sqlite-vec の Windows ロード結果が ADR に実測として追記されている（2026-11-09。ADR-005。ロードは成功、検索では未使用）

### AC-P2 MCP

- [x] Cursor から stdio で起動できるサンプル設定がある（`samples/cursor-mcp.json.example`）
- [x] `search` がヒットにパスと行範囲を付け、隣接して `dirty`、差分件数、今回更新した件数を返す（`SearchToolResult`）
- [x] `references` がクラスに対し、メンバーまたはドキュメントリンクまたはテストのいずれかを `basis` 付きで返す（テストで固定）
- [x] `trace` が解決済み `calls` または `implements` を depth と件数の上限つきで返す。ノードは名前と行範囲だけ。未解決は空配列 + 不足理由（テストで固定）。文字数の上限は値の長さだけを 8000 文字で見積もるので、JSON の応答はその 2 倍ほどになる（§9）
- [x] 既定応答にファイル全文を含めない（テストで固定）
- [x] stdout をプロトコル以外で汚さない（MCP サーバーはログを stderr へ出す）
- [x] ツール説明に、使う場面と日本語のクエリ例がある（2026-11-06）

### AC-P3 LLM

- [x] 実機で `qwen3.5:9b-q4_K_M` と `qwen3-embedding:0.6b` の VRAM 実測を ADR に追記（2026-11-03。ADR-008）
- [x] `explain` が search 上位のみをプロンプトに使う（デバッグログで確認可）（テストで固定）
- [x] 根拠が無いとき「インデックスに無い」と述べ、存在しないパスを作らない（2026-11-09。Helm / Ingress を問う実機 explain で、根拠に記述が無いと答え、パスを作らなかった）
- [ ] 仕様と実装が食い違うフィクスチャ（または既知の乖離）で矛盾を明示する
- [ ] 専用 Reranker が無くても AC-P3 は合格できる

### AC-P4 精度

- [ ] ゴールデン Hit@8 ≥ 0.9（2026-11-03 は 1.000。2026-11-08 以降は 0.867 で未達。Q9 と Q15 が外れる。§9）
- [x] `specifies` / `tested_by` が explicit と inferred に分かれて格納される（フィクスチャ）
- [x] 未コミット変更が dirty として再インデックスされる（作業ツリー SHA）
- [x] analyzer version を上げると再埋め込みが走る
- [x] 検索漏れの一覧と、対応（シノニム、n-gram、関係）がドキュメントに残る（[search-gaps.md](guides/search-gaps.md)。n-gram は未導入）
- [x] `melore eval` が dev（ゴールデン 15 件）と test（自然文 27 件、H12 を除く）の Hit@1 / Hit@8 を分ける。test が §3 の閾値を下回ると終了コード 1（2026-11-06。セット分割はテストで固定）
- [x] `melore eval` が経緯の質問（history、14 件）を `history` スコープで別に測る。終了コードには影響しない（2026-11-08）
- [x] `history` を設定した Statevia で、dev / test が履歴文書を索引しない場合を下回らない（2026-11-08。結果は §8）
- [ ] 再索引後の Statevia で、test の Hit@8 ≥ 0.93 かつ Hit@1 ≥ 0.65。検証用の失敗はシノニム表へ足さない（2026-11-10 は 0.926 / 0.556。H01 と H02 が外れる）
- [x] 未追跡の `.cs` を 1 つ足した直後、その型名の `search` が 1 位（フィクスチャ。差分が 32 件を超えるときは再索引せず `dirty: true`）（2026-11-06）
- [x] 文書チャンクの `endLine` がファイル行数以下（パーサテスト）（2026-11-06）
- [x] `explain` がクライアントへ返す evidence は ID・パス・行範囲・snippet。本文は LLM プロンプト内だけ（2026-11-06）
- [x] MCP の JSON が日本語を `\u` エスケープしない（2026-11-06）
- [ ] `trace ExecutionEngine` を depth=2 で呼んでもおよそ 8KB 以下で、`calls` が 1 件以上返る（2026-11-10 の実測は `calls` 18 件、応答 15.6KB。大きさが未達。§9）
- [ ] 検証用ヒットの行範囲がすべてファイル内で、`search` の平均応答文字数が改善前より 3 割以上減っている
- [ ] 調査を含む Statevia セッションの過半が Melore を 1 回以上呼ぶ（`mcp_invocations` の件数で数える。2026-11-10 時点で 12 件。§9）
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

## 9. 評価ログ: 現状の調査（2026-11-10）

Statevia `ba5d193a`（作業ツリーに差分なし）、索引は 2026-11-09 のもの（文書チャンク 10,724、シンボル 10,560、テスト 2,478、埋め込み 16,802）。確認は `dotnet run` でビルドした実行ファイルで行った。

### 9.1 精度

| セット | Hit@1 | Hit@8 | 外れた質問 |
| --- | --- | --- | --- |
| dev（15 問） | 0.400 | 0.867 | Q9、Q15 |
| test（27 問） | 0.556 | 0.926 | H01、H02 |
| history（14 問） | 0.714 | 1.000 | なし |

§8（2026-11-08）と同じ値で、test が閾値を下回るので `eval` の終了コードは 1。

- dev が 2026-11-03 の 1.000 から下がった原因は特定できていない。`.spec/` を索引に入れたことが原因ではない。索引 DB のコピーで `.spec/` を外して測り直しても、dev は 0.400 / 0.867 のままで、外れるのも同じ Q9 と Q15 だった（このとき test の Hit@8 は、`.spec/specs/` を期待する H14 が外れて 0.889 に下がる）。
- Q9 と Q15 は、どちらもテストプロジェクトのパスを期待する。上位 32 件まで広げると、Q9 の期待パスは 20 位、Q15 は 25 位にある。2026-11-03 より後に入れた変更のうち、テストの順位下げ（Q15 のクエリにはテストの語が無い）と、テストメソッドを埋め込みの対象から外したことが候補。
- H01 と H02 は [agent-task-accuracy-plan.md](guides/agent-task-accuracy-plan.md) の計測（2026-11-04）から外れたままで、日本語の自然文から英語の識別子へつながっていない。

### 9.2 応答時間

| 対象 | 修正前 | 修正後 |
| --- | --- | --- |
| `eval`（56 問） | 約 25 分 | 12 秒 |
| CLI の `search` 1 回（プロセスの起動を含む） | 5〜18 秒 | 0.6 秒 |
| MCP の `search`（`mcp_invocations` の `duration_ms`。2026-11-09 の 8 件の平均） | 6.9 秒 | 未計測 |

原因は `symbols (file_id)` のインデックスが無かったこと。シンボル名の部分一致がファイルごとに `symbols` を全件走査していた（1 回 3〜15 秒）。`idx_symbols_file` を足した（[data-model.md](architecture/data-model.md)）。修正の前後で `eval` の出力は順位まで同じ。全文検索は 1〜33 ミリ秒、クエリの埋め込みは 0.02〜0.08 秒、ベクトルの走査は 0.14〜0.21 秒で、ほかに遅い段階は無い。

インデックスは索引 DB に入るので、新しい実行ファイルで DB を一度開けば、発行済みの古い `melore-mcp.exe` から同じ DB を使うときも効く。古い実行ファイルで DB を作り直すと消えるので、`dotnet publish` をやり直す。

### 9.3 MCP の利用

`mcp_invocations` は 12 件（2026-11-07 から 2026-11-09）。内訳は `search` 10、`explain` 1、`references` 1、`trace` 0 で、失敗は 0 件。このうち 10 件は 2026-11-09 の約 1 分間に続けて呼ばれていて、通常の調査で使われた形跡は 2 件だけ。応答の平均は `search` が約 4,400 文字、`explain` は 5,164 文字で 19 秒かかった。

### 9.4 `trace`

`ExecutionEngine` を depth=2、既定の上限で呼ぶと、ノード 24、辺 65（`declares` 44、`calls` 18、`implements` 2、`inherits` 1）で、打ち切りの注記が付く。MCP の応答は 15,611 文字。

- `TraceService.MaxResponseChars`（8000）は ID・種別・名前・パスの長さだけを足した見積もりで、JSON のキー名と引用符を数えない。既にあるノードへの辺は上限の対象にもならない。実際の応答は見積もりの 2 倍ほどになる。
- 見積もりを JSON の実寸に合わせて試すと、応答は 7,350 文字に収まるが、メンバー 15 件の `declares` だけで上限を使い切り、`calls` が 1 件も返らなかった。引数を複数行に書いたメソッドは名前に改行と空白を含み、1 ノードが 400 文字を超える。上限だけを直すと使い勝手が下がるので、実装は変えていない。
- クラスを起点に `relTypes=calls` で絞ると、クラスからメソッドへの `declares` が落ちるので、メソッドの `calls` まで届かない。
