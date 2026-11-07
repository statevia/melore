# sonar-scan-results

SonarQube のスキャン結果（Quality Gate / issue / カバレッジ）を確認し、不足を是正する。

## 前提

- 安全手順の原則は `.cursor/skills/sonarqube-mcp-ops/SKILL.md` に従う（開始時に読む）。
- MCP **`sonarqube`** が利用可能なこと。未接続・認可エラー時は `mcp_auth` で **USER token**（project token ではない）を確認する。
- プロジェクトキーは **`statevia_melore`**、組織は `statevia`（`.github/workflows/build.yml` が正本）。
- 解析は CI（`.github/workflows/build.yml`）で行う。ローカルの再スキャン用スクリプトは無い。

## 実行してほしいこと

1. SKILL を読み、`toggle_automatic_analysis` が使えれば**最初に自動解析を無効化**する。
2. ブランチ文脈があれば対象ブランチまたは PR を明示する。
3. 現状把握（読み取りのみ）:
   - `get_project_quality_gate_status` で Quality Gate を取得。組み込み **Sonar way**（新規コードの信頼性・セキュリティ・保守性が A、Security Hotspot のレビュー 100%、カバレッジ 80% 以上、重複行 3% 以下）。
   - `search_sonar_issues_in_projects` で issue を取得（type = Bug / Vulnerability / Code Smell、Security Hotspot、severity、**new code を優先**）。
   - `get_component_measures` で `coverage` / `new_coverage` / `duplicated_lines_density` 等の不足を確認する。カバレッジは CI の `dotnet test` が `Code Coverage` コレクターで収集する。`.cursor/` と `.claude/` は解析対象外、2 つの `Program.cs` はカバレッジ対象外（`build.yml` の `begin` が正本）。
   - 必要に応じ `show_rule` でルールの趣旨・修正方針を確認する。
4. トリアージ: **新規コード違反と Quality Gate を最優先**。重大度と誤検知の可能性で分類し、対応方針（修正 / テスト追加 / 抑制の妥当性）を簡潔に提示する。
5. 是正:
   - コードは規約（`.cursor/rules/csharp-standards.mdc`）に従って修正する。
   - カバレッジ不足は対象範囲にテストを追加する（AAA・1ケース1シナリオ）。
   - issue ステータス変更（False Positive / Won't Fix 等）は**ユーザー合意がある場合のみ** `change_sonar_issue_status` を使い、理由を残す。
6. ローカル検証（push 前に green を確認）: `cd dotnet; dotnet build Melore.slnx; dotnet test Melore.slnx`。
7. 再スキャンは push または PR 更新で CI に任せる。**修正直後の即時再検証はしない**（反映遅延）。Quality Gate 判定は Sonar のプロジェクト画面を正とする。常駐の HTTP 接続では `analyze_code_snippet` は使えない。
8. 終了時も **Automatic Analysis は無効のまま**にする。有効に戻すと CI 解析と二重になる。
9. 対応内容（是正した issue / 残課題 / Quality Gate 結果）を簡潔に報告する。

## ユーザー向け短文（チャットへの貼り付け用）

SonarQube のスキャン結果を確認し、新規コードの Quality Gate と issue・カバレッジ不足を是正してください。手順は `.cursor/skills/sonarqube-mcp-ops/SKILL.md` に従ってください。
