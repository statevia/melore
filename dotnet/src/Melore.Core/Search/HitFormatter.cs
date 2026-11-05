using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// ヒットを MCP 用 DTO に写す。
/// </summary>
public static class HitFormatter
{
    /// <summary>
    /// 本文を付けるかどうかで DTO を作る。
    /// </summary>
    public static HitDetail ToDetail(IMeloreStore store, SearchHit hit, bool includeBody)
        => HitDetail.From(hit, includeBody ? store.GetBody(hit.Id) : null);

    /// <summary>
    /// 関連ヒットを DTO にする。
    /// </summary>
    public static HitDetail ToDetail(IMeloreStore store, RelatedHit related, bool includeBody)
        => HitDetail.From(
            related.Hit,
            includeBody ? store.GetBody(related.Hit.Id) : null,
            related.RelType,
            related.Basis.ToString());
}
