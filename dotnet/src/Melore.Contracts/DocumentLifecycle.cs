namespace Melore.Contracts;

/// <summary>
/// 索引したファイルの区分。現行か、完了した履歴文書か、却下した履歴文書か。
/// </summary>
/// <remarks>
/// パスと <c>melore.json</c> の <c>history</c> の glob だけで決める。文書のヘッダーにある状態は使わない。
/// 対象リポジトリでは、archive へ移した後もヘッダーが Draft のまま残ることがあるためである。
/// </remarks>
public enum DocumentLifecycle
{
    /// <summary>現行の文書とコード。<c>history</c> のどの glob にも当たらないもの。</summary>
    Current,

    /// <summary>完了して移された文書。<c>history.archived</c> に当たるもの。</summary>
    Archived,

    /// <summary>却下して移された文書。<c>history.rejected</c> に当たるもの。</summary>
    Rejected
}
