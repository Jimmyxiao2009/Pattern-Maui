$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$workDir = "E:\Desktop\项目\CrossPlatform\Pattern\apps\maui"
Write-Host "Starting Pattern.Maui from $workDir ..."
$app = Start-Process -FilePath "dotnet" -ArgumentList "run","-f","net10.0-windows10.0.19041.0","--no-build" -WorkingDirectory $workDir -PassThru
Write-Host ("Started dotnet PID={0}" -f $app.Id)

# 等待 Pattern 窗口出现（通过进程名查找）
Write-Host "Waiting for app window..."
$foundProc = $null
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Milliseconds 1000
    $procs = Get-Process | Where-Object { $_.ProcessName -match "Pattern" }
    if ($procs) {
        # 找有窗口的
        $withWin = $procs | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowHandle -ne $null }
        if ($withWin) { $foundProc = $withWin[0]; break }
    }
}

if (-not $foundProc) {
    Write-Host "No Pattern process with window found. Listing Pattern processes:"
    Get-Process | Where-Object { $_.ProcessName -match "Pattern" } | Format-Table ProcessName,Id,MainWindowTitle,MainWindowHandle
    Write-Host "Listing all top-level UIA windows:"
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $children = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) {
        if ($c.Current.ProcessId -eq $app.Id -or $c.Current.Name -match "Pattern" -or $c.Current.ClassName -match "MAUI|WinUI") {
            Write-Host ("  Name='{0}' Class='{1}' PID={2}" -f $c.Current.Name, $c.Current.ClassName, $c.Current.ProcessId)
        }
    }
    exit 1
}

Write-Host ("Window found: Title='{0}' PID={1} HWND={2}" -f $foundProc.MainWindowTitle, $foundProc.Id, $foundProc.MainWindowHandle)
