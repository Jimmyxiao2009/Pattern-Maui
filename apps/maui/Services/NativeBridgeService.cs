using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pattern.Maui.Services;

/// <summary>
/// Local authenticated OS bridge consumed by the Node sidecar. It deliberately
/// binds only to loopback and is never a browser/UI endpoint. Unsupported native
/// operations return an explicit capability response instead of hanging tasks.
/// </summary>
public sealed class NativeBridgeService : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=');
    private Task? _loop;
    private bool _started;
    private int _port;

    public string? Url => _started ? $"http://127.0.0.1:{_port}" : null;
    public string Token => _token;

    public void Start()
    {
        if (_started || OperatingSystem.IsAndroid()) return;
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        try
        {
            _listener.Start();
            _started = true;
            _loop = Task.Run(ServeAsync);
        }
        catch { _started = false; }
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch when (_cts.IsCancellationRequested || !_listener.IsListening) { break; }
            catch { continue; }
            _ = Task.Run(() => HandleAsync(context), _cts.Token);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var auth = context.Request.Headers["Authorization"];
            if (!string.Equals(auth, $"Bearer {_token}", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 401;
                await WriteAsync(context, new { error = "unauthorized" });
                return;
            }
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            var payload = path switch
            {
                "/health" => new { ok = true, service = "pattern-maui-bridge" },
                "/foreground" => Foreground(),
                "/idle" => Idle(),
                "/power" => Power(),
                "/accessibility/tree" => new { supported = false, controls = Array.Empty<object>(), note = "MAUI bridge has no accessibility provider on this host" },
                "/accessibility/action" => new { ok = false, supported = false, error = "accessibility provider unavailable" },
                "/screenshot" => new { ok = false, supported = false, error = "screenshot provider unavailable" },
                "/input" => new { ok = false, supported = false, error = "input provider unavailable" },
                "/freeze" => new { ok = true, supported = false },
                "/notify" => new { ok = true, delivered = false, note = "native notification provider not installed" },
                "/recovery/capabilities" => new { available = false, store = "maui" },
                "/recovery/list" => new { transaction = Array.Empty<object>() },
                _ => null,
            };
            if (payload is null)
            {
                context.Response.StatusCode = 404;
                await WriteAsync(context, new { error = "bridge endpoint not found" });
                return;
            }
            await WriteAsync(context, payload);
        }
        catch (Exception error)
        {
            context.Response.StatusCode = 500;
            await WriteAsync(context, new { error = error.Message });
        }
        finally { context.Response.Close(); }
    }

    private static async Task WriteAsync(HttpListenerContext context, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        context.Response.ContentType = "application/json";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private static object Foreground()
    {
        if (!OperatingSystem.IsWindows()) return new { title = "", processId = 0 };
        var handle = GetForegroundWindow();
        var title = new StringBuilder(512);
        _ = GetWindowText(handle, title, title.Capacity);
        _ = GetWindowThreadProcessId(handle, out var processId);
        return new { title = title.ToString(), processId };
    }

    private static object Idle()
    {
        if (!OperatingSystem.IsWindows()) return new { seconds = 0, supported = false };
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return new { seconds = 0, supported = false };
        var milliseconds = unchecked((uint)Environment.TickCount) - info.dwTime;
        return new { seconds = milliseconds / 1000, supported = true };
    }

    private static object Power()
    {
        if (!OperatingSystem.IsWindows()) return new { percent = -1, plugged = false, supported = false };
        if (!GetSystemPowerStatus(out var status)) return new { percent = -1, plugged = false, supported = false };
        return new { percent = status.BatteryLifePercent == 255 ? -1 : status.BatteryLifePercent, plugged = status.ACLineStatus == 1, supported = true };
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        if (_loop is not null) try { await _loop; } catch { }
        _cts.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved1; public int BatteryLifeTime; public int BatteryFullLifeTime; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
