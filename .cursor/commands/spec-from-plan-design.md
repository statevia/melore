# spec-from-plan-design

Plan モードで得た設計・合意内容を、`.spec/specs/` 配下へ **仕様セット**として追記する（新規フォルダ＋3ファイル）。

## 前提

- 同一チャット内に **Plan の出力**、またはユーザーが **`@`** で関連コンテキストを添付済みであること。
- Markdown / Spec は `.cursor/skills/markdown-spec-authoring/SKILL.md`（テンプレ準拠・markdownlint）に従う。

## 実行してほしいこと

1. **スペック用フォルダ名**が未確定なら、内容に即した **`kebab-case` の短い名前**を1つ提案し、ユーザーに確認するか、ユーザー指定を優先する。
2. **`.spec/specs/<フォルダ名>/`** を新規作成し、次の **3ファイル** を作成または更新する（既に部分的に存在する場合はテンプレに合わせて埋める）。
   - **`requirements.md`** — `.spec/templates/requirements-template.md` の見出し構成・必須項目に沿う。
   - **`design.md`** — `.spec/templates/design-template.md` に沿う。
   - **`tasks.md`** — `.spec/templates/tasks-template.md` に沿う。
3. メタ情報（バージョン・更新日など）は **既存の specs**（例: 同フォルダ内の他ファイルや、sibling spec）と同種の書き方に揃える。本文は日本語。
4. Plan の内容が不足しているセクションがある場合は、**推測で埋めず**プレースホルダや「未確定」と明記し、ユーザーへ確認質問を出す。
5. 変更した各 Markdown に対して `npx markdownlint-cli2 "<変更したファイルパス>"` を実行し、エラーがあれば修正する。
6. 完了後、作成したパス一覧と概要をユーザーに報告する。

## ユーザー向け短文（チャットへの貼り付け用）

新規フォルダを `.spec/specs/` に用意し、`requirements.md`・`design.md`・`tasks.md` を、`.spec/templates/` の各テンプレートに沿って作成してください。
