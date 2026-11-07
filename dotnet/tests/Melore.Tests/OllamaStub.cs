using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Melore.Tests;

/// <summary>
/// Ollama の HTTP API を真似るローカルサーバー。外部のモデルには接続しない。
/// </summary>
internal sealed class OllamaStub : IDisposable
{
    /// <summary>chat が返す本文。</summary>
    public const string ChatReply = "スタブの回答です。";

    private readonly HttpListener _listener = new();
    private readonly bool _available;
    private readonly bool _brokenModels;

    /// <param name="available">false なら api/tags に 503 を返す。</param>
    /// <param name="brokenModels">true なら embed と chat に 500 を返す。</param>
    public OllamaStub(bool available = true, bool brokenModels = false)
    {
        _available = available;
        _brokenModels = brokenModels;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Endpoint = new Uri($"http://localhost:{port}/");
        _listener.Prefixes.Add(Endpoint.ToString());
        _listener.Start();
        _ = ServeAsync();
    }

    /// <summary>接続先。</summary>
    public Uri Endpoint { get; }

    /// <inheritdoc />
    public void Dispose() => _listener.Close();

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var (status, body) = Respond(context.Request.Url!.AbsolutePath);
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private (int Status, string Body) Respond(string path)
        => (path, _available, _brokenModels) switch
        {
            ("/api/tags", false, _) => (503, "{}"),
            ("/api/tags", true, _) => (200, """{"models":[]}"""),
            (_, _, true) => (500, "{}"),
            ("/api/embed", _, _) => (200, """{"model":"embed-model","embeddings":[[0.1,0.2,0.3]]}"""),
            ("/api/chat", _, _) => (
                200,
                "{\"model\":\"chat-model\",\"message\":{\"role\":\"assistant\",\"content\":\"" + ChatReply + "\"},\"done\":true}\n"),
            _ => (404, "{}")
        };
}
