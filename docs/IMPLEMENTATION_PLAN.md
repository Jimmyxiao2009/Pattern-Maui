# Pattern 实现计划清单

> 状态：`[ ]` 待做 · `[~]` 进行中 · `[x]` 完成  
> 产品定位见 `PRODUCT_VISION.md`。

## 文档

- [x] PRODUCT_VISION.md（贾维斯 / 三支柱）
- [x] UX_GAPS_AND_ROADMAP.md
- [x] IMPLEMENTATION_PLAN.md（本文件）

## 阶段 A — P0/P1

### A1 编码：真项目上下文
- [x] protocol `chat.send` 增加 `workspace?` `projectName?` `attachments?`
- [x] sidecar `ChatRequest` + `buildSystemPrompt` 注入工作区
- [x] App 项目/全局发送时附带 workspace
- [x] UI 显示「已绑定工作区」证据条

### A2 编码：文件树可用
- [x] 点击文件 → 读取预览（Tauri `read_text_file` 或已有 bridge）
- [x] 目录懒加载（`list_directory` 单层 + 展开再取）
- [x] 新建项目：路径校验 + 可选浏览（Tauri pick_directory / prompt fallback）
- [x] 「附加到对话」把文件路径/摘要写入 draft

### A3 工作：时间线 + 控制
- [x] `ChatMessage.events` 类型（status/tool/task/error）
- [x] 监听 `task.updated` / `runtime.agent_state` 写入当前对话事件
- [x] Stop：本地 abort 标志，忽略后续 delta
- [x] Retry：重发上一条 user 文本

### A4 体验：Markdown + 审查窗
- [x] 助手消息基础 markdown 渲染（安全转义 + 代码块）
- [x] ReviewWindow 优先绑定 URL taskId / 最近活动任务

### A5 陪伴
- [x] 主动消息策略 localStorage：`new_chat` | `inline`
- [x] 设置页开关
- [x] 快捷窗加载最近 global 短 history
- [x] memory.update 协议 + 编辑入口

### A6 验证
- [x] svelte-check
- [x] 更新 app-flows / a11y 测试
- [x] cargo check（read_text_file + pick_directory + rfd 已通过）

## 阶段 B（后续）
- [x] 项目页 diff 面板
- [x] 工作流默认当前项目
- [x] 对话内 task 卡片
- [x] 文件夹原生 dialog 插件（rfd pick_directory + 浏览按钮；浏览器 fallback prompt）

## 阶段 C（后续）
- [x] 主动性忙闲策略
- [x] 记忆溯源跳对话
- [x] MCP 试调 UI
- [x] Skills 安装


## 阶段 D — 打磨
- [x] 前台窗口感知（Tauri get_foreground_window + sidecar runtime.foreground）
- [x] 记忆自动提取改为待确认（propose/accept/reject）
- [x] MCP 工具调用写入任务 steps 时间线
- [x] 对话事件提示待确认记忆
- [x] 对话内完整 tool 时间线可视化（ExecutionTimeline）


## 阶段 E — 安全约束层（学 Nemesis 方法论）
- [x] 竞品笔记：NemesisBot 安全取舍
- [x] 工作区默认隔离（path boundary）
- [x] 高危审批策略可读（T 级说明）
- [x] 审计 journal 可筛选回看
