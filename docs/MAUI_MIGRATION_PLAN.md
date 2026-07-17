# Pattern → .NET MAUI 移植计划

## 目标与边界

把归档的 Tauri/Svelte 桌面端和移动端能力迁移到一个 .NET MAUI 客户端，目标平台为 Windows、Android 和 macOS Catalyst。Node sidecar 保留为跨平台 Agent 运行时；Windows/macOS 由 MAUI 直接托管，Android 采用 WebDAV relay 瘦客户端，不在 APK 内嵌 Node。

原 Tauri/Rust 目录只作为源码归档和行为参考，不恢复 Rust 编译链，也不把浏览器页面或 `127.0.0.1` 作为用户入口。

## 分层方案

| 层 | MAUI 迁移后的职责 | 现状 |
| --- | --- | --- |
| UI | Shell、导航、聊天、设置、功能页、无障碍和通知 | 原生导航壳、OOBE、聊天、项目/会话/记忆/目标/任务/主动/技能/工作流/MCP/通道/模型/监控/审计/设置页已接入；复杂原生窗口仍按平台补齐 |
| Client services | `SidecarRuntime`、relay、会话/项目缓存、平台适配器 | stdio 生命周期和重连已完成 |
| Agent runtime | 模型循环、记忆、主动任务、技能、MCP、渠道、审计 | 继续复用并修复 TypeScript sidecar |
| Native bridges | Windows tray/hotkey/input/accessibility/screenshot；Android 通知与后台同步；macOS 菜单栏 | Windows loopback bridge 已提供 foreground/idle/power、Win32 输入、截图、窗口控件树/动作、冻结、tray balloon 和提示；Win32 tray、Ctrl+Alt+P、单实例保护已接入；Catalyst 已加入 Pattern 菜单与 Cmd+Option+P 快捷入口 |
| Data/security | 本地数据目录、加密密钥、权限策略、迁移和备份 | MAUI Preferences + SecureStorage、relay AES-GCM outbox/cursor 已接入；Win32 bridge 提供受限文件快照 recovery begin/prepare/commit/rollback/gc |

## 分阶段交付

### Phase 0 — 运行时稳定性（当前）

- 使用 JSONL stdio 替代 MAUI 到 sidecar 的随机 loopback WebSocket；保留 WebSocket 作为远程/旧客户端兼容协议。
- sidecar stdout 只输出协议消息，stderr 单独采集；启动握手、超时、退出码和最近 stderr 会显示在客户端状态中。
- MAUI 使用 `ArgumentList` 启动 sidecar，支持 `PATTERN_SIDECAR_PATH` / `PATTERN_NODE_PATH`，避免路径空格和 PATH/nvm 安装差异。
- 自动重连采用单生命周期锁，未完成请求在断线时失败返回，聊天 controller 在 EOF 时取消。
- 验收：`pnpm sidecar:test`、Windows/Android 编译、stdio ping 集成测试。

### Phase 1 — 核心使用闭环

- OOBE、persona 卡、provider/model profiles、API key 安全保存。
- 会话列表/新建/切换/重命名/归档/删除，聊天历史持久化、会话 ID 和取消；任务审批事件会弹出 MAUI review 操作并回传批准/拒绝。
- 记忆列表/编辑/搜索/提案审核/固化；Goals、Tasks、审批和执行时间线。
- 项目工作区、文件树、文件预览、Git diff、worktree。
- 验收：新用户从 OOBE 到首次对话、记忆写入、任务执行和恢复均不需要浏览器。

### Phase 2 — 主动能力与扩展管理

- proactive inbox/chains、cron、scheduled runs、health checks、file watch。
- Skills 安装/删除/运行、workflows、MCP server 配置/发现/调用。
- model metrics/catalog/balance、workspace/security policy、audit journal、recovery adapter。
- 在 MAUI 中统一呈现 loading/error/empty 状态，所有请求走 `RequestAsync`，禁止页面直接创建 socket。

### Phase 3 — 渠道与设备互联

- WebDAV relay 配对、设备密钥、AES-GCM envelope、X25519/XChaCha20、cursor/outbox/offline retry。
- Telegram、SMTP、IMAP 和本地 channel plugin 管理。
- Android 前台同步服务、通知点击回到会话、网络切换和后台重试。
- 验收：断网期间消息进入 outbox，恢复网络后按 cursor 幂等同步。

### Phase 4 — 原生平台体验

- Windows tray、global hotkey、quick window、review window、toast、自启动、single instance。
- Windows native OS bridge：idle、foreground、power、accessibility tree/action、screenshot、input、freeze、notify。
- macOS Catalyst 菜单栏/通知/权限说明；Android 权限、通知渠道和电池策略引导。
- 对每个平台建立 UI test smoke suite 和打包签名流水线。

### Phase 5 — 迁移收口

- 将旧 Svelte 页面逐项对照验收并删除 MAUI 占位页；补齐深链接、客户端配置/会话导入导出、备份恢复和数据 schema migration。
- TypeScript strict typecheck、C# analyzers、端到端测试和崩溃日志脱敏。
- 归档 Tauri/Rust 源码只读保存，工作区只保留纯源码和文档，不提交 `bin/obj/dist/node_modules`。

## 已发现并处理的问题

1. 原生客户端依赖随机 `127.0.0.1` WebSocket，端口、防火墙和浏览器启动顺序会造成“连不上”。改为 sidecar stdio JSONL。
2. stdout 与诊断日志混用会破坏 JSONL；协议输出固定到 stdout，诊断固定到 stderr。
3. sidecar/Node 路径在带空格的安装目录或 nvm/fnm 环境下不稳定；改用 `ProcessStartInfo.ArgumentList` 和可配置绝对路径。
4. 请求无统一超时/断线清理会造成页面永久 loading；`RequestAsync` 增加超时，断线统一结束 pending waiter。
5. Android 不能直接运行 Node sidecar；明确 relay-only 状态和配对错误，避免假装本地 Agent 已启动。
6. 当前 `dotnet run` 在无桌面会话的构建环境出现 Windows App SDK `0xC000027B`，不等同于 sidecar 连接失败；CI 以 build + stdio 集成为准，真实桌面机执行 GUI smoke test。
7. 客户端设置页提供版本化 JSON 备份导入/导出，并迁移旧的无版本备份格式；API Key、WebDAV 密码和频道密钥永不进入备份。

## 当前可运行验收（2026-07-18）

- `pnpm sidecar:test`：stdio、memory、relay、channels、computer-use、e2e、路由测试通过；Windows relay 测试结束时的 `UV_HANDLE_CLOSING` 是 Node teardown 已知噪声，测试 runner 已按 4 个断言通过处理。
- `pnpm sidecar:typecheck`：sidecar 与 workspace packages 的 TypeScript strict 检查通过；workspace alias、MCP、recovery、proactive 和 channel 类型已对齐。
- `pnpm maui:windows:debug`：`net10.0-windows10.0.19041.0` Debug 编译通过。
- `pnpm maui:android:debug`：`net10.0-android` Debug APK 编译通过（Android API analyzer 可能提示平台兼容性警告）。
- `pnpm maui:mac:debug`：`net10.0-maccatalyst` Debug 编译通过；Catalyst 真机签名、通知权限和菜单交互仍需 macOS 主机验收。
- `.github/workflows/maui-validation.yml`：在 Windows/macOS runner 上自动执行 sidecar strict/test、Windows/Android/Mac Catalyst Debug 构建；可在推送后完成 macOS 主机的最后验收。
- Windows/macOS 运行时均使用 sidecar stdio；Windows 提供 tray、Ctrl+Alt+P 快捷聊天入口、单实例保护和本地恢复快照；Android 不启动 Node，使用 WebDAV relay + 前台同步服务，并支持 `pattern://pair` 深链接配对，不要求用户访问 `127.0.0.1`。
- 生成目录只用于本地构建，提交前执行仓库根目录的清理命令，归档目录 `archive/` 保持只读源码。
- `pnpm maui:windows:debug`、`pnpm maui:android:debug`、`pnpm maui:mac:debug` 会自动 restore，干净 checkout 不再依赖遗留 `obj` 目录；Windows 调试脚本会先构建 sidecar。

## 每阶段完成定义

- 功能有 MAUI 页面入口、loading/error/empty 状态和权限说明。
- 请求/事件有协议类型、超时、取消和重连测试。
- Windows、Android、macOS 至少完成对应平台 build；涉及原生能力时有真机 smoke test。
- 不恢复 Rust 工具链，不依赖浏览器开发服务器，不把 API key 写入普通日志。
