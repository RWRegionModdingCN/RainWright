# Project boundaries

RainWright owns the complete editor plugin, world topology runtime, authoring documents, command queues, history, save recovery, map and Cartography backends, UI, and embedded resources.

- `Backend/` and `Runtime/` contain the portable production core.
- `Game/` connects that core to Rain World, BepInEx, and RWImGUI.
- `mod/` owns the Mod's metadata, translations, thumbnails, and artwork.
- `src/Shared/AuthoringSourcePaths.cs` is bundled with this checkout, so building does not read a parent repository.

The assembly identity is `RainWright`; the BepInEx plugin ID is `rainwright`. Historical `DryCycle.DevUI.DevTool.*` namespace names are retained for binary/source compatibility. They do not create a DryCycle assembly dependency.

DryCycle / Ancient Site weather, temperature, terrain, and gameplay diagnostics remain owned by that separate project's optional adapter, `DryCycle.WorldData.RWImGui`. That adapter consumes RainWright's extension points when both Mods are installed. It is not included in this repository or needed by the standalone build.

Authoring state is saved by its document boundary. UI and compatibility code publish commands and detached snapshots, while runtime visual state remains derived. User content and unsaved changes are not replaced by build or install operations.

This project was extracted from [NineteenReincarnation/DryCycle](https://github.com/NineteenReincarnation/DryCycle) on 2026-11-09. Existing code, tests, fonts, atlas resources, translations, and artwork were carried over; this repository's initial license and history were preserved. DryCycle-specific integration tests remain with DryCycle.
