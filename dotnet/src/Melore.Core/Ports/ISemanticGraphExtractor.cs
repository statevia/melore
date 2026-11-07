using Melore.Core.Models;

namespace Melore.Core.Ports;

/// <summary>
/// ソリューションから calls / implements を取る。
/// </summary>
public interface ISemanticGraphExtractor
{
    /// <summary>
    /// 解決できた辺だけ返す。開けなければ例外。
    /// </summary>
    Task<IReadOnlyList<SemanticRelation>> ExtractAsync(string solutionPath, CancellationToken cancellationToken);
}
