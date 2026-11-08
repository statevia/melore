using System.Text.Json;
using Melore.Contracts;

namespace Melore.Host;

/// <summary>
/// melore.json を読む。
/// </summary>
public static class SettingsLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// ファイルから設定を復元する。
    /// </summary>
    public static MeloreSettings Load(FileInfo file)
    {
        var json = File.ReadAllText(file.FullName);
        var settings = JsonSerializer.Deserialize<MeloreSettings>(json, ReadOptions) ?? throw new InvalidOperationException("設定を読めません: " + file.FullName);

        if (string.IsNullOrWhiteSpace(settings.RepositoryPath) || !Directory.Exists(settings.RepositoryPath))
        {
            throw new InvalidOperationException("repositoryPath が存在しません: " + settings.RepositoryPath);
        }

        ValidateHistory(settings.History);
        return settings;
    }

    /// <summary>
    /// <c>history.archived</c> / <c>history.rejected</c> の glob 1 つあたりの最大文字数。
    /// 実際のパターンは数十文字なので、誤入力や巨大な値を早めに弾く目安として 256 にする。
    /// </summary>
    public const int MaxHistoryPatternLength = 256;

    /// <summary>
    /// <c>history.archived</c> / <c>history.rejected</c> それぞれの最大件数。置き場所は数か所なので 64 で足りる。
    /// </summary>
    public const int MaxHistoryPatterns = 64;

    /// <summary>
    /// 履歴文書の glob を検証する。
    /// </summary>
    /// <remarks>
    /// 設定ファイルは不正を前提にする。<c>repositoryPath</c> の外を指せないよう、絶対パスと <c>..</c> を禁止する。
    /// 同じパスが両方に当たるかは glob だけでは決められないため、列挙時に <c>GlobLifecycleClassifier</c> が判定する。
    /// </remarks>
    /// <exception cref="InvalidOperationException">件数・長さ・書式のどれかが不正なとき。</exception>
    public static void ValidateHistory(HistorySettings? history)
    {
        if (history is null)
        {
            throw new InvalidOperationException("history は null にできません。使わないときは項目ごと省いてください。");
        }

        ValidatePatterns("history.archived", history.Archived);
        ValidatePatterns("history.rejected", history.Rejected);
    }

    private static void ValidatePatterns(string name, string[]? patterns)
    {
        if (patterns is null)
        {
            throw new InvalidOperationException(name + " は null にできません。");
        }

        if (patterns.Length > MaxHistoryPatterns)
        {
            throw new InvalidOperationException($"{name} は {MaxHistoryPatterns} 件までです。");
        }

        var invalid = patterns.FirstOrDefault(pattern => !IsValidPattern(pattern));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"{name} に不正な glob があります（空・{MaxHistoryPatternLength} 文字超・絶対パス・.. は不可）: {Truncate(invalid)}");
        }
    }

    private static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaxHistoryPatternLength)
        {
            return false;
        }

        var normalized = pattern.Replace('\\', '/');
        return !normalized.StartsWith('/')
               && !Path.IsPathRooted(pattern)
               && !normalized.Split('/').Contains("..");
    }

    /// <summary>エラーに載せる glob を 64 文字までに切る。巨大な値をそのまま出さないため。</summary>
    private static string Truncate(string? text) => text switch
    {
        null => "(null)",
        { Length: <= 64 } => text,
        _ => text[..64] + "…"
    };

    /// <summary>
    /// 環境変数 <c>MELORE_CONFIG</c> または引数 <c>--config</c> から読む。
    /// </summary>
    public static MeloreSettings LoadFromArgs(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("MELORE_CONFIG");
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                path = args[i + 1];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MELORE_CONFIG または --config で melore.json を指定してください。");
        }

        return Load(new FileInfo(path));
    }

    /// <summary>SQLite ファイルの場所。</summary>
    public static string ResolveDatabasePath(MeloreSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DatabasePath))
        {
            return Path.GetFullPath(settings.DatabasePath);
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Melore");
        Directory.CreateDirectory(root);
        return Path.Combine(root, settings.RepositoryName + ".db");
    }
}
