namespace Melore.Core.Ports;

/// <summary>
/// テキストをベクトル化する。利用不可なら null を返す実装を許す。
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>利用可能なら true。</summary>
    bool IsAvailable { get; }

    /// <summary>モデル名。索引に記録する。</summary>
    string ModelName { get; }

    /// <summary>埋め込み入力の版。モデルや前処理を変えたら上げる。</summary>
    string AnalyzerVersion { get; }

    /// <summary>埋め込み。失敗時は null。</summary>
    Task<float[]?> EmbedAsync(string text, bool isQuery, CancellationToken cancellationToken);
}
