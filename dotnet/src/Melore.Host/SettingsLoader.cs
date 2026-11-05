using System.Text.Json;
using Melore.Contracts;

namespace Melore.Host;

/// <summary>
/// melore.json を読む。
/// </summary>
public static class SettingsLoader
{
    /// <summary>
    /// ファイルから設定を復元する。
    /// </summary>
    public static MeloreSettings Load(FileInfo file)
    {
        var json = File.ReadAllText(file.FullName);
        var settings = JsonSerializer.Deserialize<MeloreSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("設定を読めません: " + file.FullName);

        if (string.IsNullOrWhiteSpace(settings.RepositoryPath) || !Directory.Exists(settings.RepositoryPath))
        {
            throw new InvalidOperationException("repositoryPath が存在しません: " + settings.RepositoryPath);
        }

        return settings;
    }

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
