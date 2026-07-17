using System.Runtime.InteropServices;

#pragma warning disable CS0067, CS0169 // Android/macOS intentionally compile the desktop-only members out

namespace Pattern.Maui.Services;

/// <summary>Registers one predictable desktop shortcut for the persistent quick chat.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public event Action? QuickChatRequested;
    private Thread? _thread;
    private uint _threadId;
    private bool _running;

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _running) return;
        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Pattern.GlobalHotkey" };
        _thread.Start();
    }

    private void MessageLoop()
    {
#if WINDOWS
        _threadId = GetCurrentThreadId();
        if (!RegisterHotKey(IntPtr.Zero, 0x5041, ModControl | ModAlt, (uint)'P')) { _running = false; return; }
        try
        {
            while (_running && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == WmHotkey && message.wParam == (IntPtr)0x5041) QuickChatRequested?.Invoke();
            }
        }
        finally { UnregisterHotKey(IntPtr.Zero, 0x5041); }
#endif
    }

    public void Dispose()
    {
        _running = false;
#if WINDOWS
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
#endif
        try { _thread?.Join(500); } catch { }
        _thread = null;
    }

#if WINDOWS
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr hWnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
#endif
}
