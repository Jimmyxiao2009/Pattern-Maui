# Pattern architecture (MAUI migration)

```text
MAUI Windows / Android client
        │ WebSocket (Windows: owned sidecar process)
        ▼
Node sidecar — agent loop, memory, proactive engine, relay, channels
        │
        ├─ model providers (OpenAI-compatible / Anthropic / local)
        └─ SQLite + FTS5 data directory
```

The client owns the sidecar lifecycle. The random loopback port is an implementation detail read from the sidecar's startup line; there is no browser URL, Vite server, Tauri window, or Rust build in this repository. Android will use the relay/service deployment when the mobile runtime packaging is added.

The old Tauri architecture and source remain available in the archived `Pattern` repository for reference and rollback.
