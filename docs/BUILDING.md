# Building and testing

## Prerequisites

- .NET 10 SDK, selected by `global.json`.
- PowerShell 7 for the repository scripts.
- For the complete game plugin: a Rain World installation with BepInEx, `BepInEx/utils/PUBLIC-Assembly-CSharp.dll`, HookGen's `BepInEx/plugins/HOOKS-Assembly-CSharp.dll`, and RWImGUI 1.12.0 or later.
- POM is optional at runtime. The dedicated POM integration regression requires a local `Pom.dll`.

The game target is .NET Framework 4.8. Reference assemblies are restored from NuGet, while game, Unity, HookGen, BepInEx, and RWImGUI references come from the installation. No game binaries are redistributed.

## Configure local paths

Either pass `-RainWorldDir` to `scripts/Build.ps1`, set the `RainWorldDir` environment variable, or copy `Directory.Build.local.props.example` to the ignored `Directory.Build.local.props` and edit it.

RWImGUI is discovered in its Workshop and local Mod plugin directories. Use `-RWImGuiPluginDir` or the equivalent local MSBuild property if your installation uses another path.

## Build the complete Mod

```powershell
./scripts/Build.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
```

The DLL is `artifacts/bin/Rainwright/Release/RainWright.dll`. The installable directory is `artifacts/mod/RainWright/`. Build output stays inside this repository's ignored `artifacts/` directory. No DryCycle checkout is read.

```powershell
./scripts/Install.ps1 -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
```

Installation copies only package files, preserves unrelated author files, and backs up replaced package files under `artifacts/install-backups/`. If a copy fails, the script attempts to restore affected files and reports each recovery result. Restart Rain World to load an updated DLL.

## Run regressions

```powershell
# No game installation needed. Also used by CI.
./scripts/Test.ps1

# Windows, using real game assemblies and an installed POM.
./scripts/Test.ps1 -IncludeGameTests -RainWorldDir 'D:/Steam/steamapps/common/Rain World'
```

The portable suite compiles the actual net48 core, validates its PE dependencies and embedded resources, then compiles the same portable sources for net10 and executes the independent test projects. Production-linked suites cover author files, save recovery, topology, routing, map lifecycles, frontend ownership, settings, and related adapters.

Game assembly tests validate the full DLL, native Remix configuration, and object/POM integration without launching the game. They do not prove GPU rendering or mouse interaction inside Unity. The probes in `tests/MapPlaneGameProbe` and `tools/RainWrightMonoHost` are separate opt-in diagnostics.

## Build profiles

| Profile | MSBuild property | Output | Purpose |
| --- | --- | --- | --- |
| Complete game plugin | default | `artifacts/bin/Rainwright/Release/` | Installable BepInEx DLL |
| Portable net48 core | `RainWrightPortableBuild=true` | `.../Release/net48-core/` | CI and metadata verification |
| Portable net10 smoke | `RainwrightStandaloneSmokeTest=true` | `.../Release/net10-smoke/` | Executable core regressions |

The portable profiles omit the game integration and cannot replace the complete Mod. Their intermediate and output paths are isolated from the production DLL.

Use `RainWrightBuildRoot` to relocate generated files. `DeployToGame` defaults to `false`; installation is explicit. Existing namespace and assembly identities remain compatible with optional integrations.

## Protection tools

`scripts/Protect-RainWright.ps1` and `scripts/Restore-RainWrightStackTrace.ps1` retain the assembly protection and private symbol restoration workflows. Protection output stays outside the game and does not install itself. See `tools/RainWrightProtection/README.md`. Pass optional external consumers through `-AdditionalConsumers` when validating their binary contracts.
