namespace Melore.Contracts;

/// <summary>
/// 検索結果の 1 件。
/// </summary>
/// <param name="Id">チャンクまたはシンボルの ID。</param>
/// <param name="Kind">知識種別。</param>
/// <param name="Title">見出しまたはシンボル名。</param>
/// <param name="Snippet">短い抜粋。</param>
/// <param name="Score">統合スコア。大きいほど関連が強い。</param>
/// <param name="Source">出典。</param>
/// <param name="Lifecycle">出典ファイルの区分。現行か履歴文書かを利用者が取り違えないために返す。</param>
public sealed record SearchHit(
    string Id,
    HitKind Kind,
    string Title,
    string Snippet,
    double Score,
    SourceSpan Source,
    DocumentLifecycle Lifecycle);
