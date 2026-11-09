using System;
using System.Collections.Generic;

namespace BepInEx.Logging
{
    internal sealed class ManualLogSource
    {
        internal readonly List<string> Errors = new();
        internal void LogInfo(string message) { }
        internal void LogError(string message) => Errors.Add(message);
    }
}

namespace DryCycle.DevUI.DevTool.Map.PlayerMap
{
    internal static class Probe
    {
        internal static readonly List<string> Events = new();
        internal static string ThrowOnEnable;
        internal static string ThrowOnDisable;
        internal static int DisableFailures;
        internal static string ThrowOnReset;

        internal static void Clear()
        {
            Events.Clear();
            ThrowOnEnable = ThrowOnDisable = ThrowOnReset = null;
            DisableFailures = 0;
        }

        internal static void Add(string name)
        {
            Events.Add("+" + name);
            if (name == ThrowOnEnable) throw new InvalidOperationException("Injected enable failure: " + name);
        }

        internal static void Remove(string name)
        {
            Events.Add("-" + name);
            if (name == ThrowOnDisable && DisableFailures-- > 0)
                throw new InvalidOperationException("Injected cleanup failure: " + name);
        }

        internal static void Reset(string name)
        {
            Events.Add("reset:" + name);
            if (name == ThrowOnReset) throw new InvalidOperationException("Injected reset failure: " + name);
        }
    }

    internal static class PlayerMapWorkspaceRuntime
    {
        internal static void Enable() => Probe.Add("Workspace");
        internal static void Disable() => Probe.Remove("Workspace");
        internal static void Reset() => Probe.Reset("WorkspaceRuntime");
    }
    internal static class PlayerMapMigrationStreamRuntime
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("MigrationStream");
        internal static void Disable() => Probe.Remove("MigrationStream");
        internal static void Reset() => Probe.Reset("MigrationStreamRuntime");
    }
    internal static class PlayerMapPlacementBootstrap
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("Placement");
        internal static void Disable() => Probe.Remove("Placement");
    }
    internal static class PlayerMapIncrementalRenderHooks
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("IncrementalRender");
        internal static void Disable() => Probe.Remove("IncrementalRender");
    }
    internal static class PlayerMapTerrainBakeBridge
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("TerrainBake");
        internal static void Disable() => Probe.Remove("TerrainBake");
    }
    internal static class PlayerMapDerivedLayoutBridge
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("DerivedLayout");
        internal static void Disable() => Probe.Remove("DerivedLayout");
    }
    internal static class PlayerMapConfigBuildPipeline
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("ConfigBuild");
        internal static void Disable() => Probe.Remove("ConfigBuild");
    }
    internal static class PlayerMapDisabledConfigFilter
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("DisabledConfigFilter");
        internal static void Disable() => Probe.Remove("DisabledConfigFilter");
    }
    internal static class PlayerMapGroupCommandRuntime
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("GroupCommands");
        internal static void Disable() => Probe.Remove("GroupCommands");
    }
    internal static class PlayerMapMigrationCommandBridge
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("MigrationCommands");
        internal static void Disable() => Probe.Remove("MigrationCommands");
    }
    internal static class PlayerMapLayerMutationFilter
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("LayerMutation");
        internal static void Disable() => Probe.Remove("LayerMutation");
    }
    internal static class PlayerMapRenderRevisionGuard
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("RenderRevision");
        internal static void Disable() => Probe.Remove("RenderRevision");
    }
    internal static class PlayerMapRenderPreparationController
    {
        internal static void Enable(BepInEx.Logging.ManualLogSource log) => Probe.Add("RenderPreparation");
        internal static void Disable() => Probe.Remove("RenderPreparation");
        internal static bool IsRunning => false;
        internal static void Reset() => Probe.Reset("RenderPreparationController");
    }
    internal static class PlayerMapRenderScheduler
    {
        internal static bool IsRunning => false;
        internal static void Reset() => Probe.Reset("RenderScheduler");
    }
    internal static class PlayerMapMigrationCommandQueue
    {
        internal static void Clear() => Probe.Reset("MigrationCommandQueue");
    }
    internal static class PlayerMapGroupCommandQueue
    {
        internal static void Clear() => Probe.Reset("GroupCommandQueue");
    }
    internal static class PlayerMapTerrainSemanticRevision
    {
        internal static void Reset() => Probe.Reset("TerrainSemanticRevision");
    }
    internal static class PlayerMapPreflightDiagnostics
    {
        internal static void Reset() => Probe.Reset("PreflightDiagnostics");
    }
    internal static class RoomMapBakeCache
    {
        internal static void Clear() => Probe.Reset("RoomMapBakeCache");
    }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class BepInPlugin : Attribute
    {
        internal BepInPlugin(string id, string name, string version) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class BepInDependency : Attribute
    {
        internal enum DependencyFlags { HardDependency }
        internal BepInDependency(string guid, DependencyFlags flags = DependencyFlags.HardDependency) { }
    }

    public class BaseUnityPlugin
    {
        // The moved legacy adapters are now compiled as production source in
        // this test; BepInEx supplies a per-plugin logger at runtime.
        internal BepInEx.Logging.ManualLogSource Logger { get; } = new();
    }
}

namespace DryCycle
{
    internal static class Plugin
    {
        internal const string ModId = "DryCycle.Tests";
        internal const string Version = "1.0.0";
    }

    internal static class AuxiliaryPluginStartupGuard
    {
        internal static bool Enable(string name, Action enable, Action rollback)
        {
            try { enable(); return true; }
            catch
            {
                rollback();
                return false;
            }
        }

        internal static void Disable(string name, Action disable) => disable();
    }
}

namespace UnityEngine
{
    internal static class Time
    {
        internal static int frameCount;
    }
}
