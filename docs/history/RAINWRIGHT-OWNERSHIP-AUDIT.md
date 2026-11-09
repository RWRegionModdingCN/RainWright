> 历史审计记录，描述迁移前的仓库布局。当前项目边界见 [架构说明](../ARCHITECTURE.md)。

> 说明：本文记录首次 787 源文件审计的历史快照。2026-10-12 中程可重复全仓检查及 V2 方案见 [RAINWRIGHT-MIDTERM-REPLAN-2026-10-12.md](RAINWRIGHT-MIDTERM-REPLAN-2026-10-12.md)，不得把旧数字当作现在进度。

# Rainwright（雨匠）归属与独立发布深度审计

审计基线：DryCycle main @ 57fbbbb144a03f1b14cc485d3758a868c52976df（迁移审计开始前）。审计脚本提交：4d50d5e9dd35473695d87d81ba91f23672713e30。
范围：完整 Git 树、src 全部 C# 源码、现有前后端 csproj/props/targets、部署构建、Mod 清单、重要测试/Guard、资源及启动生命周期。

**本文件是迁移计划和代码归属结论，不表示 Rainwright 已构建或 DryCycle 已解耦。** 逐文件的自动化扫描与行号可通过 scripts/RainwrightOwnershipAudit.py 再生，CI 的 Rainwright Ownership Audit 会提供 inventory.json / inventory.md / summary.json。扫描是源码级，不等于真实程序集链接、反射、运行时验证。

## 1. 产品边界与命名

- 正式名称：Rainwright；中文：雨匠。
- 功能定位：可不安装 DryCycle 即可使用的新 DevTool、全局/局部快捷键及顶部反馈、两套 Map、World/Room/Objects/Sound/Trigger/Relationship/Cartography、World 数据预览及局部热加载、缓存调度、作者保存/Undo/Redo/AutoSave/Crash Recovery、第三方 Mod 兼容、扩展 API、诊断。
- 建议目标：src/Rainwright/、Rainwright.dll、BepInEx GUID NR.Rainwright；发布前核对 GUID 与 Mod ID 冲突，Mod ID 和 BepInEx GUID 是两种身份。
- 独立自有 DLL 不代表把 RWImGui、它的私有 ImGui.NET、cimgui 或游戏 DLL 打入包；RWImGui 是需要正确版本的外部运行依赖，自有字体/资源也需要按许可和路径随包管理。
- DryCycle 可以向 Rainwright 可选注册天气、特殊地形、物品、生物与私有地图数据扩展。反向不得形成 Rainwright -> DryCycle 的硬依赖；不安装任何一方时，另一方的核心行为仍正常。

## 2. 全源码扫描计数（787 / 787）

| 初始归属 | 文件数 | 判定 |
|---|---:|---|
| DevTool 通用后端 | 189 | 原则上迁入 Rainwright，先修跨模块依赖 |
| RWImGui 通用前端 | 125 | 原则上迁入 Rainwright，最终与后端同 DLL |
| 藏在 DevUI/Controls 下的原版 UI 控件 | 5 | 必须纳入拆分设计，不能遗漏对 DryCycle 的反向调用 |
| 目录外共用编辑工具 | 2 | MapRoomMarquee、PaletteDirectInputRuntime，按调用/所有权作拆分 |
| DevTool 目录内 DryCycle 专属调试 | 3 | 长枪拾荒者专属代码不直接进入通用 Rainwright |
| 混合启动/配置 | 7 | 必须拆出 Rainwright 所有权，DryCycle 游戏部分留原处 |
| 独立 AI Observatory | 56 | 非本次 DevTool 迁移目标，留在其现有独立职责域 |
| DryCycle 游戏/专属扩展域 | 325 | 游戏逻辑保留 DryCycle；其中多处原版 DevUI Hook 要适配 |
| 其他 DryCycle 文件 | 75 | 默认保留，检查有无反向依赖与公共构建耦合 |
| **总计** | **787** | 完整扫描，不意味着逐项运行过 |

自动提取到 47 个 DevTool 文件直接引用 DryCycle 外部业务/启动代码；12 个外部文件反向引用 DevTool/DevUI；20 个外部文件直接安装/解除 DevInterface Hook；19 处 BepInPlugin 类型声明（含 DryCycle 主插件、AI Observatory）。自动检测只覆盖显式源码模式，动态反射、字符串调度仍需要人工核查。

## 3. 必须迁移的全部常规编辑器域

当前 src/DevUI/DevTool/ 中通用文件应按**功能完整迁移**，不能只拎 RWImGui：

| 范围 | Rainwright 中归属 | 要保护的能力 |
|---|---|---|
| Core, Commands, History | Core/Session、Commands、History | 会话/文档、Revision/Dirty、统一命令、选区、Undo/Redo 与批量事务 |
| Input | Input | 全局与视图快捷键、前台输入仲裁、游戏按键抑制、顶部反馈 |
| AutoSave | Core/AutoSave | 2.5 分钟调度、作者脏标志、快照、恢复、跨文档隔离 |
| Compatibility, Factories, Extensions | Compatibility、Factories、Extensions | Vanilla/POM/RegionKit 可选兼容、无头 Object 宿主、数据模型和公开扩展协议 |
| Dialog, Gizmos, Objects, Preview | Editors | 资源浏览、原版及第三方对象、Gizmo、实时效果预览与清理 |
| Room, Sound, Triggers, Relationships | Editors | 原版设置/环境音/触发器/生物关系的编辑及落盘 |
| World | Editors/World 与 Map/World | world.txt、区域拓扑、连接、LINEAGE、Creature 临时预览 |
| Map 直属文件、PlayerMap、Cartography | Map | Map 配置、局部房间源恢复、持久缓存、场景栅格、曲面/导出与错误窗口 |
| RWImGui 全通用子目录 | RWImGui | 页面/Shell/Window/Widget/字体/性能显示、地图 retained V2/GPU/连接路由 |
| Debug (通用部分) | Diagnostics | 工具性能与调试；长枪拾荒者专属除外 |

WorldMapFrontendBridge、WorldMapGeometryPersistentRuntime、WorldMapSourceRecoveryRuntime、MapViewPersistentCache、PlayerMapBackendLifecycle 及 RWImGui/Pages/World/WorldMap/RetainedV2 的缓存/调度/缩略图/寻路/方向标记全部属于完整编辑器，不允许因 UI 已迁移就把它们留在 DryCycle.dll。WorldCreatureLiveReload 的预览语义也保留，但不可误改为直接重建游戏真实 World 生物生成表。

## 4. 反向依赖：12 个目录外文件，不能移动后才补

这些位置源代码直接依赖 DevTool/DevUI，应作为**迁移阻断项**逐个关闭：

1. src/Creatures/LanceScavenger/AI/LanceScavengerAI.cs：AI 直接调用 DevTool.Debug 的 LanceScavengerDebugPresentationHub.Publish；把诊断数据桥移回 DryCycle 业务或抽象事件，不能要求 gameplay 安装 Rainwright。
2. src/Misc/MiscRuntime.cs：原版 DevUI 隔离、编辑器主 Hook、PlayerMap 后端的启动/清理分散于 DryCycle 生命周期；全部改由 Rainwright 一个 Owner 安装。
3. src/Plugin.cs：在 OnEnable 和 OnModsInit 启动 DevTool、汇报前端状态，还订阅 DryCycle 生命周期；删掉这些 DevTool 所有权调用，留下 DryCycle 本体行为。
4. src/TemperatureSystem/TemperatureSetsLoader.cs：直接依赖 DevTool.World 的 WorldAuthoringPathResolver，属于 gameplay -> editor 的反向引用；路径解析需改成无 UI 依赖的服务，或 DryCycle 自有解析器。
5. src/Weather/Spatial/WeatherSpatialRegistry.Persistence.cs：同样依赖 WorldAuthoringPathResolver，必须解除才能单独发布。
6. src/DayNight/DayNightPaletteDevUI.cs：使用 src/DevUI/Controls/ 的原版文本/数值控件；留在 DryCycle 专属编辑扩展。
7. src/Misc/FadePaletteCombiner.cs：引用上述通用数值控件；该可选 RegionKit 替代功能需要明确长期 Owner。
8. src/Misc/PaletteDirectInputRuntime.cs：通用原版 DevUI 输入能力，当前既被 MiscRuntime 激活又和控件共享输入焦点；优先作为 Rainwright 通用适配。
9. src/RoomSettingsExt/DevUI/RGBColorEffectEditor.cs：特定 RoomEffect 的高级调色界面，留 DryCycle，并解耦通用输入控件依赖。
10. src/TemperatureSystem/TemperatureSetsMapEditorRuntime.cs：DryCycle 私有温度地图编辑面板，留业务域并通过扩展协议接入 Rainwright。
11. src/Weather/Spatial/WeatherSpatialDevUI.cs：私有天气地图编辑面板，留业务域并通过扩展协议接入 Rainwright。
12. src/WorldLink/WorldLinkDevUI.cs：私有多业力门/世界连接对象 Representation，留 DryCycle，注册兼容协议而不是硬依赖 Rainwright。

**控件共享难点**：src/DevUI/Controls/ 五个文件特别是 DryCycleTextField、DryCycleFloatField、DryCycleIntegerField、DryCycleNumericSlider、IntegerControlInputBinding 有多处 DryCycle 专属使用者。不能直接移动导致 DryCycle.dll 必须引用 Rainwright.dll；也不能毫无区分地复制一整套静态焦点状态。候选处理是同一份公共源文件分别编译到两侧并显式仲裁焦点，或者改写 DryCycle 私有面板的控件接入，须经过行为测试才能选定。

## 5. 47 个 DevTool 外部引用文件：关键解耦项

主要分为以下六组（完整逐文件清单见扫描产物）：

- **日志/生命周期**：RecoverySnapshotStore、RelationshipSaveAdapter、MapViewPersistentCache、MapTerrainSourceDecoder、WorldMapGeometryPersistentRuntime、WorldMapRoomSource、WorldMapSourceRecoveryRuntime、WorldTextRegistry、WorldConnectionSyntax、WorldTopologyCommands、WorldTopologyRuntime、RWImGui BridgePlugin 等直接使用 DryCycle.Plugin.Logger / StartupDiagnostics / AuxiliaryPluginStartupGuard / DryCycleLifecycleEvents；替换为 Rainwright 自有日志、错误窗口和启动事务。
- **配置/保存逻辑**：AutoSaveCoordinator 引用 DayNight.RegionDayNightOptions、TemperatureSetsLoader、WeatherSpatialRegistry；EditorActions 同样耦合温度/天气；需要把通用保存调度与 DryCycle 专用数据 Provider 拆开，禁止因为没有 DryCycle 就引用失败。
- **地图特殊几何**：WorldMapGeometryRuntime using DryCycle.TerrainExt.QuicksandZone；将具体地形读取交给可选几何来源/描述接口，保留曲面、流沙、原版地形同一渲染策略。
- **World/作者路径**：WorldTopologyRegistry 包含 NR.B5 以及 typeof(DryCycle.Plugin).Assembly.Location 的硬编码定位。必须按**真正被编辑的 Mod**寻找可写源文件，绝不能把第三方 world.txt 或拓扑改动误写进 Rainwright 自己的 mod 目录、DryCycle 模组目录或 mergedmods 缓存。
- **前端业务混合**：RWImGui/Pages/World/WorldWorkspaceDataView.cs 直接持有 TemperatureSetsLoader 与 WeatherSpatialRegistry 并编辑专属业务字段；其通用数据页和私有编辑模块必须拆开，由 DryCycle 扩展注入。CartographyCanvasImages 当前读取 DryCycle 主日志，也要独立。
- **特殊调试**：DevTool/Debug/LanceScavengerDebugPresentation.cs 与 RWImGui/Debug/LanceScavenger/ 两文件应由 DryCycle 适配层持有；不要让通用 Rainwright 发布包包含专属生物 AI 调试依赖。

另外 Compatibility/LegacyDevUiQuiescenceController 通过 typeof(DryCycle.Plugin).Assembly 判断所属程序集；迁移必须重新审计其所有权/反射过滤行为，而不是仅替换一个类名。

## 6. 隐藏插件入口：19 个 BepInPlugin（高风险）

19 处插件声明中：
- 1 个 DryCycle 主入口：src/Plugin.cs，留 DryCycle。
- 1 个独立 AI Observatory 前端：src/DryCycle.AIObservatory.RWImGui/BridgePlugin.cs，留 DryCycle AI 诊断能力。
- **17 个 DevTool 目录插件类型**：包含主 RWImGui BridgePlugin、8 个 PlayerMap 配置/烘焙/增量渲染/验证相关辅助入口、2 个 World 运行时入口、6 个其他前端/辅助入口。

重点涉及：
- Map/PlayerMap/PlayerMapRuntimePlugin.cs；
- Map/PlayerMap/PlayerMapConfigBuildPipeline.cs、PlayerMapDerivedLayoutBridge.cs、PlayerMapGroupCommandRuntime.cs、PlayerMapIncrementalRender.cs、PlayerMapRenderOutputValidator.cs、PlayerMapRenderRevisionGuard.cs、PlayerMapTerrainBakeBridge.cs；
- World/WorldCreatureLiveReload.cs、World/WorldTopologyLiveTraversalFixPlugin.cs；
- RWImGui/Shell/BridgePlugin.cs；
- RWImGui/Pages/World/Creatures/WorldCreatureCatalogPicker.cs、WorldCreatureSpawnInspectorPlugin.cs；
- RWImGui/Pages/World/WorldLineageInspectorPlugin.cs、WorldSubregionSelector.cs；
- RWImGui/Pages/World/WorldMap/Player/WorldMapPlayerLocator.cs；
- RWImGui/Debug/LanceScavenger/LanceScavengerRealtimeLogPlugin.cs（专属功能，留适配层）。

部分入口之间有 HardDependency，且服务可能同时由前端/后端显式 Enable。**最终通用 Rainwright.dll 只保留一个 BepInEx 主插件入口**，所有通用组件由单一生命周期容器按拓扑依赖顺序显式安装并逆序释放；禁止继续靠加载器发现一串内部子插件确保正确性。与此同时保留旧 BepInEx GUID 的安全兼容/冲突检测，防止旧版 UI 和 Rainwright 同时激活两套 Hook。

## 7. 目录外 20 个原版 DevUI Hook 文件不能误迁

这些 Hook 归属于 DryCycle 原版功能适配（或已明确的通用控件）而非通用 Rainwright 的核心 UI：

- src/DayNight/DayNightPaletteDevUI.cs
- src/Items/DewPod/DewPodHooks.cs、DewPodPlantHooks.cs
- src/Misc/FadePaletteCombiner.cs、IndividualPlacedObjectViewer.cs、PaletteDirectInputRuntime.cs
- src/RoomSettingsExt/RGBEffects/RGBEffectRuntime.cs
- src/TemperatureSystem/SolarShadeZoneHooks.cs、TemperatureSetsMapEditorRuntime.cs
- src/TerrainExt/QuicksandZone/QuicksandZoneHooks.cs
- src/Weather/HeatWave/HeatColumnHooks.cs
- src/Weather/Spatial/WeatherSpatialBinaryRuleUiRuntime.cs、WeatherSpatialDevUI.cs、WeatherSpatialHoverHelpRuntime.cs、WeatherSpatialIssueWrapRuntime.cs、WeatherSpatialMapMenuRuntime.cs、WeatherSpatialPreviewPersistenceRuntime.cs、WeatherSpatialSelectionUiCleanup.cs、WeatherSpatialTogglePaintRuntime.cs
- src/WorldLink/WorldLinkPlacedObjects.cs

这些 Hook 即使 Rainwright 不安装，DryCycle 仍需正常注册自定义 Object/RoomEffect 并能运行。Rainwright 安装后只能通过合法的第三方 Representation/Provider 协议读取并编辑，**不得主动移除其他 Mod 的 Hook**。尤其要检查原版 UI 抑制器与自定义 Page/Panel 的可见性、选择和编辑同步，防止此前 POM/RegionKit 异常或卡顿复现。

## 8. 资源、文件格式、配置及备份的迁移约束

| 当前位置或契约 | 具体风险 | 必要处理 |
|---|---|---|
| RWImGui/Settings/DevToolUserSettingsStore.cs：BepInEx/config/DryCycle.DevTool.UI.xml | 独立化重命名后丢布局/语言/窗体尺寸/分组 | 首次载入一次性导入、校验后原子写 Rainwright 新位置，旧文件只读保留 |
| Sound/SoundGroupLibrary.cs：BepInEx/config/DryCycle/DevTool/sound-groups.xml 和 location.txt | 已设置的音效组消失或扫描目录回退 | 携同配置迁移与版本标记，保留自定义目录并隔离其他 mod 的音效组 |
| AutoSave/RecoverySnapshotStore.cs：BepInEx/config/DryCycle/AutoSave | 迁移后旧崩溃快照无法恢复/误恢复 | 旧快照发现、目标路径核验、明示操作和恢复前备份；不能自动覆盖 |
| World/WorldTopologyRegistry.cs：NR.B5 和 DryCycle.dll 所在 Mod | 非 DryCycle 作者文件误写 | 根据目标 Mod/region 明确可写源路径，不写 mergedmods，不默认写 Rainwright 自身 |
| World/WorldAuthoringPathResolver.cs | 编辑的是哪个 Mod 的 world/source | 路径选择与冲突检测成为通用核心；DryCycle Temperature/Weather 不再反向依赖 |
| Cartography/Assets/*.png，逻辑嵌入名 DryCycle.Cartography.* | 迁移后嵌入资源名变化，图标缺失 | 资源统一打进 Rainwright.dll，保留或迁移 manifest name；有程序集资源读取测试 |
| RWImGui/Settings/Fonts/DevToolFontCatalog.cs：HarmonyOS_Sans_SC_Medium.ttf | 字体路径依据 DLL 相对位置；独立包找不到或授权不符 | Rainwright/ui/fonts 路径与外部字体部署清单、缺失回退；发布前确认字体分发许可 |
| World Map V2/V3 持久缓存和渲染缓冲 | 版本/命名改变后黑缩略图、重复后台加载 | 旧缓存只读迁移、Revision/Dirty/预算验证，避免迁移时重新写慢路径 |
| Cartography/Sound/World/RoomSettings 作者文件 | 写入改错目标、缓存伪装成功 | 保留 hash/atomic/backup/dirty/错误内窗；失配拒绝保存 |

配置和生成文件以 BepInEx Config、Mod 作者目录为边界；不能简单把所有包含 DryCycle 字串的作者数据文件全局改名。真实目标 Mod ID、路径、数据 Owner 是决定写入位置的依据。

## 9. 构建、工程、Guard、回归测试归属

- src/DryCycle.csproj 目前通过默认 SDK glob 编译所有 DevTool 通用后端，只有 RWImGui 前端通过 Compile Remove 排除；新增 src/Rainwright/ 后必须在 DryCycle.csproj/Directory.Build.props/targets 显式排除，禁止两个程序集合入同一份运行时。
- src/DevUI/DevTool/RWImGui/DryCycle.DevTool.RWImGui.csproj 目前 Reference DryCycle.dll 且 ValidationTarget 要求先有 DryCycle.dll，不能直接改 AssemblyName 宣称独立。
- src/Directory.Build.targets 有自动编译并部署旧 RWImGui 前端的链路、删除旧 DLL 的逻辑、UpToDateCheckInput/Output；必须先增加 Rainwright 独立建造目标，再经实际验证退役旧链路，防止删掉旧 DLL 却没产出新 DLL。
- src/ILRepack.targets 和 DryCycle 专属 NAudio 合并归 DryCycle；Rainwright 新工程不能照搬整套 NAudio/Shader/LanceScavenger 部署构建。
- mod/modinfo.json 当前属于 Ancient Site（NR.B5），含 regionkit/crs/watcher 等专属需求；Rainwright 要单独 modinfo.json、唯一 Mod ID、仅需真实前置，不继承整张 Ancients Site 依赖清单。
- src/DryCycle.AIObservatory.RWImGui 及 Debug/AIDebugger 属于另一个 ImGui 消费端，明确**不跟 Rainwright 合并**。
- tests/AutoSave.Tests、Cartography.Tests、DevToolAuthoring.Tests、MapLayer.Tests、WorldMapBackground.Tests、WorldMapDirection.Tests、WorldMapPersistentCache.Tests、WorldMapRenderIsolation.Tests、WorldMapRouting.Tests 等需要按类型新位置更新编译链接和测试程序集路径；CreatureCatalog.Tests 需评估其对编辑器注册表的范围；IteratorFramework.Tests、LanceScavenger.Tests 属 DryCycle 本体。
- Guard/checks 的结构扫描大量定位 src/DevUI/DevTool；需要同步改为新路径和新入口，并新增：Rainwright.dll 不包含 DryCycle AssemblyRef、DryCycle.dll 不引用 Rainwright.dll、只有一个 Rainwright BepInPlugin、DryCycle 不包含通用编辑器类型和旧 ImGui 前端、无直接游戏目录构建写入。
- Guard/tools/local-build.ps1 必须能分别验证 DryCycle 本体、Rainwright DLL、AI Observatory（如需），并检查两个 DLL 各自依赖，无假编译、无静默跳过；发布脚本要进行资源、DLL、modinfo 完整性验收。

## 10. 建议迁移顺序（不可跳步）

1. **依赖封口**：修复 AI -> DevTool Debug、Weather/Temperature -> WorldAuthoringPathResolver、DryCycle Plugin/MiscRuntime 对 DevTool 的直接生命周期依赖；提取共享输入控件与默认配置策略。
2. **抽离专属 Provider**：天气/温度/长枪拾荒者/特殊地形/专属 World 视图只作为 DryCycle 可选扩展，独立 Rainwright 无对应程序集硬引用。
3. **独立启动 Owner**：一个 RainwrightPlugin/BepInEx GUID，一个同步 DevUI hook、一个输入仲裁器、一个前端 callback、一个地图背景调度、对称清理/失败回滚。
4. **完整通用源码移动**：先后端（Session/Commands/History/Map/World/Sound/Trigger/Objects/AutoSave），再 RWImGui/Render/UI；保持作者数据、Revision、缓存算法及命令语义不变。
5. **单程序集建造**：唯一 Rainwright.dll，外部 RWImGui API 保留 Private=false，不将原生/runtime DLL 重包；单独输出包与单独 modinfo；真实游戏编译和 DLL 引用审计。
6. **兼容迁移和旧路径退役**：旧配置/恢复快照只读兼容，新数据写回 Rainwright 独立路径；确认 DryCycle 无编辑器 Hook 和旧前端加载后删除旧目标。
7. **双模式验证**：仅 Rainwright（无 DryCycle）、仅 DryCycle（无 Rainwright）、两者同时安装、前置 RWImGui 缺失/版本不匹配、安装常见第三方 Objects；加长会话、地图放缩、热加载、快捷键、作者保存、崩溃恢复、启动/卸载性能测试。

## 11. 禁止误报已完成

这次任务是**记录讨论和完整依赖/所有权审计**，不是拆分实现。静态脚本覆盖全部 787 个源文件并给出逐文件基础归属，但也不能由此声称编译/运行时已解决反射、资源、DLL 链接与实际游戏状态耦合。未来实现每一阶段都要明确代码侧完成、公共 CI 成功、真实编译、实机验证这四种不同状态；绝不能为压低风险而只隐藏功能或静默回滚。
