# Pattern MAUI

The MAUI client replaces the browser/WebView application surface. On Windows and macOS it starts the existing Node sidecar itself over authenticated JSONL stdio, so users never navigate to a `127.0.0.1` page or depend on a loopback port. Android is a WebDAV relay client.

## Run on Windows

```powershell
pnpm maui:windows
```

For a clean Debug build (the command restores .NET dependencies automatically):

```powershell
pnpm install --frozen-lockfile
pnpm maui:windows:debug
```

The Settings page can export/import a versioned JSON client backup. It contains
the profile and conversation sessions, but never API keys or relay secrets;
those remain in the platform secure store.

Set `PATTERN_SIDECAR_PATH` to an absolute `sidecar/dist/index.cjs` path if launching outside the repository. Android remains a thin relay client until the Node runtime is moved into a remote/service deployment.
