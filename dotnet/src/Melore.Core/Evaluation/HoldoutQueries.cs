namespace Melore.Core.Evaluation;

/// <summary>
/// 調整に使っていない検証用クエリ。H12 は未追跡ファイルのフィクスチャで見る。
/// </summary>
public static class HoldoutQueries
{
    /// <summary>付録 A の H01–H11 と H13–H28。</summary>
    public static IReadOnlyList<GoldenQuery> All { get; } =
    [
        new("H01", "冪等性キーで実行開始の重複を防ぐ仕組みはどこ", ["ExecutionIdempotencyService.cs"]),
        new("H02", "スケジュール実行を時刻になったら起動する処理", ["ExecutionScheduleDispatchService.cs"]),
        new("H03", "テナントのシークレットを暗号化して保存する実装", ["TenantSecretProtector.cs"]),
        new("H04", "モジュールの署名を検証するコード", ["ModuleSignatureVerifier.cs"]),
        new("H05", "ログのプロパティキーの命名規則", ["docs/reference/logging-property-keys.md"]),
        new("H06", "API のエラーコード一覧", ["docs/reference/error-codes.md"]),
        new("H07", "権限キーの一覧と意味", ["docs/reference/permission-keys.md"]),
        new("H08", "環境変数の一覧", ["docs/reference/environment-variables.md"]),
        new("H09", "traceparent ヘッダからトレース ID を決める処理", ["TraceIdResolver.cs"]),
        new("H10", "状態 input の $ パス式を実行時に評価するところ", ["StateInputEvaluator.cs", "ExecutionContextPathResolver.cs"]),
        new("H11", "Join で枝が書いた vars を親へ戻す規則", ["docs/specifications/execution/fork-join.md"]),
        new("H13", "Wait を外部シグナルで再開する契約", ["docs/specifications/execution/wait-cancel.md"]),
        new("H14", "テナントのソフトリミットの仕様", [".spec/specs/tenant-soft-limits"]),
        new("H15", "Action Host の監視と再起動（supervisor）", ["action-host-supervisor", "docs/guides/action-host.md"]),
        new("H16", "定義のバージョニングをどう決めたか", ["docs/decisions/definition-versioning.md"]),
        new("H17", "イベントストアを採用した理由", ["docs/decisions/event-store.md"]),
        new("H18", "ActionInputTreeNormalizer のテスト", ["ActionInputTreeNormalizerTests.cs"]),
        new("H19", "S3 からモジュールのアーティファクトを取得する", ["AwsS3ArtifactFetcher.cs"]),
        new("H20", "OCI レジストリからモジュールを取ってくる実装", ["OrasOciArtifactFetcher.cs"]),
        new("H21", "通知アクションの SMTP 接続設定を読み込む", ["NotificationSmtpConnectionLoader.cs"]),
        new("H22", "CLI のログイン情報をどこに保存するか", ["CliCredentialsStore.cs"]),
        new("H23", "負荷試験のやり方", ["docs/guides/capacity-load-testing.md"]),
        new("H24", "予期しない例外を Sentry に送るところ", ["SentryUnexpectedExceptionReporter.cs"]),
        new("H25", "実行のイベントとコマンドの一覧", ["docs/specifications/execution/events-and-commands.md"]),
        new("H26", "TraceIdResolver のテストはどこ", ["TraceIdResolverTests.cs"]),
        new("H27", "Docker でローカル起動する手順", ["docs/guides/operations-docker.md", "docs/guides/getting-started.md"]),
        new("H28", "DB スキーマのテーブル定義", ["docs/reference/database-schema.md"])
    ];
}
