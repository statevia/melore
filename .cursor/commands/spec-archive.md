# spec-archive

完了した仕様セットを、`.spec/specs/` から **`.spec/archive/specs/`** へ移してアーカイブする。

**却下**（実装しない判断）した仕様は archive に入れず、**`.spec/reject/specs/`** へ移す。置き場の説明は [`.spec/reject/README.md`](../../.spec/reject/README.md)。

## 前提

- 対象は **`.spec/specs/<フォルダ名>/`**（通常は `requirements.md` / `design.md` / `tasks.md` を含むフォルダ）。
- 同一チャットでユーザーが **`@`** 参照したフォルダ、または明示した **kebab-case フォルダ名**を優先する。
- 既存のアーカイブ先は **`.spec/archive/specs/<同じフォルダ名>/`**（フォルダ名は変更しない）。
- 既存の却下先は **`.spec/reject/specs/<同じフォルダ名>/`**（フォルダ名は変更しない）。
- 却下の場合、`tasks.md` の未チェックは警告対象にしない（実装しないため）。

## 実行してほしいこと

1. **対象の確定**
   - ユーザー指定または `@` 参照のフォルダ名を使う。
   - 未指定なら、チャット文脈から候補を 1〜3 個挙げて確認する。
   - ソースパス: `.spec/specs/<フォルダ名>/`
   - 宛先パス（完了）: `.spec/archive/specs/<フォルダ名>/`
   - 宛先パス（却下）: `.spec/reject/specs/<フォルダ名>/`

2. **事前チェック**
   - ソースが存在しない → エラー報告して終了。
   - 宛先が既に存在する → **上書きしない**。別名案または手動解消をユーザーに確認する。
   - `tasks.md` に未完了タスク（`- [ ]`）が残っている場合は、アーカイブ前に **警告**し、続行可否をユーザーに確認する（ユーザーが明示的にアーカイブ依頼済みなら続行してよい）。却下への移動ではこの警告を省略してよい。

3. **移動**
   - フォルダごと移動する（コピー＋削除ではなく move）。
   - PowerShell 例（完了）:

     ```powershell
     Move-Item -Path ".spec/specs/<フォルダ名>" -Destination ".spec/archive/specs/<フォルダ名>"
     ```

   - PowerShell 例（却下）:

     ```powershell
     New-Item -ItemType Directory -Force -Path ".spec/reject/specs" | Out-Null
     Move-Item -Path ".spec/specs/<フォルダ名>" -Destination ".spec/reject/specs/<フォルダ名>"
     ```

4. **結果報告**
   - 移動元・移動先パス、フォルダ内の主要ファイル一覧を短く報告する。
   - 失敗時はエラー内容と次の一手を書く。

## やらないこと

- `templates/` の移動
- アーカイブからの復元（別途依頼がある場合のみ）
- Git コミット（ユーザーが明示した場合を除く）

## ユーザー向け短文（チャットへの貼り付け用）

`.spec/specs/<フォルダ名>/` を `.spec/archive/specs/<フォルダ名>/` へ移動してアーカイブしてください（却下なら `.spec/reject/specs/`）。宛先が既にある場合は上書きしないでください。
