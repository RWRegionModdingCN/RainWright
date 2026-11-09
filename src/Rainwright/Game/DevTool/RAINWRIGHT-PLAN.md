# Rainwright（雨匠）— 独立 DevTool Mod 方案与归属审计

状态：历史总体目标（首轮 787 份源码基线）；中程最新计划和项目事实见 RAINWRIGHT-MIDTERM-REPLAN-2026-10-12.md。已有 Rainwright net48 核心 DLL 独立编译，但尚无完整可安装的新 UI 插件。**本文件用于记录设计与审计，不代表已经独立编译或发布。**

## 已确定的讨论结论

- **正式名称**：Rainwright；中文名称：雨匠。
- **产品定位**：Rain World 的完整独立开发者编辑器 Mod，而不是 DryCycle 的可选 UI 皮肤。
- **建议标识（最终发布前核查冲突）**：项目目录 `src/Rainwright/`，程序集 `Rainwright.dll`，BepInEx GUID `NR.Rainwright`；展示名 Rainwright — Rain World Modding Toolkit。
- **功能范围**：新 UI 全视图、全局/视图级快捷键及动画、输入焦点、World/Player Map/Cartography、地图与作者数据局部热加载/缓存/渲染、Objects/Room/Sound/Trigger/Relationships/World 编辑、撤销重做、自动保存与崩溃恢复、第三方兼容、诊断及扩展 API。
- **发布方式**：独立文件夹和工程单独构建；目标是只发布一个包含自有编辑器代码的 DLL，另外附资源与 `modinfo.json`；RWImGui 为外部安装依赖，不能打包其原生组件；不要求用户安装 DryCycle。
- **DryCycle 关系**：DryCycle 专属玩法（天气、特殊地形、物品、生物等）保留 DryCycle；只通过可选扩展注册给 Rainwright；反向无硬引用，避免重复 Hook。
- **技术原则**：单一 DevUI Update Owner、单一输入 Owner、单一 ImGui consumer、单一地图后台调度；保留 Revision/Dirty、缓存与预算，不重写成熟算法；先移交生命周期和程序集所有权，再按需改命名空间；新模块稳定后移除旧编译入口。
- **数据原则**：作者文件格式、已保存 UI 配置、恢复快照和未知第三方字段不丢失；迁移时必须提供兼容和冲突保护。
- **流程原则**：先做完整跨目录依赖/归属审计和迁移台账，按阶段建设可独立加载、编译和打包的产物；每阶段以可执行验证作为完成依据。

## 特别确认

目前 `DryCycle.DevTool.RWImGui.dll` 已单独构建，但它**硬依赖 `DryCycle.dll`**，不能直接换名发布。与它相连的编辑核心、快捷键、地图、热加载及启动 Hook 大部分还编入 `DryCycle.dll`。必须拆清楚才能单独发布。

针对全仓 `src/` 的归属审计、逐域处置、启动顺序、资源与构建测试，见同目录的 `RAINWRIGHT-OWNERSHIP-AUDIT.md`。审计中“迁移、保留、适配、待确认”应分别处理，不能把 DryCycle 专属功能直接搬走。


## 本轮全仓审计结论（2026-10-10）

- 完整扫描源文件 787 个，其中 DevTool 目录 317 个，目录外 470 个；DevTool 直接引用 DryCycle 外部代码的文件 47 个，目录外反向引用编辑器的文件 12 个，目录外直接安装 DevInterface Hook 的文件 20 个，BepInPlugin 声明总计 19 个。
- 重要遗漏：DevUI/Controls 5 个原版输入控件；DevUI/MapRoomMarquee；Misc/PaletteDirectInputRuntime；多个 PlayerMap 子插件；World 数据的生物临时预览与拓扑热修复；SoundGroupLibrary/自动保存及 UI 配置文件迁移；独立字体与 Cartography 内嵌资源。
- 最大跨程序集阻断：LanceScavengerAI -> DevTool 调试；TemperatureSetsLoader/WeatherSpatialRegistry -> WorldAuthoringPathResolver；DevTool 的 AutoSave/EditorActions -> Weather/Temperature/RegionDayNightOptions；WorldTopologyRegistry 使用 NR.B5 和 DryCycle 插件路径作为默认目标。
- 全量自动扫描：scripts/RainwrightOwnershipAudit.py，GitHub Actions 名称 Rainwright Ownership Audit；完整设计和详细风险：RAINWRIGHT-OWNERSHIP-AUDIT.md。该检查是静态归属扫描，不是执行过游戏内验证。
