# Pattern harness market comparison

Research date: 2026-07-14. Sources are official product documentation unless noted.

## Product position

Pattern is a local-first personal desktop agent: a companion conversation layer, persistent memory, monitored channels, and a Computer Use execution slot with explicit T0–T3 approval. Its differentiated opportunity is to make long-running automation understandable and controllable for non-developers, while keeping model choice and execution evidence visible.

| Capability | Pattern (current target) | Claude Code Desktop | Codex app | Reasonix |
| --- | --- | --- | --- | --- |
| Conversation organisation | Local conversations, archive, isolated context | Sidebar sessions and side chats | Task/thread-oriented workspaces | CLI/web session flow |
| Scheduled work | Local daily recurring Computer Use task, persisted and reviewable | Local and remote scheduled tasks; local runs while desktop is open and awake | Strong long-running task workflow; scheduling is not its primary desktop surface | Configuration-first agent controls; automation is tool/workflow driven |
| Model choice | Multiple provider profiles and multiple models per provider | Anthropic API by default; enterprise gateway/Vertex options | OpenAI models and Codex surfaces | Provider definitions in TOML, including OpenAI-compatible endpoints |
| Execution safety | Per-step risk tier, screenshot/action receipt, approval window | Configurable permission modes and review workflow | Isolated worktrees, reviewable changes, skills | Allow/deny permission policy and workspace sandbox controls |
| Tool ecosystem | Built-in channels and local plug-in route; MCP should be the next platform layer | Connector UI, MCP, plugins, local desktop extensions | Skills, MCP/apps, worktrees and cloud/local surfaces | MCP, plugins, tool policy and serve mode |
| Model economics visibility | Token, cached-token and context-window view; provider adapter for balance | Usually product-plan/account oriented | Product quota/plan oriented | Provider configuration oriented |

## Evidence

- Claude Code Desktop documents recurring local tasks, a Routines UI, fresh scheduled sessions and its "app open and computer awake" constraint. It also documents local/remote environments, computer use, visual diff review, connectors and automatic worktrees: [scheduled tasks](https://code.claude.com/docs/en/desktop-scheduled-tasks), [desktop capabilities](https://code.claude.com/docs/en/desktop).
- Claude’s connector platform is based on MCP and distinguishes remotely available connectors from desktop-local extensions: [connectors overview](https://claude.com/docs/connectors/overview), [desktop vs. web connectors](https://support.claude.com/en/articles/11725091-when-to-use-desktop-and-web-connectors).
- OpenAI describes Codex app support for isolated worktrees and reusable skills: [Introducing the Codex app](https://openai.com/index/introducing-the-codex-app/).
- Reasonix documents a config-driven model/provider registry, permissions, sandbox limits, MCP timeouts and plug-in definitions: [Reasonix docs](https://reasonix.io/docs/).

## Product implications

1. **Make schedules first-class, not reminders.** A schedule should own its run history, next run, input context, approval policy and missed-run outcome. Pattern now has a local daily execution task; expand next to weekly/interval/one-off triggers, a run history and a clear "missed while asleep" state.
2. **Compete on provider neutrality and economics.** Claude and Codex are strongest inside their model ecosystems. Pattern should keep the provider-profile switcher one click away, show cached versus uncached input tokens, and add authenticated balance adapters only where vendors expose stable APIs.
3. **Treat the execution timeline as the trust surface.** Preserve screenshot, action receipt, risk tier, approval decision and final artifact for every run. This is a more approachable control model than raw terminal output for personal operations.
4. **Adopt MCP as the extension contract.** Connector discovery, per-task scopes and a user-readable permission manifest are higher-leverage than a large proprietary plug-in API. Keep local connectors local by default and make remote connectors explicit.
5. **Add project isolation for developer workflows.** Codex and Claude Code set the bar with worktrees/isolated sessions. Pattern should offer an optional project workspace and isolated Git worktree for execution tasks, without making that requirement leak into personal-assistant tasks.

## Explicit competitive gaps

| Gap | Benchmark | Pattern impact | Recommended closure |
| --- | --- | --- | --- |
| MCP connector marketplace, per-task connector scopes and signed local extensions | Claude Desktop | Pattern has channels and local plug-ins but lacks a user-facing universal integration layer. | Adopt MCP as the primary extension contract; add discovery, scope prompts, permission manifest and connector health view. |
| Remote/offline scheduling and event triggers | Claude Code Desktop Routines | Current schedules are intentionally local and run only with the app alive; no webhook/GitHub/API trigger or missed-run policy exists. | Add interval/weekly/one-shot schedules and durable run history first; introduce remote runs only with delegated, revocable credentials. |
| Worktrees, patch/diff review and project isolation | Codex app and Claude Code Desktop | Computer Use actions are auditable, but code changes do not yet have Git-native isolation or a review surface. | Add an optional project workspace, per-task worktree, diff/terminal artifact capture and merge/rollback approval. |
| Skill/package discovery and reusable operational playbooks | Codex app / Claude plugins | Pattern can execute tasks, but a user cannot yet install/share a packaged workflow with declared tools, prompts and checks. | Define a signed Pattern skill manifest, then consume compatible MCP and prompt assets. |
| Parallel task orchestration | Codex app | Pattern has companion/executor slots, not an explicit task graph, agent roles or conflict resolution. | Start with bounded parallel read/research jobs and a single approving coordinator; add workspace locking before write-capable parallelism. |
| Declarative policy, sandbox and CLI/serve surface | Reasonix | Pattern exposes T0–T3 interaction approval, but lacks reusable allow/deny policies by action/path/host and a headless automation surface. | Add policy-as-data (paths, domains, tools, destructive commands) and an optional local API/CLI that reuses the same audit ledger. |
| Cost forecasting and broader provider account telemetry | Multi-provider harnesses | Current view reports actual returned token usage and cached tokens; balance is adapter-based and only OpenRouter is queried today. | Add provider adapters for quota/balance where officially supported, price tables with dated snapshots, and pre-run budget caps. |

## Model catalog policy

Pattern ships recent presets as a fast starting point and asks OpenAI-compatible providers for the account-specific `GET /models` list after a profile is saved. This is important: market-wide static lists become stale and may include models a particular account cannot use. Anthropic presets remain editable because its API workflow does not expose the same generic model-list endpoint in Pattern’s current adapter.

Current preset families are based on vendor documentation: OpenAI GPT-5.6 (Sol via `gpt-5.6`, Terra, Luna), GPT-5.5 and GPT-5.4 (Pro, mini and nano variants where available); Claude Fable 5, Opus 4.8, Sonnet 5 and Haiku 4.5; DeepSeek V4 Pro/Flash (the legacy `deepseek-chat` and `deepseek-reasoner` aliases retire 2026-07-24); Qwen 3.7/3.6/3.5; and GLM 5.1/5V-Turbo/4.7. Sources: [OpenAI current models](https://developers.openai.com/api/docs/models/all), [GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol), [Anthropic models](https://platform.claude.com/docs/en/about-claude/models/overview), [DeepSeek model list](https://api-docs.deepseek.com/zh-cn/api/list-models/), [DeepSeek V4 announcement](https://api-docs.deepseek.com/news/news260424), [Alibaba Model Studio text models](https://help.aliyun.com/zh/model-studio/text-generation-model), and [Zhipu model overview](https://docs.bigmodel.cn/cn/guide/start/model-overview).

## Near-term roadmap

- P0: scheduled-task run history, timezone/missed-run policy, schedule pause/resume and recurrence editor.
- P0: model profile keyring isolation, usage export and provider-specific balance adapters.
- P1: MCP connector manager with per-task permissions and audit receipts.
- P1: project workspaces, Git worktrees and diff review for coding tasks.
- P2: remote/always-on scheduler only after a clear trust, encryption and credential-delegation model is designed.


## NemesisBot_Rust (security-first personal agent)

Research date: 2026-07-15. Source: [NemesisBot_Rust](https://github.com/276793422/NemesisBot_Rust) (AGPL-3.0 dual-license).

### Who and what
- Author background: security / 网安 oriented personal AI steward.
- Stack: Rust monorepo + Vue dashboard + multi-platform agents.
- Strengths to learn **as methodology**, not as a product clone:
  1. Default distrust of unconstrained execution
  2. Attribute-based / graded risk control (LOW→CRITICAL)
  3. Workspace isolation and auditability as first-class surfaces
  4. Identity/persona as local assets

### What Pattern should adopt
| Priority | Borrow | Pattern form |
| --- | --- | --- |
| P0 | Workspace isolation | Active project root is the default trust boundary for write/exec |
| P0 | Graded approval gates | Keep T0–T3, make policy human-readable in review UI |
| P0 | Audit replay | Productize journal list with filters + severity badges |
| P1 | Persona as files | IDENTITY/SOUL/USER style local assets (later) |
| P1 | Model tiers | Cheap model fewer tools (later) |

### What Pattern should **not** copy now
- Full cluster / multi-node RPC
- ClamAV / Sandboxie as a launch requirement
- 20+ channel surface area before coding/companion trust is solid
- Turning the shell into a security operations console

### Product rule
Security is a **constraint layer** under Jarvis (coding + work + companionship), not the primary UI identity.
