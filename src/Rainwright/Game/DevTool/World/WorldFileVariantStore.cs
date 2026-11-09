using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DryCycle.DevUI.DevTool.Extensions;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>Single persistence boundary for the standard world and its opt-in variants.</summary>
internal static class WorldFileVariantStore
{
    private sealed class State
    {
        internal string Region, Source, Extra, Combined, Topology;
        internal byte[] SourceBytes, ExtraBytes, CombinedBytes;
        internal string LegacyTopologyPath;
        internal byte[] LegacyTopologyBytes;
    }
    private static readonly Dictionary<string, State> states = new(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static State active;

    internal static string Load(string region, string source, string standard)
    {
        string extra = WorldModificationWorkspace.VariantPath(region), combined = WorldModificationWorkspace.VariantPath(region, true);
        Action<string> authorize = path => WorldModificationWorkspace.AuthorizeRegionMigration(region, path);
        WorldVariantFormatMigration.Migrate(standard, extra, WorldModificationWorkspace.LegacyVariantPaths(region, false), authorize);
        WorldVariantFormatMigration.Migrate(standard, combined, WorldModificationWorkspace.LegacyVariantPaths(region, true), authorize);
        // One bounded inspection of this region, never a per-frame or all-Mod directory scan.
        if (!WorldModificationWorkspace.IsPatch(source))
            Map.LegacyMapLayoutMigration.MigrateRegion(Path.GetDirectoryName(source), region, authorize,
                error => Core.EditorDiagnostics.Warning("Legacy region-file compatibility migration failed; original data was preserved: " + error));
        var state = new State { Region = region, Source = source, Extra = extra, Combined = combined,
            SourceBytes = WorldVariantFileTransaction.ReadExisting(source), ExtraBytes = WorldVariantFileTransaction.ReadExisting(extra), CombinedBytes = WorldVariantFileTransaction.ReadExisting(combined) };
        string result = standard;
        if (state.ExtraBytes != null)
        {
            string text = new UTF8Encoding(false, true).GetString(state.ExtraBytes);
            result = WorldFileVariants.Overlay(standard, text); state.Topology = WorldFileVariants.Topology(text);
        }
        else if (state.CombinedBytes != null)
        {
            string text = new UTF8Encoding(false, true).GetString(state.CombinedBytes);
            result = WorldFileVariants.Overlay(standard, text); state.Topology = WorldFileVariants.Topology(text);
        }
        string legacy = Path.Combine(WorldModificationWorkspace.RegionModRoot, "world", "WorldTopology.json");
        if (File.Exists(legacy))
        {
            state.Topology ??= WorldTopologyRegistry.SerializeAuthoringState();
            result = WorldTopologyRegistry.OverlayLegacyRoutes(region, result);
            state.LegacyTopologyPath = legacy; state.LegacyTopologyBytes = File.ReadAllBytes(legacy);
        }
        states[source] = state;
        active = state;
        return result;
    }

    internal static void Save(string region, string source, string authored, string topology = null)
    {
        if (!states.TryGetValue(source, out var state))
        { Load(region, source, WorldAuthoringPathResolver.ReadAllText(source)); state = states[source]; }
        var projection = WorldFileVariants.Project(authored);
        string topologyPayload = topology ?? state.Topology;
        string extra = projection.RequiresExtension ? WorldFileVariants.Encode(projection.Extended, topologyPayload) : null;
        string dryCycle = projection.RequiresExtension ? EditorRegionVariantContributors.CaptureDryCycle(region) : null;
        // An absent optional provider cannot establish that the author's optional data is unused.
        if (projection.RequiresExtension && !EditorRegionVariantContributors.HasDryCycle && state.CombinedBytes != null)
            dryCycle = WorldFileVariants.DryCycle(new UTF8Encoding(false, true).GetString(state.CombinedBytes));
        string combined = projection.RequiresExtension && !string.IsNullOrEmpty(dryCycle)
            ? WorldFileVariants.Encode(projection.Extended, topologyPayload, dryCycle) : null;
        WorldVariantFileTransaction.Verify(source, state.SourceBytes);
        WorldVariantFileTransaction.Verify(state.Extra, state.ExtraBytes);
        WorldVariantFileTransaction.Verify(state.Combined, state.CombinedBytes);
        // Refuse to remove an arbitrary user file even if it happens to have our reserved filename.
        foreach (byte[] existing in new[] { state.ExtraBytes, state.CombinedBytes })
            if (existing != null && !WorldFileVariants.IsOwned(new UTF8Encoding(false, true).GetString(existing)))
                throw new IOException("Unrecognized extension file; original data was preserved.");

        WorldModificationWorkspace.Write(source, projection.Standard, (basePath, basePayload) =>
        {
            var writes = new List<WorldVariantFileWrite>();
            AddChanged(writes, state.Extra, extra, state.ExtraBytes);
            AddChanged(writes, state.Combined, combined, state.CombinedBytes);
            AddChanged(writes, basePath, basePayload, state.SourceBytes);
            WorldVariantFileTransaction.Commit(writes, path =>
            {
                if (string.Equals(path, basePath, StringComparison.OrdinalIgnoreCase))
                {
                    string current = WorldAuthoringPathResolver.ResolveExistingSource("world/" + region.ToLowerInvariant() + "/world_" + region.ToLowerInvariant() + ".txt");
                    if (!string.Equals(current, source, StringComparison.OrdinalIgnoreCase)) throw new IOException("World author owner changed.");
                }
                else WorldModificationWorkspace.AuthorizeVariant(region, path);
            });
        });
        state.SourceBytes = WorldVariantFileTransaction.ReadExisting(source);
        state.ExtraBytes = extra == null ? null : WorldVariantFileTransaction.Bytes(extra);
        state.CombinedBytes = combined == null ? null : WorldVariantFileTransaction.Bytes(combined);
        state.Topology = topologyPayload;
        if (state.LegacyTopologyPath != null)
        {
            bool complete = true;
            foreach (string other in WorldTopologyRegistry.RegionNames())
            {
                if (other.Equals(region, StringComparison.OrdinalIgnoreCase)) continue;
                string directory = Path.Combine(WorldModificationWorkspace.RegionModRoot, "world", other.ToLowerInvariant());
                string world = Path.Combine(directory, "world_" + other.ToLowerInvariant() + ".txt");
                if (!File.Exists(world)) { complete = false; break; }
                string overlaid = WorldTopologyRegistry.OverlayLegacyRoutes(other, File.ReadAllText(world));
                if (WorldFileVariants.Project(overlaid).RequiresExtension)
                {
                    string variantPath = Path.Combine(directory, "rainwright", "world_" + other.ToLowerInvariant() + "_rainwright.xml");
                    if (!File.Exists(variantPath) || WorldFileVariants.Overlay(File.ReadAllText(world), File.ReadAllText(variantPath)) != overlaid)
                    { complete = false; break; }
                }
            }
            if (complete) { WorldModificationWorkspace.ArchiveLegacy(state.LegacyTopologyPath, state.LegacyTopologyBytes); state.LegacyTopologyPath = null; state.LegacyTopologyBytes = null; }
        }
    }

    private static void AddChanged(List<WorldVariantFileWrite> writes, string path, string text, byte[] existing)
    {
        if (text == null && existing == null) return;
        if (text != null && existing != null && new UTF8Encoding(false, true).GetString(existing) == text) return;
        writes.Add(new WorldVariantFileWrite { Path = path, Text = text, Expected = existing });
    }

    internal static bool SaveTopology(string payload)
    {
        string region = WorldModificationWorkspace.RegionContext;
        if (!WorldTextRegistry.EnsureLoaded(region)) throw new IOException(WorldTextRegistry.LoadError);
        Save(region, WorldTextRegistry.LoadedPath, WorldTextRegistry.AuthorText, payload);
        return true;
    }

    internal static bool RefreshCombined()
    {
        if (active?.ExtraBytes == null || !string.Equals(active.Source, WorldTextRegistry.LoadedPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(WorldTextRegistry.AuthorText)) return true;
        Save(active.Region, active.Source, WorldTextRegistry.AuthorText, active.Topology);
        return true;
    }
    internal static bool NeedsMigration(string source) => states.TryGetValue(source, out var state) && state.LegacyTopologyPath != null;

    internal static void CaptureRecovery(string document, string source, string authored, Action<string, string, string> capture)
    {
        var projected = WorldFileVariants.Project(authored);
        capture(document + "/world.txt", source, projected.Standard);
        if (!projected.RequiresExtension) return;
        string region = WorldTextRegistry.LoadedRegion;
        string topology = states.TryGetValue(source, out var state) ? state.Topology : null;
        capture(document + "/RainWright", WorldModificationWorkspace.VariantPath(region), WorldFileVariants.Encode(projected.Extended, topology));
        string dryCycle = EditorRegionVariantContributors.CaptureDryCycle(region);
        if (!string.IsNullOrEmpty(dryCycle)) capture(document + "/RainWrightDryCycle", WorldModificationWorkspace.VariantPath(region, true), WorldFileVariants.Encode(projected.Extended, topology, dryCycle));
    }

    internal static string TopologyReadPath()
    {
        if (!WorldModificationWorkspace.HasRegionOwner) return null;
        string extra = WorldModificationWorkspace.VariantPath(WorldModificationWorkspace.RegionContext);
        if (File.Exists(extra)) return extra;
        string combined = WorldModificationWorkspace.VariantPath(WorldModificationWorkspace.RegionContext, true);
        if (File.Exists(combined)) return combined;
        foreach (bool dryCycle in new[] { false, true })
            foreach (string old in WorldModificationWorkspace.LegacyVariantPaths(WorldModificationWorkspace.RegionContext, dryCycle))
                if (File.Exists(old)) return old;
        // Historical multi-region JSON remains read-only until every region has been migrated.
        string legacy = Path.Combine(WorldModificationWorkspace.RegionModRoot, "world", "WorldTopology.json");
        return File.Exists(legacy) ? legacy : null;
    }
}
