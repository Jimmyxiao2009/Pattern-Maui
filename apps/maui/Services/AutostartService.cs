using Microsoft.Win32;

#pragma warning disable CA1416 // every registry access is guarded by Supported/OperatingSystem.IsWindows

namespace Pattern.Maui.Services;

/// <summary>Best-effort per-user startup registration for the unpackaged Windows client.</summary>
public sealed class AutostartService
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "Pattern";

    public bool Supported => OperatingSystem.IsWindows();

    public bool IsEnabled
    {
        get
        {
            if (!Supported) return false;
            try { using var key = Registry.CurrentUser.OpenSubKey(RunKey, false); return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value); }
            catch { return false; }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (!Supported) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;
            if (!enabled) key.DeleteValue(ValueName, false);
            else
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) return false;
                key.SetValue(ValueName, $"\"{executable}\"");
            }
            return IsEnabled == enabled;
        }
        catch { return false; }
    }
}

#pragma warning restore CA1416
