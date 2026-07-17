using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Pattern.Maui.Services;

/// <summary>Owns the existing Node agent process. No browser or Vite server is involved.</summary>
public sealed class SidecarRuntime : IAsyncDisposable
{
    private Process? _process;
    private ClientWebSocket? _socket;
    private string? _token;
    private int _port;
    public event Action<string>? StatusChanged;
    public event Action<string>? ChatDelta;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_socket?.State == WebSocketState.Open) return;
        await StopAsync();
        StatusChanged?.Invoke("正在启动本地运行时…");
        var sidecar = FindSidecar();
        _process = Process.Start(new ProcessStartInfo("node", $"\"{sidecar}\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(sidecar)!
        }) ?? throw new InvalidOperationException("无法启动 Node sidecar。请安装 Node.js 22+。");
        var startup = await _process.StandardOutput.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("sidecar 在宣告连接信息前退出。");
        using var payload = JsonDocument.Parse(startup);
        _port = payload.RootElement.GetProperty("port").GetInt32();
        _token = payload.RootElement.GetProperty("token").GetString() ?? throw new InvalidOperationException("sidecar 未提供令牌。");
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws?token={Uri.EscapeDataString(_token)}"), cancellationToken);
        _ = ReceiveLoopAsync(_socket);
        StatusChanged?.Invoke("运行时已连接");
    }

    public async Task SendChatAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_socket?.State != WebSocketState.Open) throw new InvalidOperationException("运行时未连接。");
        var message = JsonSerializer.Serialize(new { type = "chat.send", id = Guid.NewGuid().ToString("N"), text, history = Array.Empty<object>() });
        await _socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var json = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "chat.delta" && json.RootElement.TryGetProperty("delta", out var delta)) ChatDelta?.Invoke(delta.GetString() ?? "");
            }
        }
        catch (Exception error) { StatusChanged?.Invoke($"运行时已断开：{error.Message}"); }
    }

    private static string FindSidecar()
    {
        var configured = Environment.GetEnvironmentVariable("PATTERN_SIDECAR_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "sidecar", "dist", "index.cjs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("找不到 sidecar/dist/index.cjs。先运行 pnpm sidecar:build，或设置 PATTERN_SIDECAR_PATH。\n");
    }

    private async Task StopAsync()
    {
        if (_socket is not null) { _socket.Dispose(); _socket = null; }
        if (_process is { HasExited: false }) { _process.Kill(true); await _process.WaitForExitAsync(); }
        _process?.Dispose(); _process = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
