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
public sealed record SearchHit(
    string Id,
    HitKind Kind,
    string Title,
    string Snippet,
    double Score,
    SourceSpan Source);
