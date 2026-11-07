namespace Melore.Core.Models;

/// <summary>
/// 意味解析で解決できた関係。FQN は構文解析と同じ形。
/// </summary>
/// <param name="FromFqn">呼び出し元または実装型。</param>
/// <param name="ToFqn">呼び出し先またはインターフェース。</param>
/// <param name="RelType"><c>calls</c> または <c>implements</c>。</param>
public sealed record SemanticRelation(string FromFqn, string ToFqn, string RelType);
