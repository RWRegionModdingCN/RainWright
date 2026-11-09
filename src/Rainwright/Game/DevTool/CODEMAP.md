<!-- codemap:v1 -->

# DevTool Code Map

仅用于在 DevTool 内定位职责；目标目录已明确时不要读取其他模块。

- `Commands/` — 编辑命令与统一保存入口。
- `AutoSave/` — 自动保存调度、恢复快照、崩溃恢复与诊断。
- `Background/` — 独立背景编辑、原版场景渲染适配、RegionKit 数据字段兼容与社区 TXT 保存；不调用 RegionKit 编辑器、工厂或序列化方法。
- `Compatibility/` — 外部物件数据与注册工厂适配、Rain World 公共 DevInterface 边界；物件新 UI 不创建原版页面或控件，原版桥接仅处理玩家显式打开的页面。
- `Core/` — 编辑器生命周期（EditorBackendLifecycle）、Document、Session、Selection 与核心状态；原生 DevUI 和 PlayerMap 后端的启停由这里拥有。
- `Dialog/` — 会话资源浏览与结构化预览。
- `Extensions/` — 对外 DevTool API 与扩展注册。
- `Factories/` — 原生对象、Effect、Sound、Trigger 等作者对象创建入口。
- `Gizmos/` — 场景 Gizmo 编辑命令。
- `History/` — Undo/Redo、快照与编辑事务。
- `Input/` — 输入所有权、快捷键与场景交互仲裁。
- `Map/` — World Map、Player Map 与 Cartography；地图布局 XML 的读取适配在原版 MapPage 读取标准 TXT 后应用开发者坐标，不改写作者文件。
- `Objects/` — Object Catalog、Inspector、多选与运行时协调。
- `Palette/` — 色板草稿、绘制与通道混合命令、PNG 导入/导出及统一保存接入；便携像素模型、渐变与混合运算、异步目录列表及文件事务位于 `Backend/Palette/`。系统目录对话框在 `RWImGui/Widgets/` 作为平台交互边界。
- `Preview/` — 已退役的 Effect 悬停预览历史源码（不编入游戏 DLL）；仅保留无状态、无副作用的旧公开 API。
- `Relationships/` — 生物关系编辑。
- `Room/` — RoomSettings 与 RoomEffect 编辑。
- `RWImGui/` — 通用 ImGui 前端、页面、Shell 与 Widgets；DryCycle 仓库拥有独立的天气/温度 World 编辑器适配 `DryCycle.WorldData.RWImGui.dll`，只在两个插件同时存在时加载。
- `Sound/` — 环境音浏览、创建与参数编辑。
- `Triggers/` — Trigger 与 TriggeredEvent 编辑。
- `World/` — 标准 world.txt 作者数据、按需生成的 RainWright/DryCycle XML 标记版本、旧文件迁移及世界拓扑协调；标准文件与扩展版本由统一保存边界提交。游戏载入边界兼读旧 TXT，编辑器载入时按内容识别并迁移。

UI 绘制问题优先 `RWImGui/`；实际数据、状态或行为问题优先对应后端领域，不要因为界面入口位于 ImGui 就默认在那里实现业务逻辑。
