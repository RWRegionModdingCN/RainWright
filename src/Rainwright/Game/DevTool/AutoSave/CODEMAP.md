<!-- codemap:v1 -->

# DevTool AutoSave

此目录负责 DevTool 作者数据自动保存、崩溃恢复与备份轮换，不包含前端 UI 布局。

已提交编辑的即时保存、空闲去重与失败重试由 `AutoSaveCoordinator.cs` 管理；H/O 关闭 UI、游戏进程退出和应用退出的持久化边界由 `EditorSaveLifecycle.cs` 拥有。恢复快照和校验由 `RecoverySnapshotStore.cs` 管理；退出写入失败时保留恢复标记。关系代码持久化导出由 `RelationshipSaveAdapter.cs` 管理。

正式保存统一经过 `Commands/EditorActions.Save`，Remix 参数属于 `DayNight/RegionDayNightOptions.cs`，RWImGui 只负责呈现恢复入口。恢复快照覆盖 RoomSettings、world.txt、Player Map、Cartography、Relationships，以及 Room_Attr、WorldTopology、TemperatureSets、WeatherSpatial。所有快照不直接覆盖作者文件；仍不保证第三方自行管理的文件会被自动纳入。
