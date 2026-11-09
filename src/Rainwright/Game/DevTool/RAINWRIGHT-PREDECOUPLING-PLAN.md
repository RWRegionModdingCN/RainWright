# Rainwright（雨匠）前置解耦重构方案 V1.0

方案状态：**历史 V1 前置解耦方案；当前执行基线见 RAINWRIGHT-MIDTERM-REPLAN-2026-10-12.md**。原阶段顺序保留作为技术背景。**Phase 1–4 前置解耦正在实施**（截至 2026-10-11 已有多轮生产代码测试与提交）；Rainwright 独立程序集、三模式实机验证及 Phase 5 最终全仓归属复审**尚未完成**。仍坚持先完成前置解耦、再全仓复审、最后构建 Rainwright.dll 的顺序，不为缩减引用而删除功能。

参阅：AGENTS.md、RAINWRIGHT-PLAN.md、RAINWRIGHT-OWNERSHIP-AUDIT.md、scripts/RainwrightOwnershipAudit.py。

## 0. 约束与基线

截至 2026-10-10，源码扫描覆盖 787 个 C# 文件：DevTool 目录内 317、外部 470；DevTool 内有 47 个文件直接引用 DryCycle 外部业务/启动，外部 12 个文件反向引用编辑器，外部 20 个文件直接安装原版 DevUI Hook，全仓 19 个 BepInPlugin 类声明、DevTool 内 17 个。

数字是检测出的文件数/入口数，非调用次数；尤其 20 个外部 DevUI Hook 大多是合法 DryCycle 自定义对象扩展，不能以全部删除为目标。

不可破坏的三种运行模式：
1. 只装 DryCycle：生物、天气、温度、WorldTopo 游戏通行、自定义对象及原版 DevUI 扩展正常。
2. 只装 Rainwright（未来独立构建后）：原版编辑器、新 UI、通用 Map/World/Objects/Sound/Trigger/Relationships/Cartography、保存/撤销/快捷键/恢复正常。
3. 同时安装：专属内容作为可选扩展接入，没有重复 Hook、快捷键、地图任务、输入上下文或作者文件冲突。

## Phase 0 — 建立可执行的迁移基线

- 运行 python3 scripts/RainwrightOwnershipAudit.py，归档 summary.json、inventory.json、inventory.md、Git SHA 与全绿 CI。
- 为 47/12/20/19 四组逐项建立台账：文件、Owner、危险程度、替代方案、测试、处理状态、允许保留的理由。
- 额外列出 typeof(DryCycle.Plugin).Assembly、Assembly.GetType/反射、BepInEx Chainloader、ModManager、Harmony/On./IL.、字符串类型名、内嵌图标/字体、配置与构建目标，避免 grep 漏掉间接依赖。
- 列出每一个服务/Hook 的当前安装和释放 Owner、首次注册时间、重复启用方式、异常回滚和模块是否应在 DevTools 关闭后持续工作。
- 在改动前为路径定位、作者保存、地图加载、Ctrl+Z/Y/S 和兼容页面准备可运行的生产代码测试。

验收：全部阻断项有明确归属和测试路径；无法检验的反射/Unity 绑定必须标注未验证，不能写成已清零。

## Phase 1 — 先解除 DryCycle 对编辑器的反向引用

### 1A 长枪拾荒者 AI

涉及：
- src/Creatures/LanceScavenger/AI/LanceScavengerAI.cs
- src/DevUI/DevTool/Debug/LanceScavengerDebugPresentation.cs
- src/DevUI/DevTool/RWImGui/Debug/LanceScavenger/

当前 AI 每次发布 DevTool Debug 数据。改为 DryCycle 自有、独立于编辑器的轻量观测快照/事件，长枪拾荒者专用呈现仅按需订阅。没有订阅者时不能分配大量临时对象或调用任何 ImGui；调试 UI 卸载须释放订阅。无 Rainwright 时 AI 继续工作。

### 1B World 作者路径服务反向调用

涉及：
- src/TemperatureSystem/TemperatureSetsLoader.cs
- src/Weather/Spatial/WeatherSpatialRegistry.Persistence.cs
- src/DevUI/DevTool/World/WorldAuthoringPathResolver.cs

抽离无编辑器类型引用的真实 Mod 作者源路径定位能力，供游戏业务独立使用。未来如需两程序集共用，优先同一份无状态内部源码 Link，禁止为了共享接口再增加必须随 Mod 发布的 DLL。保持原来的路径优先级、冲突保护与 mergedmods 禁止写入。检测不到唯一合法可写源，必须报错，不可回退写入 Rainwright 自身 Mod 或 DryCycle 主目录。

### 1C 旧 DevUI 控件、输入焦点与混合设置

涉及：
- src/DevUI/Controls/ 下五个文件
- src/DevUI/MapRoomMarquee.cs
- src/Misc/PaletteDirectInputRuntime.cs
- src/DayNight/DayNightPaletteDevUI.cs
- src/Misc/FadePaletteCombiner.cs
- src/RoomSettingsExt/DevUI/RGBColorEffectEditor.cs
- src/WorldLink/WorldLinkDevUI.cs
- src/TemperatureSystem/TemperatureSetsMapEditorRuntime.cs
- src/Weather/Spatial/WeatherSpatialDevUI.cs
- src/Misc/DryCycleOptions.cs
- src/DayNight/RegionDayNightOptions.cs

区分可共享的无状态计算、带状态的输入焦点和 DryCycle 自有业务编辑面板。不得直接移动五个控件造成 DryCycle 对 Rainwright 的硬引用，也不得复制两套全局输入焦点然后同时吞玩家操作。旧 Ctrl+Z/S/Y 抑制设定转 Rainwright 自有配置前须有只读迁移；RopeSpear 等玩法设置留 DryCycle。保持原版面板可在 DryCycle-only 模式正常编辑。

### 1D 将 Plugin/MiscRuntime 的编辑器启动责任收拢到过渡适配层

涉及：
- src/Plugin.cs
- src/Misc/MiscRuntime.cs

现有 Plugin.OnEnable、OnModsInit、OnDisable 直接协调 DevToolRuntime、PlayerMapBackendLifecycle、原版输入与额外组件。先提取明确的一处兼容启动边界，内部仍然调用原有系统，保持现有用户的启动和退出行为。只有新 Rainwright 独立启动真正接管后才能移走过渡边界；禁止这一步就删掉旧 Hook 导致当前 UI 失效。

验收：DryCycle 游戏业务源码不再硬链接未来的编辑器程序集；已用的 DevTools/自定义面板与玩法行为不改变；原来的诊断及完整清理链保留。

## Phase 2 — 解除通用编辑器对 DryCycle 专属业务的引用

### 2A 保存、Dirty、AutoSave 和 World 编辑

涉及：
- src/DevUI/DevTool/Commands/EditorActions.cs
- src/DevUI/DevTool/AutoSave/AutoSaveCoordinator.cs
- src/DevUI/DevTool/RWImGui/Pages/World/WorldWorkspaceDataView.cs
- TemperatureSetsLoader、WeatherSpatialRegistry、RegionDayNightOptions

采用最小可卸载作者数据 Provider/Participant：唯一标识、目标文件和文档身份、轻量 Revision/IsDirty、CaptureRecovery、Save、Reload、Dispose。保存结果必须明确 Success/Failed/Skipped 和错误；第三方/专属保存失败不可报告全局保存成功，也不可清 Dirty，崩溃快照和冲突备份不可丢。无 DryCycle Provider 仍可独立编辑所有原版数据；有 Provider 才显示天气与温度附加页。旧 AutoSave 间隔、拖拽/输入暂停和恢复策略不变。

扩展优先利用现有 DevToolExtensionApi 的版本、能力检查和 ScopedRegistration，不建立第二套互相重叠的 Registry。两个 Mod 之间可选发现只在连接时做，不能在每帧使用反射，也不能因某一方不存在而造成类型加载异常。

### 2B 日志、异常、构建和资源路径

将编辑器对 DryCycle.Plugin.Logger、StartupDiagnostics、AuxiliaryPluginStartupGuard 的调用统一接入 Rainwright 自有日志/启动事务服务。必须保留原异常、失败模块、回滚结果、可复制诊断，不允许吞错只写“已降级”。

重要持久化路径：
- BepInEx/config/DryCycle.DevTool.UI.xml
- BepInEx/config/DryCycle/DevTool/sound-groups.xml、sound-groups.location.txt
- BepInEx/config/DryCycle/AutoSave
- WorldTopologyRegistry 当前硬编码 NR.B5 和 typeof(DryCycle.Plugin).Assembly.Location
- Cartography 内嵌资源 DryCycle.Cartography.*
- ui/fonts/HarmonyOS_Sans_SC_Medium.ttf

先通过服务封装旧路径，未来迁移只读旧文件、校验新路径、备份并显式提示；作者文件定位以正在编辑的 Mod 为唯一依据。不要把所有 DryCycle 字符串一律替换。

### 2C 特殊地图几何和自定义调试

WorldMapGeometryRuntime 当前 using DryCycle.TerrainExt.QuicksandZone。将专有流沙数据读取改为可选地形几何贡献接口，保证原版/自定义曲面、缓存 Revision、缩略图非黑屏、局部重建和寻路算法不退化。专属长枪拾荒者 Debug/天气/温度/特殊地形编辑保留 DryCycle，通过扩展注册给 Rainwright。

验收：可移出的通用 Editor 核心没有 DayNight/Weather/Temperature/TerrainExt/Creatures/Items 等 DryCycle 硬引用；有专属扩展时功能与保存语义仍正确；已有算法不要为重构目录而重写。

## Phase 3 — 统一 17 个编辑器 BepInPlugin / Hook 生命周期

- 先列出真实激活者，不只统计 Attribute。PlayerMapBackendLifecycle 显式 Enable 多个组件，部分组件自身又是 BepInPlugin，BridgePlugin.OnEnable 也启用若干前端组件。
- 给 DevUI.Update、Player.checkInput、RainWorldGame.RawUpdate、MapPage.Update、RWImGui Context/Callback、WorldMap 后台任务、PlayerMap 地形烘焙/导出、WorldCreature 预览、WorldTopology 运行 Hook 分别指定唯一 Owner。
- 使用简单、有日志的安装账本：依赖顺序初始化，成功记账，失败逆序回滚，Enable/Disable 幂等，支持 OnModsInit 重复触发和 Mod Reload。不要新增复杂的通用 Guard 框架。
- 过渡期旧 BepInPlugin 可以作为不重复注册的兼容入口；**绝不能为了先把 17 改成 1 而直接删除入口并使现有用户功能消失**。独立 Rainwright 主入口完成时，才退役内部插件 Attributes 和旧 GUID。
- 严格区别游戏长期逻辑与编辑器临时逻辑：WorldTopologyRuntime 当前在 DevTools 不开启时仍通过 ShortcutHandler、Creature、AbstractCreature Hook 实现真实捷径行为。要分离编辑命令/临时热预览与永续玩法通行，保留 DryCycle-only 游戏行为，避免 Rainwright 与 DryCycle 两套 Shortcut Hook 争夺同一行为。
- 动态卸载任何组件不得解除第三方 Mod 注册的 Hook；前端失效不能让后端作者数据保存假成功。

验收：每个 Hook/背景任务只有一个有效的安装者与对称释放者；重复启用不重复执行；关闭 UI 不破坏游戏通行；错误和回滚有可复现记录。

## Phase 4 — 数据/性能回归与迁移前置验证

- DryCycle-only：不加载 Rainwright 时自定义内容、Temperature/Weather/WorldTopology、原版自定义 DevUI 控件和生物 AI 正常。
- Rainwright-only：独立 DLL 迁移后检验，无 DryCycle.dll 的情况下全通用编辑器完整运行；前置阶段未建独立 DLL 时该项只能标注待验证。
- Both：自有 Provider 动态注册/注销，反复 OnEnable/ModsInit/OnDisable 后无重复 Hook 或资源泄漏。
- 负例/故障注入：Provider 抛错、路径不唯一、文件被外部修改、丢失前端、异常关闭/崩溃快照恢复、Partial Enable 抛错并逆序回滚。
- 观察输入 Ctrl+Z/Y/S 和玩家移动冲突；World Map 缩略图、拖拽/缩放、关系视图、地图方向与连接、后台工作量及缓存命中。
- Guard、Regression Tests、CODEMAP、Rainwright Ownership Audit 全绿；使用真实 Rain World/net48 引用时才能声明完整编译通过，不能把纯语法过关当成 DLL 可运行。

## Phase 5 — 所有任务结束后的全仓复扫与遗留问题收口

先在当前源码执行：
python3 scripts/RainwrightOwnershipAudit.py --output artifacts/rainwright-ownership-final

与阶段零归档对照所有文件及引用，并进一步覆盖：
- 全仓 .cs/.csproj/.props/.targets/.ps1/.sh/.yml/.json、内嵌资源/字体/Mod 清单、旧 DLL 路径与 BepInEx GUID；
- using、global::、typeof、继承/接口/属性签名、Assembly.Load/GetType、反射、ModManager、Chainloader、Harmony/On./IL.、字符串路由；
- 所有 BepInPlugin、BepInDependency 与 Hook 的真实 Owner、重复安装、异常回滚及关闭顺序；
- 所有游戏持久写入与作者文件的实际 Mod 身份和可写源路径；
- 全部迁移过渡层残留，旧 DevTool 前端、旧自动编译与部署规则及可能失效的 Guard/测试文件引用；
- 真正分别编译后的 DryCycle.dll / Rainwright.dll Mono.Cecil AssemblyRef、TypeRef、插件元数据（该项需等独立 DLL 产生）。

最终报告要给每条发现标记：已修复、合法保留（说明 Owner 和测试）、未解决阻断、仅实机可验证。必须优先修复真正问题，不靠更改扫描关键词或排除目录获取全绿。

收口 Gate：
1. 零个未解释的 DryCycle → 编辑器程序集硬依赖；
2. 零个通用编辑器 → DryCycle 专属业务硬依赖；
3. 零个重复安装的有效 Hook / RWImGui context / 地图后台 Owner；
4. 零个未处理的非授权跨 Mod 写入路径或假成功保存；
5. 不能按普通“剩余计数为 0”删除合法的 DryCycle 自定义 DevInterface Hook；
6. 新旧测试均通过，配置/缓存/恢复文档和 Runtime 兼容清单完整；
7. 构建环境不具备时明确记录未验证的 DLL/实机内容。

只有完成上述代码边界和扫描收口，才进入下一阶段：整体迁移通用源码到 src/Rainwright/、构建 Rainwright.dll、提供独立 Mod 打包与发布脚本。

## 逐步提交节奏

固定顺序：Phase 0 → P1-A → P1-B → P1-C → P1-D → Phase 2 业务 Provider → Phase 2 资源/日志 → Phase 3 生命周期 → Phase 4 回归 → Phase 5 全仓审计。

每个问题按：保护原行为的测试 → 改正确 Owner → 新测试/故障注入 → 移除旧调用 → 扫描/编译 → 单独提交 的顺序执行。必要时退役旧代码，但不得在替代行为未验证时提前退役。
