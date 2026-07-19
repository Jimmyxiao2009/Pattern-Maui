# Pattern 桌面界面一比一迁移计划

目标：把归档的 Tauri/Svelte 桌面端视觉和交互逐页还原到 .NET MAUI。功能协议继续复用现有 sidecar；本计划只约束客户端呈现、导航、状态反馈和平台窗口体验。

## 1. 还原基线

旧桌面端的视觉锚点不是通用蓝色后台，而是“安静的石墨工作台 + 一枚琥珀信号”：

| 层 | 旧版基线 | MAUI 目标 |
|---|---|---|
| 画布 | `#0e0d10` / `#151419` | `PageBackground` / `SurfaceBackground` |
| 卡片 | `#1b1a20` / `#242229` | `PanelBackground` / `ElevatedBackground` |
| 文字 | `#ebe8e2`、`#96919c`、`#625f69` | `TextPrimary`、`TextMuted`、`TextFaint` |
| 强调 | `#e7a23b`，hover `#f1b95d` | `Accent`、`AccentHigh` |
| 状态 | green `#4db380`、blue `#70a3dc`、danger `#e05b50` | `Positive`、`Info`、`Danger` |
| 结构 | 46px 标题栏 / 72px rail / 30px 状态栏 | MAUI Shell 同尺寸、同层级 |
| 字体 | Segoe UI 系统字体；Consolas 数据字体 | Windows 使用 `Segoe UI` / `Consolas`，其他平台回退系统字体 |
| 圆角 | 6–8px 普通控件，消息 8px，快捷窗 12–16px | `RoundRectangle` 与控件 CornerRadius 对齐 |

签名元素：标题栏左侧琥珀“眼睛”信号、窄 rail 的图标+短标签、底部 `PATTERN · 版本` 状态栏。任何新页面不得回到蓝色默认控件或满屏大卡片。

## 2. 页面/组件映射

| 旧组件 | MAUI 入口 | 当前状态 | 还原重点 |
|---|---|---|---|
| `App.svelte` Shell/titlebar/rail/statusbar | `MainPage.xaml` + `MainPage.xaml.cs` | 第一轮已完成 | 46/72/30 布局、活动 rail、标题和连接状态 |
| `Oobe.svelte` | `CreateOobeView` | 功能已接入 | 左侧步骤栏、persona 选择卡、底部动作栏 |
| `ConversationsView.svelte` | `CreateConversationsView` | 功能已接入 | 会话列表、搜索/筛选、归档/删除动作 |
| `ProjectWorkspace.svelte` + `FileTree.svelte` | `CreateProjectView` | 功能已接入 | 项目头部、文件树、预览、diff/worktree 面板 |
| `MessageContent.svelte` + composer | `CreateChatView` | 第一轮已完成 | 消息元信息、用户气泡、Pattern 流式回复、失败/思考状态、附件动作 |
| `RecentsSidebar.svelte` | 聊天/会话页 | 待迁移 | 内容区旁的最近会话列和当前会话高亮 |
| `SessionPlanPane.svelte` / `SessionAgentDocks.svelte` | 会话/聊天页 | 待迁移 | 可折叠计划、目标、循环、提醒 dock |
| `MemoryEditor.svelte` | `CreateMemoryView` | 功能已接入 | 搜索工具栏、三列卡片、重要度、提案/过期状态 |
| `GoalsView.svelte` | `CreateGoalsView` | 功能已接入 | 目标焦点卡、进度条、步骤时间线 |
| `TasksView.svelte` / `TaskCard.svelte` | `CreateTasksView` | 功能已接入 | 状态徽章、审批、恢复和执行时间线 |
| `ProactiveView.svelte` / `ActiveRemindersPane.svelte` | `CreateProactiveView` | 功能已接入 | inbox、链、暂停/恢复、主动消息卡片 |
| `WorkflowsView.svelte` | `CreateWorkflowsView` | 功能已接入 | 工作流列表、运行输入、结果状态 |
| `McpView.svelte` | `CreateMcpView` | 功能已接入 | server 卡片、发现、调用、权限提示 |
| `ChannelsView.svelte` | `CreateChannelsView` | 功能已接入 | channel 列表、在线状态、配对/插件动作 |
| 模型/用量视图 | `CreateModelsView` | 功能已接入 | provider 卡片、模型 chips、指标/余额卡 |
| `SettingsView.svelte` / `SettingRow.svelte` | `CreateSettingsView` | 第一轮已分组 | 左侧设置分组、表单行、模型/人格/隐私/快捷键页签 |
| `QuickWindow.svelte` | Windows 原生快捷窗 + MAUI view | 待迁移 | 置顶小窗、历史气泡、快捷输入、主动消息卡 |
| `ReviewWindow.svelte` | MAUI 审批对话 | 功能已接入 | 风险说明、批准/拒绝、任务回执 |
| `StatusDot.svelte` / `Toggle.svelte` / `PageHeader.svelte` | MAUI 小组件/辅助方法 | 待统一 | 所有页面共用状态点、开关、页头，而不是各自拼样式 |

## 3. 实施顺序

### Phase A：公共壳层与聊天（当前）

- [x] 中性石墨/琥珀设计 token。
- [x] 标题栏、rail、内容区、状态栏四层结构。
- [x] rail 活动态、Segoe MDL2 图标映射和 UIA 可访问名称。
- [x] 聊天页头、消息元信息、用户气泡、流式 Pattern 气泡、思考/失败状态。
- [ ] 最近会话侧栏、会话上下文和可折叠 dock。

验收：Windows UIA 能找到 16 个导航按钮、聊天 Edit、附加/发送/停止；发送失败时仍保留消息气泡和连接状态。

### Phase B：设置与数据页

- [x] 设置页按“常规连接 / 运行时 / 数据与系统”分组，去掉一条横向挤满屏幕的表单。
- [ ] Settings 左侧 tab 与右侧内容区；每个 tab 对应旧版 `general/persona/model/filewatch/journal/privacy/shortcuts`。
- [ ] 统一 `SettingRow`：标题、说明、控件、成功/失败状态四列关系。
- [ ] 记忆、目标、任务、主动页从纯 JSON Editor 提升为卡片/时间线布局。

验收：空、加载、成功、失败四种状态均可见；窄窗口下内容不被横向裁切。

### Phase C：项目、扩展与渠道

- [ ] 项目页还原左文件树 + 右预览/差异布局。
- [ ] 通道/模型/MCP/技能/工作流使用统一卡片、徽章和动作栏。
- [ ] 文件监控、审计、恢复页使用旧版设置行和日志密度。

验收：每个页面的主要动作在首屏可见，所有请求显示进行中/成功/失败状态，UIA 名称不重复到无法定位。

### Phase D：平台窗口与细节

- [ ] Windows Quick Window、tray balloon、Review Window 的尺寸、阴影、圆角和快捷键。
- [ ] macOS Catalyst 菜单栏窗口；Android relay 页面采用同一 token 但遵循移动端滚动布局。
- [ ] 键盘焦点、Esc/Enter、Reduced Motion 和深浅主题切换。
- [ ] 用有效桌面会话截图做视觉 diff；RDP 下仅保留 UIA 结构验证。

## 4. 每页完成定义

1. 有旧组件 → MAUI 入口映射，且不再使用无标题的通用占位页。
2. 颜色、间距、圆角、字体和状态点来自公共 token。
3. loading、empty、error、success 都有用户可理解的文案。
4. UIA 可定位主要控件，按钮名称描述动作，不泄露 API Key。
5. Windows Debug、Visual Studio 多目标 Debug、sidecar typecheck/test 均通过。
6. 完成回归后删除 `bin/obj/dist/node_modules/.vs`，只保留源码与 `archive/`。

