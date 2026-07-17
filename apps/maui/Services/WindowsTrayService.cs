using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable CS0067, CS0169 // Android/macOS compile the desktop tray out

namespace Pattern.Maui.Services;

/// <summary>Small Win32 tray shell; no WPF/WinForms dependency is added to the MAUI app.</summary>
public sealed class WindowsTrayService : IDisposable
{
#if WINDOWS
    public event Action? ShowRequested;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _window;
#else
    public event Action? ShowRequested { add { } remove { } }
#endif
#if WINDOWS
    private bool _running;
#endif
#if WINDOWS
    private static readonly WndProc WindowProc = Dispatch;
    private const string ClassName = "Pattern.Maui.TrayWindow";
    private const uint WmTray = 0x0400 + 42;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmQuit = 0x0012;
    private const int HwndMessage = -3;
#endif

    public void Start()
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows() || _running) return;
        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Pattern.WindowsTray" };
        _thread.Start();
#endif
    }

    public void Notify(string title, string body)
    {
#if WINDOWS
        if (_window == IntPtr.Zero) return;
        var icon = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = _window, uID = 42, uFlags = 0x10,
            szInfo = body.Length > 255 ? body[..255] : body,
            szInfoTitle = title.Length > 63 ? title[..63] : title,
            dwInfoFlags = 0,
        };
        ShellNotifyIcon(1, ref icon);
#endif
    }

    private void MessageLoop()
    {
#if WINDOWS
        _threadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);
        var windowClass = new WndClass { lpfnWndProc = WindowProc, hInstance = module, lpszClassName = ClassName };
        RegisterClass(ref windowClass);
        _window = CreateWindowEx(0, ClassName, "Pattern", 0, 0, 0, 0, 0, new IntPtr(HwndMessage), IntPtr.Zero, module, IntPtr.Zero);
        if (_window == IntPtr.Zero) { _running = false; return; }
        Instances[_window] = this;
        AddTrayIcon(_window);
        try { while (_running && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); } }
        finally
        {
            RemoveTrayIcon(_window);
            Instances.Remove(_window);
            DestroyWindow(_window);
            UnregisterClass(ClassName, module);
            _window = IntPtr.Zero;
        }
#endif
    }

    public void Dispose()
    {
#if WINDOWS
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        try { _thread?.Join(500); } catch { }
        _thread = null;
#endif
    }

#if WINDOWS
    private static readonly Dictionary<IntPtr, WindowsTrayService> Instances = [];
    private static void AddTrayIcon(IntPtr window)
    {
        var icon = new NotifyIconData { cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = window, uID = 42, uFlags = 1 | 2 | 4, uCallbackMessage = WmTray, hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), szTip = "Pattern" };
        ShellNotifyIcon(0, ref icon);
    }
    private static void RemoveTrayIcon(IntPtr window)
    {
        var icon = new NotifyIconData { cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = window, uID = 42 };
        ShellNotifyIcon(2, ref icon);
    }
    private static IntPtr Dispatch(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmTray && (uint)lParam.ToInt64() is WmLButtonDblClk or WmRButtonUp && Instances.TryGetValue(window, out var tray)) tray.ShowRequested?.Invoke();
        return DefWindowProc(window, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass { public uint style; public WndProc lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string? lpszMenuName; public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData { public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; public uint uTimeout; public uint dwState; public uint dwStateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; public uint dwVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo; public uint uVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle; public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr hWnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WndClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
#endif
}
