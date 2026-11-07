# タスク定義書

## 記載ルール

- タスクは **小さく独立して完了できる単位** に分割する
- 各タスクに **対象ファイル / 目的 / 要件番号 / 完了条件** を必ず記載する
- 既存資産を使う場合は `_Leverage` に明記する
- 複雑な仕様では、タスク本文だけでなく**処理フロー図に対応する実装タスク**を含める
- 最終タスクとして **`@docs` の仕様更新** を必ず追加する（API IF / 振る舞い / エラー契約の変更を反映）

## タスクテンプレート

- [ ] [連番]. [タスク名]
  - File: [対象ファイルパス]
  - 内容: [実施内容]
  - 目的: [このタスクが必要な理由]
  - _Leverage: [再利用する既存ファイル/モジュール。なければ `なし`]_
  - _Requirements: [要件番号。例: 1.1, 2.3]_
  - _Definition of Done: [完了判定条件]_

## 例（バックエンド実装）

- [ ] 1. 型定義を追加する
  - File: `src/types/feature.ts`
  - 内容: 機能用データ構造の型を定義する
  - 目的: 実装全体の型安全性を担保する
  - _Leverage: `src/types/base.ts`_
  - _Requirements: 1.1_
  - _Definition of Done: 型定義がビルドを通過し、既存型との整合が取れている_

- [ ] 2. サービス実装を追加する
  - File: `src/services/FeatureService.ts`
  - 内容: ユースケース処理とエラーハンドリングを実装する
  - 目的: 業務ロジックをアプリ層から分離する
  - _Leverage: `src/services/BaseService.ts`, `src/utils/errorHandler.ts`_
  - _Requirements: 3.2_
  - _Definition of Done: 正常系・異常系を満たし、単体テストが追加されている_

- [ ] 3. APIエンドポイントを実装する
  - File: `src/api/feature.ts`
  - 内容: CRUD API と入力バリデーションを追加する
  - 目的: 外部から機能を利用可能にする
  - _Leverage: `src/controllers/BaseController.ts`, `src/utils/validation.ts`_
  - _Requirements: 4.2, 4.3_
  - _Definition of Done: リクエスト検証とステータスコード仕様を満たす_

## 図対応タスク（必要時）

- [ ] [連番]. 処理フロー図を作成/更新する
  - File: `design.md` または `requirements.md`
  - 内容: 複雑な分岐・状態遷移・外部連携を `mermaid` 図で可視化する
  - 目的: 文章だけでは伝わりにくい処理の流れを明確にする
  - _Requirements: [該当要件番号]_
  - _Definition of Done: 実装内容と図が一致し、レビューで追跡可能_

## 完了時の最終タスク（必須）

- [ ] [連番]. `@docs` の仕様書を更新する
  - File: `docs/*.md`（変更内容に対応する仕様書）
  - 内容: 実装で追加/変更した API IF・UI 挙動・エラー契約・運用ルールを仕様書へ反映する
  - 目的: 実装とドキュメントの乖離を防ぎ、後続開発・運用の判断コストを下げる
  - _Leverage: 既存の `docs/architecture/data-model.md`, `docs/guides/cli.md` など_
  - _Requirements: [該当要件番号]_
  - _Definition of Done: 変更した仕様が docs に反映され、関連箇所の整合が取れている_
