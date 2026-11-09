<!-- codemap:v1 -->

# Map Code Map

仅用于 Map 内部职责消歧。

- `./` — World Map 核心：作者状态、编辑运行时、几何、持久缓存、地形来源与恢复。
- `Cartography/` — 制图游戏侧房间来源、场景/渲染和导出适配。作者文档、格式存储、路径和资源定位由 `src/Rainwright/Backend/Cartography/` 持有。
- `PlayerMap/` — Player Map 游戏侧构建、缓存、世界读取、渲染调度与运行时适配。纯房间文本几何编译、预检与保存安全策略归 `src/Rainwright/Backend/Map/`。

World Map 编辑器问题默认先看本目录根部；不要因为名称包含 world 就先进入 `src/WorldLink/`。纯 ImGui 布局与绘制仍属于 `../RWImGui/`。
