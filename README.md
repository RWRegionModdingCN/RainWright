<h1 align="center">RainWright</h1>

<p align="center"><strong>面向 Rain World 区域创作者的可视化开发工具</strong></p>

<p align="center">
  <a href="./README.md"><img src="https://img.shields.io/badge/%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87-%E5%BD%93%E5%89%8D-238636?style=for-the-badge" alt="简体中文（当前）"></a>
  <a href="./README.en.md"><img src="https://img.shields.io/badge/English-Switch-0969da?style=for-the-badge" alt="Switch to English"></a>
</p>

<p align="center">
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-Apache--2.0-2ea043?style=flat-square" alt="Apache-2.0 License"></a>
  <img src="https://img.shields.io/badge/Game-Rain%20World-986f4b?style=flat-square" alt="Rain World">
  <img src="https://img.shields.io/badge/Runtime-.NET%20Framework%204.8-512bd4?style=flat-square" alt=".NET Framework 4.8">
  <a href="https://github.com/RWRegionModdingCN/RainWright/actions/workflows/ci.yml"><img src="https://github.com/RWRegionModdingCN/RainWright/actions/workflows/ci.yml/badge.svg" alt="Build and test"></a>
  <a href="https://github.com/RWRegionModdingCN/RainWright/stargazers"><img src="https://img.shields.io/github/stars/RWRegionModdingCN/RainWright?style=flat-square&color=e3b341" alt="GitHub Stars"></a>
</p>

<p align="center"><img src="./docs/images/banner.png" width="800" alt="RainWright — Redesigned tools. Unified workflow."></p>

---

制作 Rain World 区域时，房间物件、世界连接、刷怪和地图往往需要在多个工具与文本文件之间来回切换。

**RainWright** 将这些工作整合进一个可视化编辑空间，重新设计原版 Dev Tools 的页面布局与操作方式，让区域制作、调试和修改更加直观。

项目现已独立维护。编辑器、世界拓扑、地图后端、字体和 Cartography 图集统一由 `RainWright.dll` 提供，运行和构建均不需要 DryCycle / Ancient Site。

## 安装与目录

运行需要 Rain World、游戏开发工具，以及 **Rain World ImGUI API 1.12.0 或更新版本**和其 Library Loader。先在 Remix 中启用这些依赖，再启用 RainWright，并开启游戏开发工具。

本仓库包含源码和 Mod 资源。完整游戏 DLL 需要使用本机 Rain World 程序集构建；CI 生成的 portable core 仅用于开发验证。

```powershell
# 安装 .NET 10 SDK 与 PowerShell 7 后，在仓库根目录执行。
./scripts/Build.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
./scripts/Install.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
```

构建后的完整 Mod 位于 `artifacts/mod/RainWright/`，也可以将该目录复制到游戏的 `RainWorld_Data/StreamingAssets/mods/` 下，再重启游戏并在 Remix 中启用。

- `src/`：编辑器源码与项目自带的路径校验工具。
- `mod/`：Mod 清单、翻译、缩略图与美术工程源文件。
- `tests/`：可移植回归、真实游戏程序集测试与验证探针。
- `scripts/`：构建、测试、安装及程序集保护入口。
- `docs/`：开发说明、依赖、项目边界与界面素材。
- `artifacts/`：本地生成的 DLL、可安装 Mod 和安装备份，不纳入 Git。

构建路径与可选游戏程序集配置见 [开发说明](./docs/BUILDING.md)。

## 主要功能

- **统一开发界面**：整合房间、物件、音效、世界、地图、触发器及关系编辑页面。
- **可视化物件编辑**：搜索与筛选物件、批量选择、属性编辑、资源选择和场景节点拖动。
- **世界连接与刷怪编辑**：编辑双向与单向连接、精确管道端点及生物生成，并支持热重载。
- **地图制作**：管理 Player Map 与开发地图布局，通过 Cartography 工作区组织地图元素。
- **声音管理**：编辑声音、组织自定义声音分组，在同一界面中管理区域音效。
- **编辑历史与恢复**：撤销、重做、自动保存恢复，以及可调整的快捷键与工作区预设。
- **面向内容 Mod 的保存**：选择编辑目标 Mod，对原版区域的修改输出为 Remix 标准 `modify/world/` 补丁。

<p align="center"><img src="./docs/images/interface.png" width="800" alt="RainWright 界面重构"></p>
<p align="center"><img src="./docs/images/world-editing.png" width="800" alt="RainWright 世界编辑"></p>
<p align="center"><img src="./docs/images/workflow.png" width="800" alt="RainWright 编辑工作流"></p>

## 开始编辑

进入区域后打开开发工具，先使用顶部的 **“编辑目标 Mod...”** 选择自己的内容 Mod，再修改和保存文档。修改其他 Mod 或原版区域时，使用 Remix 的重新应用 Mod 流程验证补丁。

房间、物件、音效和触发器的未保存修改保留在编辑会话中。拓扑元数据使用目标 Mod 下的 `world/WorldTopology.json`，个人设置保存在 `BepInEx/config`。

运行 `./scripts/Test.ps1` 可执行无需游戏安装的回归测试。完整构建、游戏程序集测试及扩展边界见 [开发说明](./docs/BUILDING.md) 和 [架构说明](./docs/ARCHITECTURE.md)。

## 许可与鸣谢

项目采用 [Apache-2.0](./LICENSE) 许可证。第三方字体与素材保留各自的许可及来源说明，详见 [第三方声明](./THIRD_PARTY_NOTICES.md)。

---

<div align="center">
  <p>如果这个项目对你有帮助，欢迎点击 <strong>Star</strong> 支持开发。</p>
  <a href="https://github.com/RWRegionModdingCN/RainWright/stargazers">为 RainWright 点亮 GitHub Star</a>
</div>
