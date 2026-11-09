# RainWright

独立 Rain World 编辑器 Mod。插件、通用编辑后端、世界拓扑、中文/英文字体与 Cartography 图集均在 `newest/plugins/RainWright.dll` 中。

需要安装并启用 RWImGUI 1.12.0 及其 Library Loader。编辑器不依赖 DryCycle / Ancient Site；开启游戏开发工具后使用新 UI。关闭 UI 不会停止精确管道与单向连接的玩法支持。

编辑原版或其他 Mod 提供的区域时，先点击顶部“编辑目标 Mod...”，选择已在 Remix 启用的自有内容 Mod。本次游戏会固定使用这个目标，避免把打开的文档和撤销历史转写进另一个项目。选择前先保存已有区域改动；房间、物件、音效和触发器仍保留当前内存编辑。

对原版区域的连接、刷怪、Properties、Player Map 布局、房间 settings 和模板修改，保存为社区标准的 `modify/world/<区域>/...txt` 或 `modify/world/<区域>-rooms/...txt`。指令使用 Remix 原生 `[FINDLINEREGEX]`、`[REPLACE_n]`、`[ADDBEFORE_n]` 和 `[ADD]`；现有 `[MERGE]`、条件和其他作者内容保留。读取当前有效数据，保存时仅输出改动，不把整个区域复制成覆盖文件，也不写原版和 `mergedmods`。已有自有区域完整源文件继续按完整源文件保存。

补丁由 Rain World 自带的 Remix 合并器应用；发行或重进区域验证前，在 Remix 中重新应用 Mod，并按需要调整正常的 Mod 加载顺序。若在外部编辑器改过同一个补丁，本次保存会报告冲突并保留外部文件。管道精确端点元数据沿用现有 `world/WorldTopology.json`，地图图片与生成元数据放在目标 Mod 的标准 `world/` 资源目录，均不作为 `modify` 文本指令写出。

未选择编辑目标时，世界、房间和地图作者数据继续使用既有源目录策略。选择编辑目标后，原版区域通过上述标准 modify 补丁保存；WorldTopology 元数据也归入所选 Mod。未选目标时已有 WorldTopology.json 保持原位置；没有既有文件时，新拓扑的默认目录是本 Mod 的 world/。配置留在 BepInEx/config，历史 DryCycle UI 配置作为只读导入源。

如果同时安装 Ancient Site，`DryCycle.WorldData.RWImGui.dll` 可选适配器负责天气、温度、特殊地形、旧 Remix 设置和 DryCycle AI 诊断；仅在两个插件均存在时加载。

本软件使用未修改的 HarmonyOS Sans（Copyright 2021 Huawei Device Co., Ltd.）和 Ubuntu Mono。完整许可文本与原始字体同时嵌入 DLL，资源名为 RainWright.Fonts.LICENSE-HarmonyOS.txt 和 RainWright.Fonts.LICENSE-Ubuntu.txt。
