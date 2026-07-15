# Pattern Mobile

Pattern Mobile 是桌面主控的瘦客户端：它不运行 agent，只通过 WebDAV 中继收发消息、接收主动提醒与任务状态。

最低支持 Android 7.1（API 25）；当前以 Android 35 SDK 编译。

## 已实现

- WebDAV mailbox 轮询（10 秒）
- 与桌面端兼容的 AES-256-GCM 信封加解密
- 本地设备 ID、游标去重与 mobile-client 心跳
- 远程聊天消息发送与接收
- 手动配对：在桌面端「通道 → Pattern Mobile / WebDAV → 手动配对」查看信息

## 开发

```powershell
pnpm mobile:dev
pnpm mobile:check
pnpm mobile:build
```

## Android

安装 Android Studio 后，设置：

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:NDK_HOME = "$env:ANDROID_HOME\ndk\<version>"
pnpm --dir apps/mobile tauri android init
pnpm --dir apps/mobile tauri android dev
```

## iOS

在 macOS/Xcode 上运行：

```bash
pnpm --dir apps/mobile tauri ios init
pnpm --dir apps/mobile tauri ios dev
```

首次使用前在桌面端配置 WebDAV；仅将配对密钥输入你自己控制的移动设备。
