---
name: serena-workflow-research-impl
description: >-
  Serena MCP でコード調査・設計前調査・実装を進める。serena-melore が使えるとき、シンボル探索とシンボル編集を grep/read より優先する。
  ユーザーが調査、設計前の確認、実装、改修、原因究明、影響範囲確認を依頼したときに適用する。
disable-model-invocation: false
---

# Serena：調査・設計前調査・実装での利用

## 前提

- MCP サーバー **`serena-melore`**（`dotnet/`）が接続されていること。定義は `.cursor/mcp.json` と `.mcp.json`、対象と除外は `dotnet/.serena/project.yml` が正本。
- 未接続・エラー時は、サイレントにフォールバックせず **作業を停止**し、`serena-melore` の有効化を促す。ユーザーが「Serena なしで続行」と明示したときだけ Read / Grep 等へ切り替える。
- **`activate_project` は使わない**。
- Serena の `initial_instructions` にある **「コードファイルの Read を全面禁止」**は、**エージェントの運用では次の優先ルールに落とす**（厳格適用はしない）。

## いつこのスキルを使うか

次の依頼に含まれるとき、このスキルを先に読み、可能な範囲で Serena を使う。

- 仕様・挙動の調査、設計前のコード調査
- バグ原因の追跡、影響範囲の把握
- 実装・改修・リファクタ（コード変更を伴うタスク）

## 調査・設計前調査の流れ

1. **`serena-melore` が ready** か確認する。未起動なら有効化を促して停止する。
2. **シンボルの場所がはっきりしない**
   - 広く候補を拾うなら **`find_symbol`**（`name_path_pattern`、`relative_path` でディレクトリを絞ると速い）。
   - ファイルが決まっているなら **`get_symbols_overview`** で構成を把握。
3. **定義・参照・実装の追跡**
   - **`find_declaration`** / **`find_referencing_symbols`** / **`find_implementations`** を用途に応じて使う。
4. **型チェック・診断**
   - 必要に応じて **`get_diagnostics_for_file`**。
5. **メモリ**
   - 名前から有用そうなら **`list_memories`** / **`read_memory`**（Serena メモリ）。

調査では **`include_body`** は必要になるまで `false` にし、必要なシンボルだけ本文を読む。

## 実装の流れ

1. 上記と同様に **`serena-melore` を ready にする**。
2. 変更対象シンボルを **`find_symbol`** 等で特定し、破壊的変更なら **`find_referencing_symbols`** で参照を確認。
3. **編集手段の選び方**
   - **シンボル単位で丸ごと差し替え** → **`replace_symbol_body`**
   - **シンボルの前後に挿入** → **`insert_before_symbol`** / **`insert_after_symbol`**
   - **メソッド内の数行など局所** → **`replace_content`**
   - **型付きリネーム** → **`rename_symbol`**
   - **安全な削除** → **`safe_delete_symbol`**

## ツール早見

| 目的 | 主な手段 |
| --- | --- |
| ファイル内のシンボル一覧 | `get_symbols_overview` |
| 名前パターンでシンボル検索 | `find_symbol` |
| 宣言へ | `find_declaration` |
| 参照箇所 | `find_referencing_symbols` |
| 実装列挙 | `find_implementations` |
| 診断 | `get_diagnostics_for_file` |
| シンボル本文の置換 | `replace_symbol_body`、局所は `replace_content` |
| メモリ | `list_memories`、`read_memory`、`write_memory` 等 |

各ツールの引数は **MCP スキーマ**を必ず確認してから呼ぶ。

## 通常ツールとの棲み分け（推奨）

- **コードの発見・構造把握・シンボル編集**は Serena を優先する。
- **ドキュメントのみ・設定ファイルだけ・Serena 対象外のファイル**は従来どおり Read / 検索でよい。
- Serena と通常ツールの **二重実行は避け**、同じ目的なら一方に寄せる。

## 関連

- マニュアル初回: MCP `initial_instructions`（過度に厳しい禁止事項は本スキルで緩和）
