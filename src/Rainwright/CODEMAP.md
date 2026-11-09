<!-- codemap:v1 -->

# RainWright

- `Backend/` — 可移植作者文档、命令传输、历史事务、配置与保存策略。
- `Runtime/` — 可移植生命周期、前端所有权、地图几何与空间索引。
- `Game/DevTool/` — 游戏侧完整编辑后端、输入、Undo/Redo、自动保存与恢复。
- `Game/DevTool/World/` — 世界拓扑、精确管道与单向通行；由 RainWright 插件生命周期拥有，关闭 UI 不会停止。
- `Game/DevTool/RWImGui/` — 主原生 UI、页面、字体、绘制与资源生命周期。
- `Game/DevTool/Personalization/` — Remix 个性化配置的投影、工作预设导入导出与既有配置 Owner 的统一应用边界。
- `Game/Remix/` — 可复用介绍页接口、Futile 绘制的 Mod 详情面板和按 Mod ID 注册的 Remix 边界适配；不依赖 RWImGUI 绘制。
- `Assets/Fonts/` — 随完整 DLL 内嵌的原始字体与许可证。
- DryCycle 天气、温度和特殊地形的可选适配由 DryCycle 独立仓库拥有，不属于本项目。
