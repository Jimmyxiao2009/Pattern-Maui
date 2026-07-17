using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
#endif

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
            JsonElement? requestBody = null;
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var raw = await reader.ReadToEndAsync(_cts.Token);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try { requestBody = JsonDocument.Parse(raw).RootElement.Clone(); } catch { }
                }
            }
            var payload = path switch
            {
                "/health" => new { ok = true, service = "pattern-maui-bridge" },
                "/foreground" => Foreground(),
                "/idle" => Idle(),
                "/power" => Power(),
                "/accessibility/tree" => AccessibilityTree(),
                "/accessibility/action" => AccessibilityAction(requestBody),
                "/screenshot" => Screenshot(),
                "/input" => Input(requestBody),
                "/freeze" => Freeze(requestBody),
                "/notify" => Notify(requestBody),
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
        if (!OperatingSystem.IsWindows()) return new { idleSeconds = 0, supported = false };
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return new { idleSeconds = 0, supported = false };
        var milliseconds = unchecked((uint)Environment.TickCount) - info.dwTime;
        return new { idleSeconds = milliseconds / 1000, supported = true };
    }

    private static object Power()
    {
        if (!OperatingSystem.IsWindows()) return new { percent = -1, plugged = false, supported = false };
        if (!GetSystemPowerStatus(out var status)) return new { percent = -1, plugged = false, supported = false };
        return new { percent = status.BatteryLifePercent == 255 ? -1 : status.BatteryLifePercent, plugged = status.ACLineStatus == 1, supported = true };
    }

    private static object AccessibilityTree()
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) return Unsupported("accessibility");
        var root = GetForegroundWindow();
        if (root == IntPtr.Zero) return new { supported = true, controls = Array.Empty<object>() };
        var controls = new List<object>();
        AddControl(root, controls);
        EnumChildWindows(root, (handle, _) => { if (controls.Count < 500) AddControl(handle, controls); return true; }, IntPtr.Zero);
        return new { supported = true, window = root.ToInt64().ToString("x"), controls };
#else
        return Unsupported("accessibility");
#endif
    }

    private static object AccessibilityAction(JsonElement? request)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows() || request is null) return new { ok = false, supported = false, error = "accessibility provider unavailable" };
        var target = ResolveControl(request.Value);
        if (target == IntPtr.Zero) return new { ok = false, supported = true, error = "control not found" };
        var action = request.Value.TryGetProperty("action", out var actionValue) ? actionValue.GetString() : "invoke";
        if (string.Equals(action, "setValue", StringComparison.OrdinalIgnoreCase))
        {
            var value = request.Value.TryGetProperty("value", out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;
            SendMessage(target, WmSetText, IntPtr.Zero, value);
            return new { ok = true, supported = true, action = "setValue" };
        }
        SendMessage(target, BmClick, IntPtr.Zero, IntPtr.Zero);
        return new { ok = true, supported = true, action = "invoke" };
#else
        return new { ok = false, supported = false, error = "accessibility provider unavailable" };
#endif
    }

    private static object Screenshot()
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) return Unsupported("screenshot");
        try
        {
            var width = GetSystemMetrics(SmCxScreen);
            var height = GetSystemMetrics(SmCyScreen);
            if (width <= 0 || height <= 0) return new { ok = false, supported = true, error = "screen dimensions unavailable" };
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = stream.ToArray();
            var path = Path.Combine(FileSystem.AppDataDirectory, $"screenshot-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png");
            File.WriteAllBytes(path, bytes);
            return new { ok = true, supported = true, path, pngBase64 = Convert.ToBase64String(bytes), width, height };
        }
        catch (Exception error) { return new { ok = false, supported = true, error = error.Message }; }
#else
        return new { ok = false, supported = false, error = "screenshot provider unavailable" };
#endif
    }

    private static object Input(JsonElement? request)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows() || request is null) return Unsupported("input");
        try
        {
            var body = request.Value;
            var type = body.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : string.Empty;
            switch (type)
            {
                case "click":
                    SetCursorPos(body.GetProperty("x").GetInt32(), body.GetProperty("y").GetInt32());
                    var button = body.TryGetProperty("button", out var buttonValue) ? buttonValue.GetString() : "left";
                    var down = string.Equals(button, "right", StringComparison.OrdinalIgnoreCase) ? RightDown : LeftDown;
                    var up = string.Equals(button, "right", StringComparison.OrdinalIgnoreCase) ? RightUp : LeftUp;
                    MouseEvent(down); MouseEvent(up); return new { ok = true, type };
                case "scroll":
                    MouseEvent(MouseWheel, (uint)(body.TryGetProperty("amount", out var amount) ? amount.GetInt32() : 0)); return new { ok = true, type };
                case "type":
                    var text = body.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? string.Empty : string.Empty;
                    foreach (var character in text) SendUnicode(character);
                    return new { ok = true, type, count = text.Length };
                case "key":
                    var key = body.TryGetProperty("key", out var keyValue) ? keyValue.GetString() ?? string.Empty : string.Empty;
                    SendKey(key, body.TryGetProperty("modifiers", out var modifiers) ? modifiers : default);
                    return new { ok = true, type, key };
                default: return new { ok = false, supported = true, error = $"unknown input type: {type}" };
            }
        }
        catch (Exception error) { return new { ok = false, supported = true, error = error.Message }; }
#else
        return new { ok = false, supported = false, error = "input provider unavailable" };
#endif
    }

    private static object Freeze(JsonElement? request)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows() || request is null) return Unsupported("freeze");
        var frozen = request.Value.TryGetProperty("frozen", out var value) && value.GetBoolean();
        var ok = BlockInput(frozen);
        return new { ok, supported = true, frozen };
#else
        return new { ok = true, supported = false };
#endif
    }

    private static object Notify(JsonElement? request)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows() || request is null) return Unsupported("notify");
        var title = request.Value.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : "Pattern";
        var body = request.Value.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() : string.Empty;
        MessageBeep(0x00000040);
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero) FlashWindow(foreground, true);
        return new { ok = true, supported = true, delivered = true, title, body };
#else
        return new { ok = true, delivered = false, supported = false };
#endif
    }

    private static object Unsupported(string capability) => new { ok = false, supported = false, error = $"{capability} provider unavailable" };

    public async ValueTask DisposeAsync()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try { BlockInput(false); } catch { }
        }
#endif
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        if (_loop is not null) try { await _loop; } catch { }
        _cts.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved1; public int BatteryLifeTime; public int BatteryFullLifeTime; }
#if WINDOWS
    private const uint WmSetText = 0x000C;
    private const uint BmClick = 0x00F5;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MouseWheel = 0x0800;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    private static void AddControl(IntPtr handle, ICollection<object> controls)
    {
        var name = new StringBuilder(512);
        _ = GetWindowText(handle, name, name.Capacity);
        var className = new StringBuilder(256);
        _ = GetClassName(handle, className, className.Capacity);
        controls.Add(new
        {
            refId = $"hwnd:{handle.ToInt64():x}",
            @ref = $"hwnd:{handle.ToInt64():x}",
            name = name.ToString(),
            automationId = GetDlgCtrlID(handle).ToString(),
            controlType = className.ToString(),
            enabled = IsWindowEnabled(handle),
        });
    }

    private static IntPtr ResolveControl(JsonElement request)
    {
        if (request.TryGetProperty("ref", out var reference))
        {
            var raw = reference.GetString() ?? string.Empty;
            if (raw.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase) && long.TryParse(raw[5..], System.Globalization.NumberStyles.HexNumber, null, out var value)) return new IntPtr(value);
        }
        var root = GetForegroundWindow();
        var candidates = new List<IntPtr>();
        if (root != IntPtr.Zero) { candidates.Add(root); EnumChildWindows(root, (handle, _) => { candidates.Add(handle); return candidates.Count < 500; }, IntPtr.Zero); }
        var wantedId = request.TryGetProperty("automationId", out var id) ? id.GetString() : null;
        var wantedName = request.TryGetProperty("name", out var name) ? name.GetString() : null;
        foreach (var candidate in candidates)
        {
            var text = new StringBuilder(512); _ = GetWindowText(candidate, text, text.Capacity);
            if (!string.IsNullOrWhiteSpace(wantedName) && !string.Equals(text.ToString(), wantedName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(wantedId) && !string.Equals(GetDlgCtrlID(candidate).ToString(), wantedId, StringComparison.OrdinalIgnoreCase)) continue;
            return candidate;
        }
        return IntPtr.Zero;
    }

    private static void MouseEvent(uint flags, uint data = 0) => mouse_event(flags, 0, 0, data, UIntPtr.Zero);

    private static void SendUnicode(char character)
    {
        var input = new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = character, dwFlags = KeyEventUnicode } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        input.U.ki.dwFlags = KeyEventUnicode | KeyEventKeyUp;
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(string value, JsonElement modifiers)
    {
        var keys = new List<byte>();
        if (modifiers.ValueKind == JsonValueKind.Array) foreach (var modifier in modifiers.EnumerateArray()) if (VirtualKeys.TryGetValue(modifier.GetString() ?? string.Empty, out var modifierKey)) keys.Add(modifierKey);
        if (VirtualKeys.TryGetValue(value.ToLowerInvariant(), out var key)) keys.Add(key);
        foreach (var code in keys) SendVirtual(code, false);
        for (var index = keys.Count - 1; index >= 0; index--) SendVirtual(keys[index], true);
    }

    private static void SendVirtual(byte key, bool up)
    {
        var input = new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = up ? KeyEventKeyUp : 0 } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static readonly Dictionary<string, byte> VirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alt"] = 0x12, ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["win"] = 0x5B,
        ["enter"] = 0x0D, ["tab"] = 0x09, ["escape"] = 0x1B, ["esc"] = 0x1B, ["backspace"] = 0x08,
        ["space"] = 0x20, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["home"] = 0x24, ["end"] = 0x23, ["delete"] = 0x2E, ["insert"] = 0x2D,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73, ["f5"] = 0x74,
        ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77, ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
    };

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, string? lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool BlockInput(bool block);
    [DllImport("user32.dll")] private static extern bool FlashWindow(IntPtr hWnd, bool invert);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
#endif
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
