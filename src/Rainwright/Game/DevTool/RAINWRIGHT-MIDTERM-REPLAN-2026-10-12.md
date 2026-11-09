# Rainwright（雨匠）中程全仓审计与 V2 执行方案

日期：2026-10-12。扫描基线：`212c45e329c890a914bb5e294f9e83c32bb481c6`（main）。
性质：**中程检查与重新排程**，不是 Phase 5 发布前最终全仓复审。

## 1. 执行依据与扫描口径

- 可重复脚本：`python3 scripts/RainwrightMidtermAudit.py --evaluate`。扩展原有 `scripts/RainwrightOwnershipAudit.py`，全树检索插件、Hook、Legacy 路径，解析全部 csproj 和项目依赖边，同时在 GitHub Actions 求值 **实际 MSBuild Compile Item**，不靠“目录在 Rainwright”推断编译所有权。
- 本次实际执行的 [Rainwright Midterm Audit](https://github.com/NineteenReincarnation/DryCycle/actions/runs/38186691316) 工作流包含 `summary.json`、`findings.json`、`project-graph.json`、`plugin-entries.json`、`hook-sites.json`、`multi-owner-hook-candidates.json`、`legacy-path-candidates.json`、`source-ownership.json` 和 `MIDTERM.md`。逐项定位使用该构建产物。
- 检索了 1,121 个有效仓库文件、**src 内 847 个 C# 文件**（整个仓库含测试约 946 个 C# 文件），src 内 DevTool 目录 **338 个**，**35 个 csproj**。原 787 份源码数据只属于 10 月 10 日的历史基线。
- 检出 20 处 `BepInPlugin` 注解、24 处 `BepInDependency` 注解、342 处 On/IL += 字面安装、358 处 -= 字面释放、51 个被多份源码安装的 Hook 目标、49 处旧 Mod/作者配置路径候选。**这些均为静态源码匹配数量，不等于 51 个重复生效 Hook 或 49 个非法写入。**
- 原扫描分类：DevTool -> DryCycle 外依赖候选 **12 文件**（部分为注释或本应在可选扩展中的引用）；DevTool 外 -> DevUI 候选 **10 文件**（包括合法的旧 DevUI 控件引用和 1 个 Rainwright 命名空间假阳性）；DryCycle 游戏域直接安装原版 DevInterface Hook 的文件 **20 个**（大多为玩法的原版编辑扩展）。必须逐个定 Owner，不作为统一删除目标。
- 项目求值：`DryCycle.csproj` 659 个 Compile 项，其中 **199 个 DevTool 源文件**；`DryCycle.DevTool.RWImGui.csproj` **122 个 Compile 项**；`Rainwright.csproj` **46 个 Compile 项**，其中 **33 个 C# 源码物理处于 Rainwright 目录，13 个仍以 Compile Include 链接 DevTool 源码**。三个求值结果均未发现项目内部重复 Compile Identity。
- CI 的 net48 `Rainwright.dll` 已有独立编译、真实 PE AssemblyRef 与类型/资源校验、net10 跨程序集回归。**这只验收了核心程序集，并未验收包含实际原生 UI/BepInEx 的可安装 Rainwright Mod。**

## 2. 重要完成成果

已在真实 Rainwright 核心程序集持有的能力包括：Cartography 文档/存储/路由/PNG/渲染层与 Retained Scene 缓存；WorldDocument 与 SoundGroup 模型；地图正交路由、交叉/交汇/箭头几何、房间与连接空间索引、Dirty 与视图邮箱；Objects 分类/搜索/语义缩放/可见行选择；Sound 多选/音效组状态；页面注册与工作区布局；前端唯一实例、Hook/Atlas 清理及焦点/会话保留策略。

`DryCycle.DevTool.RWImGui.dll` 仍包含 122 个实际前端 Compile 项、原生 ImGui 和 BepInEx 入口，`DryCycle.dll` 仍编译绝大多数通用编辑器后端。故目前**核心迁移已有实际成果，但完整编辑器的程序集与发布边界尚未贯通**。

## 3. 分级问题台账

| 编号 | 优先级 | 实际证据 | 必须解决的结果 |
|---|---|---|---|
| RW-M11 | P0 | `src/DryCycle.csproj` 无条件 `ProjectReference Rainwright/Rainwright.csproj` | DryCycle-only 的真实游戏 DLL 不能硬引用 Rainwright；拆出最小游戏原版 DevUI 及可选扩展，避免跨程序集类型身份复制 |
| RW-M01 | P0 | 旧 `DryCycle.DevTool.RWImGui.csproj` 仍有 `Reference Include="DryCycle"`、`DryCycleAssemblyPath` 强制验证 | 新 Rainwright 主编辑器不再链接 DryCycle.dll，游戏业务通过可选 Adapter 注册 |
| RW-M04 | P0 | 实际 DryCycle Compile 仍有 **199 个** DevTool 文件 | 会话、命令、保存/撤销、输入、世界地图/Player Map 后端等按可运行功能链整块迁给 Rainwright；DryCycle 保留专属玩法 |
| RW-M02 | P0 | `Shell/BridgePlugin.cs` 含 `[BepInDependency("Anno", HardDependency)]`，GUID 仍是 DryCycle 前端 | Rainwright 独立主入口使用不与其他 Mod 冲突的 GUID；只能依赖真正必要的 RWImGUI/游戏基础服务 |
| RW-M03 | P0 | 仅发现 `mod/modinfo.json`（`NR.B5`），缺少 Rainwright 独立清单 | 独立文件夹、modinfo、资源和离线可校验的发行包，明确外部 RWImGUI 依赖 |
| RW-M07 | P1 | `Rainwright.csproj` 当前仅产出游戏无关 core，没有 BepInEx 入口 | 打通**实际带 UI 入口的 net48 Rainwright.dll** 构建；Smoke 不代替原生游戏程序集验证 |
| RW-M05 | P1 | 13 个 Rainwright 编译源文件仍存放在 `src/DevUI/DevTool/` | 在真正依赖边界理清后物理迁到 `src/Rainwright/`，同步 Guard/测试，不制造第二份 TypeIdentity |
| RW-M06 | P1 | 20 个插件 Attribute、51 个多源码 Hook 目标 | 逐个核对真实激活者、生命周期和释放；保留 DryCycle 专属原版 DevUI Hook，杜绝重复 Owner |
| RW-M12 | P1 | `DevToolFontAtlasIntegration.cs` 使用 `new MonoMod.RuntimeDetour.Hook` | 按禁止自 Hook 约束研究 RWImGUI 预 Atlas 扩展或 On/IL 方案；绝不破坏中文字体在上传前注入与失败回滚 |
| RW-M08 | P1 | 49 处旧 ID/路径匹配、UI 配置和作者路径需区分 Owner | DryCycle 玩法路径合法保留；Rainwright 编辑器设置只读旧配置、备份并迁移；作者写入不得污染 mergedmods |
| RW-M10 | P1 | 现有 CI 主验收核心 net48/测试 net10，不是完整 Rainwright-only 游戏前端 | 完整前端 DLL 编译、可安装包、三种安装模式和故障注入回归 |
| RW-M09 | P2 | 旧文档仍记 787 文件、尚未构建 DLL；与当前构建事实矛盾 | 以本中程报告为最新决策，旧文档作为历史，不再照旧状态汇报 |

### 需核实但**不能按扫描次数视为 Bug**

- `On.DevInterface.MapPage.Update` 被多个专属天气 UI、温度 UI 及 Rainwright 兼容逻辑使用；`On.DevInterface.ObjectsPage.CreateObjRep` 由多个专属物品/地形系统合法扩展。要求检查 Hook 调用顺序、同层叠加、关闭互不卸载、每个源 Enable/Disable 是否对称，而不是“全改成一个 Hook”。
- `src/DevUI/DevTool/Core/EditorDiagnostics.cs` 的外依赖匹配在注释里；`src/Rainwright/Runtime/Sound/SoundGroupWorkspaceCore.cs` 的“反向引用”主要是保留的旧命名空间。两者不能当作生产程序集硬依赖。
- 通用前端仍有直接 `DryCycle.Plugin.Logger`：`WorldMapConnectionResourceStore`、`WorldMapConnectionWork`、`WorldMapRoomResourceStore`、`WorldMapShortcutPresentation`、`DevToolOverlay`、`UiModeSwitch`。应接 Rainwright 自有诊断服务，逐条保留失败详情和后台线程安全。
- `WorldTopologyRuntime` 当前物理位置属于 DevTool、但涉及长期生效的 WorldLink 游戏通行；必须按行为重新划分，**不能整文件盲搬进 Rainwright**。
- 原有 `LegacyFrontendPluginIds`、`DevToolPlayerMapCompatibilityPlugins` 等兼容 BepInEx 壳保留时要核实是否真正安装业务 Hook；只有证明空壳和 GUID 兼容的前提下才收口。
- `FrontendSessionVisibilityPolicy` 经过独立状态机测试，但真实 Alt+Tab/DX11 context 释放时序仍需要游戏内验证。

## 4. V2 重新规划：先打通纵向功能，再迁移剩余源码

开发顺序不再按“挑选一个纯数学/状态工具 -> 编进 Rainwright -> 报百分比”的循环推进。所有迁移须绑定明确的程序集依赖变化、真实使用者和纵向功能回归。

### M0 — 稳定基线及阻断映射（本轮中程审计完成）

保留源码所有权表、Hook 安装源、项目引用图及 CI 扫描产物。把 199 个游戏内 DevTool Compile 项、122 个旧前端项逐文件分为：应迁 Rainwright / DryCycle 游戏长期行为 / 可选 DryCycle Adapter / 原版 DevUI 共享源码 / 待查；记录类型入口与测试。优先确定跨 Mod 公共协议，避免随意复制状态和丢失第三方注册。产出机器可重算的清单。

### M1 — 解除 DryCycle → Rainwright 反向链接（首要阻断）

先保护 DryCycle-only 所需原版编辑控件、温度/天气/物品的 DevInterface 逻辑，再将 `DevToolLegacyBootstrap` 对通用编辑器的调度收口到可选注册适配；完成 `DryCycle.csproj` ProjectReference 拆除。对于共用状态，优先让唯一活跃 Rainwright 进程拥有注册表及输入所有权，通过插件存在时一次性连接的适配层接入；不能靠复制两份带状态 Registry 或每帧反射解决。**Gate：真实 DryCycle.dll 的 AssemblyRef 不含 Rainwright、只装 DryCycle 仍能正常运行玩法及原版控件。**

### M2 — 迁移通用编辑器后端、形成完整作者事务

按依赖图成组处理 Core/Session、Commands/History、Undo/Redo、Input/Shortcut/Feedback、AutoSave/Recovery、World/Room/Object/Sound 及地图/制图后端，最终从 DryCycle 的 199 个 DevTool Compile 项中只保留合法专属/兼容部分。每批迁移同步修复启动 Owner、资源释放、源文件路径与 Game/Editor 编译排除。**Gate：Rainwright 内部能闭合会话→命令→Dirty→保存/撤销→正确错误返回→清理；不能借由旧 DryCycle 主进程隐式完成。**

### M3 — 真实原生前端与独立插件入口

将旧 RWImGui 的通用页面、Overlay、Renderer/FrameCallback、窗口、对象与地图视图直接交给独立 Rainwright 入口。最终发行目标为**一个装有自有代码的 `Rainwright.dll`**；开发阶段可以用 Core-only/smoke 条件配置加快回归，但不能把只有 backend 的 DLL 假称独立 Mod。前端只引用 Rainwright、自带 API 及 BepInEx/Unity/Rain World/RWImGUI 合法外部依赖。单入口管理 context、On/IL Hook、后台任务和顶栏。DryCycle 特有 World Data/长枪拾荒者只通过可选扩展加入。**Gate：移除 frontend 对 DryCycle 的 Reference 和 BepInDependency(Anno)，真实插件程序集元数据验证无 DryCycle AssemblyRef。**

### M4 — 兼容、作者数据和独立 Mod 组装

核定独立 BepInEx GUID 与 `modinfo.json` ID，资源（Cartography PNG/中文字体）和 RWImGUI 外部要求；迁移 `BepInEx/config/DryCycle/...` 下旧作者配置时执行只读导入、目标验证、备份、冲突提示，不丢未知字段。解决或经专门审查替代字体自 Hook。构建产物目录不能带 DryCycle.dll、私有 RWIMGUI 二进制或缺失资源；保留原有插件扩展兼容。**Gate：一键本地/CI 出可安装 Rainwright 文件夹和 ZIP，并验证 DLL、插件 GUID、资源、需求以及配置迁移。**

### M5 — 三模式、实机及性能回归

1. **DryCycle-only**：无 Rainwright Mod，正常运行游戏长期 Hook、生物、天气、世界通行和 DryCycle 自身原版 DevUI 控件。
2. **Rainwright-only**：无 DryCycle.dll，在原版与带第三方 Mod 环境中完成所有通用编辑视图、热加载、缩略图、Ctrl+S/Z/Y、存储/恢复/导出。
3. **Both**：DryCycle 专属扩展可注册/注销，OnModsInit/Enable/Disable 重复调用无重复 Hook、键盘输入抢占、后台地图缓存或文件冲突。

还要针对扩展抛错、Font/Atlas 清理失败、Alt+Tab、全屏切换、第三方 Objects 自定义绘制、编辑存档文件被外部修改进行故障回归。CI 加入真实 net48 前端与各发行模式 AssemblyRef/GUID 检测；最后才做发布前全仓终审。

## 5. 分阶段验收及优先级

| 顺序 | 必达产物 | 发布前必须满足 |
|---|---|---|
| M1 | 零 DryCycle.dll → Rainwright.dll 硬依赖 | DryCycle-only 不需要安装 Rainwright |
| M2 | Rainwright 掌握完整通用后端与事务 | Ctrl+S/Z/Y、恢复、异常保存、作者源路径一致 |
| M3 | 独立真实 Rainwright BepInEx + RWImGUI 前端 | Rainwright.dll 零 DryCycle AssemblyRef，只有一个 native UI Owner |
| M4 | 独立 modinfo、资源、配置迁移和打包 | 包内无不应打包的游戏/RWImGUI DLL |
| M5 | DryCycle-only / Rainwright-only / Both 全面验收 | Guard、回归、真实游戏加载及生命周期正确 |
| 最终 | 所有任务结束后的 Phase 5 全仓复审 | 只处理审计残余，按 P0/P1/合法保留分类收口 |

## 6. 进度口径修正

过去重复报告的 **97% 是局部前置解耦/纯核心抽离的估算**，没有以完整独立 Mod 为分母；现在发现真实核心 46 个 Compile 项与游戏侧 199、前端 122 项尚未合并，不能继续将 97% 称为“整体独立发行进度”。

**V2 的“可独立发布”总体完成度暂按约 45% 人工粗估**，且今后只有 P0 阻断项归零、实际 Rainwright-only 可构建/加载、三模式验收进展才提高对应阶段百分比。按功能与发布闭环评估，不再根据零散迁移文件数机械增长。

安全约束继续有效：禁止新增 RuntimeDetour 自 Hook，除本次查明的旧字体集成问题须专项处理；统一允许的 On/IL 生命周期；第三方兼容与作者文件无损优先；直接提交 main、提交后才报告通过的 CI；完成代码侧任务之后再作最终 Phase 5 全仓复审。
