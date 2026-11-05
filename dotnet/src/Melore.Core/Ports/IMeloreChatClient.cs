namespace Melore.Core.Ports;

/// <summary>
/// 短い説明文を生成する。利用不可なら null を返す実装を許す。
/// </summary>
public interface IMeloreChatClient
{
    /// <summary>デーモンとモデルが使えるなら true。</summary>
    bool IsAvailable { get; }

    /// <summary>モデル名。</summary>
    string ModelName { get; }

    /// <summary>1 往復の完了。失敗時は null。</summary>
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
