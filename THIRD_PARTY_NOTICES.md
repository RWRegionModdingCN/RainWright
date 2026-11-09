# Third-party notices

The repository's Apache-2.0 license does not replace licenses or ownership of third-party assets.

- **HarmonyOS Sans**: Copyright 2021 Huawei Device Co., Ltd. The original, unmodified font and its full license are kept in `src/Rainwright/Assets/Fonts/`. The game DLL embeds both the font and `LICENSE-HarmonyOS.txt`.
- **Ubuntu Mono**: the original font and `LICENSE-Ubuntu.txt` are kept in the same directory and embedded in the DLL. See the license for its copyright holders and terms.
- **Cornifer sprite sheets**: the Cartography atlases retain their upstream provenance in `src/Rainwright/Backend/Cartography/Assets/README.md`. These assets retain their original ownership.
- **Rain World, Unity, BepInEx, RWImGUI, HookGen, and optional Mod assemblies** are referenced from the user's installation. Game and third-party runtime DLLs are not included in this source repository or copied into the Mod package.

NuGet dependencies are declared in their owning projects. Reference assemblies and build-time helpers are not new game runtime dependencies.
