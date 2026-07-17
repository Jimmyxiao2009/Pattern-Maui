using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Pattern.Maui.Services;

public sealed record ChatTurn(string Role, string Content);

/// <summary>
/// Owns the Node agent process and its authenticated WebSocket. The MAUI UI never
/// has to open a browser URL; loopback is an internal transport only.
/// </summary>
public sealed class SidecarRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateLock = new();
    private Process? _process;
    private ClientWebSocket? _socket;
    private bool _stdioMode;
    private CancellationTokenSource? _runtimeCts;
    private Task? _receiveTask;
    private bool _intentionalStop;

    public event Action<string>? StatusChanged;
    public event Action<string>? ChatDelta;
    public event Action? ChatDone;
    public event Action<string>? ChatError;
    public event Action? ChatCancelled;
    public event Action<JsonElement>? RuntimeEvent;

    public bool IsConnected => _stdioMode
        ? _process is { HasExited: false }
        : _socket?.State == WebSocketState.Open;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsAndroid())
        {
            StatusChanged?.Invoke("移动端中继模式：请先完成设备配对");
            return;
        }
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return;
            await StopCoreAsync();
            _intentionalStop = false;
            StatusChanged?.Invoke("正在启动本地运行时…");

            var sidecar = FindSidecar();
            var node = FindNode(sidecar);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = node,
                Arguments = $"\"{sidecar}\" --stdio",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(sidecar)!,
            }) ?? throw new InvalidOperationException("无法启动 Node sidecar。");

            _process = process;
            _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = DrainStderrAsync(process, _runtimeCts.Token);
            _ = MonitorProcessAsync(process, _runtimeCts.Token);

            var startup = await ReadStartupPayloadAsync(process, _runtimeCts.Token);
            _stdioMode = startup.TryGetProperty("transport", out var transport) && transport.GetString() == "stdio";
            if (_stdioMode)
            {
                _receiveTask = ReceiveStdoutLoopAsync(process, _runtimeCts.Token);
            }
            else
            {
                var port = startup.GetProperty("port").GetInt32();
                var token = startup.GetProperty("token").GetString();
                if (port is < 1 or > 65535 || string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("sidecar 返回了无效的端口或令牌。");
                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?token={Uri.EscapeDataString(token)}"), connectTimeout.Token);
                _socket = socket;
                _receiveTask = ReceiveLoopAsync(socket, _runtimeCts.Token);
            }
            await ConfigureFromEnvironmentAsync(cancellationToken);
            StatusChanged?.Invoke("运行时已连接");
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task<string> SendChatAsync(
        string text,
        IReadOnlyList<ChatTurn>? history = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var message = JsonSerializer.Serialize(new
        {
            type = "chat.send",
            id,
            text,
            history = history ?? Array.Empty<ChatTurn>(),
            sessionId,
        });
        await SendTextAsync(message, cancellationToken);
        return id;
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { await StartAsync(cancellationToken); return; }
            catch (Exception error) { last = error; await Task.Delay(250 * (attempt + 1), cancellationToken); }
        }
        throw new InvalidOperationException("Agent 运行时连接失败。请检查 Node.js 22+、sidecar 构建和错误日志。", last);
    }

    public async Task CancelChatAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;
        await SendTextAsync(JsonSerializer.Serialize(new { type = "chat.cancel", id }), cancellationToken);
    }

    public async Task ConfigureAsync(object configuration, CancellationToken cancellationToken = default)
    {
        var process = _process ?? throw new InvalidOperationException("sidecar 未启动。");
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { method = "runtime.configure", @params = configuration }));
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private Task ConfigureFromEnvironmentAsync(CancellationToken cancellationToken)
    {
        var provider = Environment.GetEnvironmentVariable("PATTERN_PROVIDER") ?? "openai-compatible";
        var endpoint = Environment.GetEnvironmentVariable("PATTERN_ENDPOINT") ?? "https://api.openai.com/v1";
        var model = Environment.GetEnvironmentVariable("PATTERN_MODEL") ?? "gpt-4o-mini";
        var apiKey = Environment.GetEnvironmentVariable("PATTERN_API_KEY") ?? "";
        return ConfigureAsync(new
        {
            provider, endpoint, model, apiKey,
            persona = "You are Pattern, a helpful personal AI companion.",
            personaName = "Pattern",
            userName = "User",
            proactive = new { enabled = false, paused = false, bedtimeHour = 23 },
        }, cancellationToken);
    }

    private async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        if (_stdioMode)
        {
            var process = _process ?? throw new InvalidOperationException("运行时未启动。");
            await process.StandardInput.WriteLineAsync(text);
            await process.StandardInput.FlushAsync(cancellationToken);
            return;
        }
        var socket = _socket ?? throw new InvalidOperationException("运行时未连接。");
        if (socket.State != WebSocketState.Open) throw new InvalidOperationException("运行时连接已断开。");
        await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveStdoutLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var json = JsonDocument.Parse(line);
                    Dispatch(json.RootElement);
                }
                catch (JsonException error)
                {
                    Debug.WriteLine($"[pattern-sidecar] invalid JSONL output: {error.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (!_intentionalStop)
            {
                _stdioMode = false;
                _ = ReconnectAfterDisconnectAsync();
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var json = JsonDocument.Parse(message.ToArray());
                Dispatch(json.RootElement);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_intentionalStop) StatusChanged?.Invoke($"运行时已断开：{error.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (!_intentionalStop && _socket == socket) _ = ReconnectAfterDisconnectAsync();
        }
    }

    private void Dispatch(JsonElement message)
    {
        if (!message.TryGetProperty("type", out var type)) return;
        switch (type.GetString())
        {
            case "chat.delta": ChatDelta?.Invoke(message.TryGetProperty("delta", out var delta) ? delta.GetString() ?? "" : ""); break;
            case "chat.done": ChatDone?.Invoke(); break;
            case "chat.error": ChatError?.Invoke(message.TryGetProperty("message", out var error) ? error.GetString() ?? "未知错误" : "未知错误"); break;
            case "chat.cancelled": ChatCancelled?.Invoke(); break;
            case "chat.event":
            case "runtime.agent_state":
            case "proactive.impulse":
            case "proactive.inbox.updated": RuntimeEvent?.Invoke(message.Clone()); break;
        }
    }

    private async Task ReconnectAfterDisconnectAsync()
    {
        try
        {
            StatusChanged?.Invoke("运行时断开，正在重连…");
            await Task.Delay(500);
            await EnsureConnectedAsync();
        }
        catch (Exception error) { StatusChanged?.Invoke($"运行时重连失败：{error.Message}"); }
    }

    private async Task<JsonElement> ReadStartupPayloadAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null) throw new InvalidOperationException("sidecar 在宣告连接信息前退出。");
            try
            {
                using var candidate = JsonDocument.Parse(line);
                if ((candidate.RootElement.TryGetProperty("port", out _) && candidate.RootElement.TryGetProperty("token", out _))
                    || (candidate.RootElement.TryGetProperty("transport", out var transport) && transport.GetString() == "stdio"))
                    return candidate.RootElement.Clone();
            }
            catch (JsonException) { /* tolerate diagnostic lines before the startup record */ }
        }
        throw new TimeoutException("等待 sidecar 端口令牌超时。");
    }

    private async Task DrainStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                Debug.WriteLine($"[pattern-sidecar] {line}");
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task MonitorProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            if (!_intentionalStop && !cancellationToken.IsCancellationRequested) StatusChanged?.Invoke($"sidecar 已退出（代码 {process.ExitCode}）");
        }
        catch (OperationCanceledException) { }
    }

    private static string FindNode(string sidecar)
    {
        var configured = Environment.GetEnvironmentVariable("PATTERN_NODE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var root = new DirectoryInfo(Path.GetDirectoryName(sidecar)!);
        while (root is not null)
        {
            foreach (var candidate in new[] { Path.Combine(root.FullName, "node.exe"), Path.Combine(root.FullName, "resources", "node", "node.exe") })
                if (File.Exists(candidate)) return candidate;
            root = root.Parent;
        }
        return OperatingSystem.IsWindows() ? "node.exe" : "node";
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
        throw new FileNotFoundException("找不到 sidecar/dist/index.cjs。先运行 pnpm sidecar:build，或设置 PATTERN_SIDECAR_PATH。");
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try { _intentionalStop = true; await StopCoreAsync(); }
        finally { _lifecycle.Release(); _lifecycle.Dispose(); }
    }

    private async Task StopCoreAsync()
    {
        _runtimeCts?.Cancel();
        _runtimeCts?.Dispose();
        _runtimeCts = null;
        if (_socket is { State: WebSocketState.Open } socket)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None); } catch { }
        }
        _socket?.Dispose();
        _socket = null;
        if (_receiveTask is not null) { try { await _receiveTask; } catch { } _receiveTask = null; }
        if (_process is { HasExited: false }) { try { _process.Kill(true); await _process.WaitForExitAsync(); } catch { } }
        _process?.Dispose();
        _process = null;
        _stdioMode = false;
    }
}
