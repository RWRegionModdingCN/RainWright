<!-- codemap:v1 -->

# RainWright Code Map

- `src/Rainwright/` — RainWright DLL 的编辑后端、游戏集成、原生 UI 与嵌入资源。
- `src/Shared/` — 本项目自带的作者路径校验工具；构建不读取其他仓库。
- `mod/` — Mod 清单、翻译、缩略图及可编辑美术源文件；DLL 由构建产生。
- `tests/` — 独立回归测试、真实游戏程序集测试与离屏验证探针。
- `tools/` — 程序集保护、堆栈还原、Mono 探针宿主及区域修复工具。
- `scripts/` — 独立构建、测试、安装与保护入口。
- `docs/` — 使用、开发、依赖及迁移边界说明。
- `.github/workflows/` — 不分发游戏程序集的可移植构建与测试。
