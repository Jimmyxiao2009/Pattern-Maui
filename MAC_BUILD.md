# Pattern 0.3.0 - macOS 开发源码包

## 包含
- 完整工作区源码（含 2026-07-14 所有修改）
- 不含 node_modules / target / dist / .git

## macOS 开发

```bash
xcode-select --install
brew install node pnpm rustup
rustup install stable

tar -xzf Pattern-0.3.0-mac-dev.tar.gz
cd Pattern
pnpm install

# 开发
pnpm tauri dev

# 构建
pnpm tauri build
```

## 产物
- App: apps/desktop/src-tauri/target/release/bundle/macos/Pattern.app
- DMG: apps/desktop/src-tauri/target/release/bundle/dmg/

首次编译 Rust 依赖会较久。
