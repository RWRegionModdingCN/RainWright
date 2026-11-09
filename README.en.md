<h1 align="center">RainWright</h1>

<p align="center"><strong>A visual development toolkit for Rain World region creators</strong></p>

<p align="center">
  <a href="./README.md"><img src="https://img.shields.io/badge/%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87-%E5%88%87%E6%8D%A2-0969da?style=for-the-badge" alt="Switch to Simplified Chinese"></a>
  <a href="./README.en.md"><img src="https://img.shields.io/badge/English-Current-238636?style=for-the-badge" alt="English (current)"></a>
</p>

<p align="center">
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-Apache--2.0-2ea043?style=flat-square" alt="Apache-2.0 License"></a>
  <img src="https://img.shields.io/badge/Game-Rain%20World-986f4b?style=flat-square" alt="Rain World">
  <img src="https://img.shields.io/badge/Runtime-.NET%20Framework%204.8-512bd4?style=flat-square" alt=".NET Framework 4.8">
  <a href="https://github.com/RWRegionModdingCN/RainWright/actions/workflows/ci.yml"><img src="https://github.com/RWRegionModdingCN/RainWright/actions/workflows/ci.yml/badge.svg" alt="Build and test"></a>
  <a href="https://github.com/RWRegionModdingCN/RainWright/stargazers"><img src="https://img.shields.io/github/stars/RWRegionModdingCN/RainWright?style=flat-square&color=e3b341" alt="GitHub Stars"></a>
</p>

<p align="center"><img src="./docs/images/banner.png" width="800" alt="RainWright — Redesigned tools. Unified workflow."></p>

---

Creating a Rain World region often means switching between room objects, world connections, creature spawns, maps, and text files.

**RainWright** brings these tasks into one visual workspace. It redesigns the layout and interactions of the vanilla Dev Tools to make region creation and debugging easier to manage.

RainWright is now maintained as an independent project. Its editor, world topology, map backend, fonts, and Cartography atlases are provided by `RainWright.dll`. Neither running nor building it requires DryCycle / Ancient Site.

## Installation and layout

You need Rain World, its developer tools, **Rain World ImGUI API 1.12.0 or later**, and the API's Library Loader. Enable the dependencies and RainWright in Remix, then enable the game's developer tools.

This repository contains source code and Mod resources. Building the complete game DLL requires assemblies from your own Rain World installation. The portable core produced by CI is a development artifact.

```powershell
# Install .NET 10 SDK and PowerShell 7, then run from the repository root.
./scripts/Build.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
./scripts/Install.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
```

The complete Mod is generated in `artifacts/mod/RainWright/`. You can also copy that directory into the game's `RainWorld_Data/StreamingAssets/mods/`, restart the game, and enable it in Remix.

- `src/`: editor source code and bundled author-path validation utilities.
- `mod/`: Mod metadata, translations, thumbnails, and editable artwork.
- `tests/`: portable regressions, game assembly tests, and validation probes.
- `scripts/`: build, test, installation, and assembly protection commands.
- `docs/`: development notes, dependencies, architecture, and presentation assets.
- `artifacts/`: generated DLLs, installable Mod files, and installation backups; excluded from Git.

See [Building and testing](./docs/BUILDING.md) for configuration and reference assembly details.

## Main features

- **One development workspace** for rooms, objects, sounds, worlds, maps, triggers, and relationships.
- **Visual object editing** with search, filtering, multiple selection, property and resource choices, and draggable scene handles.
- **World connections and creature spawns**, including two-way and one-way links, precise pipe endpoints, and hot reloading.
- **Map authoring** for Player Map and developer layouts, with a Cartography workspace for map elements.
- **Sound management** with custom sound groups and regional sound editing.
- **Editing history and recovery** through undo, redo, automatic save recovery, configurable shortcuts, and workspace presets.
- **Content Mod authoring** that writes changes to vanilla regions as standard Remix `modify/world/` patches.

<p align="center"><img src="./docs/images/interface.png" width="800" alt="RainWright interface overhaul"></p>
<p align="center"><img src="./docs/images/world-editing.png" width="800" alt="RainWright world editing"></p>
<p align="center"><img src="./docs/images/workflow.png" width="800" alt="RainWright editing workflow"></p>

## Start editing

After entering a region, open the developer tools and use **Edit Target Mod...** in the top bar to select your content Mod before editing and saving. Reapply Mods through Remix when validating patches to vanilla or other Mods' regions.

Unsaved room, object, sound, and trigger edits remain in the editing session. Topology metadata lives in the target Mod's `world/WorldTopology.json`; personal settings live in `BepInEx/config`.

Run `./scripts/Test.ps1` for regressions that do not require the game. See [Building and testing](./docs/BUILDING.md) and [Architecture](./docs/ARCHITECTURE.md) for the complete build, game assembly tests, and extension boundaries.

## License and acknowledgements

The project uses the [Apache-2.0 license](./LICENSE). Third-party fonts and assets retain their own licensing and attribution; see [Third-party notices](./THIRD_PARTY_NOTICES.md).

---

<div align="center">
  <p>If this project helps you, consider giving it a <strong>Star</strong>.</p>
  <a href="https://github.com/RWRegionModdingCN/RainWright/stargazers">Star RainWright on GitHub</a>
</div>
