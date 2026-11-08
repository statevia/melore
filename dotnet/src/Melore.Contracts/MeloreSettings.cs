namespace Melore.Contracts;

/// <summary>
/// 対象リポジトリとモデルの設定。
/// </summary>
public sealed class MeloreSettings
{
    /// <summary>表示名。DB ファイル名にも使う。</summary>
    public string RepositoryName { get; init; } = "repository";

    /// <summary>対象 Git リポジトリの絶対パス。</summary>
    public string RepositoryPath { get; init; } = "";

    /// <summary>取り込む glob。リポジトリルート相対。</summary>
    public string[] Include { get; init; } = ["docs/**/*.md", "dotnet/**/*.cs"];

    /// <summary>除外 glob。</summary>
    public string[] Exclude { get; init; } =
    [
        "**/bin/**",
        "**/obj/**",
        "artifacts/**",
        ".local/**",
        "nodejs/**/node_modules/**",
        "**/.env",
        "**/.env.*"
    ];

    /// <summary>Roslyn 用ソリューションのリポジトリ相対パス。未設定なら構文解析のみ。</summary>
    public string? SolutionPath { get; init; }

    /// <summary>SQLite ファイル。null のとき %LOCALAPPDATA%/Melore/{name}.db。</summary>
    public string? DatabasePath { get; init; }

    /// <summary>Ollama 接続。</summary>
    public OllamaSettings Ollama { get; init; } = new();

    /// <summary>履歴文書の置き場所。未設定ならすべて現行として扱う。</summary>
    public HistorySettings History { get; init; } = new();
}

/// <summary>
/// 完了・却下した文書を区別する glob。
/// </summary>
/// <remarks>
/// include / exclude で索引に入ったファイルにだけ効く。両方に当たるパスは設定エラーとする。
/// 書式は include と同じリポジトリルート相対の glob。
/// </remarks>
public sealed class HistorySettings
{
    /// <summary>完了して移された文書の glob。例: <c>.spec/archive/**</c>。</summary>
    public string[] Archived { get; init; } = [];

    /// <summary>却下して移された文書の glob。例: <c>.spec/reject/**</c>。</summary>
    public string[] Rejected { get; init; } = [];
}

/// <summary>
/// Ollama のエンドポイントとモデルタグ。
/// </summary>
public sealed class OllamaSettings
{
    /// <summary>既定はローカルデーモン。</summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>公式タグ `qwen3-embedding:0.6b`。</summary>
    public string EmbeddingModel { get; init; } = "qwen3-embedding:0.6b";

    /// <summary>公式タグ `qwen3.5:9b-q4_K_M`。explain で使う。</summary>
    public string ChatModel { get; init; } = "qwen3.5:9b-q4_K_M";
}
