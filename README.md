# Pattern

Pattern is a personal AI companion whose agent runtime runs as a Node sidecar. The client is now .NET MAUI (Windows, Android and macOS Catalyst targets) and no longer opens a browser or Vite/Tauri development URL.

## Run the MAUI Windows client

Requirements: .NET 10 SDK with the `maui-windows` workload, Node.js 22+, and pnpm.

```powershell
pnpm install
pnpm sidecar:build
pnpm maui:windows
```

The MAUI process starts `sidecar/dist/index.cjs --stdio` and communicates over authenticated JSONL stdin/stdout. It does not depend on a random loopback port, browser URL, firewall rule, or Vite server. To use a packaged sidecar, set `PATTERN_SIDECAR_PATH` and optionally `PATTERN_NODE_PATH` to absolute paths.

## Layout

- `apps/maui` — native MAUI shell and the first chat surface (Windows/macOS local Agent; Android relay mode).
- `sidecar` — model loop, memory, proactive features, channels, relay, and task runtime.
- `packages/*` — shared protocol and business logic.

The previous Svelte/Tauri desktop and mobile clients are preserved in the archived `Pattern` GitHub repository. This repository intentionally contains no Rust/Tauri build targets or debug caches.

## Checks

```powershell
pnpm sidecar:test
dotnet build apps/maui/Pattern.Maui.csproj -f net10.0-windows10.0.19041.0
```
