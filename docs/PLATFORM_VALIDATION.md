# Pattern 0.3.0 — 平台验收清单

本清单区分仓库已验证的交付物与必须在目标平台、真实账户或签名证书环境中完成的发布验收。未勾选项不是代码自动可替代的工作。

## 当前已验证（Windows）

- [x] `pnpm test`：Sidecar、桌面与移动端静态检查
- [x] `pnpm test:ui`：OOBE、记忆/任务/通道、快捷键设置流程、桌面/移动端无障碍冒烟
- [x] 原生 Windows UI Automation：OOBE 对话框、输入控件、Invoke/Value Pattern 与后台隔离
- [x] `pnpm build`、`pnpm mobile:build`、桌面 `cargo check`
- [x] `pnpm mobile:android:build`：Android universal debug APK
- [x] WebDAV local/PROPFIND、中继加密、X25519 配对、任务与无视觉 UIA Sidecar 测试

## Windows 发布前

- [ ] 使用真实模型提供商、WebDAV、Telegram/SMTP/IMAP 账户完成端到端联调
- [ ] 在干净 Windows 10 与 Windows 11 用户账户中验证安装、自动启动、托盘、快捷键冲突回退
- [ ] 准备代码签名证书并执行签名、SmartScreen 与卸载/升级测试
- [ ] 若要分发 Node Sidecar，随安装包提供兼容 Node 22 运行时；或在安装 Bun 后验证 `pnpm sidecar:binary`

## macOS / iOS

- [ ] 在 macOS 13+ 编译桌面端，授予屏幕录制与辅助功能权限，验证 AX、快捷窗与审查窗
- [ ] 在 Xcode 上执行 `pnpm --dir apps/mobile tauri ios init`，完成 iOS 工程、真机配对与后台刷新测试
- [ ] 使用 Apple Developer 证书完成签名、公证与 TestFlight/侧载流程

## 明确延期的架构替代项

- [ ] pi-mono Agent：当前自研 Agent loop 已由 `@pattern/core` 边界隔离；替换前需要重新验收流式对话、记忆和 Computer Use。
- [ ] sqlite-vec：当前 FTS5 + 混合精排是可用基线；引入原生扩展前必须验证 Windows/macOS 打包。
- [ ] mDNS 直连与 OneBot：分别属于 v2 局域网优化与可选高风险第三方通道，不纳入 0.3.0 发布阻塞项。
- [ ] Tailwind 迁移：现有手写主题已用于已验证界面；迁移属于维护性重构，非功能缺口。
