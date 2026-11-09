using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using RWIMGUI.Interop;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// RWImGUI 1.12 has one native ImGui context; IMGUIContext only selects an input/render callback.
/// Its API has no pre-atlas-build event. Extend that external boundary immediately before the
/// DX11 backend builds/uploads the atlas, on the same Present thread that owns it.
/// </summary>
internal static unsafe class DevToolFontAtlasIntegration
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BackendNewFrame();

    // Keep the reverse-P/Invoke thunk rooted for as long as the native renderer can call it.
    private static readonly BackendNewFrame BeforeFrame = BeforeBackendFrame;
    private static readonly IntPtr BeforeFramePointer = Marshal.GetFunctionPointerForDelegate(BeforeFrame);
    private static ILHook hook;
    private static delegate* unmanaged[Cdecl]<void> originalNewFrame;
    private static ManualLogSource log;
    private static volatile bool enabled;
    private static bool failureLogged;

    // Hook.Dispose may throw while the detour is still attached. A live
    // pre-frame function pointer can also outlast the managed Hook instance.
    internal static bool PendingCleanup =>
        hook != null ||
        (IntPtr)ImGUIBackendInterface.ImGui_ImplDX11_NewFrame == BeforeFramePointer;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled && hook != null) return;
        if (PendingCleanup)
            throw new InvalidOperationException(
                "RWImGUI font integration has pending Hook/pointer cleanup; " +
                "another install would duplicate its native callback.");
        log = logger;
        enabled = true;
        failureLogged = false;
        try
        {
            // RWImGUI asks for this handle after loading its backend exports, before installing
            // Present. Hook only this ordinary IntPtr-returning method: Rain World's MonoMod cannot
            // copy Present's C# function-pointer locals (its reflection importer stack-overflows).
            MethodInfo getHandle = typeof(ImGUIBackendInterface).GetMethod("GetCImGUIAIOLibHandle", BindingFlags.Public | BindingFlags.Static);
            if (getHandle == null) throw new MissingMethodException(typeof(ImGUIBackendInterface).FullName, "GetCImGUIAIOLibHandle");
            // RWImGUI 1.12 exposes no atlas initialization event. Instrument only
            // its managed handle-return boundary; preserve the original return
            // value and attach before InitializeAll installs the native Present.
            hook = new ILHook(getHandle, il =>
            {
                var cursor = new ILCursor(il);
                while (cursor.TryGotoNext(MoveType.Before, instruction => instruction.OpCode == OpCodes.Ret))
                {
                    cursor.Emit(OpCodes.Dup);
                    cursor.EmitDelegate<Action<IntPtr>>(BackendHandleResolved);
                    cursor.Index++;
                }
            });

            // Normal startup reaches GetCImGUIAIOLibHandle later from RainWorld.Start. Some loader
            // orders resolve the export earlier, though; if the pointer is already available, arm
            // the pre-frame callback immediately instead of waiting for a method call that will
            // never happen again.
            AttachBeforeBackendFrame();

            log?.LogInfo(
                originalNewFrame != null
                    ? "DevTool font integration installed and attached to RWImGUI's backend pre-frame boundary."
                    : "DevTool font integration installed; waiting for RWImGUI's backend exports.");
        }
        catch (Exception error)
        {
            // Fonts are optional, but a partly installed detour is not.
            // Preserve the regular fallback font and attempt symmetric cleanup
            // before Bridge startup proceeds. If it fails, PendingCleanup
            // keeps the Bridge owner alive until a subsequent retry succeeds.
            enabled = false;
            try
            {
                Disable();
            }
            catch (Exception cleanupError)
            {
                log?.LogWarning(
                    "RWImGUI font integration rollback left a pending Hook: " + cleanupError);
            }
            log?.LogError(
                "DevTool font integration could not be installed; using RWImGUI's fallback font: " +
                error);
        }
    }

    internal static void Disable()
    {
        enabled = false;
        if ((IntPtr)ImGUIBackendInterface.ImGui_ImplDX11_NewFrame == BeforeFramePointer)
        {
            if (originalNewFrame == null)
                throw new InvalidOperationException(
                    "RWImGUI font callback cannot be restored: original backend pointer missing.");
            ImGUIBackendInterface.ImGui_ImplDX11_NewFrame = originalNewFrame;
        }

        // Clear the owned Hook only after MonoMod has actually released it.
        // If Dispose throws, keep the same Hook for retry and block a second
        // Bridge instance instead of silently losing its cleanup obligation.
        ILHook installedHook = hook;
        if (installedHook != null)
        {
            installedHook.Dispose();
            hook = null;
        }
    }

    private static void BackendHandleResolved(IntPtr handle)
    {
        if (enabled && handle != IntPtr.Zero) AttachBeforeBackendFrame();
    }

    private static void AttachBeforeBackendFrame()
    {
        var current = ImGUIBackendInterface.ImGui_ImplDX11_NewFrame;
        if (current == null || (IntPtr)current == BeforeFramePointer) return;
        originalNewFrame = current;
        ImGUIBackendInterface.ImGui_ImplDX11_NewFrame = (delegate* unmanaged[Cdecl]<void>)BeforeFramePointer;
        log?.LogInfo("DevTool font registration attached before RWImGUI's atlas upload on the Present thread.");
    }

    private static void BeforeBackendFrame()
    {
        try
        {
            if (enabled) DevToolFontCatalog.RegisterBeforeBackendFrame(log);
        }
        catch (Exception error)
        {
            // Never propagate a managed exception through RWImGUI's native callback boundary.
            if (!failureLogged) log?.LogError("DevTool font registration failed before atlas upload: " + error);
            failureLogged = true;
        }
        originalNewFrame();
    }
}
