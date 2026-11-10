# エージェントのタスク精度の計測と改善計画

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.2 |
| 更新日 | 2026-11-10 |

Melore MCP を使ったときと使わないときで、エージェントの調査タスクの精度がどう違うかを測った結果と、そこから立てた改善計画。§2 と §3 は 2026-11-04 の計測（コーパスは Statevia、索引 revision `cfa507d3`、作業ツリーに未コミット変更あり）。§1 と §4 は 2026-11-10 の調査（[evaluation.md](../evaluation.md) の §9）で更新した。

## 1. 結論

- Melore `search` は、所在探しで grep の初手を明確に上回る（2026-11-04。Hit@8 0.86 対 0.64、Hit@1 0.50 対 0.14）。
- 精度は受け入れの閾値に届いていない（2026-11-10。test は Hit@1 0.556 / Hit@8 0.926、dev は Hit@8 0.867）。
- まだ使われていない。Statevia 側に利用方針を追加し（`CLAUDE.md` / `AGENTS.md` / `.cursor/rules/melore-mcp-research.mdc`）、ツール説明に使う場面を書いたが、`mcp_invocations` は 2026-11-07 からの 3 日で 12 件で、通常の調査で呼ばれた形跡は 2 件だけ。実際のタスク精度への効果はほぼゼロのまま。
- 使われない理由の 1 つだった応答時間は直した。MCP の `search` は 1 回 7 秒前後かかっていた（インデックスの不足。2026-11-10 に修正し、CLI では 0.6 秒）。
- Melore の有無で回答の正しさがどう変わるかは、まだ測っていない（E1）。

## 2. 計測方法

| 項目 | 内容 |
| --- | --- |
| タスク | 調整に使っていない 28 件（[付録 A](#付録-a-検証用タスク)）。実装の所在 14、仕様・文書 11、テスト 2、Spec 1。正解パスは事前に `find` / `git show` で確認 |
| Melore あり | `melore search "<自然文>"`（limit 8）。MCP 経由でも同じ順位になることを確認 |
| Melore なし | エージェントが最初に打ちそうな正規表現で 1 回だけ grep。範囲は `melore.json` の include / exclude と同じ（未追跡ファイルを含む）。一致行数の多い順 |
| 判定 | 期待パスの部分文字列のどれか 1 つが上位 k 件に含まれれば合格 |

grep 側は初手の 1 回だけで、打ち直しは数えていない。実際のエージェントは打ち直すので最終的には届くが、その分ツール呼び出しが増える。今回の計測は所在探しの段階までで、回答の正しさまでは測っていない（[§5 の E1](#e1-エンドツーエンドの-ab)）。

## 3. 結果

| 指標 | Melore `search` | grep（初手 1 回） |
| --- | --- | --- |
| Hit@1 | 0.50 | 0.14 |
| Hit@8 | 0.86（甘めの判定で 0.89） | 0.64 |
| MRR@8 | 0.63 | 0.31 |
| 候補の数 | 8 | 中央値 約 30 ファイル、最大 191 |
| 応答時間 | CLI で平均 0.4 秒 | — |

grep は H05 / H14 / H15 で 1 件もヒットしなかった（初手の語が文書に無い）。

### 3.1 Melore が取りこぼしたもの

| ID | 期待 | 原因 |
| --- | --- | --- |
| H01 | `ExecutionIdempotencyService.cs` | 日本語の自然文（冪等性キー）から英語の識別子へつながらない。識別子名で検索すれば 1 位に出る。上位はテストメソッドが占めた |
| H02 | `ExecutionScheduleDispatchService.cs` | 400 行を超えるクラスが 1 チャンクになっていて、埋め込みの意味が薄まっている |
| H12 | `InProcessBranchScope.cs` | 新規の未追跡ファイルが索引に無い。HEAD が同じなので索引が古いと検知されない |
| H20 | `OrasOciArtifactFetcher.cs` | `OciModuleSource` / `IOciArtifactFetcher` は返しているので実用上はたどり着ける（採点が厳しい分の取りこぼし） |

順位が低かったもの: H14（6 位）、H19（8 位）、H21（6 位）。いずれも、インターフェースや隣のクラスが実装クラスより上に来た。

### 3.2 ツールごとの所見

| ツール | 所見 |
| --- | --- |
| `explain` | 「Join で枝が書いた vars を親へ戻す規則」に対し、書いたパスだけを戻す・枝の完了順の後勝ちを正しく答え、出典 ID を付けた。作り話は見られなかった。evidence に全チャンクの body が入り、応答が大きい |
| `references` | `TraceIdResolver` に対し、メンバー（`declares`）とテスト（`tested_by`、`Explicit`）を正しく返した |
| `trace` | `ExecutionEngine` を depth=1 で呼ぶと 68 ノード・52KB になり、クライアントのトークン上限を超えて使えなかった。calls は未抽出 |
| 行範囲 | 224 件のうち 8 件で、文書末尾のチャンクの `endLine` がファイル行数 + 1（同じパターンのずれ） |
| JSON | 日本語が `\uXXXX` に escape される。ASCII 中心の trace 応答でも、escape をやめると 52,398 → 41,285 文字（−21%） |

## 4. 改善計画

優先度は、実際のタスク精度への効き目の大きさで決める。§4.1 が 2026-11-10 時点の順番で、§4.2 は 2026-11-04 に立てた項目とその結果。

### 4.1 これからの順番（2026-11-10）

使われなければ精度を上げても効かないので、使われる状態にすることを先に置く。

| 順 | 項目 | 内容 | 受け入れ |
| --- | --- | --- | --- |
| 1 | 修正を Statevia に届ける | `dotnet publish src/Melore.Mcp -c Release` をやり直し、Statevia 側で MCP を再接続する。発行済みの実行ファイルは `idx_symbols_file` を作らない | Statevia から呼んだ `search` の `duration_ms` が 1 秒以下 |
| 2 | 利用を数え直す | 応答時間を直した後の `mcp_invocations` を日ごと・ツールごとに数える。増えなければ、Statevia のトランスクリプトで、調査の初手に何を使ったかを見る | 調査を含む Statevia セッションの過半が Melore を 1 回以上呼ぶ（P0-1 の受け入れ） |
| 3 | `get` と `status` を足す | [mcp-tool-candidates.md](mcp-tool-candidates.md) の §2。`search` の snippet で足りないときに、ファイルを読み直さず ID で本文を取れるようにする | `search` → `get` で、検証用タスクの期待箇所の本文に届く |
| 4 | dev の期待を決め直す | Q15 はテストの順位下げと引き換えになっている。dev の期待を変えるか、順位下げの条件を変えるかを決める（[search-gaps.md](search-gaps.md) の §4） | dev の Hit@8 ≥ 0.9。test を下げない |
| 5 | 自然文から識別子へつなぐ（P1-1 の続き） | H01 と H02 が外れたまま。FTS の n-gram 列で日本語本文の部分一致を拾う。シノニム表へは足さない | test の Hit@8 ≥ 0.93、Hit@1 ≥ 0.65 |
| 6 | `trace` の応答の予算（P1-3 の続き） | メンバーの多い型で `declares` が上限を使い切る。名前の空白を詰める、関係種別ごとに枠を分ける、などで `calls` を先に返す | `trace ExecutionEngine` を depth=2 で呼ぶと、およそ 8KB 以下で `calls` が 1 件以上返る |
| 7 | E1 | Melore ありとなしで回答の正しさを比べる。3 と 5 の前後で 1 回ずつ | 下の E1 |

専用 Reranker（インターフェースや隣のクラスが実装クラスより上に来る問題の候補）は、5 の後も Hit@1 が届かないときに検討する。Ollama の外にランタイムが要る（ADR-009）。

### 4.2 2026-11-04 に立てた項目と結果

| 項目 | 結果（2026-11-10） |
| --- | --- |
| P0-1 使われる状態にする | ツール説明は直した。受け入れは未達（12 件。§1） |
| P0-2 古い索引を検知する | 済み。未追跡を含む差分を 32 件まで検索前に再索引する |
| P1-1 自然文から識別子へつなぐ | 実装は入れた（型名 + summary + remarks の埋め込み、テストの順位下げ）。受け入れは未達（test 0.926 / 0.556） |
| P1-2 評価セットを分ける | 済み。`melore eval` が dev / test を分け、test が閾値を割ると終了コード 1 |
| P1-3 `trace` を使えるようにする | `limit`・関係種別のフィルタ・`calls` / `implements` の抽出は済み。応答の大きさは未達（15.6KB） |
| P2-1 出力の正しさとコスト | `endLine`、日本語の escape、`explain` の evidence は済み。`search` の応答文字数が 3 割減ったかは確かめていない（MCP の実績は平均 約 4,400 文字） |
| E1 エンドツーエンドの A/B | 未実施 |

以下は当時の記述。

### P0-1 使われる状態にする

Statevia 側の方針追加は済み。Melore 側では次を行う。

- ツールの `Description` を、何ができるかだけでなく、どんなときに使うかが分かる文にする（例: 「所在が分からない仕様・実装・テストを探すとき、grep より先に使う」）。日本語のクエリ例を入れる。
- 受け入れ: 方針追加後の Statevia セッションで、調査を含むセッションの過半が Melore を 1 回以上呼ぶ（トランスクリプトで数える）。

### P0-2 古い索引を検知する

- `search` / `references` の前に、作業ツリーとの差分（`git status --porcelain`、未追跡ファイルを含む）を確かめる。差分のあるファイルだけ再索引するか、少なくとも応答に `dirty: true` と対象ファイル数を付ける。
- 受け入れ: 未追跡の `.cs` を 1 つ足した直後に、その型名の `search` が 1 位で返る（H12）。

### P1-1 自然文から識別子へつなぐ

- 型ごとに「型名 + `<summary>` + `<remarks>`」だけの短いチャンクを別に作り、埋め込みの対象にする（H01 / H02）。
- クエリにテスト関連の語（テスト、Tests、検証）が無いときは、テストシンボルの順位を下げる。
- 受け入れ: 検証用 28 件で Hit@8 ≥ 0.93、Hit@1 ≥ 0.65。

### P1-2 評価セットを調整用と検証用に分ける

- 既存の `GoldenQueries` を調整用（dev）、[付録 A](#付録-a-検証用タスク) を検証用（test）として `melore eval` に入れ、両方の数値を出す。
- 検証用の失敗をシノニムで直接埋めない。今あるシノニム（例: `("禁止", "NewGuid")`）はゴールデンのクエリ専用になっていて、過学習の兆候がある。直すときは、汎用の仕組み（P1-1、n-gram など）で直す。
- 受け入れ: `melore eval` が dev / test を別々に出し、test の閾値を割ると終了コードが非 0 になる。

### P1-3 `trace` を使えるようにする

- `limit` と関係種別のフィルタを足し、ノードは名前・種別・行範囲だけにする（snippet は出さない）。既定の応答をおよそ 8KB 以下にする。
- Roslyn の意味解析で `calls` / `implements` を抽出する（[mcp-tool-candidates.md](mcp-tool-candidates.md) と整合させる）。
- 受け入れ: `trace ExecutionEngine` を depth=2 で呼んでもクライアント上限を超えない。`calls` が 1 件以上返る。

### P2-1 出力の正しさとコスト

- 文書末尾のチャンクの `endLine` のずれを直す（行範囲がファイル行数以下であることをテストで固定する）。
- MCP のツール結果の本文で `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` を使い、日本語を escape しない。プロトコルの封筒は SDK の既定のままで、受信側が JSON を解くと本文の文字列に戻る。
- `explain` の evidence から body を外し、ID・パス・行範囲・snippet だけにする。
- 受け入れ: 検証用 28 件の全ヒットで行範囲がファイル内。`search` の平均応答文字数が 3 割以上減る。

### E1 エンドツーエンドの A/B

- 検証用タスクのうち 10 件ほどを「答えを文章で返す」形にし、`claude -p --strict-mcp-config` で Melore ありとなしを比べる。指標は正答率（人手判定）・ツール呼び出し回数・入力トークン数。
- P0 と P1-1 の前後で 1 回ずつ行い、改善の効果を確かめる。（2026-11-10: 未実施。§4.1 の 7）

## 付録 A 検証用タスク

期待パスは部分文字列の OR。「grep 初手」は Melore なしの比較で使った正規表現（大文字小文字を区別しない）。

| ID | 種別 | クエリ | grep 初手 | 期待パス | Melore 順位 | grep 順位 |
| --- | --- | --- | --- | --- | --- | --- |
| H01 | impl | 冪等性キーで実行開始の重複を防ぐ仕組みはどこ | `Idempotency` | `ExecutionIdempotencyService.cs` | — | 14 |
| H02 | impl | スケジュール実行を時刻になったら起動する処理 | `Schedule.*Dispatch\|ScheduleDispatch` | `ExecutionScheduleDispatchService.cs` | — | 14 |
| H03 | impl | テナントのシークレットを暗号化して保存する実装 | `TenantSecret\|Protect` | `TenantSecretProtector.cs` | 1 | 3 |
| H04 | impl | モジュールの署名を検証するコード | `Signature` | `ModuleSignatureVerifier.cs` | 1 | 2 |
| H05 | doc | ログのプロパティキーの命名規則 | `プロパティキー\|property key` | `docs/reference/logging-property-keys.md` | 1 | — |
| H06 | doc | API のエラーコード一覧 | `エラーコード\|error code` | `docs/reference/error-codes.md` | 1 | 9 |
| H07 | doc | 権限キーの一覧と意味 | `権限キー\|permission key` | `docs/reference/permission-keys.md` | 3 | 2 |
| H08 | doc | 環境変数の一覧 | `環境変数` | `docs/reference/environment-variables.md` | 2 | 6 |
| H09 | impl | traceparent ヘッダからトレース ID を決める処理 | `traceparent` | `TraceIdResolver.cs` | 1 | 2 |
| H10 | impl | 状態 input の $ パス式を実行時に評価するところ | `StateInput\|PathResolver` | `StateInputEvaluator.cs`, `ExecutionContextPathResolver.cs` | 2 | 8 |
| H11 | doc | Join で枝が書いた vars を親へ戻す規則 | `Join` | `docs/specifications/execution/fork-join.md` | 2 | 14 |
| H12 | impl | In-process の Fork 枝スコープ InProcessBranchScope | `InProcessBranchScope` | `InProcessBranchScope.cs` | — | 3 |
| H13 | doc | Wait を外部シグナルで再開する契約 | `Wait.*(再開\|resume)\|resume` | `docs/specifications/execution/wait-cancel.md` | 1 | 13 |
| H14 | spec | テナントのソフトリミットの仕様 | `ソフトリミット\|soft.?limit` | `.spec/specs/tenant-soft-limits` | 6 | — |
| H15 | doc | Action Host の監視と再起動（supervisor） | `[Ss]upervisor` | `action-host-supervisor`, `docs/guides/action-host.md` | 1 | — |
| H16 | doc | 定義のバージョニングをどう決めたか | `バージョニング\|versioning` | `docs/decisions/definition-versioning.md` | 2 | 6 |
| H17 | doc | イベントストアを採用した理由 | `イベントストア\|event store` | `docs/decisions/event-store.md` | 1 | 3 |
| H18 | test | ActionInputTreeNormalizer のテスト | `ActionInputTreeNormalizer` | `ActionInputTreeNormalizerTests.cs` | 1 | 1 |
| H19 | impl | S3 からモジュールのアーティファクトを取得する | `S3.*Fetch\|ArtifactFetcher` | `AwsS3ArtifactFetcher.cs` | 8 | 4 |
| H20 | impl | OCI レジストリからモジュールを取ってくる実装 | `Oci\|ORAS\|oras` | `OrasOciArtifactFetcher.cs` | — | 2 |
| H21 | impl | 通知アクションの SMTP 接続設定を読み込む | `Smtp` | `NotificationSmtpConnectionLoader.cs` | 6 | 13 |
| H22 | impl | CLI のログイン情報をどこに保存するか | `Credential` | `CliCredentialsStore.cs` | 3 | 4 |
| H23 | doc | 負荷試験のやり方 | `負荷試験\|capacity` | `docs/guides/capacity-load-testing.md` | 2 | 2 |
| H24 | impl | 予期しない例外を Sentry に送るところ | `Sentry` | `SentryUnexpectedExceptionReporter.cs` | 1 | 5 |
| H25 | doc | 実行のイベントとコマンドの一覧 | `イベント.*コマンド\|Command` | `docs/specifications/execution/events-and-commands.md` | 1 | 17 |
| H26 | test | TraceIdResolver のテストはどこ | `TraceIdResolver` | `TraceIdResolverTests.cs` | 1 | 1 |
| H27 | doc | Docker でローカル起動する手順 | `docker compose\|docker-compose` | `docs/guides/operations-docker.md`, `docs/guides/getting-started.md` | 1 | 1 |
| H28 | doc | DB スキーマのテーブル定義 | `CREATE TABLE\|テーブル` | `docs/reference/database-schema.md` | 1 | 1 |

H12 は計測時点で Statevia の作業ブランチにだけある未追跡ファイル。検証用セットに入れるときは、フィクスチャで未追跡ファイルを作る形にする。
