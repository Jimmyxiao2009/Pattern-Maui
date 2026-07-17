# Start Pattern with Vite (dev URL) + desktop exe. Keeps both alive.
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $Root 'apps\desktop\package.json'))) {
  $Root = 'E:\Desktop\项目\CrossPlatform\Pattern'
}
$ExeCandidates = @(
  (Join-Path $Root 'apps\desktop\src-tauri\target\release\pattern-desktop.exe'),
  (Join-Path $env:LOCALAPPDATA 'pattern\pattern-desktop.exe'),
  'C:\Program Files\Pattern\pattern-desktop.exe'
)
$Exe = $ExeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Exe) { throw 'pattern-desktop.exe not found' }

Write-Host "Root: $Root"
Write-Host "Exe:  $Exe"

# Ensure Vite on 1420
function Test-Vite {
  try {
    $r = Invoke-WebRequest 'http://127.0.0.1:1420/' -UseBasicParsing -TimeoutSec 2
    return $r.StatusCode -eq 200
  } catch { return $false }
}

if (-not (Test-Vite)) {
  Write-Host 'Starting Vite on 127.0.0.1:1420 ...'
  $viteCmd = "pnpm --dir apps/desktop dev -- --host 127.0.0.1 --port 1420 --strictPort"
  Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $viteCmd -WorkingDirectory $Root -WindowStyle Minimized
  $deadline = (Get-Date).AddSeconds(45)
  while ((Get-Date) -lt $deadline) {
    if (Test-Vite) { Write-Host 'Vite ready'; break }
    Start-Sleep -Milliseconds 500
  }
  if (-not (Test-Vite)) { throw 'Vite failed to start on :1420' }
} else {
  Write-Host 'Vite already up'
}

# Single instance: if already running, just focus
$existing = Get-Process pattern-desktop -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) {
  Write-Host "Already running PID $($existing.Id) — focusing"
} else {
  # Prefer local install path for Start Menu consistency
  $localDir = Join-Path $env:LOCALAPPDATA 'pattern'
  $localExe = Join-Path $localDir 'pattern-desktop.exe'
  New-Item -ItemType Directory -Force -Path $localDir | Out-Null
  if ((Resolve-Path $Exe).Path -ne $localExe) {
    Copy-Item -LiteralPath $Exe -Destination $localExe -Force
    $Exe = $localExe
  }
  Write-Host 'Launching Pattern...'
  Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
  Start-Sleep -Seconds 2
}

# Focus main window
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class PatternFocus {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  public static bool Focus() {
    var procs = System.Diagnostics.Process.GetProcessesByName("pattern-desktop");
    if (procs.Length == 0) return false;
    uint pid = (uint)procs[0].Id;
    IntPtr main = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      var t = new StringBuilder(256); var c = new StringBuilder(256);
      GetWindowText(h, t, 256); GetClassName(h, c, 256);
      if (c.ToString().Contains("Tauri") && t.ToString() == "Pattern") { main = h; return false; }
      return true;
    }, IntPtr.Zero);
    if (main == IntPtr.Zero) return false;
    ShowWindow(main, 9);
    SetWindowPos(main, IntPtr.Zero, 80, 50, 1280, 820, 0x0040);
    return SetForegroundWindow(main);
  }
}
"@
Start-Sleep -Seconds 1
$ok = [PatternFocus]::Focus()
Write-Host $(if ($ok) { 'Window focused. (Closing the window only hides to tray — check system tray.)' } else { 'Launched but could not focus window yet.' })
Get-Process pattern-desktop -ErrorAction SilentlyContinue | Format-Table Id, StartTime
