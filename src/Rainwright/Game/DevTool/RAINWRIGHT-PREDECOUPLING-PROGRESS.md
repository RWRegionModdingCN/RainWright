# Rainwright 前置解耦执行台账

此文件记录代码侧实际提交与验证结果。方案详见 RAINWRIGHT-PREDECOUPLING-PLAN.md；全仓原始审计见 RAINWRIGHT-OWNERSHIP-AUDIT.md。

## 基线

- 2026-10-10 全仓共 787 个 C# 源文件，DevTool 目录 317 个。
- 原始静态审计：DevTool 外部引用文件 47 个、外部反向引用 12 个、外部 DevUI Hook 文件 20 个、BepInPlugin 类声明 19 处。
- 数量并非全部需要归零：DryCycle 自定义物件与天气面板中的合法 DevUI Hook 必须保留。
- 全仓扫描脚本：scripts/RainwrightOwnershipAudit.py，CI Rainwright Ownership Audit。按用户确认的流程，**全仓归属扫描仅在所有前置解耦任务代码侧处理完成后，于最终 Phase 5 手动触发一次完整复审**。过程中的每批修改只执行局部契约检查、生产代码测试、Guard、Regression Tests 与 CODEMAP；不提前进行全仓扫描。

## 代码侧实施状态

| 阶段 | 内容 | 状态 | 证据 |
| --- | --- | --- | --- |
| Phase 0 | 静态全仓归属与阻断基线 | 已完成初始静态扫描；动态/反射/程序集映射待最终验收 | 930cb4b5；Rainwright Ownership Audit |
| P1-A | LanceScavenger AI 不再引用 DevTool.Debug | 代码完成，待完整 net48/实机 | 0eac3f45；Guard |
| P1-B | TemperatureSets/WeatherSpatial 不再依赖 WorldAuthoringPathResolver | 代码完成，生产路径检查已运行回归 | 0eac3f45；DevToolAuthoring.Tests |
| P1-C | 旧 DevUI 输入控件与焦点共享拆分 | 已完成输入焦点租约、三个快捷键独立设置/旧 Remix 只读回退及程序集归属租约；真正分 DLL 后的两插件兼容仍需验证 | LegacyTextInputSources + RainwrightShortcutInputSettingsStore + LegacyDevUiOwnedAssemblies |
| P1-D | 分散的 DevTool 启动/清理调用收拢 | 通用后端源码 Owner 已进入 EditorBackendLifecycle；DryCycle 旧 Plugin 仍负责过渡调用，正式交给独立 Rainwright 入口待办 | EditorBackendLifecycle + LegacyBootstrapTests |
| Phase 2 | 保存 Provider、可选业务、日志/持久化及专属 UI 解耦 | 进行中：World 专属扩展 DLL/地形 Provider、AutoSave/快捷键/Relationships/Sound/Cartography 资源与配置归属已处理；独立 Editor Backend 程序集仍未建立 | EditorAuthoringParticipants + DryCycle.WorldData.RWImGui + CI |
| Phase 3 | 子 BepInPlugin / Hook 单一 Owner 与对称回滚 | 进行中：World 四个前端辅助入口已经交给 Bridge；7 个 PlayerMap 辅助入口使用租约；作者族谱必需服务归 EditorBackend；WorldTopology 游戏 Hook 使用独立账本；剩余启动桥/组件需继续验证 | FrontendHookLedger + PlayerMapAuxiliaryCompatibilityLease + WorldTopologyHookLedger + CI |
| Phase 4 | 配置与作者数据迁移、回归、真实 DLL 验证 | 已覆盖配置故障/重载/并发、作者保存、世界游戏 Hook 故障；真实 net48、三种安装模式、字体/GPU/UI 主线程实机仍未验收 | DevToolAuthoring.Tests + WorldCreatureAuthoring.Tests + WorldTopologyLifecycle.Tests + CI |
| Phase 5 | 最终完整重新扫描并处理所有阻断 | 待实施 | 不允许把合法 DryCycle 专属 Hook 当作残留删除 |

## 已提交的具体修改

### 0eac3f45 — 游戏业务与编辑器的两处反向引用

1. 将 LanceScavenger 调试数据快照/环形历史生产者从 DevTool.Debug 移至 Creatures/LanceScavenger/Diagnostics；AI 的 Debug.Publish 仍在原模拟时机，UI 的消费类型引用同步指向新命名空间，维持原采样率。
2. 把 mergedmods 检查移入 src/Shared/AuthoringSourcePaths.cs；TemperatureSetsLoader 与 WeatherSpatialRegistry 使用它，原编辑器 WorldAuthoringPathResolver 也委托同一实现。
3. DevToolAuthoring.Tests 真实编译这一公共路径方法并运行合并路径、相似前缀、目录遍历、空值的反例测试。
4. Guard 加入 P1-A/B 反向程序集依赖契约检查，防止后续又把 AI/天气/温度指向 DevTool。
5. 仅迁移对象所有权与路径判断，不改变原本的地图连接算法、作者文件格式或 AI 战斗算法。

### cdccd71a — DevTool Legacy 启动集中

1. 新建 Misc/DevToolLegacyBootstrap.cs，收拢现有核心 DevUI.Update、两套兼容辅助模块、PlayerMap 后端及静态目录预热的安装/卸载。
2. MiscRuntime 的 EnableDevToolCore/EnableDevToolExtras/DisableDevToolBackend 变成委托，不再直接引用各个编辑器具体类型，现有 Plugin.OnEnable/OnModsInit/OnDisable 的调用时机不变。
3. 直接把生产 DevToolLegacyBootstrap 源码链接到 DevToolAuthoring.Tests，测试核心/额外初始化幂等、关闭逆序、初始化失败、单一地图失败和清理异常时不中断剩余清理。
4. 这只是**可整体退役的过渡适配层**，不等于独立插件入口已经完成。

## 关键未处理边界

- EditorActions 与 AutoSaveCoordinator 的 Temperature/Weather 保存和恢复已交给 EditorAuthoringParticipants；WorldWorkspaceDataView 通过独立 DryCycle.WorldData.RWImGui.dll 注册专属天气/温度 UI。AutoSave 和 Ctrl+Z/S/Y 已有 Rainwright 独立设置窗口及持久化，旧 Remix 作为只读动态 Provider 回退；真正分 DLL 后的依赖和加载顺序尚未验收。
- WorldMapGeometryRuntime 对 QuicksandZoneData 的硬引用已改为可选 EditorMapTerrainContributors，DryCycle 的 QuicksandZone 通过过渡启动层注册；地图缓存指纹加入注册版本。单独 DLL 发布与实际 GPU/地图热加载验证仍待后续。
- WorldTopologyRuntime 的真实游戏通行 Hook 已由持久游戏 Owner 的 WorldTopologyHookLedger 持有；编辑器关闭不能移除它。WorldTopologyRegistry 当前有针对 NR.B5 专属 gameplay 的路径约束，若将此存储用作第三方 Mod 作者数据，仍须重新划定作者 Mod 身份和只写正确目录的规则，不能直接把游戏代码迁入 Rainwright。
- DevUI/Controls 的五个控件以及 PaletteDirectInputRuntime 仍被 DryCycle 专有面板使用；有状态输入焦点不能直接复制两个静态单例。
- PlayerMap 主后端和 7 个辅助入口已采用租约交接；WorldCreatureSpawnInspector、WorldLineageInspector、WorldSubregionSelector、WorldCreatureCatalogRuntime 辅助入口保留 BepInEx GUID，但其具体工作现统一由 BridgePlugin 调度，不再双重启停。世界作者族谱保存归 EditorBackendLifecycle 的必需项；所有已退役的 BepInPlugin GUID 仍需在独立发布迁移阶段慎重兼容。
- 新的 Rainwright.dll 尚未建立；两 DLL 无相互 AssemblyRef 只能在真实编译后验证。
- 用户的真实游戏程序、POM/RegionKit、RWImGui 字体及 GPU 渲染没有自动化实机验证。

## 每批提交的收口规则

每批代码变更要求通过 Guard、Regression Tests 与 CODEMAP，并执行针对受影响模块的测试；**Rainwright Ownership Audit 仅在 Phase 5 所有代码侧任务完成后手动执行最终全仓复审**。发生 CI 失败立即定位修复，并记录真实环境不能验证的事项。最终 Phase 5 必须运行全仓扫描、对比本文件的基线并对所有未知残留逐项说明和修复，不能仅报告“扫描完成”。


### P1-C — 旧控件输入焦点桥接（本轮）

- 新增 Input/LegacyTextInputSources.cs：允许 DryCycle 原版 DevUI 控件以可释放租约公开“是否正在文本编辑”，编辑器的每帧读取无分配、无需加载 DryCycle 控件类型。
- DevToolLegacyBootstrap 仅在已有编辑器过渡生命周期内注册一次输入源，并在关闭、部分失败时解除注册；原版数字输入与调色板原逻辑不改。
- EditorInputRouter 的 HasLocalTextFocus 不再直接引用 DryCycleInputFocus 或 PaletteDirectInputRuntime，仍在原位置处理游戏按键截获。
- 加入生产代码测试：双输入源独立移除、重复 Dispose、输入源抛错自动隔离、旧 DevUI 文本输入与调色板焦点接管及 Bootstrap 退出释放。
- 旧 Remix 中 Ctrl+Z/S/Y 键位解锁选项仍归 DryCycleOptions，需在后续 Phase 2 设置 Provider 与迁移时处理；不能为了减引用硬删原配置。

### 复审时间约束

- 本次以前运行过的 787→789 全仓扫描为**阶段基线**，不是本轮最终复审。
- 进行中的前置解耦不再自动触发 Rainwright Ownership Audit。仅当 Phase 1—4 所有代码侧阻断关闭、回归测试通过后，才手动发起 Phase 5 全仓审计，逐项复审遗留问题。


### Phase 2 — 作者数据后端 Provider（本轮）

- EditorAuthoringParticipants 不引用 DryCycle 天气/温度，实现 scoped 注册、多个独立 Dirty、Save、Recovery，提供异常诊断；不存在注册者时通用 Map 保存逻辑可以运行。
- EditorActions.SaveMapWorkspace 在通用 World/RoomAttr/Topology 保存后调用 SaveDirty，尝试全部脏 Provider，任何保存失败或保存后仍脏都会令整体保存失败；不清除其他 Provider 的 Dirty。
- AutoSaveCoordinator 的 RegionMap dirty 与 Recovery 通过同一 Provider 获取，保持原始恢复快照 suffix TemperatureSets、WeatherSpatial。
- DryCycle 专属 Provider 由 DevToolLegacyBootstrap 过渡注册，在 Disable/启动失败时注销；未来替换成 DryCycle 可选扩展注册入口。
- 直接链接生产 EditorAuthoringParticipants 到 DevToolAuthoring.Tests，覆盖两个 Provider、重复 ID、保存失败继续执行、恢复路径、抛错隔离及实际 Bootstrap 注册/注销。
- 尚未完成：天气/温度 World UI 的 ImGui 展示抽离、独立自动保存配置、作者目标路径权限/身份、特殊地形贡献与插件生命周期。不要将后端 Provider 改造视为整个 Phase 2 已完成。


### Phase 2 — AutoSave 配置独立读取边界（本轮）

- 新增 EditorAutoSavePreferences：通用编辑器默认开启自动保存、150 秒间隔，可绑定一个可释放的设置提供者；运行期限制为 30—1800 秒，读取开关失败时保守禁用，避免意外写作者文件。
- AutoSaveCoordinator 不再直接读取 DryCycle.DayNight.RegionDayNightOptions；DevToolLegacyBootstrap 从旧 Remix 配置提供原值，不改变用户目前已配置的开关和间隔，关闭时释放。
- 真实源测试验证默认值、间隔限制、旧 Remix 绑定、重复注册拒绝、异常提供者、注销与反向依赖的静态 Guard。
- 此处仅去除读取硬依赖，**没有重新设计、迁移或删除现有 Remix 设置 UI/文件**。独立 Rainwright 自有设置及旧键迁移需要留在后续发布阶段完成。


### P1-C — Ctrl+Z/S/Y 设置与 EditorInputRouter 解耦（本轮）

- 新增 EditorShortcutGameplayPolicy：三个键默认保留给编辑器，支持唯一可释放设置 Owner；读取异常时宁可继续保留编辑器快捷键，也不意外触发玩家输入。
- EditorInputRouter 不再 using DryCycle.Misc 或直接调用 DryCycleOptions；旧 DryCycleOptions 数值通过 DevToolLegacyBootstrap 注册，从而保留开发者曾经保存的允许游戏输入选项。
- 加入真实生产代码测试，覆盖三个键的当前值、重复注册拒绝、异常处理、Bootstrap 退出恢复默认策略；Phase 5 之前不扫描全仓。
- 最终 Rainwright 独立安装时需要由自身配置替换旧设置 Owner，并提供一次性只读迁移，不能由这次改动直接删除原 Remix 设置。


### Phase 2 — World Map / Cartography 特殊地形贡献（本轮）

- 新建 Map/EditorMapTerrainContributors.cs：通用、无 DryCycle 地形类型的地形数据描述与类型注册，单一类型唯一拥有者、可释放租约、不可变字典快照、读取不分配、插件异常卸载与诊断。
- WorldMapGeometryRuntime 不再 using DryCycle.TerrainExt.QuicksandZone 或引用 QuicksandZoneData；调用通用 Contributor，将曲面、实体地形与材质区间渲染交给现有 AddSplineFlatBand。旧的 QuicksandBody / QuicksandMaterial 类型与颜色语义不变。
- 缓存的 GeometrySettingsFingerprint 合并 Contributor.Revision，注册/注销时可触发下次允许的实时房间重建；保留现有帧预算与所有 Draw/栅格化核心逻辑。
- DevToolLegacyBootstrap 通过专属数据适配器提供 QuicksandZone 曲面、深度、区段，初始化/退出时成对注册/释放；不安装或运行失败时通用地图不加载 DryCycle 流沙类型。
- DevToolAuthoring.Tests 链接真实生产 Contributor，覆盖缺省无注册、流沙分类/区间、生命周期、缓存版本、重复类型冲突、异常卸载隔离。
- WorldMapRoomSourceQueue 增加 ContributorRevision 校验：异步完成结果和温缓存均不复用旧地形扩展版本；WorldMapSourceRecoveryRuntime 检测旧版本时重新触发 CPU 解码，保留原有缩略图与帧预算。
- 仍需真实 Rain World 构建、运行时地图拖缩/缩略图与第三方插件实测；最终全仓复审只在所有代码侧事项完成后执行。


### Phase 2 — 专属 World 数据页与通用导航解耦（本轮）

- 保留原 WorldWorkspaceDataView 的完整天气/温度 ImGui 绘制、规则矩阵、预览与保存行为，将其作为 DryCycleWorldWorkspaceDataView 放入 RWImGui/Extensions/DryCycle。
- 在原 WorldWorkspaceDataView 位置建立通用、无天气/温度类型依赖的可选页面渲染入口。没有专属编辑器时显示明确提示，脏标志/SaveDirty 使用此前完成的 EditorAuthoringParticipants 后端，不丢未保存状态。
- 过渡期 RWImGui BridgePlugin 负责向通用入口注册专属绘制器和带日志的异常回调，关闭时释放；绘制发生异常时自动停用故障绘制器，保留其他 World Map 工作空间。
- DevToolAuthoring.Tests 直接编译通用页面入口，通过轻量 ImGui stub 测试未安装、正常注册、脏标志、重复注册拒绝、异常隔离与资源释放。
- 注意：专属绘制器当前仍编译在旧 DryCycle.DevTool.RWImGui 前端程序集，**下一阶段拆成单独 Rainwright.dll 时必须迁到 DryCycle 可选 UI 适配程序集**；本轮只是解耦通用页面的源码类型关系，不能宣称已经完成跨程序集可选安装。

### Phase 2 — 作者文件根目录约束（2026-10-11）

- 在共享 AuthoringSourcePaths 加入 TryCombineUnderModRoot：规范化相对路径并拒绝绝对路径、盘符与 `..` 跳出当前 Mod 根目录，防止编辑器错误地修改其他 Mod 或游戏本体。
- WorldAuthoringPathResolver 仅接受激活 Mod 的真实现存源文件；不再将 AssetManager 的读取结果回退当作可写作者文件。缺少真实源文件时，现有调用方会收到明确的不可保存错误，而非制造成功。
- 扩展生产代码路径反例测试，覆盖 Windows 分隔符、绝对路径、跨 Mod 邻接路径、上级路径与空值。
- 本批尚未进行完整 Rain World/net48 编译或实机验证；Phase 2 日志独立及 WorldTopology 所有权仍在后续处理。
- 仍遵守最终 Phase 5 才执行 Rainwright Ownership Audit，不提前运行全仓扫描。
 
### Phase 2 — 编辑器独立诊断起点（2026-10-11）

- 新增 DevTool/Core/EditorDiagnostics：独立 BepInEx 日志源 `Rainwright.Editor`，记录完整异常，初始化时不依赖 DryCycle.Plugin.Logger。
- WorldAuthoringPathResolver 的解析错误、回滚恢复失败、临时文件清理失败改由编辑器自身日志报告。尤其原来临时文件清理异常被静默丢弃，现在会保留异常上下文。
- 该提交仅覆盖通用 World 作者路径服务；其他模块尤其 BridgePlugin 的 DryCycle 启动诊断引用仍未解耦，Phase 2 尚未完成。
- 目前没有完整 Rain World/net48 编译验证；Guard / Regression / CODEMAP 等 CI 状态以 GitHub 结果为准。

### Phase 2 — AutoSave 与音效组路径诊断解除游戏插件日志引用（2026-10-11）

- RecoverySnapshotStore 的启动标记、捕获/清理、恢复清单与文件恢复异常统一使用 Rainwright.Editor 诊断源；保留完整错误文字，不需先初始化 DryCycle.Plugin.Logger。
- SoundGroupLibrary 的路径重置错误同样改成独立编辑器日志；只迁移日志 Owner，未改动用户现有 sound-groups 文件或位置配置。
- 这是 Phase 2 日志解耦的第二批，尚不等于其他日志/启动事务都完成。旧配置与 RecoverySnapshotStore 的持久化路径保持只读迁移前的原状。

### CI 补救 — AutoSave 生产源测试依赖（2026-10-11）

- 前一批 f28af132 的 Production regression suites 出现 CS0234：AutoSave.Tests 单独编译生产 RecoverySnapshotStore，未将新的 EditorDiagnostics 一起纳入测试程序集。
- 本次在 AutoSave.Tests 显式链接生产 EditorDiagnostics，使用 **测试专用** 最小 BepInEx 日志桩替代外部 Rain World 依赖，并添加日志源身份及完整异常文本断言；生产端不增加新依赖，也不跳过或删除任何测试。
- 修复结果以本次 CI 运行结果为准。

### Phase 2 — World 通用编辑域日志迁移（2026-10-11）

- WorldRoomAttractionRegistry、WorldTextRegistry、WorldCreatureLiveReload、WorldTopologyCommands 不再直接访问 DryCycle.Plugin.Logger，统一使用 `Rainwright.Editor` 日志源，完整错误文字保留。
- 尚未迁移 WorldTopologyRuntime/Registry 的长期游戏通行与 DryCycle-owned 作者文件身份，不将它们误判为纯 UI；这些属于后续 Owner 分层阻断项。
- 仍有 BridgePlugin 启动事务及其他 editor 模块的日志需要继续审查，本轮只检查并修改已知的 World 文件，未提前执行最终全仓审计。

### Phase 2 — WorldTopology 游戏所有者路径约束（2026-10-11）

- WorldTopologyRegistry 保持 DryCycle gameplay Owner，不随 Rainwright 独立 UI 安装位置选择 `WorldTopology.json` 的写入目标。
- 首选当前激活且 Mod ID 匹配 `NR.B5` 的真实源目录，优先读取已存在的作者文件；仅在新建文件时使用该 Mod 的首个受控源目录。
- 为早期启动保留 DLL 目录兼容回退，但必须解析沿途 `modinfo.json` 且 `id` 与 DryCycle 的 Mod ID 匹配；拒绝任何其他 Mod 的 manifest，且不再使用 `AssetManager.ResolveFilePath` 的可能合并生成路径作为写入回退。
- 路径必须经过共享 `TryCombineUnderModRoot` 检验及 mergedmods 排除；无法确认来源时 Save 返回失败并保留原 dirty 状态。玩法运行时 Hook 保持不变。
- 该项涉及 Rain World ModManager/Json 的实际环境交互，仍需要真实 net48/游戏验证；未提前执行最终全仓审计。

### Phase 2 — RWImGui 前端启动诊断独立化（2026-10-11）

- 新增 EditorStartupDiagnostics，使用 Rainwright.Editor 自有 BepInEx 日志源，而非 DryCycle.StartupDiagnostics 单例；BridgePlugin 的 Step、Marker、Failure 已全部迁移。每一阶段记录 BEGIN/OK/FAIL，失败保留完整异常对象且原样重抛。
- 编辑器日志 sink 故障不可使原本可运行的启动流程失败；新增 AutoSave.Tests 对生产日志/阶段记录代码的真实编译测试，包括正常、异常、重抛同一实例及 Marker。
- BridgePlugin 仍保留现有 Hook 和 DryCycleLifecycleEvents 订阅；此提交仅改变日志 Owner，不冒险改动其时序或第三方回调。Phase 3 前必须消除这些生命周期耦合。
- 未触发最终全仓归属扫描；完整 net48 和实机仍待验证。

### Phase 3 — RWImGui BridgePlugin 生命周期第一批（2026-10-11）

- 新增 FrontendHookLedger 作为 **前端局部安装账本**：先记录再安装 Hook，防止 HookGen 部分成功又抛错造成不受控残留；失败自动尝试反向卸载，清理失败保留租约、不得再次叠加安装。
- BridgePlugin 的 RainWorld.Start、RainWorld.OnModsInit 及过渡 DryCycle.AfterModsInit 订阅由账本管理；关闭时统一逆序释放，不再无条件对不曾安装的 Hook 作减法。
- 增加静态前端 Owner 仲裁：只允许一个 BridgePlugin 实例拥有 Hook/回调，重入 OnEnable 不重复挂载；未能清干净时保留 Owner、阻止另一实例叠加，日志明确报告故障。
- DevToolAuthoring.Tests 直接链接生产账本并覆盖重复启用、逆序卸载、部分安装失败、清理异常、重试释放；这一批只收敛 BridgePlugin，不代表已统一全部 17 个插件入口。
- WorldTopology 游戏通行 Hook 未改动；最终全仓扫描仍只能于 Phase 5 进行。

### Phase 3 — Player Map 后端部分启用回滚（2026-10-11）

- PlayerMapBackendLifecycle 改为显式的按顺序登记、逆序解除服务安装账本，覆盖 14 个后端步骤；每一步先记账后 Enable，应对 Hook 安装部分成功后抛错。
- 故障发生后立即逆序回滚并重新抛出原始异常；失败的 Disable 会记录完整异常并保留租约，阻止在残留 Hook 上重复安装，后续 Disable 可以重试。
- ResetTransientState 各阶段独立隔离异常，单一清理失败不再阻止其他缓存、任务和队列清空；Workspace 真正关闭前仍先完成临时状态重置。
- 新增独立生产源码 PlayerMapLifecycle.Tests 并加入 Regression CI，验证完整安装/逆序清理、失败注入、重复启用、卸载重试、保留异常。
- 此阶段只修复 Player Map 的后端 owner，尚未处理辅助 BepInPlugin 对同一静态组件的跨插件并发启停，也没有宣称 Phase 3 完成或进行最终全仓扫描。

### Phase 3 — Player Map 兼容 BepInPlugin 避免抢夺后端所有权（2026-10-11）

- 保留 PlayerMapRuntimePlugin 的 BepInEx 入口与原有早期兼容启用能力；当 PlayerMapBackendLifecycle 已启用、正在启用或仍有未释放的服务时，辅助入口不得重复 Enable/Disable 同一 Workspace、调度器或缓存。
- 中途失败且后端尚有待清理 Hook 的场景同样被保护；恢复成功后才允许兼容入口自行提供旧式 fallback。
- 独立 PlayerMapLifecycle.Tests 现在链接**真实** PlayerMapRuntimePlugin，并通过反射调用其实际 OnEnable/OnDisable，测试仅兼容入口、集中式后端活动、失败清理待重试三种模式。
- 仅解决 PlayerMapRuntimePlugin 此一争用点；其余辅助 BepInPlugin 仍需逐项确认真实有效 Hook Owner。本批不进行最终全仓扫描。

### Phase 2/4 — Rainwright UI 布局配置只读迁移（2026-10-11）

- DevToolUserSettingsStore 的**写入 Owner**正式改为 BepInEx/config/Rainwright.UI.xml，独立的 RainwrightUi 文档根；旧 BepInEx/config/DryCycle.DevTool.UI.xml 仅在新文件完全不存在时读取。
- 原有 Window 坐标、Groups、声音/浏览器 splitter、字体/语言的版本迁移语义沿用生产代码；成功读取旧文件后由原有原子保存与 .bak 路径写到新文件，绝不更名、写回或删除 DryCycle 原件。
- 若新文件存在（即使损坏），仍只读该文件并报告完整异常，绝不静默回退旧值；错误旧 XML 不生成半成品。多个 Mod 并装时旧 DryCycle UI 不再与 Rainwright 写同一设置文件。
- 扩充生产 UiSettingsTests，覆盖字节级旧数据未变化、迁移后的优先级、合法导入、损坏新文件不回退、损坏旧文件不创建目的文件，以及既有布局备份回归。此步是 UI 布局迁移，不代表 SoundGroup/Recovery 的路径已经迁完。
- 编译/实机和最终全仓归属复审仍待后续阶段。

### Phase 2/3 — RWImGui 前端摆脱 DryCycle 私有 ModsInit 事件（2026-10-11）

- BridgePlugin 删除对 DryCycleLifecycleEvents.AfterModsInit 的附加订阅和静态类型引用；启动回调仅由原版 On.RainWorld.OnModsInit 完成初始授权，Hook 仍由已测试的 FrontendHookLedger 成对登记/释放。
- 对 BepInEx 热启用（原版 OnModsInit 已发生）增加轻量补偿：仅当前端还未被允许注册、且确有活跃 DevTool session 时打开原来的回调重试门。未运行编辑器时不会早期接触 RWImGUI 原生上下文。
- DryCycle 游戏启动、玩法 Hook 未动；这一批解除单一事件硬依赖，不意味着整份 BridgePlugin 或 UI 扩展已脱离 DryCycle.dll；需 Rainwright-only 实机测试启动时序。
- 继续遵守最终 Phase 5 才执行完整全仓审计。

### Phase 2/4 — 崩溃恢复数据单独归属和只读旧快照导入（2026-10-11）

- RecoverySnapshotStore 的新快照、active.lock、清理/裁剪仅使用 BepInEx/config/Rainwright/AutoSave；DryCycle/AutoSave 不再作为 Rainwright 写入目标。
- 首次 Rainwright 启动时，若旧 DryCycle/AutoSave/active.lock 存在且尚无迁移完成标记，按原有 SHA-256 与作者源校验读取有效旧候选。复制到 Rainwright 目录时先完整校验数据、最后发布 manifest；任何阶段失败只清理自己的临时副本，不触碰旧锁、旧快照与作者源；仅在成功完成候选处理后记录一次性迁移标记。
- 旧崩溃快照恢复必须由开发者明确触发。恢复成功后仍暂停继续写入等待重载，并且新作者备份/临时文件使用 .Rainwright-before-recovery.bak/.Rainwright-recovery.tmp；既有 .DryCycle-before-recovery.bak 永不覆盖。
- AutoSave.Tests 真实编译 RecoverySnapshotStore 并新增旧崩溃导入、只读原件/锁、显式恢复、无崩溃标记不导入、损坏 SHA 不导入、重复正常开启不重复提示及新旧备份共存等测试。
- 仍未完成世界编辑器专属扩展程序集隔离、SoundGroup 目录迁移、Rainwright.dll 构建以及最终 Phase 5 全仓复审。

### Phase 2/4 — Sound Group 配置归 Rainwright 并只读导入（2026-10-11）

- SoundGroupLibrary.DefaultLocalDirectory 从 BepInEx/config/DryCycle/DevTool 改为 BepInEx/config/Rainwright/SoundGroups，新的 sound-groups.location.txt 只在 Rainwright 路径写入/删除。
- 新增独立 SoundGroupConfigPaths：首次新配置不存在时，从旧目录或旧 location.txt 指向的自定义目录**只读**查找已有 sound-groups.xml，限制最大 8MB、拒绝 DTD、检查 SoundGroups 根并比较完整文件哈希；校验后原字节复制到 Rainwright 自有路径再发布迁移标记，不把新目标目录指回旧外部目录。
- 已存在的 Rainwright 群组、开发者设定的新 location.txt 和显式 Reset 优先级最高，禁止旧文件重置或意外覆盖新内容；旧自定义目录失效只报告诊断，不擅自回退导入另一份可能过时的旧文件。
- DevToolAuthoring.Tests 链接生产路径/迁移代码并测试正常导入、第三方 XML 保留、旧文件保持不变、自定义外部目录只读迁移、新配置优先级、Reset 不重导、DTD 非法 XML 安全拒绝。
- 注意：迁移仅涉及编辑器的**本地组配置**；第三方 Mod 自己声明的组资源继续按照原有 ActiveMods 发现逻辑读取，不更改作者 Mod 数据。独立程序集和最终全仓复审仍待后续。

### Phase 3 — Player Map 7 个辅助 BepInPlugin 兼容租约（2026-10-11）

- PlayerMapBackendLifecycle 的服务账本增加单服务 OwnsService 和单服务 generation（仅启用时更新，无逐帧审计）；能识别成功安装、部分启用失败以及清理完成后的“后端曾经接管”事实。
- 7 个 Player Map 辅助 BepInPlugin（ConfigBuild、DerivedLayout、GroupCommands、IncrementalRender、RenderOutputValidator、RenderRevision、TerrainBake）统一使用 PlayerMapAuxiliaryCompatibilityLease。辅助插件只在对应后端服务没有 Owner 时提供旧式 fallback，重复 OnEnable 不叠加，失效回滚原样抛出，OnDisable 不再卸载被集中式后端当前或曾经接管的服务。
- 测试直接链接生产 BackendLifecycle 与 CompatibilityLease，覆盖旧式独立启动、重复启用、中央中途接管、中央先关闭再收到辅助 OnDisable、后台活跃时辅助 OnEnable、故障注入、清理失败保留租约。
- 没有提前删除 BepInPlugin/旧 GUID；尚未完成所有辅助插件、WorldTopology 永续玩法和专属扩展程序集的最终 Owner 迁移。全仓 Phase 5 扫描仍未触发。

### Phase 2 — 专属 World UI 从通用 RWImGui 前端分离（2026-10-11）

- 将 DryCycleWorldWorkspaceDataView.cs 从 DryCycle.DevTool.RWImGui.dll 的 Compile 清单移除，移除 BridgePlugin 对该类的直接引用、注册和清理字段；WorldWorkspaceDataView 通用 scoped API 及无 Provider 时的空状态/安全保存语义保留。
- 新增 **DryCycle.WorldData.RWImGui.dll**（BepInEx 插件 GUID DryCycle.Rainwright.WorldData），只在 DryCycle 主插件和 RWImGui 前端均加载后注册天气/温度专属展示委托；禁用时只释放自己持有的租约，不卸载第三方 World 数据 Provider。
- DryCycle.dll 与现有 RWImGui.dll 分别为适配程序集声明 InternalsVisibleTo，适配 DLL 只引用现有两端和游戏已安装的 ImGui.NET，不向通用端反向添加对天气/温度的程序集引用或额外运行时依赖。
- 生产编译依赖顺序改为 DryCycle → DevTool RWImGui → DryCycle World Data adapter；打包部署、UpToDateCheck、旧 DLL 清理均包含第三个输出，缺失 DLL 导致父项目构建失败而非假成功。
- DevToolAuthoring.Tests 链接真实可选插件入口源码，模拟注册重复、两个实例冲突、禁用非持有者、注册重新获得、Dirty 与渲染代理。
- **重要：当前通用 DevTool 后端仍位于 DryCycle.dll；Rainwright-only 真实独立编译尚未完成。** 这个提交移除了前端对专属天气/温度 UI 的编译引用，但并不宣称完成最终 Rainwright.dll 或通过 net48/游戏实测。Phase 5 全仓复审按约定暂不执行。

### Phase 3 — World 辅助插件局部检查记录（2026-10-11）

- 确认 WorldCreatureCatalogRuntimePlugin 有独立 Update/MainThread 目录泵，BridgePlugin 只在 BepInEx 未发现该插件时启用 fallback；此逻辑不能在统一入口前直接退役，否则部分环境会失去生物目录。
- 查明 WorldCreatureSpawnInspectorPlugin 除启用 WorldCreatureSpawnInspector 外，还单独负责 WorldTimelineSlugcatIconCatalog 的 Initialize/Pump/Shutdown；BridgePlugin 只管理前者，因此不能直接取消整个辅助插件入口，否则会丢失时间线蛞蝓猫图标缓存及持续刷新。
- 查明 WorldLineageInspectorPlugin 与 BridgePlugin 都对 WorldLineageInspector 调用 Enable/Disable，仍有双 Owner 的启停风险；后续需把编辑器前端租约与插件回退租约分开测试。
- WorldSubregionSelectorPlugin 自己拥有子区域选择器，BridgePlugin 并未直接安装该服务，不应随其他辅助入口机械删除。
- 此次为局部代码审阅结论，没有把这些辅助插件的实际 Owner 修复标记为完成；Phase 5 全仓审计尚未执行。

### Phase 3 — World 生成、谱系及时间线图标单一前端 Owner（2026-10-11）

- WorldCreatureSpawnInspectorPlugin、WorldLineageInspectorPlugin 保留旧 BepInPlugin GUID 以维持加载兼容，但移除独立 Enable/Disable 和 Update，不再与 BridgePlugin 争抢编辑状态或图集缓存。
- BridgePlugin 独立负责两个 Inspector 的原有启停与 WorldTimelineSlugcatIconCatalog.Initialize/Shutdown；只在 Map 视图处于新 UI 活跃期时从 Unity 主线程 PumpMainThread，沿用 ≤3 次每帧生成预算，不从 DX11 ImGui callback 加载图集。
- 图标生成异常记录完整错误并禁用该期的图标刷新，不影响其余 World 编辑器、生物目录或者地图工作。即使 BepInEx 辅助类没有被发现，主前端仍能渲染时间线图标。
- 扩充局部 Guard 检测实际源码的单一 Owner；专用生物目录的独立 main-thread catalog pump 未修改。Phase 5 全仓复扫仍未执行。

### Phase 2/3 — 编辑器后端 Hook Owner 从 DryCycle 特殊 Bootstrap 中抽离（2026-10-11）

- 新增 Core/EditorBackendLifecycle：编辑器自己维护 LegacyDevUiQuiescenceController、NativeSoundTriggerDevUiScheduler、DevToolRuntime、PlayerMapBackendLifecycle 的原子步骤账本，Core 在 RainWorld 进程初始化前调用，Extras 延迟到 OnModsInit，失败保留完整 EditorStartupDiagnostics，逆序卸载，未成功解除的服务保留后续重试，杜绝旧过渡层无条件两次移除 Hook。
- DryCycle.Misc.DevToolLegacyBootstrap 只注册并释放 6 种 DryCycle-only 兼容数据/输入/配置 Provider（包括流沙、温度、天气、Remix、旧 DevUI 控件），通用后端核心/Map 服务直接委托 EditorBackendLifecycle；异常导致核心失败时只退役自身 Provider，不再次暴力清除独立 Hook。
- 原生产 LegacyBootstrapTests 现同时链接新的 EditorBackendLifecycle，并继续测试两阶段单次启用、Enable 故障回滚、PlayerMap 非致命故障、强制 Cleanup 故障不中断后续服务、保留待重试账本与注册注销。
- 这是把公共后端生命周期的源码 Owner 搬入 Editor/Core，而非直接创建 Rainwright.dll；DryCycle.Plugin 在过渡阶段仍显式调用 MiscRuntime 三个桥方法，必须在真正独立插件入口稳定后才能完全退役旧桥。

### Phase 3 — World 子区域选择器归属前端 Owner（2026-10-11）

- WorldSubregionSelectorPlugin 保留原 GUID 和 BridgePlugin BepInDependency，但不再对 WorldSubregionSelector 做单独 Enable/Disable；避免子插件延迟卸载清理正在编辑的子区域状态。
- BridgePlugin 成为子区域选择器的单一启停 Owner：可选初始化异常日志完整保留，关闭时执行独立清理；即使 BepInEx 只发现了 BridgePlugin，也能使用可视化子区域选项而非降级到文字框。
- Guard 局部检测真实 frontend/helper 的职责关系。仍未执行 Phase 5 全仓审计。

### Phase 3 — 生物目录缓存/主线程泵唯一前端 Owner（2026-10-11）

- 退役 WorldCreatureCatalogRuntimePlugin 的 OnEnable/Update/OnDisable 调度实现，保留 BepInPlugin GUID 作为过渡 BepInEx 依赖兼容入口；删除 BridgePlugin 对 Chainloader.PluginInfos 的附属检测与二号后备泵方案。
- BridgePlugin 在新 UI 已就绪、编辑器会话活跃时于主线程按需初始化并 Pump 生物目录，避免加载阶段固定启动完整缓存 IO，也避免未发现辅助 BepInPlugin 时完全不构建目录；除 UI 使用期间外不消耗主线程目录泵。
- 部分初始化抛错时尝试释放刚创建的资源并记录原始完整堆栈，防止幽灵目录 Owner。关闭 frontend 的持久缓存 flush 不再 silent catch，而是记录包含异常的错误消息。
- 局部 Guard 禁止旧 helper 重新引入第二条后台泵或依赖 BepInEx 发现来选择 Owner。尚需 net48/实机下比较大型生物列表首次打开的 IO 时间与帧稳定性。

### Phase 3 — World 生物族谱保存服务变为核心必需项（2026-10-11）

- WorldCreatureAuthoringRuntimePlugin 保留已有 BepInPlugin GUID，但独立 OnEnable/OnDisable 退役，避免单独卸载后让编辑器正在使用的 WorldLineageRegistry 保存失效。
- EditorBackendLifecycle 在核心 DevToolRuntime 成功启动后安装 WorldCreatureAuthoringHooks 作为 **Required** 步骤；安装部分失败时核心启动按原样失败并逆序回滚，不假装作者保存服务可用。
- WorldCreatureAuthoringHooks 使用 Rainwright.Editor 独立日志源，不再要求 DryCycle.Plugin.Logger 在调用时就绪。SaveAdditional 遇到“族谱脏数据但 authoring service 未运行”明确返回 false 并记录日志，阻止用户数据静默未保存。
- LegacyBootstrapTests 链接并运行真正生产 EditorBackendLifecycle，验证新的族谱服务恰好启动一次、逆序停用且未启动服务不被误卸载；局部 Guard 保护作者保存必需服务归属和拒绝假成功语义。
- WorldTopology 永久 Shortcut gameplay Hook 尚未拆入最终 DryCycle-only 所有者；Phase 5 全仓复扫仍留到前置解耦和迁移工作完成后。

### Phase 3 — 族谱脏数据不可被未启用服务隐藏（2026-10-11）

- 复查 WorldTextRegistry.Dirty 对 WorldCreatureAuthoringHooks.AdditionalDirty 的调用，发现旧逻辑把「服务 enabled」错误当成「数据有无修改」前提，可能导致服务不可用时隐藏未保存修改。现额外脏状态始终返回 WorldLineageRegistry.Dirty，不依赖启动 Owner。
- 当保存服务未启动，UI 仍显示世界作者数据存在待保存修改，真正执行 SaveAdditional 时返回 false 并记录完整诊断；不能静默写入成功，也不能让 Clear 丢弃脏数据。局部 Guard 加入这个真实读链路的回归约束。

### Phase 3/4 — 独立生产族谱作者服务回归（2026-10-11）

- 从 WorldCreatureLiveReload.cs 提取 WorldCreatureAuthoringHooks 到独立生产 World/WorldCreatureAuthoringHooks.cs。现在保存/脏数据策略可以在不编译生物实时运行时、BepInPlugin UI 和其他业务源码的测试环境中直接编译。
- 新建 tests/WorldCreatureAuthoring.Tests，使用真实生产 Hooks 文件和最小 Rain World 类型桩，验证空脏状态、离线服务拒绝假成功、写失败保持 Dirty、文件落盘后 Reload 失败、正常保存并清脏、无区域时跳过重读、房间号寻找、预览活动与停用边界。
- Regression Tests 工作流纳入该生产测试，局部 Guard 已把检查对象指向拆出的真源文件；UI 与地图业务逻辑未重写。真实 net48 编译仍受本机 Rain World 引用的限制。

### Phase 3/4 — 族谱已落盘但 world.txt 重载失败时保持待同步并支持重试（2026-10-11）

- WorldCreatureAuthoringHooks 使用 pendingReloadRegion 区分“LINEAGE 尚未成功写入”与“LINEAGE 写入成功、WorldDocument 尚未重新加载”。后一状态仍使 WorldTextRegistry.Dirty 为 true，避免下一次保存和 UI 误报完全成功。
- 再次保存只尝试 WorldTextRegistry.Reload，不重复写入已经落盘的 LINEAGE 行；成功后清空 pending 状态，失败继续保留，并输出含 region 和待重试说明的日志。
- WorldTextRegistry.Reload 拒绝真实未保存 document/lineage 脏数据，但允许仅由 pendingReloadRegion 引起的“待同步”状态调用，避免 Reload 因自己的 Dirty 标记自锁。
- 生产 WorldCreatureAuthoring.Tests 扩展“写入成功→第一次重载失败→脏状态持续→仅重试重载→成功清除”链路，Guard 固化 Reload 脏状态区分。实机磁盘故障注入仍待 Phase 4。

### Phase 3/4 — 永久游戏 WorldTopology Hook 安装账本与泄漏重试（2026-10-11）

- 新增 World/WorldTopologyHookLedger，独立于编辑器前端 / DevTool backend；DryCycle 游戏 WorldTopologyRuntime 的四条 Shortcut/Creature Hook 和 WorldConnectionSyntax 都在外部 add 之前记账，失败逆序解除，失败解除保留原完整异常和待清理注册，不再无条件 -= 未安装事件。
- WorldTopologyRuntime.Enable：若上一次卸载有悬挂 Hook，先尝试清理；仍失败则阻止二次安装，防止同一个 ShortcutHandler 回调重复执行。OnDisable 失败时保留 pendingRoutes 及 topologyRevision，防止仍活跃的捷径中的生物丢失精确目标位置数据，后续 Disable 再尝试释放。
- WorldConnectionSyntax 改为只在成功 -= WorldLoader.MappingRooms 后才清 enabled，保证外层 ledger 重试时不会因为内部布尔值已先置 false 而假装卸载成功。
- 新增 tests/WorldTopologyLifecycle.Tests，直接编译真实生产 HookLedger 源码，验证安装逆序/幂等、部分 HookGen add 失败、原异常保留、清理失败保留、重复安装阻断、诊断 sink 抛错时仍可恢复；CI 工作流及局部 Guard 更新。
- WorldTopologyRuntime 及其真实 Shortcut gameplay 行为**必须继续由 DryCycle 游戏方拥有**，不应被 Rainwright UI 关闭时卸载。本批未改路线查找和 world.txt 编码，真实游戏捷径通行仍需 Phase 4 实机测试。

### Phase 3 — 世界捷径修复的诊断所有者归永久游戏生命周期（2026-10-11）

- WorldTopologyLiveTraversalFixPlugin 保留原 BepInEx GUID，但其 OnEnable/OnDisable 不再竞争日志源与游戏通行状态；WorldTopologyRuntime 的持久 Hook 账本正式拥有该 helper 的 Enable/Disable，不会受 Rainwright UI 生命周期影响。
- 连接热修复和捷径预通行检查即使辅助 BepInPlugin 未发现，也会回退到 DryCycle 自有游戏日志源。异常日志从仅 .Message 改为完整 Exception.ToString，避免插件未加载或失败时静默吞掉真实世界捷径问题。
- WorldTopology HookLedger 的 6 个步骤按注册逆序释放，WorldConnectionSyntax 仍唯一拥有其 HookGen 事件；Phase 5 全仓复审未提前触发。
 
### 当前阶段尚未完成的代码与验收阻断（2026-10-11）

- **核心程序集真正独立**：EditorBackendLifecycle 已搬入编辑器 Core，但核心类型仍在 DryCycle.dll，由 DryCycle.Plugin 经 MiscRuntime 过渡激活。还未创建可完全独立加载的 Rainwright.dll，暂不能声称“卸载 DryCycle 后编辑器独立可用”。
- **DryCycle 专属 Provider 独立注入**：旧 DevToolLegacyBootstrap 仍承载 6 类专属输入/地形/Remix/作者数据适配；Rainwright.dll 迁出后需以可选 DryCycle 适配器注册，不许保留相反程序集依赖。
- **游戏 WorldTopology 区分**：永久 ShortcutHook 留在 DryCycle 游戏侧；NR.B5 专属 WorldTopology.json 不可被错误当成任意 Mod 的 world.txt 作者目标。第三方 Mod 正确所有权、兼容保存和路径失败提示仍要收口。
- **用户旧 Remix 设置**：AutoSave 间隔及 Ctrl+Z/S/Y 游戏快捷键策略仍从 DryCycle 的旧配置提供；独立 Rainwright 的本地设置 UI、与旧值的明确迁移优先级还未独立完成。
- **平台验收**：CI 目前是真实生产源文件的 net10 故障回归和 C# 语法/构建结构检测；无法代替带 Rain World、BepInEx、POM、RWImGui、RegionKit 真正依赖的 net48 全量 DLL 编译及游戏主线程/GPU/字体实机验证。没有这些证据时不得标记发行完成。
- **Phase 5 后置**：完成所有前置代码边界和回归后才执行全仓归属审计；不得因本轮局部 Guard 全绿就宣布最终程序集无依赖。

### Phase 3 — ModsInit 额外服务失败恢复（2026-10-11）

- 修正 DevToolLegacyBootstrap.EnableDevToolExtras 的状态顺序：核心 BackendLifecycle 若尚未成功，不能预先锁死 _devToolExtrasAttempted，必须记录 DEFERRED 并允许下次重新进入。
- 生产回归新增核心首次故障 → extras 不启动、不预热 → 故障解除再次 ModsInit → 核心和 PlayerMap extras 各仅启动一次 → 清理恢复的真实源码路径验证。
- 这是对当前最新 main 的增量修复；World 四个辅助插件已由其他进展交给 BridgePlugin，避免重复重写。全仓 Phase 5 扫描未执行。

### Phase 3 — Player Map extras 失败重试而非锁死（2026-10-11）

- EditorBackendLifecycle 将“地图安装成功”与“RoomSettings 静态目录预热已尝试”拆开；地图安装失败、清理成功后允许下次 ModsInit 重新安装，预热只运行一次。
- 当 HookLedger 仍保留 PlayerMapBackendLifecycle 的失败清理动作时，重复 extras 只报阻断诊断、不再叠加 Hook；不隐藏未释放服务。
- DevToolLegacyBootstrap 只有在 EditorBackendLifecycle.ExtrasReady 确认成功后才锁定额外服务已完成状态；覆盖核心失败恢复和可选地图安装失败恢复两种独立路径。
- LegacyBootstrapTests 链接生产状态机，新增故障注入→恢复→再次请求、Map 真实调用次数、静态目录预热无重复的断言。
- 本次局部回归不是 Phase 5 全仓扫描；真正 Rainwright.dll net48 构建与实机验证仍须执行。

### Phase 4 — HookGen 双重故障与编辑器重启的真实生产代码测试（2026-10-11）

- WorldTopologyLifecycle.Tests 补充「部分 add 成功后抛错，紧接 rollback remove 也失败」：完整原始异常不被掩盖，释放失败的 Hook 保留可重试 owner，后续 Disable 可收口。
- WorldCreatureAuthoring.Tests 补充「LINEAGE 已落盘→world.txt reload 失败→编辑器停用→离线保存被拒绝→再次启用后只重试 Reload」：不能假成功、不会重复写入作者文件。
- 两项测试均直接编译实际生产源。未触发 Phase 5 全仓扫描，真正 net48/实机仍待验证。

### Phase 3 — Creature Catalog 目录缓存/主线程任务的部分启动清理（2026-10-11）

- BridgePlugin 的 WorldCreatureCatalogPicker.Initialize/Shutdown 已收口为专有 CreatureCatalogRuntimeLease；调用 Initialize **之前**登记清理义务，成功初始化、部分初始化抛错及原始异常传播分开处理。
- 部分启动后 Shutdown 再次失败，不会清空所有权或在下次重新初始化时叠加原来的缓存/主线程任务；OnDisable 会继续尝试释放，失败记录原始异常；清理成功后才允许重新启动。
- DevToolAuthoring.Tests 直接编译真实租约类，注入部分初始化失败、清理失败、重复 Enable/Disable、继续重试和保留异常身份的回归。
- 原本已经统一到 BridgePlugin 的独立 Catalog helper GUID 保留兼容，不引入新的逐帧 Hook 或重建目录算法。完整 Rainwright.dll 与实机仍未验收。

### Phase 2 — World 连接语法日志移出 DryCycle.Plugin（2026-10-11）

- WorldConnectionSyntax 两处失败日志（实时节点不存在、已同步拓扑但快捷管道渲染刷新抛错）不再静态引用 DryCycle.Plugin.Logger；改由 editor-owned EditorDiagnostics.Warning 输出。
- 原解析、Exact Endpoint 映射、连接同步、原版快捷图层重建触发时机均保持不变。异常文本改为完整 ToString，并保留 Error 对象供编辑器日志读取，不再只输出 Message。
- Guard 的 Phase 2 局部契约保护这条通用连接语法代码不得重新依赖游戏主插件类；只检验定向模块，不运行最终全仓审计。

### Phase 2 — 离线地形解码移除 QuicksandZoneData 硬引用（2026-10-11）

- MapTerrainSourceDecoder 的曲面地形与 AuthoringSource Worker 现在统一使用 EditorMapTerrainContributors.TryPopulateDetachedData；取消直接 new DryCycle.TerrainExt.QuicksandZone.QuicksandZoneData，未知专属地形不假装有可用原版几何。
- 现有带可释放租约的 Terrain Provider 增加**可选** detached PlacedObject.Data 工厂（旧 Register 签名/三个参数调用保持兼容）；DryCycle 以 createDetachedData 提供自己流沙数据构造函数，支持后台解析其 FromString 参数和已有几何材质分段，不依赖游戏 PlacedObject Hook 是否在 worker 已挂载。
- Provider 解析支持大小写不敏感的作者类型名；失败时注销单个违规 Provider、推进缓存 Revision、报告包含原始 InnerException 的错误；不改原版 TerrainHandle、LocalTerrain、CurvedSlope、SuperSlope 的数据路径。
- DevToolAuthoring.Tests 链接生产 MapTerrainSourceDecoder 以及 Terrain Provider，新增工厂实例归属、曲面与流沙作者数据、独立运行无 DryCycle Provider、不存在类型自动跳过、错误隔离测试。Guard 阻止通用离线解码器重新导入 DryCycle 专属类型。
- 此轮仅移除专有数据类型引用，独立 Rainwright.dll 编译和 GPU 图像实测尚未完成；最终全仓 Phase 5 审计仍未启动。

### Phase 2 — World Map 后端日志与 DryCycle.Plugin 解耦（2026-10-11）

- MapViewPersistentCache、WorldMapGeometryPersistentRuntime、WorldMapGeometryRuntime、WorldMapRoomSource、WorldMapSourceRecoveryRuntime 共五个通用地图后端模块已移除对 DryCycle.Plugin.Logger 的直接静态引用。
- 缓存加载/存盘/Flush、缩略图后台解码异常、退役任务失败和恢复统计保留原 Info/Warning/Error/Debug 日志级别，由 EditorDiagnostics 的 BepInEx 源统一记录。
- 小地图失败仍用 Debug，绝不升级成每帧 Info 噪音；原算法、异步负载、栅格与缓存指纹不变。
- 局部 Guard 检查这五个模块不再反向依赖游戏日志；最终 Phase 5 全仓扫描未执行。

### Phase 2/4 — 自动保存与 Relationships 导出归 Rainwright（2026-10-11）

- AutoSaveCoordinator 的自动保存结果、合并缓存拒绝、可选作者数据恢复异常和崩溃快照异常全部改为 EditorDiagnostics 的 Rainwright.Editor 日志源；保留完整异常对象及具体文档位置，不再读取 DryCycle.Plugin.Logger。
- RelationshipSaveAdapter 的源代码导出路径改为 BepInEx/config/Rainwright/Relationships/relationship-overrides.txt；导出结果/异常也不依赖 DryCycle 的游戏日志。若 TryGetText 无法获得文本，返回明确失败而不是假成功。
- 新增 RelationshipExportPaths：首次导出可只读复制原 BepInEx/config/DryCycle/Relationships/relationship-overrides.txt 至 Rainwright 路径；后续 Rainwright 文件优先；旧文件永不移动/重命名/删除/覆盖。首次导出时当前文本替换迁移副本后保存备份 .bak。
- 新建 tests/RelationshipExportPaths.Tests，直接链接生产路径迁移逻辑，覆盖空安装、只读旧文件导入、新文件优先、备份与旧文件原样保留；纳入 CI。新增局部 Guard 禁止两个通用保存模块再使用 DryCycle.Plugin.Logger。
- 未创建 Rainwright.dll，也未跑最终 Phase 5 全仓复审；完整 net48 与游戏磁盘路径/文件锁实测待后续。

### Phase 2/4 — Relationships 真实导出端到端故障回归（2026-10-11）

- RelationshipExportPaths.Tests 除迁移路径源码外，现在也直接编译真实 RelationshipSaveAdapter，使用受控 RelationshipPage 和 BepInEx Paths 桩，验证生物关系排序、真正 Export 后 Rainwright 目标内容/备份/旧 DryCycle 源未动、明确清空后不改旧源。
- 向 Paths.ConfigPath 注入不可作为目录的现存文件，验证 Export 必须返回 false 并记录完整 IOException/UnauthorizedAccessException，而非只显示“已保存”。原文件及 .bak 的防丢行为以真实导出方法验证，未改变游戏生物关系玩法 Hook。

### P1-C/Phase 2 — 旧 DevUI 控件程序集的可释放归属（2026-10-11）

- LegacyDevUiQuiescenceController 不再引用 `DryCycle.Plugin` 程序集或游戏 Logger；延期页面刷新失败统一写入 EditorDiagnostics 并保留完整异常。
- 新增 LegacyDevUiOwnedAssemblies 轻量登记表：编辑器自身和原版程序集天然归属，其余只有受信任的 DryCycle 过渡启动层明确 Register 的程序集才被归类为已被重建 UI 替代的休眠控件；未知第三方程序集仍保留完整原版 Update 路径。
- DryCycle 通过 DevToolLegacyBootstrap 登记 `typeof(DryCycle.Plugin).Assembly`，在清理、故障回滚时解除租约；后续 Rainwright.dll 独立打包时无需反向依赖 DryCycle 插件类，Both 模式下的本体扩展控件仍得到原先的休眠行为。
- 新增直接链接生产 Registry 的测试，覆盖启动/关闭、异常路径、未知程序集、重复登记拒绝、单独 lease Dispose；定向 Guard 防止旧程序集反向依赖回归。没有执行 Phase 5 全仓扫描。

### Phase 2/4 — 独立 Rainwright AutoSave 配置与旧 Remix 只读优先级（2026-10-11）

- 新增 AutoSave/RainwrightAutoSaveSettingsStore：自有文件 BepInEx/config/Rainwright.AutoSave.xml，含开关及 30–1800 秒间隔，禁止 DTD/过大 XML；写入成功才改变实际运行策略，替换原文件时生成 .bak。
- EditorAutoSavePreferences 设置优先级为：**Rainwright 已保存的自有设置 > DryCycle 旧 Remix（只读动态 Provider）> 默认开启/150 秒**。第一次未设置时原用户旧值保持原样，未擅自写入旧配置，也不在 Mod OnEnable 早期读取未加载的 Remix 默认值作永久迁移。
- 新文件存在但损坏时禁用自动保存、保留原文件并报告完整错误，避免从另一 Mod 的旧值意外恢复后台作者文件写入；用户明确 Apply 才重写新文件。EditorBackendLifecycle 在安装核心 Hook 前加载该设置，不依赖 DryCycle 插件前端。
- DevToolAuthoring.Tests 真实编译 Store/Preferences；新增首启回退、旧值保留、自有文件权威、Mod 卸载、重启持久化、备份、恶意 XML、拒绝错误间隔、明确修复的生产回归。
- 配置编辑 UI 的下一提交独立接入 Rainwright 前端。本阶段不删除 DryCycle 旧 Remix 项目，不执行 Phase 5 全仓扫描。

### Phase 2 — 独立 AutoSave 设置 UI 接入（2026-10-11）

- 新增 RWImGui/Shell/Windows/AutoSaveSettingsWindow.cs：控制中心的「AutoSave → Settings」打开独立可移动设置窗口，不长期占用场景空间；可编辑自动保存启用状态与 30–1800 秒间隔。
- UI 显示当前配置是 Rainwright 自有文件还是旧 Remix/默认值只读回退，点击「应用并保存」才落盘并激活新值，点击「取消修改」恢复运行值，失败显示原始错误文本且不假装成功。绘制循环没有文件 IO、反射或新全局 Hook。
- 已添加局部 Guard 约束设置来源优先级、DTD 拒绝/禁用自动写入、Rainwright UI 操作入口。此 UI 在真正 RWImGUI/net48 游戏环境中的字体、尺寸、输入抢占待后续实机验证。

### Phase 3/4 — AutoSave 新设置按主线程排队写入，禁止 RWImGUI 渲染 IO（2026-10-11）

- 找出初版设置 UI 在 ImGui callback 里直接 TrySaveOwned（文件 IO）的性能/线程错误，修为 TryQueueOwned 的受锁无 IO 请求队列；待处理状态及上一次真实结果/错误通过单调递增版本号传到窗口，不提前显示保存成功。
- AutoSaveCoordinator.Tick 在稳定帧定时门之前由 Unity 主线程 ProcessPendingOwnedWrites；设置窗口刷新只读状态，不接触磁盘。重复点击会明确拒绝重复入队，处理完成后才能接收新写入。
- 真正写入失败会在 Rainwright.Editor 记录完整异常，并反馈到 UI。DevToolAuthoring.Tests 扩充真实生产设置源码回归：未落盘前新值不能生效、重复请求不覆盖、主线程 Pump 后生效且版本递增。Guard 防止任何人未来把文件写回 ImGui 渲染函数。

### Phase 3/4 — 设置保存跨会话及配置根目录变化的收口（2026-10-11）

- AutoSaveCoordinator.Tick 在 DevToolSessionLive gate 前处理配置写请求，减少暂停/进出房间后等待主线程调度才写配置的问题；Shutdown 在主线程做最后一次冲刷，不把未写完的 GUI 请求带到下次会话。
- RainwrightAutoSaveSettingsStore.Load 检测 configRoot 在排队期间变化时拒绝把旧路径上的设置意外写到新 Mod/新账户目录，撤销旧请求，完成版本号加一、失败原因可见；同一路径重复 Load 不会擅自清理用户尚未处理的请求。
- 真正生产源码回归覆盖旧路径排队→切换新根目录→禁止重定向写入→回读旧文件仍正常；Guard 要求 Tick 与 Shutdown 双入口都有主线程冲刷。

### Phase 3/4 — AutoSave 异步转移阶段写入目标路径固定（2026-10-11）

- 为 RWImGUI→Unity 主线程保存请求捕获配置文件 TargetPath，并在真正写入的同一临界区校验预期路径，堵住请求已离队但 LoadOwned 切换配置根目录的竞争窗口；不把旧设置重定向写入其他账户或 Mod 的配置目录。
- 未新增逐帧 IO，不改原作者数据和旧 Remix 设置；独立程序集/net48/实机尚需后续验证。

### Phase 2/4 — Cartography 内嵌图集资源归 Rainwright（2026-10-11）

- 将 DryCycle.csproj 当前内嵌图标图集的资源名从 DryCycle.Cartography.* 迁为 Rainwright.Cartography.*；CartographyIconCatalog 通过以自身程序集为 Owner 的 CartographyResourceLocator 查找，优先 Rainwright 命名，兼容旧程序集 DryCycle 命名，不到其他已加载插件程序集胡乱搜图。
- 抽出无 System.Drawing 依赖的资源定位器，新增 tests/CartographyResourceLocator.Tests，直接链接生产源码，测试新旧同名优先级、单独 Rainwright-only/legacy-only、缺失报告、非法资源名阻止；新增 CI 与局部 Guard。
- 图标切割、颜色、缩略图导出算法保持不变。未来独立 Rainwright.csproj 必须正式包含同一组资源，否则仍无法声称独立制图功能通过；真实 net48/图标解码、GPU 实测待 Phase 4。

### Phase 2/4 — Ctrl+Z/S/Y 游戏按键冲突策略独立归 Rainwright（2026-10-11）

- 新增 Input/RainwrightShortcutInputSettingsStore，以 Rainwright.ShortcutInput.xml 独立保存三个 Ctrl 快捷键期间的玩家输入放行开关；不再要求 DryCycle.Remix 才能持久配置。
- 首次无自有配置时只读沿用旧 DryCycleOptions 的已生效值（即原 EditorShortcutGameplayPolicy Provider），不抢先抓取旧 Remix 默认值；自有配置存在后独立优先，即使 DryCycle 卸载也保持。损坏/DTD 版本默认全部 false 以保护作者快捷键。
- 新 UI 控制中心偏好行用 AutoSave、Keys 两个小按钮按需打开设置窗；Keys 只在用户点击应用时排队，Unity 主线程统一写入。保存生成 .bak 备份，支持错误状态/取消修改；关闭 UI 不影响原版跳跃/玩家输入。
- AutoSaveCoordinator.Tick 和 Shutdown 在通用主线程中处理待落盘输入策略，保留配置根目录锁定与旧 Mod 配置只读。真实生产回归覆盖旧策略/新策略优先级、排队不提前改变输入、重启持久、损坏 fail-closed、跨根目录写拒绝与备份。
- 全部为 Phase 2/4 前置解耦，最终 Rainwright.dll 单独构建和 Phase 5 全仓审计尚未开始。

### Phase 3/4 — 玩家按键热路径不使用持久配置锁（2026-10-11）

- RainwrightShortcutInputSettingsStore 将 Ctrl+Z/S/Y 三项输入例外合并为单个 3-bit int 快照。Player.checkInput 等热路径只做 Volatile.Read 与位运算，不在玩家按 Z/S/Y 时争抢配置锁，不分配临时对象。
- 配置加载、验证、写入仍走受锁主线程；保存真正完成后一次性发布新的输入策略快照。保留已有三模式、Fail-closed 及测试语义。

### P1-C / Phase 2 — 修复 DryCycle 相同 Mod ID 下的双 Remix 接口竞争（2026-10-11）

- DryCycleOptions 与 RegionDayNightOptions 原本都调用 MachineConnector.SetRegisteredOI(NR.B5,...)，而 Remix 每个 Mod ID 只允许一个 OptionInterface。RegionDayNightOptions 首先注册时，DryCycleOptions 的旧 Ctrl+Z/S/Y 配置会掉回默认 false。
- canonical RegionDayNightOptions 绑定旧配置的**原始键名** UnlockCtrlZGameplayInput / UnlockCtrlSGameplayInput / UnlockCtrlYGameplayInput，Mod Reload 的 Reuse/Register 和 Initialize 都确保绑定；DryCycleOptions 的三个 Getter 仅在自己未注册时只读委托已注册的 RegionDayNightOptions。
- DryCycleOptions.Register 发现当前正由 RegionDayNightOptions 持有 NR.B5 时直接复用，不再发起无效的第二次 SetRegisteredOI；RopeSpear、天气、日夜等已有绑定不改，旧用户配置不重写。
- Rainwright 自有 Rainwright.ShortcutInput.xml 优先，旧 Remix 只读回退，三键在未显式保存 Rainwright 新配置前应保持原值。局部 Guard 防止这些旧键重命名，真实 Rain World Remix 配置加载次序仍需实机验证。

### Phase 3/4 — 独立设置的渲染读取无锁化（2026-10-11）

- 修复两类设置状态每次 ImGui 绘制都 lock(Sync) 的隐性性能风险：Unity 主线程在配置 Load/TrySave 时曾持锁执行 XML/磁盘 IO，导致渲染线程读取完成版本、错误或开关也会等待磁盘，虽然点击动作只入队仍可能卡顿。
- RainwrightAutoSaveSettingsStore 和 RainwrightShortcutInputSettingsStore 使用 Volatile.Read 读取文件归属、AutoSave 值、三键策略及成功/错误/版本；对应写操作使用 Volatile.Write，先发布结果和异常，最后发布完成版本。原受锁的入队、路径验证、磁盘写入与备份保持不变。
- 局部 Guard 阻止两套配置回到同步读锁；生产功能回归继续运行，真正 DX11/native 帧时间和 net48 仍需实机验证。不触发 Phase 5 全仓审计。

### Phase 4 — 无锁渲染状态真实生产代码并发回归（2026-10-11）

- 新增 EditorSettingsRenderLockTests：直接链接两套真实 Rainwright 配置生产源码，测试用例模拟 Unity 主线程在 XML/磁盘 IO 中持有各自 Sync 锁，另一线程同时执行 UI 每帧使用的 HasOwner/Enabled/Interval/CompletedRevision/Result 和玩家快捷键查询，要求**不等待释放锁即能完成**。
- 检测的是实际并发行为，不只是 grep。主线程入队和文件写入锁仍保留、UI/玩家热路径无锁，严格区分这两种场景。

### Phase 3/4 — 配置根大小写严格归属（2026-10-11）

- 两套 Rainwright 设置的待写 TargetPath 校验和配置根目录变更检测改用 StringComparison.Ordinal。Linux/Steam Deck 上大小写不同可能指向不同作者目录，必须撤销旧任务；Windows 上等价大小写路径也选择安全撤销。
- 真实生产回归注入 case-only 配置路径切换，验证旧写请求取消、原作者文件内容不变、策略仍按正确文件重载。Guard 阻止 OrdinalIgnoreCase 重新进入这两条归属路径。

### 2026-10-11 — 进度盘点及 UI 非阻塞提交补强

盘点起点为 main 的 54cc7109：Production Regression、Build Safety 与 Guard/CODEMAP 均为 SUCCESS。本次盘点仅查看现有阶段台账、重点路径和 CI，**不是 Phase 5 全仓所有权扫描**。

| 工作链 | 已完成的代码侧工作 | 仍然阻断最终独立发布的事项 |
| --- | --- | --- |
| P1-A/B | 生物调试快照与作者目录定位已移出编辑器反向依赖，生产路径回归 | 游戏实机及分程序集验证 |
| P1-C | 原版控件焦点租约、Ctrl+Z/S/Y 新旧设置归属、旧 NR.B5 Remix 配置回退 | DryCycle-only / Both 下的原版控件与动态重载 |
| P1-D | EditorBackendLifecycle 管理通用 DevUI Hook，DryCycleLegacyBootstrap 仅过渡注册专属 Provider | Rainwright 独立入口及独立 DLL 的 Core 源码实际迁移 |
| Phase 2 | World 温度天气适配 DLL、通用 Map 地形 Provider、Relationships/Sound/CrashRecovery/Cartography/AutoSave 设置和资源归属 | 尚未生成可单独运行的 Rainwright.dll，故不能验证真实程序集引用 |
| Phase 3 | Bridge/PlayerMap 租约及 WorldTopology 永久游戏 Hook、世界作者族谱保存与异常恢复 | 独立/共存插件装卸顺序和 BepInEx 实际发现行为 |
| Phase 4 | 生产源码定向回归、错误回滚、旧配置、跨文件根目录保护、线程争用测试 | 真实 Rain World net48 编译、中文字体/ImGui/GPU/缩略图、三种安装模式实机 |
| Phase 5 | 尚未启动 | 前置阶段代码侧关闭后，才运行一次完整全仓归属复审 |

新发现的实际性能问题：两套设置从 ImGui 点击 Apply 时虽然只入队，但仍会争抢主线程持有的 XML/disk lock，可能冻结 native Present 回调。本轮仅将 UI 入队入口改为 Monitor.TryEnter 非阻塞尝试；锁繁忙时明确返回可重试错误，不把请求插到错误 Mod 配置目录。补充了直接使用生产源码的双线程加锁压力回归。磁盘 IO 仍只发生在 Unity 主线程，并保留备份和失败结果。

独立程序集不得在尚未分离的代码上虚称成功；这份台账后续继续按实际提交证据修订。

### Phase 4 — WorldTopology 已损坏主文件恢复写入保护（2026-10-11）

- 发现 WorldTopologyRegistry.Reload 从现有 .bak 成功恢复时置 Dirty，但下一次 Save 使用普通 AtomicWriteAllText，先将损坏主文件复制覆盖 .bak，可能毁坏仅存的已验证好备份。另一个错误是无备份、主文件加载失败时 Dirty=false，普通 Ctrl+S 仍可将空文档写回损坏文件；用户并没有明确放弃旧作者数据。
- 新增 WorldTopologyRecoveryWriter：仅当已从好备份恢复时使用 File.Replace(temp, primary, null)，不覆盖 .bak；文件系统不支持安全替换、备份丢失或作者目录无效时直接失败，旧数据仍在。普通非恢复保存仍走旧原子写入函数，避免扩散更改。
- WorldTopologyRegistry.Save 拒绝将无法解析且没有已验证恢复来源的数据写回磁盘，非 Dirty 时不重复创建/重写玩法文件；成功保存才清理 Dirty/LoadError/恢复标识；失败保留所有状态并报告完整异常。
- 新增生产源码编译测试 WorldTopologyRecovery.Tests，涵盖好备份保留、损坏主文件更换、主文件已移除、备份丢失、非法/缺失作者目标及临时文件清理。局部 Guard 约束恢复写入必须有专用路径；真实游戏及跨 Mod 目录权限验证仍待后续。

### Phase 2/3 - WorldTopology gameplay path ownership (2026-10-11)

- The generic WorldTopologyRegistry no longer directly uses DryCycle.Plugin, NR.B5, ModManager.ActiveMods, or the game DLL assembly location to choose author file targets. Only the registered WorldTopologyPathOwnership lease provides a trusted path.
- src/WorldLink/DryCycleWorldTopologyPaths.cs owns the existing DryCycle gameplay Mod path search, including targeted/newest roots, verified modinfo.json fallback and mergedmods exclusion. Other Mods never inherit this source path.
- The persistent WorldTopologyRuntime registers its lease before installing gameplay hooks and releases the lease only after all hooks have been detached. Failed HookGen cleanup retains the original owner until retry succeeds.
- Registry reloads after owner revision changes only when it has no unsaved edits. Missing owner fails closed rather than guessing a Rainwright-owned directory.
- Production-linked WorldTopologyOwnership.Tests covers game-only path ownership, Rainwright-only refusal, duplicate-owner rejection, scoped dispose, Mod reload, and provider exception isolation; integrated into regression CI and Guard.

### Phase 4 - WorldTopology prevents stale-author-path writes (2026-10-11)

- The saved LoadedPath no longer authorizes its own write after an active Mod is unloaded, reloaded, or retargeted. WorldTopologyRegistry.Save resolves the active NR.B5 owner anew and uses WorldTopologyAuthorWriteGuard to check both the loaded owner revision and the actual author target path.
- A removed owner, changed owner or different file path fails visibly and retains Dirty/in-memory edits. New documents may save only when their originally observed owner revision remains stable.
- WorldTopologyOwnership.Tests directly links the production authorization guard and injects missing owner, changed file path, changed lease revision, stable owner and new document cases. Guard keeps Save dependent on this check.

### Phase 2 - Inactive gameplay Mod cannot own topology files (2026-10-11)

- When ActiveMods has been populated and contains no NR.B5, DryCycleWorldTopologyPaths now refuses the assembly-directory fallback. Merely having an old DryCycle.dll on disk is not permission to write gameplay files for a disabled Mod.
- The verified-assembly search remains available only during genuinely early startup (no active list). The production-linked ownership regression and Guard enforce this distinction.

### Phase 2/4 - Empty ActiveMods and topology recovery owner safety (2026-10-11)

- An initialized but empty ActiveMods list now refuses the containing-assembly path fallback. The fallback is limited to the genuinely unavailable (null) list, and even a list created during path lookup blocks it.
- WorldTopologyRegistry.TryGetRecoveryText no longer reuses LoadedPath as a recovery destination without rechecking the gameplay path lease, matching the Save authorization check. On owner unload, revision change or retargeting, unsaved topology remains in memory but no stale author-target snapshot is emitted.
- The production-linked ownership test covers an empty initialized Mod list; the local Rainwright contract enforces both file-save and recovery-capture authorization.

### Phase 4 - WorldTopology crash restore owner reauthorization (2026-10-11)

- RecoverySnapshotStore.ProcessRequests independently rechecks the active gameplay topology path before restoring any WorldTopology.json snapshot, including imported historical DryCycle recovery snapshots. A missing/different owner refuses restoration without consuming the snapshot or creating backup/write files.
- AutoSave.Tests compiles the production WorldTopologyPathOwnership service alongside RecoverySnapshotStore and exercises owner absent, owner switched and correct owner recovery (including pre-recovery backup and reload suspension). Guard now requires the restore-time owner gate.
- The generic recovery behavior of other authoring files is unchanged. Additional cross-Mod author provenance checks remain part of Phase 4; do not interpret this topology-specific gate as a universal restore authorization system.

### Phase 4 - Generic active-Mod crash recovery authorization (2026-10-11)

- RecoverySnapshotStore now reauthorizes non-topology author destinations with WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget immediately before restoring a snapshot. A writable destination must be an existing active Mod source selected by the current priority and targeted/newest folder rules; an old snapshot cannot authorize writes to inactive Mods, unrelated Mods, generated mergedmods or newly shadowed lower-priority files.
- WorldTopology.json continues to require its stronger gameplay-owner lease, including when copied from legacy DryCycle recovery. Generic author snapshots remain retained and report failure when live ownership is unavailable; successful restore still creates a pre-recovery backup and suspends further writes.
- AutoSave.Tests links the production resolver and shared source path code to test the actual live ownership policy, plus disabled/switch/overlay and successful restore, instead of mocking author permissions. Further symlink/Windows physical-path validation and true game integration remain unverified.

### Phase 4 - Recovery candidate external-edit conflict protection (2026-10-11)

- RecoverySnapshotStore records the existing author's SHA-256 and presence state when a crash-recovery candidate is listed. An explicit restore now rechecks both immediately before touching the author source; a newly created file, removed file or externally changed content refuses recovery without consuming the snapshot or claiming reload success.
- AutoSave.Tests adds production-source regressions for an external edit and an author file appearing after candidate discovery. No extra hash is computed on routine editor frames; hashes run only while discovering candidates and handling an explicit restore request.

### Phase 4 - Crash recovery staging/backup non-destructiveness (2026-10-11)

- Explicit recovery no longer copies to a fixed .Rainwright-recovery.tmp using overwrite=true. Each restore stages via a unique same-directory CreateNew-style File.Copy(false), so predictable stale files or existing staging links cannot be clobbered.
- An existing .Rainwright-before-recovery.bak is never overwritten by another restore; subsequent restores create distinct GUID-suffixed backup names and still preserve pre-recovery author bytes. Cleanup deletes only this operation's own staging file.
- AutoSave.Tests covers both pre-existing backup and stale staging file plus clean temporary-file removal; Guard ensures unsafe fixed overwrite copying cannot be reintroduced.

### Phase 4 - Crash snapshot author Mod identity (2026-10-11)

- Newly captured generic recovery manifests optionally record the active source Mod ID in ownerModId. Restore now requires that recorded ID to match the current Mod that owns the same verified author source. A replacement Mod with the same directory path does not inherit its predecessor's snapshot write authority.
- Existing legacy recovery manifests do not have ownerModId and remain importable/readable without rewriting. Their restores still require the current active Mod/source priority gate, though historical manifests cannot retroactively attest the captured owner ID.
- AutoSave.Tests validates same-path identity replacement refusal and a successful explicit restore after the original identity is restored; the linked production source resolver provides the actual identity.

### Phase 3 - RWImGui optional frontend module lifecycle ledger (2026-10-11)

- BridgePlugin now registers its 20 optional frontend services through the existing FrontendHookLedger. Enable records the matching Disable before activation; partial failures and failed cleanup remain tracked for retry rather than releasing the Bridge while hooks/resources remain alive.
- Shutdown releases these optional services in reverse install order after flushing map cache, eliminating the manually duplicated list of optional shutdown calls. A pending optional service prevents a second owner instance or re-enable from stacking subscriptions until cleanup completes; failed removal retains diagnostics.
- The production-linked FrontendHookLedger.Tests expands failed optional shutdown, duplicate attach refusal and partial-enable cleanup retry coverage. Guard requires the Bridge to actually use this ledger rather than just leaving it unused.

### Phase 3 - RWImGUI localization Harmony patch retains failed cleanup (2026-10-11)

- RwImGuiLocalizationReloadGuard now saves its Harmony owner before invoking the external Patch method, allowing Bridge optional-feature rollback to unpatch even after a partially successful Patch throws.
- Disable no longer treats a missing UnpatchSelf or a reflection invocation failure as successful cleanup; it propagates the failure while retaining the original Harmony instance until a later successful retry.
- Production-linked tests inject a fake Harmony cleanup failure/missing method and verify the reference survives for retry, then clears exactly once on success. Guard protects the ownership and error propagation contract.

### Phase 3 - Player Map 14-submodule partial-install and retryable cleanup (2026-10-11)

- PlayerMapFrontendLifecycle uses the existing frontend ledger to register all 14 submodules and their paired Disable handlers. Each initializer has a pre-recorded cleanup obligation; on failure it rolls back installed and partially installed modules in reverse order while preserving the original exception.
- Disable no longer skips cleanup merely because the aggregate enabled flag was never set, and failed submodule removal blocks a second initialization until later cleanup succeeds. Workspace retained state is reset after a clean release.
- Production-linked PlayerMapFrontendLifecycle.Tests fault-injects mid-enable, failed removal, duplicate enable and successful retry with test-only lightweight child module stubs. Guard requires all 14 module installs and the pending-cleanup check.

### Phase 4 - Active Mod author recovery symlink/junction escape guard (2026-10-11)

- Shared AuthoringSourcePaths.IsUnredirectedExistingModSource validates each existing path component from the active author Mod root to the recovery target and refuses file/directory reparse points, symlinks and junctions. Missing/unreadable attributes fail closed; checks are only on explicit recovery, not routine editor frames.
- Generic crash restore authorization now requires both current Mod ownership/priority and this physical-path safety gate. Regression Tests create real directory and file symlinks pointing outside the author root and verify external source bytes are unchanged after rejected restore.
- The rule is scoped below the author Mod root; mounted/linked ancestor game library directories are not assumed invalid. This does not claim to eliminate every possible filesystem race without OS-level open-by-handle APIs.

### Phase 2/4 - Gameplay-owned WorldTopology write path symlink safety (2026-10-11)

- DryCycleWorldTopologyPaths validates the actual NR.B5 author path components against symlink/junction redirection before returning a target. The shared helper permits absent trailing components for first-time authoring while checking all existing path segments.
- WorldTopologyOwnership.Tests verifies both a world/ directory link and a WorldTopology.json file link pointing into another Mod are rejected, then verifies the original NR.B5 source still resolves. This applies to gameplay Save and future editor-owned recovery leases.
- This is a scoped file/path ownership check; OS-level races after resolution and unusual dangling-link behavior remain separate concerns.

### Phase 3 - World Map legacy visual dual-Refresh Hook rollback (2026-10-11)

- WorldMapLegacyVisualGuard now tracks RoomPanel.Refresh and MiniMap.Refresh through the shared frontend Hook ledger before each attach. A partial failure of the second Hook rolls back both registered callbacks rather than depending on the aggregate enabled flag.
- Failed detach remains recorded and blocks duplicate activation, while Disable retries pending cleanup independently of enabled and restores the vanilla MapPage presentation only after Hook cleanup succeeds.
- FrontendHookLedger production tests fault-inject the second Refresh attach and verify the first subscription remains accounted for and is released. Guard confirms both production Hook sites are ledger-managed.

### Phase 3 - Timeline slugcat icon atlas resources survive failed unload (2026-10-11)

- WorldTimelineSlugcatIconCatalog records the atlas lease before the external LoadImage call, so even a partial load that throws is included in teardown. Shutdown uses a small generic owned-atlas cleanup helper; successfully absent/unloaded atlas names are removed while failed unloads retain the original name and throw to Bridge's optional lifecycle ledger.
- Reinitializing a timeline icon catalog with pending atlas cleanup is refused. If Futile's atlas manager no longer exists, there is no active manager resource to unload and the stale in-memory inventory can be cleared.
- The production-linked FrontendOwnedAtlasCleanup.Tests inject a failed UnloadImage and verifies other atlases still release, failed names remain for retry and a later cleanup succeeds without duplicate or lost ownership. Guard checks the real timeline code consumes the helper.

### Phase 3 - World Map snapshot/geometry throttle callbacks owned together (2026-10-11)

- WorldMapUpdateThrottle records ShouldPublish and ShouldPrimeGeometry registration with two separate entries in its frontend callback ledger; partial failure in the second callback rolls back the first. Disable attempts all outstanding cleanup even if aggregate enabled was never set.
- Failed callback removal remains tracked for retry and blocks a second Enable. Cache/index resets occur after fully detached callbacks to avoid falsely reporting a clean shutdown.
- The production Hook ledger test adds an injected failed second registration; Guard enforces the two World Map throttle callback pairs.

### Phase 3 - Vanilla World Map visibility restored on Hook cleanup failure (2026-10-11)

- WorldMapLegacyVisualGuard.Disable now restores suppressed vanilla MapPage sprites even when one of its Refresh hook detach calls fails. The still-live callback sees enabled=false and cannot re-hide UI, while the failed Hook remains in the ledger and continues to block duplicate installation until cleanup can be retried.
- Guard protects cleanup ordering so future refactors do not strand vanilla map presentation behind an early throw.

### Phase 3 - Native RWImGUI AlwaysCallback partial attach ownership (2026-10-11)

- BridgePlugin now uses a persistent FrontendHookLedger for the native RWImGUI AlwaysCallback instead of a success-only boolean. Its removal obligation is registered before calling AddAlwaysCallback; an Add that publishes a callback then throws is rolled back immediately. If rollback fails, the pending entry blocks every subsequent frame's registration attempt.
- OnDisable attempts ReleaseAll regardless of whether the original Add returned successfully, and keeps Bridge ownership when the native removal still fails. A successful retry releases the callback and permits a clean future startup.
- Production FrontendHookLedger regression injects partial native registration and failed first remove, verifies no duplicate add and successful subsequent cleanup. Guard enforces the Bridge's real integration.

### Phase 2/3 - World Map frontend callbacks exclusive ownership (2026-10-11)

- Core WorldMapFrontendBridge no longer silently overwrites ShouldPublish, ShouldPrimeGeometry, RasterEnhancer or GeometryBackgroundBudget callbacks. An identical delegate is idempotent, but a different owner receives a visible InvalidOperationException. A mismatched owner cannot unregister an existing callback.
- Persistent cache's five callbacks are a single immutable registration bundle installed or released using compare-and-swap; readers see all five or none, never a half-updated provider. Missing callback members are rejected before touching a live owner, preserving the editor-independent backend boundary.
- Production-linked WorldMapBackground.Tests covers competing callback owners, precise disposal, reattachment after release, and all-or-none persistent cache ownership including mismatched removal. Guard prevents a return to direct overwrite registration.

### Phase 3 - World Map Retained V2 independent cleanup of cache and GPU resources (2026-10-11)

- WorldMapRetainedV2Runtime now retains its lifetime obligation from the start of initialization until both persistent callback detach and retained state reset have succeeded. Failed shutdown prevents re-enable instead of treating the partially released runtime as idle.
- Disable attempts the persistent cache and retained GPU/scene cleanup independently, aggregating errors instead of short-circuiting. The room, route, renderer and render texture reset paths are each attempted even if another resource throws, and presentation/readiness state is cleared in a finally block.
- Guard enforces separate cache/scene cleanup and explicit render texture release. Runtime GPU failure injection must still be verified in a real Unity game session.

### Phase 3 - RWImGUI font atlas Detour rollback and owner retention (2026-10-11)

- DevToolFontAtlasIntegration now exposes PendingCleanup for its native pre-frame pointer and MonoMod Hook. Enable refuses a new install while either old resource is still owned; startup exceptions trigger immediate cleanup and keep the existing fallback RWImGUI font instead of reporting an untracked partially installed detour.
- Disable restores only its own native pre-frame pointer, refuses to write a missing original pointer, and clears the Hook reference only after Dispose succeeds. Failed disposal leaves the exact Hook instance for retry.
- BridgePlugin retries an incomplete font detour cleanup on its next Enable, blocks reactivation while it remains, and retains the Bridge singleton on Disable if font cleanup failed. Guard checks the actual source and owner gates.

### Phase 2/4 - World Map persistent V3 cache moved to Rainwright ownership (2026-10-11)

- MapViewPersistentCacheStore.ResolveCachePath now writes V3 data under BepInEx/cache/Rainwright/MapView, not the historical DryCycle/DevTool/MapView directory. DryCycle gameplay does not become the implicit owner of standalone editor cache files.
- If a new Rainwright cache does not exist, Load reads historical DryCycle V3 first (retaining its route and thumbnail data), then DryCycle V2, without modifying either old file. An existing Rainwright V3 remains authoritative even if corrupt, preventing stale fallback from hiding a new cache failure.
- Production-linked WorldMapPersistentCache.Tests covers V3 read-only migration, owner priority, unchanged legacy bytes and corrupt current-cache refusal alongside the existing V2 compatibility tests. Guard anchors the new write path and legacy resolver.

### Phase 4 - World Map persistent cache writer coalesces obsolete revisions (2026-10-11)

- MapViewPersistentCacheStore keeps one pending full snapshot per destination and replaces its queued value with the latest revision while a background disk writer is busy. Already in-flight writes finish unchanged; each destination's most recent pending revision is then committed. Other destinations remain independently queued.
- The coalesced queue uses a linked list and O(1) path-to-node index instead of allocating unbounded per-frame author snapshots while the disk falls behind. An unexpected Task.Run scheduling failure clears the writerRunning latch so another request can retry rather than deadlocking Flush.
- Production-linked persistent cache tests enqueue sixty revisions, require at most one pending request for the same file, verify final revision and unrelated destination survive, and verify Flush empties pending inventory.

### Phase 4 - Persistent World Map writer reports IO failure and preserves old cache (2026-10-11)

- MapViewPersistentCacheStore.Flush now requires both a drained write queue and no outstanding failed destination. Worker errors are tracked by canonical destination comparison; a later successful write to that same cache clears its failure without masking unrelated outstanding errors.
- Removed the non-atomic File.Copy(overwrite:true) fallback after File.Replace failure. An IO or sharing error leaves a previously valid V3 destination untouched rather than risking truncation; the unique staging file is still cleaned up and the error is visible in diagnostics.
- Production-linked WorldMapPersistentCache.Tests forces a failed write using an existing directory at the destination, verifies Flush rejects false success, tests independent successful writes cannot erase the error, and checks successful retry clears the failure.

### Phase 3 - Parent editor lifetime retains pending Player Map child services (2026-10-11)

- PlayerMapBackendLifecycle.DisableAndVerify performs the existing reverse teardown but raises an explicit failure when any service still has an outstanding cleanup lease. The ordinary best-effort Disable path remains available to compatibility plugins and does not change their semantics.
- EditorBackendLifecycle now uses the verified stop callback for the Player Map extras. A child Hook that fails to detach cannot trick the parent into discarding its cleanup step, and duplicate startup remains blocked until the exact failed child is finally released.
- The production PlayerMapLifecycle.Tests injects two consecutive failed TerrainBake unsubscriptions and a successful third cleanup, verifying parent-facing failure and eventual release; DevToolAuthoring legacy bootstrap stubs keep their original behavior and exercise the same stop contract.

### Phase 4 - Bounded retry for failed World Map cache writes (2026-10-11)

- MapViewPersistentCacheStore exposes a thread-safe, per-destination HasFailedWrite predicate. The World Map runtime can observe completed writer errors without blocking its render frame or treating an unrelated destination as failed.
- PersistentTryScheduleSave preserves Dirty if async queueing itself throws, reports the scheduling exception and retries only after the existing debounce. For completed disk write failures it rebuilds the current snapshot and retries at most once every 30 seconds while idle; a new user edit still follows the normal four-second save debounce.
- FlushPersistentCache now distinguishes a genuinely timed-out background writer from failed disk writes and reports the latter as errors instead of falsely describing them as timeouts.
- The production-linked cache tests cover exact-path failures and successful clearing; Guard binds the runtime's retry policy and failure diagnostics.

### Phase 2 - Cartography atlas error reporting no longer depends on DryCycle.Plugin (2026-10-11)

- CartographyGameAssets now routes atlas readback failures through Rainwright's EditorDiagnostics rather than DryCycle.Plugin.Logger. The generic editor no longer requires game plugin log state to diagnose this graphics adapter.
- An uninitialized Futile atlas manager causes a deferred retry without consuming queued icon requests, avoiding null-reference failure during cross-Mod or late atlas loading.
- Guard protects the explicit logger ownership and delayed-ready atlas safety contract; sprite registration/render appearance remains unchanged.

### Phase 2/4 - Cartography author project and diagnostics ownership (2026-10-11)

- CartographyRuntime and its async region source loader no longer call DryCycle.Plugin.Logger; all command, save/export and source errors now use editor-owned EditorDiagnostics while retaining their original exception detail.
- Default Cartography projects now resolve under BepInEx/config/Rainwright/Cartography, keeping new author writes outside the old game Mod's settings directory. When no editor-owned project exists, the historical DryCycle/Cartography XML is parsed read-only into the editor workspace; its original bytes are not rewritten by Save, and the first Save targets the Rainwright project.
- A present Rainwright project, even one that fails validation, remains authoritative: no silent stale legacy fallback. Legacy content's hash is not mistaken for the new target's disk revision.
- Existing production Cartography.Tests no longer link a fake DryCycle.Plugin logger, verify failures via editor diagnostics, and exercise path precedence and actual Ctrl+S target ownership.

### Phase 2/4 - Explicit Open of legacy Cartography remains read-only (2026-10-11)

- CartographyProjectPaths.IsLegacyDefaultPath performs canonical, root-boundary-aware legacy author path recognition. An explicitly opened DryCycle/Cartography project is parsed normally but its future ordinary Save location is redirected to Rainwright's corresponding author file.
- When a Rainwright author file already exists, its real bytes supply the collision-safe disk baseline; otherwise no old DryCycle hash is falsely used to authorize a new file's overwrite. Custom third-party paths outside the historical DryCycle directory retain normal explicit Open/Save behavior.
- Cartography.Tests now exercises a real Open command against the legacy location and verifies the resulting active project still points at Rainwright, with the old XML unchanged. Guard enforces this separate entry point.

### Phase 2 - Native Map authoring and command diagnostics detached from DryCycle.Plugin (2026-10-11)

- NativeMapAuthoringStateHub no longer depends on DryCycle.Plugin.Logger to report legacy-map mirroring, third-party config imports, invalid author sources, position-override saves and reads. Error objects are passed to editor-owned EditorDiagnostics to preserve exception detail.
- MapEditorRuntime's map command failure handling now uses the same editor diagnostic boundary; gameplay plugin logger construction or absence cannot change editor command processing.
- Guard checks both actual production files for absence of direct game Plugin logging. Native author path authorization, atomic writes, Dirty state and vanilla mirror semantics are otherwise unchanged.

### Phase 2 - Player Map workspace backend diagnostics no longer tied to DryCycle.Plugin (2026-10-11)

- All eleven direct calls to DryCycle.Plugin.Logger in PlayerMapWorkspaceRuntime now use editor-owned EditorDiagnostics.Info/Warning/Error. Command failures, protected author saves, persisted dirty-state retention, restored region sessions and room visibility failures remain visible without a gameplay plugin logger.
- No save-path or room editor behavior is changed in this pass; the change removes an unwanted game plugin type dependency from the editor's shared backend. Guard rejects reintroduction of the direct logger reference.

### Phase 2 - Eight Player Map auxiliary BepInEx plugin entrypoints moved out of backend sources (2026-10-11)

- Eight live BepInEx compatibility classes (Runtime, ConfigBuild, DerivedLayout, GroupCommands, IncrementalRender, RenderOutputValidator, RenderRevisionGuard and TerrainBakeBridge) now reside together in src/Misc/DevToolPlayerMapCompatibilityPlugins.cs, the gameplay/legacy host owner.
- Production Player Map backend sources no longer install or reference the DryCycle.AuxiliaryPluginStartupGuard directly. All existing legacy BepInPlugin GUIDs, BepInDependency metadata, Hook-free fallback entrypoint logic and main game startup-failure isolation are preserved byte-for-byte inside the moved class bodies.
- The production-linked PlayerMapLifecycle.Tests now link the actual moved host classes as well as the core PlayerMapRuntimePlugin.cs backend activity gate. Guard rejects reintroducing BepInPlugin and startup-guard code into the editor backend files.

### Phase 2 - Four retired Player Map BepInEx identities moved to compatibility host (2026-10-11)

- The four source-compatibility-only public plugin identity classes (ConfigSerializerV2, ConnectionMetadata, MigrationStreamSerializer, NormalizedConfig) now also live in src/Misc/DevToolPlayerMapCompatibilityPlugins.cs.
- These retired identities and all their original IDs/versions remain available to gameplay/legacy consumers, but no longer force an isolated Rainwright backend to resolve DryCycle.Plugin.Version. Their operational backend serializer helpers stay at the existing Player Map source paths.
- PlayerMapLifecycle production tests verify all four former GUIDs and Guard enforces the editor/game ownership boundary.

### Phase 2/4 - Existing Rainwright Cartography documents protected on explicit legacy Open (2026-10-11)

- Manually opening a legacy DryCycle project no longer adopts the unrelated current Rainwright file hash as its save baseline. If a Rainwright destination already exists, ordinary Save correctly reports an optimistic-concurrency conflict; old and new XML documents remain intact. Missing destinations can still be created normally on first explicit Save.
- Production Cartography runtime tests exercise the actual Open and SaveActive commands against both files and assert the conflict and byte preservation. Guard protects the authoring boundary from regression.

### Phase 2 - Two World BepInEx compatibility identities moved to DryCycle host (2026-10-11)

- WorldCreatureAuthoringRuntimePlugin and WorldTopologyLiveTraversalFixPlugin keep their exact historical BepInPlugin GUIDs and dependencies but are now compiled from src/Misc/DevToolWorldCompatibilityPlugins.cs, not from Rainwright editor backend source.
- The WorldCreatureLiveReload and WorldTopologyLiveTraversalFix implementation files now have no BepInPlugin metadata or hard Plugin.ModId/Version references. WorldTopology's unusual failure path uses its existing optional logger when available and EditorDiagnostics as a fallback, never DryCycle.Plugin.Logger.
- Guard checks the host's two preserved identities and rejects putting the plugin attributes or game plugin references back into these World backend files.

### Phase 2/3 - Five World frontend compatibility plugins isolated; Player Locator pump owned by Bridge (2026-10-11)

- Moved five frontend BepInPlugin GUID classes to RWImGui/Compatibility/LegacyFrontendPluginIds.cs while preserving their historical names/IDs and dependency metadata. Generic World inspector, creature catalog, lineage, subregion and player locator source files no longer contain legacy entrypoints.
- The player locator was the final competing active BepInEx helper. Its legacy entry now only reserves a GUID. BridgePlugin registers WorldMapPlayerLocator using its rollback-capable optional-feature lifecycle and captures marker snapshots in PumpOptionalFrontendWork on the Unity main thread. No extra helper Update is required; the DX11 Present callback still only consumes immutable snapshots.
- Guard verifies all five compatibility GUIDs, absence of BepInPlugin in UI page implementation files, absence of competing helper Update/OnEnable/OnDisable, and presence of a single Bridge-owned lifecycle/pump.

### Phase 2 - Cartography Canvas GPU upload logging is editor-owned (2026-10-11)

- CartographyCanvasImages now initializes its GPU atlas texture bridge with EditorDiagnostics.LogSource instead of the gameplay Plugin.Logger. A standalone RWImGUI frontend will not require DryCycle.Plugin solely to report a late GPU upload failure.
- Threading, texture upload deadlines, pixel sizing, fallback behavior and cleanup are unchanged; Guard protects the logging dependency boundary.

### Phase 2/3 - DryCycle-specific LanceScavenger diagnostics moved to optional frontend extension (2026-10-11)

- The generic RWImGUI project excludes Debug/LanceScavenger sources. Both the detailed AI diagnostics page and opt-in 20Hz log writer are compiled via the DryCycle.WorldData.RWImGui optional extension instead; the editor no longer needs the gameplay creature snapshot types to compile its debug workspace.
- DevToolDebugWorkspaceView offers a single optional diagnostics tab slot. A gameplay extension registers a localized tab label, draw callback and stop callback, making the LanceScavenger tab visible only with the extension. Leaving the tab and unloading the extension stop AI snapshot capture. No per-frame runtime reflection is used.
- OptionalFrontendDebugSection is a production-linked, compare-and-swap lease. Competing adapters are rejected; failed Stop retains the original owner for retry rather than letting a second plugin instance register a duplicate consumer. The existing World Data adapter registers/releases its World tab and AI diagnostics independently and retains failed disposal leases.
- Production-linked tests verify optional tab ownership, failed shutdown retention, extension unload and reactivation, and guard enforces the generic/frontend-optional compile boundary. The existing AI recording start/stop and log format are unchanged.

### Phase 3 - World Map background budget and raster callback leases (2026-10-11)

- WorldMapBackgroundBudget and WorldMapRasterReadbackFallback now use the same production FrontendHookLedger owner as the Map publish/prime callbacks. Registration records ownership before attaching delegates; failures attempt rollback, and an incompletely detached callback blocks another instance instead of silently overwriting the bridge.
- Disable first changes each service into harmless fallback mode, attempts callback removal, clears its transient caches/cooldown state, and explicitly reports any failed detach so the parent Bridge lease retains the cleanup step for retry.
- Existing published callback semantics, background throttling, raster quality and per-frame budgets are unchanged. Guard checks the real production owner sites, and FrontendHookLedger regression coverage exercises partial installation and cleanup retry.

### Phase 3 - Optional diagnostics renderer faults contained inside generic debug workspace (2026-10-11)

- DevToolDebugWorkspaceView isolates a game-supplied optional diagnostics renderer exception inside its own section instead of unwinding the generic editor's entire debug page. It reports the exception once, stops repeatedly calling the failing renderer and shows a diagnostic message until a fresh optional provider registers.
- The optional stop callback likewise reports once and keeps native drawing resilient to a game-extension shutdown error. Existing OptionalFrontendDebugSection lease ownership still retains the failed Stop and blocks competing registration until it is successfully released.

### Phase 4 - Non-deploy compile paths for frontend and optional World Data adapter (2026-10-11)

- Both RWImGUI and DryCycle World Data extension projects now support an explicit `DeployToGame=false` mode. The default remains deploy-to-game for existing Visual Studio and single-command workflows, with no change to runtime assembly identity.
- Frontend supports `DryCycleAssemblyPath` as a compile-time dependency without writing output into the live game directory. Optional World Data adapter additionally accepts `DevToolFrontendAssemblyPath`. Both fail with a clear missing-input error rather than relying silently on an installed DryCycle plugin.
- Local sandbox/CI can now compile these separate frontend assemblies with supplied references while reserving deployment only for `DeployToGame=true`. Guard checks the compile-only inputs and output conditions.

### Phase 3 - Retained World Map GPU cleanup after scene reset failures (2026-10-11)

- WorldMapRetainedV2Runtime.ResetRetainedState now runs Synchronizer, RenderSceneState, MainSceneState, SceneTransfer and GPU/room cleanup as independent cleanup owners. Failures aggregate with the original exception and specific owner name, and the existing final presentation reset always executes.
- Production-linked RetainedCleanupBatch tests inject simultaneous first-owner and GPU failures, verify the intervening and final cleanup actions still run, and verify a second teardown can complete successfully. Guard rejects reintroduction of the earlier fail-fast cleanup sequence.

### Phase 3 - Retained V2 World Map persistent cache callback bundle owned by one retryable lease (2026-10-11)

- WorldMapPersistentRetainedCache no longer flips enabled=true before attempting registration of its five persistent capture/restore callbacks. The cache callback bundle now uses FrontendHookLedger and publishes enabled only after a successful atomic bridge registration.
- During Disable it flips enabled=false first, then attempts to release the bundle; an incomplete unregister leaves a recorded owner that prevents another Enable, while stale delegates early-return instead of touching disposed render data. Clearing transient cached room/route state is still attempted, and the outer RetainedV2 runtime receives an explicit cleanup failure for retry.
- Guard enforces the updated callback ownership sequence; production-linked FrontendHookLedger and WorldMapFrontendBridge regression tests already cover partial callbacks and atomic bundle contention.

### Phase 4 - Non-deploy builds cannot accidentally resolve stale installed game DLLs (2026-10-11)

- Frontend and extension default assembly HintPaths only when DeployToGame=true. DeployToGame=false now strictly requires explicit DryCycleAssemblyPath and (for the optional adapter) DevToolFrontendAssemblyPath, even if the developer has an old game installation in the default Steam location.
- This prevents a false successful compile against an outdated installed DLL when validating extraction of editor backend types. Guard protects the conditional reference input contract.

### Phase 2/4 - First physical Rainwright.dll and separate, tested source compilation (2026-10-11)

- Added src/Rainwright/Rainwright.csproj, a real net48 assembly named Rainwright with an explicit game-agnostic source manifest: FrontendHookLedger, CreatureCatalogRuntimeLease, OptionalFrontendDebugSection and RetainedCleanupBatch. These four tested, editor-owned lifecycle types are no longer compiled into DryCycle.DevTool.RWImGui.dll; the latter references the new assembly as a project dependency.
- Preserved all class namespaces, constructor contracts and internal access via explicit friend assembly grants while moving their CLR assembly ownership. No duplicate type definitions are shipped in the frontend, and DryCycle gameplay still does not reference these frontend-only lifecycle helpers.
- Native frontend deployment now clears an old Rainwright DLL before rebuild, requires a fresh sidecar to exist, tracks it in Visual Studio up-to-date checks, and removes stale files when RWImGUI is absent. Build safety Guard verifies the exact compilation manifest and dependency edge.
- Regression CI now actually compiles the production Rainwright.csproj in an isolated net10 SDK mode (same source, no Rain World or BepInEx reference), complementing existing lifecycle behavioral tests. This is the first physical DLL slice, not yet the final all-UI Rainwright distribution.

### Phase 4 - Rainwright standalone SDK smoke compilation target coherence (2026-10-11)

- Fixed the independent Rainwright project smoke-build configuration: the project itself chooses net10.0 when RainwrightStandaloneSmokeTest=true and net48 otherwise. CI no longer overrides the TargetFramework global property externally in a way that allowed Restore to resolve the wrong reference assets; production net48 packaging is unchanged.

### Phase 2 - Pure World Map scheduling and scene invalidation moved into Rainwright.dll (2026-10-11)

- The shared Rainwright assembly now exclusively compiles WorldMapBackgroundSchedulingPolicy and WorldMapDirtySet, in addition to its lifecycle primitives. The native frontend excludes their original source paths so they cannot be emitted into two runtime assemblies.
- These runtime-neutral retained-map types still participate in the real hidden/OH cadence and retained scene invalidation work, while their production source continues to be exercised by the existing regression suites. The independent Rainwright.dll SDK compile checks that both map classes remain free of game-only dependencies.

### Phase 2 - Retained World Map view transform and lock-free mailbox moved into Rainwright.dll (2026-10-11)

- Rainwright.dll now owns WorldMapViewTransform and WorldMapViewTransformMailbox, both platform-neutral frame-to-frame retained-map state primitives. The native frontend uses their original namespaces through its Rainwright project reference and no longer compiles duplicate definitions.
- Added System.Numerics.Vectors framework reference for the net48 Vector2 API. Production-linked DevToolAuthoring regression checks screen/world invertibility, visible world bounds, coherent revision and transform publication, and dimension/zoom normalization.

### Phase 2/4 - Four real backend authoring policy and configuration types moved from DryCycle.dll into Rainwright.dll (2026-10-11)

- DryCycle.csproj now directly references Rainwright.csproj and excludes EditorAutoSavePreferences, RainwrightAutoSaveSettingsStore, EditorShortcutGameplayPolicy and RainwrightShortcutInputSettingsStore from the DryCycle gameplay compile. Rainwright.dll exclusively compiles these portable editor backend services and grants internal access to the transitional gameplay host.
- This is the first physical backend dependency inversion: DryCycle's legacy Remix providers register their scoped callbacks into Rainwright-owned AutoSave/shortcut state. Existing owned XML precedence, atomic flags and legacy fallback logic are retained without rewriting stored files.
- Added framework System.Xml.Linq reference to the Rainwright assembly. The independent SDK build checks these real production settings/lease sources without BepInEx or game DLLs.
- When RWImGUI is missing, its cleanup task now retains Rainwright.dll and explicitly verifies that it was deployed: DryCycle gameplay now depends on the editor-owned settings DLL even when no native debug UI is installed. Active native frontend builds still refresh the Rainwright runtime via their existing project reference.

### Phase 2 - Store-first backend extraction preserves error-reporting assembly direction (2026-10-11)

- Independent net10 compilation revealed EditorAutoSavePreferences and EditorShortcutGameplayPolicy still report corrupt settings and failed writes through EditorDiagnostics, a BepInEx-backed host diagnostic. Their entry/policy implementations remain in DryCycle.dll temporarily, preserving the established error reporting behavior without introducing a circular Rainwright -> DryCycle reference.
- Rainwright.dll now physically owns only RainwrightAutoSaveSettingsStore and RainwrightShortcutInputSettingsStore. DryCycle.dll references Rainwright.dll for their internal persisted state through the friend assembly contract, retaining the same XML migration, fail-closed settings and async-save logic.
- The source guard explicitly keeps the remaining public policy adapters on the host side until diagnostics can be supplied through a neutral, scoped editor-owned logging interface. This avoids declaring a false clean compiler boundary while continuing actual backend extraction.

### Phase 4 - Real net48 Rainwright.dll CI build added (2026-10-11)

- Added a compile-only net48 Microsoft .NET Framework reference assembly package to Rainwright.csproj. It does not ship to the Rain World game and lets an Ubuntu CI runner build the actual deployment target instead of relying solely on net10 behavioral regression suites.
- Regression CI now compiles Rainwright's real net48 DLL independently and also compiles its identical production sources on modern net10 for headless tests; neither build requires a Rain World installation, BepInEx or Steam directory.


### Phase 2 — 通用作者 Provider 与迁移 I/O 进入 Rainwright.dll（2026-10-11）

- EditorAuthoringParticipants 从 DryCycle.dll 移至 Rainwright.dll；DryCycle、原 RWImGui 前端和可选 World Data 适配器共享同一进程内作者 Provider 注册表，避免不同程序集静态状态分叉。保留原公共 API 和委托签名。
- SoundGroupConfigPaths 与 RelationshipExportPaths 这两组只读旧数据迁移/新路径归属逻辑也由 Rainwright.dll 单独编译；原有文件路径、迁移判定、原文件保留及错误反馈行为保持不变。
- DryCycle.csproj 明确排除这三个源文件；可选 World Data 适配器显式引用 Rainwright 项目，减少跨程序集解析的偶然性。
- Guard 增加编译归属契约；Regression Tests 增加直接引用真实 Rainwright 程序集的运行时 Owner/注册租约测试（独立 net10 目标），保留现有生产源测试及实际 net48 DLL 编译。
- 后续继续推进含游戏类型的地图、制图和作者编辑后端；真实游戏 UI/Hook/GPU 验证不能由纯程序集测试替代。


### Phase 2 — 制图作者文档与持久化模型进入 Rainwright.dll（2026-10-11）

- 按实际依赖拆分 CartographyDocument.cs：作者文档、图层、对象、连接样式和文档校验进入独立 CartographyDocumentModel.cs；仍需要原版地图几何的 CartographySource/RoomSource/TileRun 保留在宿主源码。
- 将 CartographyAppearance、CartographyStorage、CartographyProjectPaths、CartographyJson 连同新文档模型编译进 Rainwright.dll；保持原 Cartography 文档格式、XML/JSON、哈希、原子保存、迁移和透明背景规则不变。
- DryCycle.csproj 排除上述作者模型，避免重复定义文档类型；两个直接编译生产 Cartography 代码的测试项目仍保留相同类型组合和语义。
- 独立 DLL 的 Smoke 测试加入制图类型 Owner 校验，Guard 新增模型归属与游戏几何边界检查；不把地图 GPU/地形运行时错误归因到这一阶段的纯模型迁移。


### Phase 2 — world.txt 作者文档与声音组数据模型真实迁入 Rainwright（2026-10-11）

- 完整 WorldDocument.cs 编入 Rainwright.dll，DryCycle.dll 取消重复编译；房间连接精确编辑、未知第三方行原样保留、CREATURES 记录解析和序列化使用现有生产逻辑，无文件格式改动。
- SoundGroupModels.cs 的公开声音组快照、样本和诊断类型以及内部写入模型统一归 Rainwright.dll，防止第三方 UI 对同名类型产生两个程序集身份。net48 构建增加编译期 PolySharp 以支持 init 属性。
- 跨程序集 Smoke 直接从实际 Rainwright.dll 引用 WorldDocument 与 SoundGroupLibrarySnapshot；验证 world.txt CRLF 原样往返、连接无效操作、变更/保存后的作者行与未知扩展行保留，以及程序集身份。
- Guard 添加编译归属检查；保留 WorldLoader、实时世界游戏 Hook 和生物生成运行时集成在宿主程序集，不随这批源码迁移。

### CI 回归修复 — 世界文本测试换行与跨目标引用（2026-10-11）

- Rainwright net48/net10 实际编译均通过；独立程序集测试失败原因为新测试样例误写了字面量反斜杠 r/n，导致无法按行解析 Rooms 段。修复测试数据为真正 C# 转义换行后再验证，不修改生产世界解析算法。
- System.Numerics.Vectors、System.Xml.Linq、System.Runtime.Serialization 显式引用限定于 net48；net10 SDK 使用内置参考程序集，清理跨框架重复引用警告。


### Phase 2 — 制图内嵌 PNG 资源与查找器迁入 Rainwright（2026-10-11）

- CartographyResourceLocator 由 Rainwright.dll 单独编译，三份制图内置 sprite sheets 通过同一程序集的 EmbeddedResource 归属；DryCycle.dll 不再嵌入重复 PNG，不改变资源标识 Rainwright.Cartography.*。
- CartographyIconCatalog 的资源查找 Owner 从自身游戏模块程序集替换为资源查找器所属的 Rainwright 程序集，兼容原先的资源名回退机制与调色/裁图流程。
- 独立程序集测试直接读取真实 Rainwright.dll 中 Objects.png 的 PNG 签名并验证非法资源名拒绝；Guard 对编译移除/添加、资源搬迁与查找入口增加契约。

### Phase 4 — 真实运行时产物与 SDK 测试产物隔离（2026-10-11）

- RainwrightStandaloneSmokeTest=true 的 OutputPath 单独写入 net10-smoke 文件夹，禁止同名测试 DLL 覆盖 net48 可部署 DLL；默认本地和游戏 Release 构建路径保持原样。
- 回归 CI 加入两个框架输出存在且非空检查；真实 Rainwright 程序集测试进一步校验它不包含 DryCycle 或 DryCycle.* 的反向 AssemblyRef，避免以后迁入更多编辑器代码时出现隐性循环依赖。

### Phase 2 — 制图命令编辑引擎迁入 Rainwright（2026-10-11）

- 将 CartographyEditing 的命令类型、不可变修订 Clone/Validate、移动/删除/复制/粘贴、样式与图层、房间可见性、排列分布逻辑搬入真正 Rainwright.dll 的 CartographyEditingCore.cs。
- 游戏侧 CartographyEditing.cs 仅保留适配器，沿用旧 Apply(original, source, command) 与 SameItem/SameLayer 调用契约；仅对 Align/AlignPorts/AddMissingRooms 传入场景 Bounds、管道 Port 和补房间委托，其他高频命令不分配无用委托。
- 命令和原作者文档同一程序集身份、撤销/保存的原子验证不变；Guard 检测核心不得反向依赖 CartographySource/CartographySceneBuilder/Unity/BepInEx；跨程序集运行时 Smoke 直接执行 Rainwright 核心命令并验证编辑快照不被污染。


### Phase 4 — Cartography 游戏适配器对真实 DLL 的跨程序集回归（2026-10-11）

- 新增 tests/CartographyAdapter.Tests/net10：直接引用真实 Rainwright.dll、只链接游戏侧 CartographyEditing.cs 适配器，以最小场景提供者校验现有 UI 调用签名。
- 覆盖高频 Move 不触发场景回调、深克隆编辑版本、SameItem/SameLayer、房间 Bounds 对齐、管道 Port 对齐与连线复位、AddMissingRooms 回调、SolidTerrain 样式迁移；阻止编辑器抽出后发生界面看似正常却实际丢失命令的隐性回归。

### Phase 2 — 编辑引擎源码完成 Rainwright 专属目录归属（2026-10-11）

- CartographyEditingCore.cs 物理移动到 src/Rainwright/Backend/Cartography/，不再只由 Rainwright.csproj 从 DevUI 源码目录软链接；游戏适配器保持 DevUI/DevTool/Map/Cartography/CartographyEditing.cs。
- Cartography.Tests net48 的 production-link 显式指向新实际路径；Guard 对核心所有权、样式命令和场景依赖的检查改为读取 Rainwright/Backend 归属。
- 原游戏项目保留对旧路径的 Compile Remove 防御性条目，以避免开发者本地残留旧源码被默认通配误编入 DryCycle.dll。


### Phase 2 — 制图连线路径编辑几何迁入 Rainwright（2026-10-11）

- 将制图路径命中检测、最近编辑手柄、手动连接线节点转换与轴约束拖拽完整迁移到 src/Rainwright/Backend/Cartography/CartographyRouteGeometry.cs，源码和程序集均归 Rainwright 所有；端点仍属于实时游戏房间口，不作为可拖动内部控制点。
- 旧 CartographyRouteEditing 保留与游戏场景节点相关的薄适配器，通过单一 Rainwright 类型转发，现有 ImGui 调用与绘制逻辑无需改接口；不反向引用 CartographySceneNode。
- RainwrightAssembly 测试实际 DLL 中的端点排除、路径投影、无效命中、手动节点深拷贝、轴约束拖拽；CartographyAdapter.Tests 额外覆盖场景节点进入迁移后几何核心的前端调用完整路径；Guard 强制单向依赖。


### Phase 2 — Mod 地区文件快照/目录归属迁入 Rainwright（2026-10-11）

- 将 CartographyRegionOption 和 CartographyRegionFiles（游戏资产解析器选定文件快照、路径解析、作者文本读取、修改时间与长度指纹、StreamingAssets 目录定位、地区可用性目录）物理迁至 src/Rainwright/Backend/Cartography/CartographyRegionFiles.cs。
- CartographySourceCache 与 CartographyRegionLoader 仍保留在游戏宿主；后台加载沿用唯一已解析的路径快照，避免 Rainwright 自行重做 mod 资源优先级，文件改动可自动使 cache fingerprint 失效。
- net48 Cartography 作者测试链接 Rainwright 的实体源码，net10 RainwrightAssembly 直接通过真正 Rainwright DLL 测试选定路径、作者文本读取、文件更改指纹失效、地区目录翻译与无效地区过滤，Guard 强制保持游戏数据缓存和纯路径解析的边界。


### Phase 2 — 已解析地区来源 LRU 与文件失效校验独立化（2026-10-11）

- 游戏专属 CartographySourceCache 现在只是静态委托桥接器；原先的基于文件路径/修改时间/长度指纹的命中判断、显式 refresh 跳过缓存、容量上限和最久未访问条目淘汰、读取过程中作者文件变更拒绝逻辑迁入 Rainwright 独立 CartographySourceFingerprintCache<TSource>。
- 原本每个地区数据都由 DryCycle 游戏侧 CartographyRegionLoader 读取，现通过无捕获静态委托按需调用；不会将 Unity/房间实体类型作为 Rainwright 公共数据依赖，也不重复加载已解析的 Mod 资源。
- 扩大真实 Rainwright.dll 跨程序集回归覆盖：首次读取、缓存命中不解析、显式刷新、源文件改动自动失效、LRU 容量与淘汰、加载中源文件变化不提交破损快照；Guard 保护不可回退到游戏侧 LRU。


### Phase 2 — PNG 导出压缩与分块流迁移至 Rainwright（2026-10-11）

- 将原先 CartographyPngEncoder 的 PNG IHDR/IDAT/IEND、Deflate 压缩、Adler-32、CRC32、跨带像素行流与 64 KiB IDAT 分块完整迁入 Rainwright 独立源文件 CartographyPngStreamEncoder.cs，不依赖 System.Drawing/Unity/BepInEx；游戏侧仅保留 Bitmap 锁定和 ARGB 行像素拷贝适配器。
- 流式编码保留原先每带高度、约 200 万像素上界和单行内存开销，不缓存全图；新增输入行数、宽高参数验证，拒绝生成无效 PNG。
- 独立 DLL net10 回归真实将编码后的 IDAT zlib 解压并验证 RGBA 通道/透明度及每行 Filter None，同时测试缺少 scanline 时拒绝无效文件；既有 net48 Cartography 作者测试链接 Rainwright 的编码器实体源码，Guard 保证所有压缩与分块逻辑没有回落到游戏侧。


### Phase 2 归属加固 — 防止主工程递归编译 Rainwright/Backend（2026-10-11）

- 查明 SDK 默认递归 Compile glob 会覆盖整个 src/ 目录，因此物理迁至 src/Rainwright/Backend 下的独立编辑命令/路径/LRU/PNG 核心可能重新落入 DryCycle.dll，引发运行期程序集身份重复。
- 在 DryCycle.csproj 和主程序集专属 Directory.Build.targets 中双重排除 Rainwright/**/*.cs；仍通过单独 ProjectReference 获取 Rainwright.dll，未来新增 backend 自动保持单一程序集归属。
- Guard 增加此全路径编译边界的静态阻断断言。


### Phase 2 — 制图三层渲染顺序与连线堆叠迁至 Rainwright（2026-10-11）

- 制图 RenderPass（UnderRooms、Content、Guides）、可见图层过滤、历史 links 图层在最后一间房间后显示、路线在全部图层 pass 保留以及按 pass 组批的策略迁入 src/Rainwright/Backend/Cartography/CartographyRenderLayerPlanner.cs；不依赖 CartographySceneNode、CartographyPrimitive、Bitmap、Unity。
- 游戏侧 CartographyComposition.Build 保持原接口，读取一次原始场景图元标签转换成不可变的 render descriptor，调用 Rainwright 计划器再还原为宿主图层；原文档 Layers 顺序不变。
- net48 Cartography.Tests 使用实体计划器源码；Rainwright 独立程序集回归新增 legacy 房间连线层次、导览 pass、隐藏图层过滤和正常三层输出完整性校验，Guard 强制双向职责不回流。

### Phase 4 — 真实游戏侧制图组合适配器回归（2026-10-11）

- CartographyAdapter.Tests 链接生产 CartographyComposition.cs 而不复制图层算法，真实 Rainwright.dll 提供 RenderLayerPlanner；验证连接线 legacy 层顺序、普通房间、边框、辅助线、隐藏图层、节点引用恒等与原作者图层不变，并检查场景图元筛选与 Rainwright render pass 完全一致。

### Phase 2 — 制图 retained scene 缓存与失效策略物理迁移（2026-10-11）

- 将 CartographyScene.cs 的 Retained SceneCache 缓存命中、作者对象与图层克隆、房间源引用身份、样式指纹、SolidTerrain/Crop/RoomNames/Terrain/Water 精确失效和 Prune 行为迁入 src/Rainwright/Backend/Cartography/CartographySceneRetentionCache.cs。
- DryCycle 留下 CartographySceneCache.cs 薄包装类型并沿用 TryGet/Store/Prune 接口，承接 CartographyRoomSource/CartographySceneNode 类型；泛型 Rainwright 核心无需引用 Unity/Bitmap/游戏房间模型。
- 更新 Cartography net48 作者测试的源码链接与 Guard 中曲面缓存测试归属路径，禁止未来双份缓存语义悄然回流。

### Phase 4 — retained scene 缓存真实程序集回归（2026-10-11）

- RainwrightAssembly.Tests 直接验证真实 Rainwright.dll 的 Generic SceneRetentionCache：首次缓存、错误保留、对象编辑/房间来源替换/房间名/曲面/样式修改失效与删除淘汰。
- CartographyAdapter.Tests 直接编译游戏侧 CartographySceneCache.cs 适配器，确认其以原 API 正确使用 Rainwright 的泛型缓存并传递节点引用与错误信息，不需要真实 Unity 房间对象。

### Phase 4 — 真实 MSBuild Compile Item 独立程序集归属验证（2026-10-11）

- Regression Tests 增加 MSBuild -getItem:Compile 的实际工程 item 求值，不读取/猜测 XML 标签的最终结果；自动拒绝任何 Rainwright/**/*.cs 重新编入 DryCycle.dll，并要求宿主 CartographySceneCache.cs 适配器仍在游戏程序集编译输入中。

### Phase 2/4 — WorldMap retained 路线空间索引迁入 Rainwright（2026-10-11）

- 将 WorldMapRouteSpatialIndex 的空间桶、局部查找、保留数组引用、精确线段命中、路由移除、重置以及 ReaderWriterLockSlim 跨线程同步完整迁入 src/Rainwright/Runtime/WorldMap/WorldMapRouteSpatialCore.cs（无 Unity/游戏对象引用）。
- 游戏侧保留 WorldMapRouteSpatialIndex : WorldMapRouteSpatialCore 的薄适配，只转换 ConnectionRouteResource 的 ConnectionId/Points，原世界地图视图和查询调用接口不变。
- 原 WorldMapRouting.Tests 源码链接真实新核心，新增 WorldMapSpatialAdapter.Tests 直接引用已编译 Rainwright.dll 加载生产游戏桥接器；RainwrightAssembly.Tests 也独立执行空间查询、鼠标线段命中、保留路线数组、清理和重置。
- Guard 确保无反向游戏资源依赖，Regression Tests 增加实际跨程序集桥接 CI。

### Phase 2/4 — World Map 正交路由和方向标记源码/程序集迁移（2026-10-11）

- 将约 114 KiB WorldMapOrthogonalRouter 及 WorldMapDirectionMarkerGeometry 整体迁往 src/Rainwright/Runtime/WorldMap/ 并编入真实 net48 Rainwright.dll；不复制、不包装第二份求解器，现有世界地图前端继续直接调用原命名空间下的单一类型身份。
- 前端工程显式 Compile Remove 两个旧路径（保持防回退）；原路由及方向标记独立回归的 Source Link 切换到 Rainwright 真正归属目录；RainwrightAssembly.Tests 对真实 DLL 验证程序集身份与房间管道方向推断。
- 新类型不依赖 Unity/BepInEx、DryCycle.Plugin 或生物/世界玩法。Guard 检查编译归属和依赖方向，现有短路、避障、导向和缓存覆盖继续用于行为兼容验收。

### Phase 2/4 — WorldMap 连线交汇过渡算法迁移（2026-10-11）

- 将 WorldMapJunctionWeavePlanner 约 19 KiB 连线分流/交汇错位/过渡段几何完整迁到 src/Rainwright/Runtime/WorldMap/，与正交路由和方向标记共用单独的 Rainwright.dll 类型身份；宿主保留负责将算法结果应用到真实 ConnectionRouteResource 的 CorridorLaneAllocator。
- WorldMapRouting.Tests 编译迁移后的真实源码，RainwrightAssembly.Tests 对物理程序集、未分配偏移、相同偏移和短路径无效输入进行验证；Guard 明确禁止 BepInEx/Unity/游戏路线类型反向耦合。

### Phase 4 — net48 Rainwright 核心 DLL 持续构建产物（2026-10-11）

- main 全部 Regression Tests 通过后，GitHub Actions Artifact 保留真实 net48 Rainwright.dll（命名 Rainwright-net48-core-<commit SHA>，保留 14 天），不会错把 net10 smoke DLL 当成运行目标。
- 该二进制只是开发与回归比对使用的核心程序集，尚不包含独立 BepInEx UI 插件入口与发布所需的 modinfo.json，不能标记为可直接安装的独立 Mod。
- 正式发布打包将在前端硬依赖解除且完整装配验收后另行接入。

### Phase 4 — CI 产物上传路径修复（2026-10-11）

- 首轮核心 DLL Artifact 上传步骤因 actions/upload-artifact 禁止 ../ 相对路径失败；构建和功能回归本身通过。改为将已经验证的 net48 DLL 复制到工作区内 artifacts/rainwright-net48，再上传；拒绝空文件，保持 net10 smoke 产物隔离。

### Phase 2 — World Map 房间空间索引与原子批次迁移至 Rainwright（2026-10-11）

- WorldMapSpatialIndex 的房间空间桶、局部区域索引、最高可见层命中、读写锁、增量替换和失效判断已迁至 Rainwright/Runtime/WorldMap/WorldMapRoomSpatialCore.cs。
- 保留同名 WorldMapSpatialIndex 前端入口，从真实场景和宿主资源提取 WorldMapRoomIndexUpdate，提交单个原子批次；端口更新只填充缺失项，删除移动不会留下旧空间桶。该核心不依赖游戏 Room/Unity 类型。

### Phase 4 — 世界地图房间空间索引真实 DLL 回归（2026-10-11）

- RainwrightAssembly.Tests 直接验证编译后 Rainwright.dll 的房间空间索引：可见层命中、排序稳定的区域查询、端口更新 only-if-missing、删除淘汰。
- WorldMapSpatialAdapter.Tests 同时链接游戏侧生产 RoomSpatial 适配器，通过最小场景桩调用真实 DLL，验证全量重建、移动旧桶清理、单纯管道变化不刷新、强制失效、删除和重置。

### Phase 2 — Object 视图搜索、可见性和悬浮状态迁入 Rainwright（2026-10-11）

- 将 Objects 场景 CategoryNormal/Ghost/Hidden 分类策略、搜索和模糊匹配、焦点、鼠标悬浮优先级、选中对象保留以及 retained Revision/Reset 完整迁入 Rainwright/Runtime/Objects/ObjectSceneVisibilityCore.cs。
- 原 ObjectSceneVisibilityState 仍对 UI 提供一致调用 API，只把 EditorObjectSnapshot 映射为轻量只读 ObjectSceneDescriptor，避免 per-frame 分配和对 DryCycle.Core 的 Rainwright 反向引用；Guard 约束单一所有权。

### Phase 4 — Object Scene 真实跨程序集状态与前端回归（2026-10-11）

- 新增 ObjectSceneAdapter.Tests/net10，以真实 Rainwright.dll + 原前端 ObjectSceneVisibilityState.cs（仅模拟 EditorObjectSnapshot 所需字段）验证类型唯一、null/常规分类、模糊与来源匹配、Normal/Ghost/Hidden 分类循环、搜索/聚焦优先、选中永不消失、悬浮不触发布局 Revision 和跨房间 Reset。

### Phase 2/4 — Objects 视图语义缩放和展示优先级迁入 Rainwright（2026-10-11）

- ObjectSceneSemanticZoomBand、缩放三分层及出入滞回阈值、Far 层低重要度对象裁剪、搜索/聚焦/选中/悬浮对象始终可见、已呈现对象是否可交互，都在 Rainwright/Runtime/Objects/ObjectSceneProjectionCore 中统一管理。
- 原 ObjectSceneProjectionPolicy 保持 ViewportSnapshot/EditorObjectSnapshot 旧接口，将视图和重要度转换为纯参数；实际 DLL 跨程序集 ObjectsAdapter 回归验证 Far/Mid/Near 滞回与强制保留用户目标。

### Phase 2 — Sound 场景多选状态与快照迁入 Rainwright（2026-10-11）

- 完整迁移 SoundWorkspaceState 中的房间切换清理、初始旧单选同步、异步旧索引预期值、Ctrl 多选、Shift 范围选择、全选/清空、选择越界淘汰、已选索引的不可变排序快照到 Rainwright/Runtime/Sound/SoundSceneSelectionCore.cs。
- 前端 SoundWorkspaceState 原静态 API 不变，游戏侧仅把 EditorSoundPresentationSnapshot 分解为 RoomKey、SoundCount 和 SelectedIndex；音效组、资源库等游戏宿主逻辑仍保留当前归属。只有单一 Rainwright 实例维护 SceneSelection 状态，不在每帧分配新列表快照。

### Phase 4 — 声音场景生产前端多选回归（2026-10-11）

- 新增 SoundSelectionAdapter.Tests/net10 真实 DLL 跨程序集测试，直接编译原 SoundWorkspaceState 前端桥接器，保留实际 Rainwright 声音快照类型；覆盖选中初始化、Ctrl 多选、Shift 范围、多选快照不污染待执行队列、旧索引确认、音效数量缩短淘汰、房间切换清零及 Legacy Reset。

### Phase 2/4 — 世界地图跨路线交叉检测归属 Rainwright（2026-10-11）

- 将原 27 KiB WorldMapRouteCrossingResolver 的空间桶、交叉去重、密集路口候选数上限、跨线位置与桥接标记的稳定排序/LOD 策略完整迁入 Rainwright/Runtime/WorldMap/WorldMapRouteCrossingCore.cs。
- 核心 Build<TRoute> 以只调用一次的路线路径静态 getter 接受现有 ConnectionRouteResource，无额外字典/路径克隆；原 WorldMapRouteCrossingResolver.Build 签名和结果类型不变，游戏前端仅保留薄适配。
- 真实 DLL Smoke 和生产前端 SpatialAdapter 测试覆盖正交线路内部交叉，以及空输入无标记；Guard 强制无游戏路线类型反向引用。

### Phase 2 — 声音工作区音效组模型进一步解除宿主依赖（2026-10-11）

- SoundWorkspaceState 中剩余的当前音效组、SoundLibraryDestination、组快照引用一致性/本地回退、组 ID 检查和唯一 ID 命名逻辑迁入 Rainwright/Runtime/Sound/SoundGroupWorkspaceCore.cs。
- 前端同一静态 SoundWorkspaceState API 保持，唯一 DryCycle.SoundGroupLibrary 读取被限制在 Groups 属性；新核心只接受已存在的 Rainwright SoundGroupSnapshot 数组，不反向调用游戏或后台资源服务。

### Phase 2 — Objects 浏览器分组与可见行范围选择独立化（2026-10-11）

- Objects 视图中心场景和浏览器共用的分类排序、字符串规范化、同名条目的来源/稳定 ID/原始索引二级排序，以及 Shift 可见行范围计算迁入 `src/Rainwright/Runtime/Objects/ObjectSceneListPlanner.cs`。
- 游戏 UI 保留原 ObjectSceneListProjectionSnapshot/IDevToolExplorerListItem/命令入队路径，只将已筛选对象映射为值类型描述符；缓存仍按实际对象数组引用与查询键复用，避免额外逐帧重建。
- 范围选择在点击时基于当前显示的行快照计算，支持 Ctrl 追加时跳过已选，隐藏/未匹配对象不得入队。

### Phase 4 — Objects 浏览器真实 DLL 分组与过滤后范围选择回归（2026-10-11）

- ObjectSceneAdapter.Tests/net10 直接链接生产 ObjectSceneListProjection.cs，测试 Rainwright 独立类型身份、分类/标签稳定排序、相同对象数组缓存复用、过滤后只有可见行可选、Ctrl+Shift 跳过已选、不可见锚点不得入队；命令队列用最小桩而非重编游戏运行时。

### Phase 3 — BepInEx 原生前端单例生命周期门闩迁入 Rainwright（2026-10-11）

- 新增 src/Rainwright/Runtime/FrontendPluginOwnerGate.cs：基于 Interlocked 的唯一实例入场、幂等同实例重入、异实例拒绝，以及 native Hook/Font/Callback 清理不完整时的保留机制，核心不依赖 BepInEx/Unity/DryCycle。
- 新增生产 frontend Shell/BridgePluginOwnership.cs 类型适配；BridgePlugin 的 OnEnable/OnDisable/ShutdownBridgeState 改为委托 Rainwright 独占门闩，维持清理失败不允许新实例重装钩子的既有安全语义。
- 新增真实 DLL + 生产适配器 net10 回归，覆盖幂等注册、重复插件拒绝、未清理资源阻止释放、成功重启、32 路并发竞争仅一名所有者和 null 拒绝。

### Phase 4 — 实际 net48 DLL PE 依赖与符号归属验证（2026-10-11）

- 新增 tests/RainwrightBinaryContract.Tests：在 Linux CI 只读取真实 net48 Rainwright.dll 的 PE 元数据，核对实际 AssemblyName、必需的核心 TypeDefinition、Cartography 内嵌 PNG 和所有 AssemblyReference。
- 显式拒绝输出 DLL 对 DryCycle、BepInEx、Unity、ImGui、RainWorld 游戏程序集的硬引用；不通过运行 net10 smoke DLL 来推断发布 DLL 的程序集依赖，未来新模块一旦误引入宿主类型会直接阻断产物归档。

### Phase 3/4 — 原生 Retained View 会话监测迁入 Rainwright（2026-10-11）

- 将原 BridgePlugin.cs 尾部 DevToolRetainedViewLifecycle 抽为独立游戏前端适配器，内部统一调用新 Rainwright Runtime/FrontendRetainedSessionGate<TGame>；Rainwright 负责观察上一次真正存活的游戏会话、瞬时 UI Owner 丢失不清理、由游戏显式关闭信号触发一次性释放。
- 实际 RainWorldGame 关闭检测、DevToolOverlay 和 PageViewRegistry 清理仍留在前端，避免 Rainwright 对 Unity/游戏循环反向依赖。
- 新增 net10 实际 Rainwright.dll + 生产前端适配器回归，覆盖页面同步、Debug 工作区取代普通页面、Alt+Tab 瞬断不释放、MainLoop 切换/DevTools 关闭一次清理以及 Disable 总清理。

### Phase 4 — 实际前端 MSBuild 源码归属验证（2026-10-11）

- Regression Tests 增加 `dotnet msbuild -getItem:Compile` 对真实 `DryCycle.DevTool.RWImGui.csproj` 的解析检查，确保 BepInEx BridgePlugin、桥接的实例所有权和 retained view 前端适配器被恰好编译一次，并拒绝 HookLedger、CreatureCatalogLease、OptionalFrontendDebugSection、RetainedCleanupBatch 被前端重新编译。
- 不需要本地游戏资产即可在 CI 证明程序集编译 item 归属，不以 Guard 纯文本静态断言取代编译器所实际接收的文件清单。

### Phase 3 — 页面注册/导航/生命周期管理器独立化（2026-10-11）

- Rainwright Runtime/FrontendPageRegistryCore<TMode,TPage> 统一负责页面 Mode/大小写不敏感 ID 去重、导航排序、快速查询、激活/失活与所有页面 Reset，保留在失活回调抛错时先取消活动页标记的安全语义。
- DevToolPageViewRegistry 现为只登记具体 Room/Objects/Sound/Map 等游戏页面的桥接器，保持现有静态调用 API、内置模式覆盖校验与导航展示顺序。核心无需任何 DevTool 游戏枚举、页面类、BepInEx 或 Unity 引用。

### Phase 4 — 页面注册与故障回滚真实跨程序集测试（2026-10-11）

- FrontendPageRegistry.Tests/net10 直接编译生产 DevToolPageViewRegistry.cs，调用真正 Rainwright.dll 的泛型页面管理器，使用最小测试页面覆盖七项内置页面登记、大小写不敏感查询、排序、页面重复切换不反复初始化、跨页面切换、全局 Reset。
- 泛型核心单独测试重复 Mode/ID 拒绝、注册失败不污染目录、激活失败不保留 Active、失活失败不保留旧 Active、故障后仍能重新开启页面。

### Phase 3/4 — 原生贴图 Atlas 归属清理迁入 Rainwright（2026-10-11）

- 将资源卸载成功后释放所有权、失败继续保留名称等待重试、异常报告保护迁入 `src/Rainwright/Runtime/FrontendOwnedAtlasCleanup.cs`，从旧 RWImGui 文件夹物理移除；不会将 Futile/Unity 游戏纹理对象引入 Rainwright。
- 既有 DevToolAuthoring.Tests 链接新源码位置，真实 RainwrightAssembly net10 测试额外覆盖混合存在/已不存在 Atlas、卸载失败后只保留失败项、重试成功后释放与错误报告次数。net48 二进制实体验证要求该类型存在。

### Phase 2/4 — 通用页面状态键与中央工作区布局独立化（2026-10-11）

- DevToolPageStatusState 包含页面状态等值/哈希键，已经脱离绘制 interface，迁入 Rainwright/Runtime 供所有页面复用；原 IDevToolPageView 和游戏专属绘制接口保持不变。
- DevToolWorkspaceLayout 整体迁至 Rainwright/Runtime，原始 520–900 水平尺寸、360–620 垂直尺寸及 HUD 边距规则保持，CI 使用真实 Rainwright.dll 校验桌面与紧凑屏幕尺寸。
- net48 PE 二进制必须包含上述两个核心类型，Guard 防止回迁/重编。

### Phase 3/4 — 焦点切换临时会话失联修复（2026-10-11）

- 修正 BridgePlugin 之前的会话对象 null 直接判断退出导致 Alt+Tab 保存机制失效的问题。
- 将最后一次确认有效的 RainWorldGame 对象身份、失焦/恢复、Paused/Resumed、OverlayHidden 和可见性推导迁入独立 Rainwright FrontendSessionVisibilityPolicy；宿主仅提供真实游戏终止信号，不反向引入 Unity/game 类型。
- 暂时丢失游戏 owner 不再错误解除原生上下文，也不会将暂停误当作恢复；真正主循环退出仍清理。

### Phase 3/4 — 四个生命周期通用模块物理归属 Rainwright（2026-10-11）

- 将 `FrontendHookLedger`、`CreatureCatalogRuntimeLease`、`OptionalFrontendDebugSection<TSnapshot>` 与 `RetainedCleanupBatch` 的唯一生产源码从 DryCycle/RWImGui 子目录移动到 `src/Rainwright/Runtime/`。这些类型原本已由 Rainwright.dll 编译，现在不再依赖来自游戏前端目录的相对文件链接。
- 真实 Rainwright csproj 只从自己的 Runtime 编译四项，并更新 DevToolAuthoring.Tests 生产源码链接；原 UI 调用保持相同类型身份、Hook 安装/失败重试行为及有序清理语义。
- Guard 继续验证旧 frontend Compile Remove 和新 Rainwright Compile Include 成对存在，检查新物理源码位置存在且旧路径不存在；诊断页面可选注册与地图 retained 错误汇总测试改为新归属位置。

### Phase 3/4 — Game 终止与滞后会话 Hub 顺序兼容（2026-10-11）

- 强化 Rainwright FrontendSessionVisibilityPolicy：当 DevToolSessionHub 仍错误报告 live 时，真实 game.processActive/DevTools/MainLoop 结束优先关闭 UI，避免仍显示/泵出已失效的窗口；同一 live hub 中 RainWorldGame 实例更换视为新会话，正确重启可选模块，不把旧游戏状态携带过去。
- 实际 DLL 回归新增 Hub 滞后、会话无断档游戏替换和旧游戏退出不污染新实例。

### Phase 2/4 — 世界地图后台调度、Dirty、变换和邮箱物理迁移（2026-10-11）

- WorldMapBackgroundSchedulingPolicy、WorldMapDirtySet、WorldMapViewTransform、WorldMapViewTransformMailbox 四个纯世界地图模块搬入 `src/Rainwright/Runtime/WorldMap/`；真实 Rainwright.csproj 从自身目录编译，不再通过 DevUI 相对源码链接。
- WorldMapBackground.Tests 与 DevToolAuthoring.Tests 的生产源码链接更新到新归属；原前端继续使用唯一跨程序集类型实例以保留后台节流、增量更新与 mailbox 线程同步。
- Guard 同时检查旧 frontend Compile Remove、新 Rainwright Compile Include、源码新路径存在和旧路径删除，预防日后编译双份类。
