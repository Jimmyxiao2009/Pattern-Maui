# Pattern

Pattern is a personal AI companion whose agent runtime runs as a Node sidecar. The client is now .NET MAUI (Windows + Android target) and no longer opens a browser or Vite/Tauri development URL.

## Run the MAUI Windows client

Requirements: .NET 10 SDK with the `maui-windows` workload, Node.js 22+, and pnpm.

```powershell
pnpm install
pnpm sidecar:build
pnpm maui:windows
```

The MAUI process starts `sidecar/dist/index.cjs`, reads its random port/token from stdout, and connects internally. You do not need to visit `127.0.0.1` or keep a Vite server running. To use a packaged sidecar, set `PATTERN_SIDECAR_PATH` to its absolute path.

## Layout

- `apps/maui` — native MAUI shell and the first chat surface.
- `sidecar` — model loop, memory, proactive features, channels, relay, and task runtime.
- `packages/*` — shared protocol and business logic.

The previous Svelte/Tauri desktop and mobile clients are preserved in the archived `Pattern` GitHub repository. This repository intentionally contains no Rust/Tauri build targets or debug caches.

## Checks

```powershell
pnpm sidecar:test
dotnet build apps/maui/Pattern.Maui.csproj -f net10.0-windows10.0.19041.0
```
