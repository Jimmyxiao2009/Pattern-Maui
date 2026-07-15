$ErrorActionPreference = 'Stop'
$env:CARGO_TARGET_DIR = 'C:\codex-build\pattern-mobile'
pnpm --dir apps/mobile tauri android build --debug --apk
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
