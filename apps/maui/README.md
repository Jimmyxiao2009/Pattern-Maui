# Pattern MAUI

The MAUI client replaces the browser/WebView application surface. It starts the existing Node sidecar itself, so users never navigate to a `127.0.0.1` page.

## Run on Windows

```powershell
pnpm sidecar:build
dotnet run --project apps/maui/Pattern.Maui.csproj -f net10.0-windows10.0.19041.0
```

Set `PATTERN_SIDECAR_PATH` to an absolute `sidecar/dist/index.cjs` path if launching outside the repository. Android remains a thin relay client until the Node runtime is moved into a remote/service deployment.
