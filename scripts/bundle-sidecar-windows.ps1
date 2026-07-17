# Assemble a portable Windows runtime for Pattern sidecar (no console window when launched with CREATE_NO_WINDOW).
# Output: apps/desktop/src-tauri/resources/pattern-runtime/
#   node.exe + index.cjs + package.json + node_modules (prod deps with native better-sqlite3)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$SidecarDir = Join-Path $Root 'sidecar'
$OutDir = Join-Path $Root 'apps\desktop\src-tauri\resources\pattern-runtime'

Write-Host "==> Building sidecar bundle (esbuild prod)"
Push-Location $SidecarDir
try {
  pnpm exec esbuild src/index.ts `
    --bundle --platform=node --format=cjs --target=node22 `
    --external:better-sqlite3 `
    --external:@huggingface/transformers `
    --outfile=dist/index.prod.cjs
} finally {
  Pop-Location
}

if (Test-Path $OutDir) {
  Remove-Item $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutDir 'node_modules') -Force | Out-Null

Write-Host "==> Copying Node runtime"
$node = (Get-Command node -ErrorAction Stop).Source
Copy-Item $node (Join-Path $OutDir 'node.exe') -Force

Write-Host "==> Copying index.cjs"
Copy-Item (Join-Path $SidecarDir 'dist\index.prod.cjs') (Join-Path $OutDir 'index.cjs') -Force

# Minimal package.json so better-sqlite3 bindings can resolve module root
@'
{
  "name": "pattern-runtime",
  "private": true,
  "type": "commonjs"
}
'@ | Set-Content -Path (Join-Path $OutDir 'package.json') -Encoding UTF8

Write-Host "==> Installing production native deps into runtime pack"
# Install only what must stay external (native / optional heavy)
$pkg = @{
  name = 'pattern-runtime-pack'
  private = $true
  dependencies = @{
    'better-sqlite3' = '12.11.1'
  }
} | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $OutDir 'package.json') -Value $pkg -Encoding UTF8

Push-Location $OutDir
try {
  # Use npm for a self-contained node_modules (no pnpm symlinks — works after copy to Program Files)
  # npm writes deprecation notices to stderr; don't treat as terminating errors
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  npm install --omit=dev --no-package-lock --no-fund --no-audit
  $code = $LASTEXITCODE
  $ErrorActionPreference = $prev
  if ($code -ne 0) { throw "npm install failed with exit $code" }
} finally {
  Pop-Location
}

# Restore minimal package.json after install (keep deps recorded for tooling)
$finalPkg = @{
  name = 'pattern-runtime'
  private = $true
  type = 'commonjs'
  dependencies = @{
    'better-sqlite3' = '12.11.1'
  }
} | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $OutDir 'package.json') -Value $finalPkg -Encoding UTF8

# Smoke: node must load native better-sqlite3 from this pack
Write-Host "==> Smoke-checking runtime pack"
Push-Location $OutDir
try {
  & .\node.exe -e "require('better-sqlite3'); console.log('ok-better-sqlite3')"
  if ($LASTEXITCODE -ne 0) { throw "runtime pack smoke failed" }
} finally {
  Pop-Location
}

Write-Host "==> Runtime pack ready:"
Get-ChildItem $OutDir | Format-Table Name, Length -AutoSize
Write-Host "node_modules size:" ((Get-ChildItem (Join-Path $OutDir 'node_modules') -Recurse -File | Measure-Object Length -Sum).Sum / 1MB).ToString('0.0') "MB"
