namespace Melore.Core.Evaluation;

/// <summary>
/// 経緯（決めた理由・却下した理由・改修の中身）を問うクエリ。<c>scope=history</c> で測る。
/// </summary>
/// <remarks>
/// 期待パスは対象リポジトリ（Statevia）の <c>.spec/archive/specs/</c> と <c>.spec/reject/specs/</c>。
/// 2026-11-08 の A/B 計測で使った 14 問を元に、Spec のフォルダ名と語が重ならない言い回しへ直した。
/// 目標値は未決定なので、eval の終了コードには反映しない（history-document-scope の U5）。
/// </remarks>
public static class HistoryQueries
{
    /// <summary>R01–R14。</summary>
    public static IReadOnlyList<GoldenQuery> All { get; } =
    [
        new("R01", "定期実行の run-as の所属を、どこで確かめるようにしたのか。その経緯", ["archive/specs/schedule-run-as-permission-check", "archive/specs/schedule-project-boundary"]),
        new("R02", "状態が失敗したときに別の状態へ遷移できるようにした理由", ["archive/specs/execution-failed-transition", "archive/specs/nodes-on-failed-support"]),
        new("R03", "ID を時刻順に並ぶ形式で採番することにした理由", ["archive/specs/uuid-v7-id-generation-consistency"]),
        new("R04", "ノードの識別子を短い 16 進の文字列にした背景", ["archive/specs/execution-node-id-hex12"]),
        new("R05", "ワーカーとスケジューラのプロセスを分けたのはなぜか", ["archive/specs/runtime-worker-scheduler-process-split"]),
        new("R06", "Fork の枝ごとに子の実行を作る設計にした経緯", ["archive/specs/runtime-fork-child-executions"]),
        new("R07", "ランタイムがメモリに状態を持たない方針になった理由", ["archive/specs/runtime-stateless-architecture", "archive/specs/runtime-stateless-worker"]),
        new("R08", ".NET のメジャーバージョンを上げたときの作業内容", ["archive/specs/dotnet10-migration"]),
        new("R09", "ログインの失敗回数による締め出しを DB に保存するようにした理由", ["archive/specs/login-failure-lock-persistence"]),
        new("R10", "一覧取得の API で全件を返すのをやめた理由", ["archive/specs/core-api-list-paged-only"]),
        new("R11", "識別子ごとに専用の型を作る案を見送った理由", ["reject/specs/typed-identifier-keys"]),
        new("R12", "テナントの秘密情報を保存時に暗号化すると決めた経緯", ["archive/specs/tenant-secret-encryption"]),
        new("R13", "実行のたびに発生する DB への書き込みを減らした改修", ["archive/specs/execution-primary-write-reduction"]),
        new("R14", "ワークフロー定義でノードを名前で参照する書き方に変えた理由", ["archive/specs/nodes-format-id-to-name"])
    ];
}
