namespace Melore.Core.Models;

/// <summary>
/// Git が検出したリネーム。
/// </summary>
/// <param name="FromPath">旧相対パス。</param>
/// <param name="ToPath">新相対パス。</param>
public sealed record GitRename(string FromPath, string ToPath);
