# CLI

| 項目 | 値 |
| --- | --- |
| 種別 | Guide |
| Version | 0.2 |
| 更新日 | 2026-11-08 |

人が使うコマンド。発行先はリポジトリルートの `artifacts/melore/melore.exe`。エージェント向けの同じ機能は MCP であり、この文書の対象外。

## 1. 準備

```powershell
cd dotnet
dotnet publish src/Melore.Cli -c Release
```

`melore.json.example` をリポジトリルートの `melore.json` にコピーし、`repositoryPath` を対象クローンの絶対パスにする。各コマンドは `--config` が必須。

```powershell
..\artifacts\melore\melore.exe --help
..\artifacts\melore\melore.exe search --help
```

索引 DB は `databasePath` が null のとき `%LOCALAPPDATA%\Melore\<repositoryName>.db`。

`search` / `references` / `explain` は実行前に `git status --porcelain`（未追跡を含む）を見る。include / exclude に合う差分が 32 件以内ならそのファイルだけ再索引する。超えた場合は再索引せず、検索は続行する。`trace` と `eval` はこの更新をしない。

`.gitignore` の対象になっているファイル（Statevia の `.spec/` など）は `git status --porcelain` に出ないため、この自動の再索引では拾われない。編集や archive への移動は、次に `index` を実行するまで反映されない。

## 2. 共通オプション

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--config <file>` | なし（必須） | `melore.json` のパス |
| `--limit <n>` | `8` | `search` / `references` / `explain` / `eval` の件数。`trace` では別の既定（64） |
| `--scope <scope>` | `current` | `search` / `explain` の検索スコープ。`current`（現行だけ）、`history`（完了・却下した履歴文書だけ）、`all`（すべて）。大文字小文字は区別しない。それ以外の値はパースエラーになり、許可値と使い方を出して終了コード 1 |

`explain` の件数は 1 から 8 に丸める。`search` が返すヒットは最大 32 件。`trace` の深さは 1 から 6 に丸める。

## 3. コマンド

パスは `dotnet` ディレクトリから見た例。

### index

対象リポジトリを差分インデックスする。終わると `status` と同じ行を出す。`solutionPath` がありソリューションを開けるとき、解決できた `calls` と `implements` を保存する。開けなければ構文解析のまま続ける。

```powershell
..\artifacts\melore\melore.exe index --config ..\melore.json
```

終了コードは 0。

### status

索引の件数とリビジョン、MCP の利用回数を出す。

```powershell
..\artifacts\melore\melore.exe status --config ..\melore.json
```

| 行 | 内容 |
| --- | --- |
| `repository` | `repositoryName` |
| `revision` | 解析済み HEAD。無ければ `(none)` |
| `document_chunks` | 文書チャンク数 |
| `symbols` | シンボル数 |
| `tests` | テスト数 |
| `vector` | ベクトル検索が使えるか |
| `embeddings` | 埋め込みが 1 件でもあるか |
| `updated_at` | 最終索引時刻（UTC）。無ければ `(none)` |
| `mcp_invocations` | MCP ツールを呼んだ回数。CLI は数えない |
| `mcp_last_at` | 最後に MCP を呼んだ時刻。無ければ `(none)` |

終了コードは 0。

### search

自然言語またはキーワードで仕様とコードを検索する。先に差分を反映する。

```powershell
..\artifacts\melore\melore.exe search "Fork Join の親子" --config ..\melore.json
..\artifacts\melore\melore.exe search "ExecutionEngine" --config ..\melore.json --limit 16
..\artifacts\melore\melore.exe search "ノード ID を 12 桁の 16 進にした理由" --config ..\melore.json --scope history
```

`--scope history` は、`melore.json` の `history` に当たる履歴文書だけを検索する。決めた理由や却下した理由を調べるときに使う。絞り込みは全文・シンボル・ベクトルのそれぞれで候補を取る段階で行うので、既定のスコープの順位は履歴文書の件数に押されない。

最初の 1 行。`\t` はタブ。

```text
dirty\t<true|false>\tfiles\t<差分ファイル数>\trefreshed\t<今回索引した数>
```

`dirty` が true のときは差分が 32 件を超え、索引は古いまま。続く各ヒットは 2 行。

```text
<Kind>\t<path>:<start>-<end>\t<title>[\t<lifecycle>]
  <snippet>
```

`lifecycle` は区分が `current` 以外（`archived` / `rejected`）のときだけ付く。現行だけの出力は従来と同じ形。

0 件のときは `ヒットなし`。終了コードは 0。

### references

仕様チャンクまたはシンボルの関連を列挙する。先に差分を反映する。`target` は ID、完全修飾名、単純名のいずれか。

```powershell
..\artifacts\melore\melore.exe references ExecutionEngine --config ..\melore.json
..\artifacts\melore\melore.exe references ExecutionEngine --config ..\melore.json --limit 32
```

1 行が 1 件。`\t` はタブ。

```text
<basis>\t<relType>\t<path>:<start>\t<title>[\t<lifecycle>]
```

`basis` は `explicit` または `inferred`。関係をたどった先が履歴文書なら、行末に区分が付く。終了コードは 0。

### trace

シンボルの所属と呼び出しをたどる。ノードは名前と行範囲だけ。差分の自動索引はしない。

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--depth <n>` | `2` | たどる深さ。1 から 6 |
| `--limit <n>` | `64` | ノード数の上限。応答はおよそ 8KB で打ち切る |
| `--rel <types>` | 空（全部） | 関係種別。カンマ区切り。例: `calls,implements` |

```powershell
..\artifacts\melore\melore.exe trace ExecutionEngine --config ..\melore.json
..\artifacts\melore\melore.exe trace ExecutionEngine --config ..\melore.json --depth 2 --limit 64 --rel calls,implements
```

起点が見つかったとき。`\t` はタブ。

```text
root\t<name>\t<path>:<start>
<basis>\t<relType>\t<from> -> <to>
```

打ち切りや未抽出の `calls` があるときは、その理由を最後に 1 行出す。起点が無いときは理由だけを出して終了コード 1。見つかれば 0。

### explain

検索の上位だけを根拠に説明する。先に差分を反映する。Ollama の chat が無いときは固定文と根拠一覧を返す。

```powershell
..\artifacts\melore\melore.exe explain "Join で枝が書いた vars を親へ戻す規則" --config ..\melore.json
..\artifacts\melore\melore.exe explain "Fork Join の親子" --config ..\melore.json --limit 4
..\artifacts\melore\melore.exe explain "型付きの識別子キーを採用しなかった理由" --config ..\melore.json --scope history
```

`--scope` は根拠を取る検索のスコープ。既定は `current`。

`\t` はタブ。

```text
<回答本文>
limitation\t<不足があれば>
chat\t<true|false>\t<model>
evidence_ids\t<id,id,...>
<Kind>\t<path>:<start>-<end>\t<title>[\t<lifecycle>]
```

根拠行に本文は出さない。終了コードは 0。

### eval

調整用（dev、ゴールデン 15 件）と検証用（test、自然文 27 件。未追跡ファイル依存の H12 は含まない）の Hit@1 と Hit@8 を、既定のスコープ（`current`）で測る。あわせて経緯の質問（history、14 件）を `history` スコープで測る。差分の自動索引はしない。測る前に `index` する。

```powershell
..\artifacts\melore\melore.exe eval --config ..\melore.json
..\artifacts\melore\melore.exe eval --config ..\melore.json --limit 8
```

セットごとに次の形。`\t` はタブ。

```text
<dev|test|history>\thit_at_1\t<0.000>
<dev|test|history>\thit_at_8\t<0.000>
<dev|test|history>\t<id>\t<HIT|MISS>\t<query>
  <miss のとき上位パス。最大 8 行>
```

test の Hit@8 が 0.93 未満、または Hit@1 が 0.65 未満のとき終了コード 1。それ以外は 0。history は参考値で、終了コードに影響しない。`history` を設定していない索引では 0 になる。

## 4. 設定ファイル

`melore.json` のフィールド。大文字小文字は区別しない。コメントと末尾カンマは読める。

| フィールド | 役割 |
| --- | --- |
| `repositoryName` | 表示名。DB ファイル名にも使う |
| `repositoryPath` | 対象 Git リポジトリの絶対パス。ディレクトリが無いと起動時に失敗する |
| `include` | 取り込む glob |
| `exclude` | 除外 glob。`.env` を含む |
| `solutionPath` | Roslyn の意味解析に使うソリューション。リポジトリ相対または絶対。空なら構文解析のみ |
| `databasePath` | SQLite の絶対パス。null なら `%LOCALAPPDATA%\Melore\<repositoryName>.db` |
| `ollama.endpoint` | 既定 `http://localhost:11434` |
| `ollama.embeddingModel` | 既定 `qwen3-embedding:0.6b` |
| `ollama.chatModel` | 既定 `qwen3.5:9b-q4_K_M`。`explain` で使う |
| `history.archived` | 完了して移された文書の glob。例: `.spec/archive/**`。当たるファイルの区分は `archived` |
| `history.rejected` | 却下して移された文書の glob。例: `.spec/reject/**`。当たるファイルの区分は `rejected` |

`history` は任意。省くと全ファイルの区分が `current` になり、`--scope history` のヒットは空になる。glob は include と同じ書式で、include / exclude で索引に入ったファイルにだけ効く。各 64 件まで、1 件 256 文字まで。空文字・絶対パス・`..` は起動時にエラーにする。同じファイルが `archived` と `rejected` の両方に当たると、そのファイルを列挙・再索引する時点（`index`、または検索前の自動の再索引）でエラーにする。

区分はパスだけで決め、文書のヘッダーにある状態は見ない。glob を変えて `index` すると、ファイルを解析し直さずに区分だけを付け直す。

Ollama が止まっていても `index` の解析と、FTS・シンボルの `search` は動く。ベクトル検索と `explain` の文章生成だけが使えない。
