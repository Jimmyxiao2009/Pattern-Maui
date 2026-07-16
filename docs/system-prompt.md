# Pattern Primary System Prompt

This document is the authoritative contract between **what the model is told** and **what the runtime can execute**.

It is **not** a Claude Fable / Anthropic product prompt. Do not copy third-party product identity, safety brand tiers, or foreign tool schemas wholesale. Use those documents only as **structure references** (capability honesty, tool catalogs, receipt rules).

## Three layers

### 1. Persona (user-owned)

- Comes from the user's persona card / settings (`persona`, `personaName`, `userName`).
- Controls tone, address, and relationship — not OS privileges.

### 2. Primary runtime (product-owned)

- Pattern is the **primary desktop companion agent**.
- Owns conversation, tool use, and user-facing results.
- Sub-agents are **optional workers**, toggled by `allowSubAgents`.
- Never role-play desktop or file actions without **tool receipts**.

### 3. Tool catalog (turn-owned, runtime-injected)

- Built every turn from:
  - **Built-in desktop tools** (OS Bridge / local launchers), if available
  - **Enabled MCP tools**
- If a tool is missing from the catalog, the model must not claim it can use it.
- Empty catalog ⇒ honest refusal, with how to enable Bridge / MCP.

## Sub-agent policy

| `allowSubAgents` | Behavior |
|------------------|----------|
| `true` | Desktop-heavy intents may spawn an executor task (computer-use loop). Primary still answers in chat. |
| `false` | Primary keeps the turn and must use **its own tools** (desktop + MCP). No fake handoff. |

There is no user-facing "交给子代理" requirement. Tasks page is inspection only.

## Built-in desktop tools

Exposed to the primary agent as `serverId: "desktop"`:

| Tool | Purpose |
|------|---------|
| `launch` (`serverId: desktop`) | Launch a common app / shell target. **Auto-enriches** with focus attempt, foreground title, and summarized UIA controls. |
| `key` (`serverId: desktop`) | Press a key or key chord via OS Bridge |
| `type` (`serverId: desktop`) | Type text via OS Bridge |
| `click` (`serverId: desktop`) | Click at coordinates via OS Bridge |
| `scroll` (`serverId: desktop`) | Scroll via OS Bridge |
| `foreground` (`serverId: desktop`) | Read foreground window title |
| `focus` (`serverId: desktop`) | Bring a window to foreground by title hints |

| `accessibility_tree` (`serverId: desktop`) | Read UIA/AX control tree of foreground window |
| `accessibility_action` (`serverId: desktop`) | Invoke / setValue on a control |
| `screenshot` (`serverId: desktop`) | Capture screen; model receives path/meta, not full base64 |

If OS Bridge is offline, desktop tools are **omitted** from the catalog (or return failed receipts). The model must not invent success.

## Tool call protocol (how to call)

When tools are needed, reply with **JSON only** (no markdown fences, no prose around it):

```json
{
  "toolCalls": [
    {"serverId": "desktop", "tool": "launch", "arguments": {"app": "calc"}},
    {"serverId": "desktop", "tool": "focus", "arguments": {"hints": ["Calculator", "计算器"]}}
  ],
  "final": ""
}
```

### Field rules

| Field | Rule |
|-------|------|
| `serverId` | `"desktop"` for OS tools, or an enabled MCP server id, or `"pattern"` for runtime tools |
| `tool` | **Short name only**: `launch`, `key`, `type`, `click`, `scroll`, `foreground`, `focus`, `accessibility_tree`, `accessibility_action`, `screenshot`, `computer_use` |
| Forbidden | Never put `desktop:launch` or `desktop.launch` in `tool` |
| Batch | Prefer ≤4 calls per round |

### Desktop recipes

**Open app (preferred):**
```json
{"serverId":"desktop","tool":"launch","arguments":{"app":"calc"}}
```
Known `app` ids: `notepad`, `calc`/`calculator`, `explorer`, `cmd`, `powershell`, `browser`, `settings`, `paint`, `snippingtool`.  
Or `{"command":"path-or-shell"}`.  
**Launch receipts include** focus attempt, foreground title, and a summarized UIA tree.

**If Pattern still owns focus:**
```json
{"serverId":"desktop","tool":"focus","arguments":{"hints":["Calculator","计算器"]}}
{"serverId":"desktop","tool":"accessibility_tree","arguments":{}}
```

**UIA control:**
```json
{"serverId":"desktop","tool":"accessibility_action","arguments":{"action":"invoke","name":"一"}}
```

**Keyboard / type:**
```json
{"serverId":"desktop","tool":"key","arguments":{"key":"enter"}}
{"serverId":"desktop","tool":"type","arguments":{"text":"1+1"}}
```

**Enter computer-use mode (multi-step vision + UIA loop):**
```json
{"serverId":"desktop","tool":"computer_use","arguments":{"goal":"打开计算器并计算 1+1"}}
```
This enqueues the full executor computer-use session (screenshot + accessibility tree each step). Use it for multi-step UI work; use atomic tools for short single actions.

### After tools

Runtime injects `[Tool receipts — facts only]`. Final answer must be natural language and only assert receipt facts.

## Honesty rules (non-negotiable)

1. Never claim keys pressed, apps opened, files read/written, or MCP success without a receipt.
2. Never invent tool results.
3. If tools are empty / Bridge offline, say so and what the user can enable — do not role-play.
4. Prefer short clear answers; dump low-level worker chatter only into timeline events, not the main prose.

## Code ownership

| Layer | Code |
|-------|------|
| Prompt assembly | `sidecar/src/index.ts` → `buildSystemPrompt` |
| Tool catalog + loop | `sidecar/src/index.ts` → `listCompanionTools`, `runCompanionToolLoop` |
| OS Bridge | `apps/desktop/src-tauri/src/bridge.rs` |
| Sub-agent toggle | `apps/desktop/src/App.svelte` (`allowSubAgents`) |

When changing capabilities, **update this doc and the runtime catalog together**.
