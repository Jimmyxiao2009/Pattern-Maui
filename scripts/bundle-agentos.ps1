[CmdletBinding()]
param(
    [string]$CoreRoot,
    [switch]$SkipValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$patternRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($CoreRoot)) {
    $CoreRoot = [System.IO.Path]::GetFullPath((Join-Path $patternRoot '..\..\Core'))
}
$publisher = Join-Path $CoreRoot 'scripts\publish-windows.ps1'
if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
    throw "AgentOS Core publisher not found: $publisher"
}

& $publisher -Configuration Release -SkipValidation:$SkipValidation
if ($LASTEXITCODE -ne 0) {
    throw "AgentOS publish failed with exit code $LASTEXITCODE"
}

$source = Join-Path $CoreRoot 'artifacts\win-x64\agentos.exe'
$resourceDir = Join-Path $patternRoot 'apps\desktop\src-tauri\resources'
$destination = Join-Path $resourceDir 'agentos.exe'
New-Item -ItemType Directory -Path $resourceDir -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) {
    throw 'Bundled AgentOS hash does not match the validated source artifact.'
}

$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    source = $source
    destination = $destination
    sha256 = $destinationHash
    validationReport = Join-Path $CoreRoot 'artifacts\validation\report.json'
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resourceDir 'agentos-integration.json') -Encoding utf8
Write-Host "Bundled AgentOS: $destination"
Write-Host "SHA-256: $destinationHash"
