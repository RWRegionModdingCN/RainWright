# RainWright 拆分与安装验收

日期：2026-10-12。用户要求直接安装 Mod、不生成 ZIP、不启动游戏；附件中的实机验收与 ZIP 计划不作为本次执行授权。

## 交付

- 安装目录：`D:/Steam/steamapps/common/Rain World/RainWorld_Data/StreamingAssets/mods/RainWright/`。
- Mod 格式：`modinfo.json`、`README.md`、`newest/plugins/RainWright.dll`。
- RainWright.dll：10,668,032 字节；SHA256 `715C2A96543CF1AE0D2F109DE666EF7B2A427F8338CD2022E880AC091612C40F`。
- DLL 内含原生 UI、通用编辑后端、地图编辑、撤销重做、自动保存与恢复、世界拓扑运行时；不存在 DryCycle AssemblyRef。
- DryCycle.dll 已独立重建，不引用 RainWright，不再编译通用编辑器。天气、温度、特殊地形和专属诊断接入由 `DryCycle.WorldData.RWImGui.dll` 提供，BepInEx 同时要求 Anno 与 rainwright 两个插件 GUID。
- Ancient Site 中已更新 DryCycle.dll 及可选适配器，原 `DryCycle.DevTool.RWImGui.dll` 和符号文件已移出加载目录。
- 原插件备份：`C:/Users/Orrhisk/Desktop/Build/DryCycle/backups/rainwright-extraction-20261012-185911-86d90162/`，含安装记录与原始 DLL/PDB。
- 后续构建/安装入口：`scripts/Install-RainWright.ps1`；默认仅构建并验证，传 `-Install` 才部署。部署先备份与核对，再覆盖；失败时逐文件恢复并保留原异常及恢复结果。

## 内嵌资源与依赖

资源共 7 个：HarmonyOS Sans SC Medium、Ubuntu Mono Regular、对应两份许可证、三张 Cartography PNG 图集。字体从用户指定 Ancient Site 复制，字节未修改，运行时直接从内存提供给字体图集，不依赖外置字体文件。

RWImGUI 1.12.0+ 和其 Library Loader 仍是外部运行依赖，已在 Mod 清单中声明。Unity、Rain World 与 BepInEx 使用游戏现有程序集，不打包为私有副本。Windows x64 / net48 是实际游戏目标；CI 的便携核心使用独立输出目录，不能冒充完整 Mod。

RainWright 保持历史 API 命名空间和配置文件名以便兼容；展示名称、主程序集、Mod 文件夹均为 RainWright。世界拓扑功能归属已迁移，作者文件仍保存到活动 Mod 的真实原始目录；无既有拓扑文件时默认新建到 RainWright 的 world/。

## 已执行验证

- 实际 Rain World / RWImGUI 引用下编译 RainWright、DryCycle、可选适配器；均为零编译错误。
- 对安装后的真实 PE 检查 1,495 个类型、7 个资源、19 个允许依赖、17 个唯一插件 GUID，验证主入口与可选适配器依赖门控。
- 检查 RainWright 无 DryCycle 依赖、DryCycle 无 RainWright 依赖，以及两边无重复编辑器类型。
- 便携 net48 核心编译和 PE 验证通过，核心包含 368 个类型、3 个资源；现代测试程序集所有权检查通过。
- 编辑事务、配置迁移、未知配置字段保留、损坏 UI 配置保护、自动保存/恢复、Player Map 生命周期、世界生物作者操作、世界拓扑生命周期/路径/恢复，以及地图、声音、对象、前端所有权与资源回收等回归检查通过。
- 已安装 DLL 的托管地图模拟测试通过 190 项断言；使用实际 B5 作者数据只读构造测试对象，71 个房间缩略图在 24 个有预算的批次内完成。仅测试输出中的 Unity fixture 被改写，游戏 DLL 和作者文件未修改。
- MSBuild 实际编译归属检查、相关 Guard 与 15 项构建安全单元测试通过；CODEMAP 脚本测试和 diff 空白检查通过。
- Ancient Site 的 WorldTopology.json、WeatherSpatial.json、TemperatureSets.json 与两份原字体，共 5 个文件的安装前后 SHA256 一致。

## 验证范围

未启动游戏，未执行三种安装组合的真实加载、GPU/UI/Alt+Tab 或实机性能验收。托管地图 fixture 不能替代 GPU 和游戏内验证。

本机不具备 Windows 符号链接创建权限，自动保存与拓扑路径测试的相关用例明确输出 SKIP；Linux CI 保留并执行这些用例。RainWright/适配器构建保留现有游戏依赖带来的 Mono.Cecil 版本冲突警告（MSB3277），未替换游戏或 RWImGUI 的运行库。

本次没有生成 ZIP，也没有启动游戏。完整安装日志：`C:/Users/Orrhisk/Desktop/Build/rainwright-install.log`。

## 后续本地重编译与重复资源清理

按用户后续要求，重新 Rebuild Release DryCycle 到全新的隔离目录；0 警告、0 错误，产物 2,457,088 字节，已替换 Ancient Site/newest/plugins/DryCycle.dll。

确认两份外置字体与已安装 RainWright 的内嵌字体 SHA256 一致后，删除 Ancient Site/newest/ui/fonts 中的 HarmonyOS_Sans_SC_Medium.ttf 和 UbuntuMono-Regular.ttf，并清理空的 fonts/ui 目录。旧 UI DLL 和 RainWright sidecar 已在上一轮移除，本轮目录核查没有发现剩余重复 DLL 或三张图集的外置副本。另删除不对应当前 Release 的旧 DryCycle.pdb；当前 Release 设置 DebugType=none，不产生新 PDB。

本轮被替换/删除文件的备份：`C:/Users/Orrhisk/Desktop/Build/DryCycle/cleanup/20261012-190956/backup/`。WorldData 适配器、AIObservatory 与三份世界作者 JSON 共 5 个文件哈希未变。对安装后的程序集独立性、内嵌资源和可选适配器门控再次验证通过。安装脚本也已修正，避免以后复制陈旧的 DryCycle.pdb，并在备份后清理与内嵌字体完全一致的副本。未启动游戏。
