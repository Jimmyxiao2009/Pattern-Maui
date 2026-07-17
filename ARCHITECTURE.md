# Pattern architecture (MAUI migration)

```text
MAUI Windows / macOS client
        │ JSONL stdin/stdout (owned sidecar process)
        ▼
Node sidecar — agent loop, memory, proactive engine, relay, channels
        │
        ├─ model providers (OpenAI-compatible / Anthropic / local)
        └─ SQLite + FTS5 data directory
```

The native client owns the sidecar lifecycle. The local transport is a single process pipe, so the user never needs a browser URL, Vite server, random loopback port, firewall exception, Tauri window, or Rust build. The sidecar still exposes its authenticated WebSocket listener for remote/legacy clients.

Android is a relay-only client: it pairs with the sidecar deployment through WebDAV, queues messages while offline, and does not attempt to run Node inside the APK.

See [docs/MAUI_MIGRATION_PLAN.md](docs/MAUI_MIGRATION_PLAN.md) for the feature-by-feature migration and acceptance plan.

The old Tauri architecture and source remain available in the archived `Pattern` repository for reference and rollback.
