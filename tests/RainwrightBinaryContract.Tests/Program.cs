using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass RainWright.dll, optionally --portable or an independent DryCycle.dll.");
bool portable = args.Skip(1).Contains("--portable");

using var stream = File.OpenRead(args[0]);
using var image = new PEReader(stream);
if (!image.HasMetadata || !image.PEHeaders.IsDll)
    throw new InvalidDataException("The net48 Rainwright output is not a managed DLL.");

MetadataReader meta = image.GetMetadataReader();
string owner = meta.GetString(meta.GetAssemblyDefinition().Name);
if (owner != "RainWright")
    throw new InvalidDataException("Wrong assembly identity: " + owner);

string[] referenced = meta.AssemblyReferences
    .Select(handle => meta.GetString(meta.GetAssemblyReference(handle).Name))
    .ToArray();

// This assertion examines the *actual deployable net48 PE metadata*, not
// net10-linked production sources or MSBuild's compile-item evaluation.
string[] prohibited = portable ? new[] {
    "DryCycle", "BepInEx", "UnityEngine", "UnityEngine.CoreModule",
    "UnityEngine.InputLegacyModule", "ImGui.NET", "rain-world-imgui-api",
    "PUBLIC-Assembly-CSharp", "HOOKS-Assembly-CSharp"
} : new[] { "DryCycle", "DryCycle.DevTool.RWImGui" };
foreach (string assembly in referenced)
{
    if (prohibited.Any(blocked =>
        assembly.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
        assembly.StartsWith(blocked + ".", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException(
            "RainWright.dll has a forbidden runtime dependency: " + assembly);
}

string[] types = meta.TypeDefinitions.Select(handle =>
    meta.GetString(meta.GetTypeDefinition(handle).Name)).ToArray();
string[] essential =
{
    "FrontendPluginOwnerGate`1",
    "FrontendHookLedger",
    "CreatureCatalogRuntimeLease",
    "OptionalFrontendDebugSection`1",
    "RetainedCleanupBatch",
    "WorldMapBackgroundSchedulingPolicy",
    "WorldMapDirtySet",
    "WorldMapViewTransform",
    "WorldMapViewTransformMailbox",
    "FrontendPageRegistryCore`2",
    "FrontendSessionVisibilityPolicy`1",
    "FrontendOwnedAtlasCleanup",
    "DevToolPageStatusState",
    "DevToolWorkspaceLayout",
    "WorldMapOrthogonalRouter",
    "WorldMapRoomSpatialCore",
    "WorldMapRouteSpatialCore",
    "WorldMapRouteCrossingCore",
    "ObjectSceneListPlanner",
    "SoundSceneSelectionCore",
    "SoundGroupWorkspaceCore",
    "CartographyPngStreamEncoder",
    "CartographySceneRetentionCache`2",
    "WorldDocument",
    "CartographyDocument",
    "RainWrightPersonalPreferences",
    "RainWrightWorkspacePreset"
};
foreach (string required in essential)
{
    if (!types.Contains(required, StringComparer.Ordinal))
        throw new InvalidDataException(
            "net48 DLL is missing its production Rainwright-owned type: " + required);
}

string[] resources = meta.ManifestResources.Select(handle =>
    meta.GetString(meta.GetManifestResource(handle).Name)).ToArray();
if (!resources.Any(name => name.StartsWith(
        "Rainwright.Cartography.", StringComparison.Ordinal) &&
        name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidDataException("Cartography sprites were not embedded in net48 Rainwright.dll.");

Console.WriteLine(
    $"PASS: physical net48 RainWright.dll contains {types.Length} owned types, " +
    $"{resources.Length} resources and {referenced.Length} allowed references; " +
    (portable ? "portable core has no game/native linkage." : "standalone editor has no DryCycle linkage."));

if (!portable)
{
    foreach (string required in new[] { "BridgePlugin", "EditorBackendLifecycle", "DevToolRuntime",
        "WorldTopologyRuntime", "WorldTopologyRegistry", "WorldConnectionSyntax", "AutoSaveCoordinator", "PlayerMapWorkspaceRuntime",
        "RainWrightOptions", "RainWrightWorkspaceRuntime", "RainWrightPresetsWindow", "RainWrightShortcutInput" })
        if (!types.Contains(required)) throw new InvalidDataException("Standalone editor is missing: " + required);
    foreach (string required in new[] { "BepInEx", "ImGui.NET", "rain-world-imgui-api", "HOOKS-Assembly-CSharp" })
        if (!referenced.Contains(required)) throw new InvalidDataException("Standalone editor runtime reference is missing: " + required);

    var guids = new HashSet<string>(StringComparer.Ordinal);
    bool mainEntry = false;
    foreach (var handle in meta.TypeDefinitions)
    {
        var type = meta.GetTypeDefinition(handle);
        foreach (var attributeHandle in type.GetCustomAttributes())
        {
            var attribute = meta.GetCustomAttribute(attributeHandle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = meta.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            string attributeName = meta.GetString(meta.GetTypeReference((TypeReferenceHandle)member.Parent).Name);
            if (attributeName is not ("BepInPlugin" or "BepInDependency")) continue;
            var value = meta.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) throw new InvalidDataException("Invalid plugin attribute.");
            string id = value.ReadSerializedString() ?? "";
            if (attributeName == "BepInDependency" && id == "Anno")
                throw new InvalidDataException("RainWright retains the Anno hard dependency.");
            if (attributeName == "BepInPlugin")
            {
                if (!guids.Add(id)) throw new InvalidDataException("Duplicate BepInEx GUID: " + id);
                if (meta.GetString(type.Name) == "BridgePlugin" && id == "rainwright") mainEntry = true;
            }
        }
    }
    if (!mainEntry) throw new InvalidDataException("RainWright has no standalone BepInEx entry.");

    var resourceRoot = image.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress;
    foreach (string font in new[] { "HarmonyOS_Sans_SC_Medium.ttf", "UbuntuMono-Regular.ttf" })
    {
        string name = "RainWright.Fonts." + font;
        var handle = meta.ManifestResources.SingleOrDefault(h => meta.GetString(meta.GetManifestResource(h).Name) == name);
        if (handle.IsNil) throw new InvalidDataException("Missing embedded font: " + name);
        var resource = meta.GetManifestResource(handle);
        var bytes = image.GetSectionData(resourceRoot + checked((int)resource.Offset)).GetReader();
        int size = bytes.ReadInt32();
        byte[] data = bytes.ReadBytes(size);
        if (size < 1024 || data[0] != 0 || data[1] != 1 || data[2] != 0 || data[3] != 0)
            throw new InvalidDataException("Invalid embedded TrueType font: " + name);
        string source = Path.GetFullPath(Path.Combine("src", "Rainwright", "Assets", "Fonts", font));
        if (File.Exists(source) && !data.AsSpan().SequenceEqual(File.ReadAllBytes(source)))
            throw new InvalidDataException("Embedded font differs from the unmodified source: " + name);
    }
    foreach (string license in new[] { "LICENSE-HarmonyOS.txt", "LICENSE-Ubuntu.txt" })
        if (!resources.Contains("RainWright.Fonts." + license))
            throw new InvalidDataException("Font license is not embedded: " + license);

    string? dryCycle = args.Skip(1).FirstOrDefault(File.Exists);
    if (dryCycle != null)
    {
        using var dryStream = File.OpenRead(dryCycle);
        using var dryImage = new PEReader(dryStream);
        var dryMeta = dryImage.GetMetadataReader();
        foreach (var reference in dryMeta.AssemblyReferences)
            if (dryMeta.GetString(dryMeta.GetAssemblyReference(reference).Name).StartsWith("RainWright", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DryCycle.dll still requires RainWright.");
        var editorTypes = meta.TypeDefinitions.Select(h => meta.GetTypeDefinition(h))
            .Select(t => meta.GetString(t.Namespace) + "." + meta.GetString(t.Name)).ToHashSet();
        foreach (var handle in dryMeta.TypeDefinitions)
        {
            var type = dryMeta.GetTypeDefinition(handle);
            string name = dryMeta.GetString(type.Namespace) + "." + dryMeta.GetString(type.Name);
            if (name.StartsWith("DryCycle.DevUI.DevTool.") && editorTypes.Contains(name))
                throw new InvalidDataException("Duplicate editor type identity in DryCycle: " + name);
        }
    }
    if (args.Length > 2 && File.Exists(args[2]))
    {
        using var adapterStream = File.OpenRead(args[2]);
        using var adapterImage = new PEReader(adapterStream);
        var adapterMeta = adapterImage.GetMetadataReader();
        var adapterReferences = adapterMeta.AssemblyReferences
            .Select(h => adapterMeta.GetString(adapterMeta.GetAssemblyReference(h).Name)).ToArray();
        if (!adapterReferences.Contains("RainWright") || !adapterReferences.Contains("DryCycle") ||
            adapterReferences.Contains("DryCycle.DevTool.RWImGui"))
            throw new InvalidDataException("Optional adapter must reference RainWright and DryCycle directly.");
        var requiredDependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in adapterMeta.TypeDefinitions)
        {
            var type = adapterMeta.GetTypeDefinition(handle);
            if (adapterMeta.GetString(type.Name) != "DryCycleWorldDataEditorPlugin") continue;
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = adapterMeta.GetCustomAttribute(attributeHandle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var member = adapterMeta.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference ||
                    adapterMeta.GetString(adapterMeta.GetTypeReference((TypeReferenceHandle)member.Parent).Name) != "BepInDependency") continue;
                var blob = adapterMeta.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid adapter dependency attribute.");
                string id = blob.ReadSerializedString() ?? "";
                // The enum overload writes HardDependency as int32 = 1.
                if (blob.ReadInt32() == 1) requiredDependencies.Add(id);
            }
        }
        if (!requiredDependencies.Contains("rainwright") || !requiredDependencies.Contains("Anno"))
            throw new InvalidDataException("Optional adapter must be gated on both gameplay and editor plugin GUIDs.");
        Console.WriteLine("PASS: optional adapter references both assemblies and is gated on both BepInEx owners.");
    }
    Console.WriteLine($"PASS: standalone BepInEx owner, {guids.Count} unique GUIDs, embedded fonts/licenses, independent gameplay assembly.");
}
