using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// RWImGUI 1.12 rebuilds its localization registry every time RainWorld.OnModsInit is raised.
/// Rain World can legitimately run that lifecycle again after the initial mod setup. The current
/// RWImGUI localization builder is not idempotent and throws when the same culture is registered
/// twice. Guard only exact repeat loads that have already completed successfully.
/// </summary>
internal static class RwImGuiLocalizationReloadGuard
{
    private static readonly object Sync = new();

    private static ManualLogSource log;
    private static object harmony;
    private static string successfulSourceFingerprint = string.Empty;
    private static bool duplicateSuppressionLogged;

    internal static void Enable(ManualLogSource logger)
    {
        log = logger;

        lock (Sync)
        {
            if (harmony != null)
                return;

            Type managerType = FindLoadedType("RWIMGUI.Core.LocalizationManager");
            Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
            Type harmonyMethodType = FindLoadedType("HarmonyLib.HarmonyMethod");
            if (managerType == null || harmonyType == null || harmonyMethodType == null)
            {
                log?.LogWarning(
                    "RWImGUI localization reload guard was not installed because the required runtime types are unavailable.");
                return;
            }

            MethodInfo target = managerType
                .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "LoadAvailableLocales", StringComparison.Ordinal))
                        return false;

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(string[]);
                });

            MethodInfo prefix = typeof(RwImGuiLocalizationReloadGuard).GetMethod(
                nameof(LoadAvailableLocalesPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(RwImGuiLocalizationReloadGuard).GetMethod(
                nameof(LoadAvailableLocalesPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || prefix == null || postfix == null)
            {
                log?.LogWarning(
                    "RWImGUI localization reload guard could not resolve LoadAvailableLocales(string[]).");
                return;
            }

            try
            {
                object instance = Activator.CreateInstance(
                    harmonyType,
                    new object[] { "RainWright.RWImGUI.LocalizationReloadGuard" });
                object prefixMethod = Activator.CreateInstance(
                    harmonyMethodType,
                    new object[] { prefix });
                object postfixMethod = Activator.CreateInstance(
                    harmonyMethodType,
                    new object[] { postfix });

                MethodInfo patch = harmonyType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "Patch", StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 5 &&
                               typeof(MethodBase).IsAssignableFrom(parameters[0].ParameterType);
                    });

                if (patch == null)
                {
                    log?.LogWarning(
                        "RWImGUI localization reload guard could not resolve Harmony.Patch.");
                    return;
                }

                // Retain the patch identity before calling external Harmony
                // code: Patch may install handlers and still throw. The Bridge
                // ledger can then call Disable to undo a partial installation.
                harmony = instance;
                patch.Invoke(
                    instance,
                    new[] { (object)target, prefixMethod, postfixMethod, null, null });

                log?.LogInfo(
                    "RWImGUI localization reload guard installed; exact successful locale-source reloads will be skipped.");
            }
            catch (Exception error)
            {
                log?.LogWarning(
                    "RWImGUI localization reload guard installation failed: " +
                    Unwrap(error).Message);
                // Do not misreport a partial patch as an installed/healthy
                // optional feature. BridgePlugin will attempt scoped rollback.
                throw;
            }
        }
    }

    internal static void Disable()
    {
        lock (Sync)
        {
            if (harmony != null)
            {
                try
                {
                    MethodInfo unpatch = harmony.GetType().GetMethod(
                        "UnpatchSelf", BindingFlags.Instance | BindingFlags.Public);
                    if (unpatch == null)
                        throw new MissingMethodException(
                            "RWImGUI localization guard cannot resolve Harmony.UnpatchSelf.");
                    unpatch.Invoke(harmony, null);
                }
                catch (Exception error)
                {
                    // This reference still owns a possible live Harmony patch.
                    // Preserve it and propagate failure so the Bridge lifecycle
                    // ledger keeps the owner and retries on the next shutdown.
                    log?.LogWarning(
                        "RWImGUI localization reload guard cleanup failed: " +
                        Unwrap(error).Message);
                    throw;
                }
            }

            harmony = null;
            successfulSourceFingerprint = string.Empty;
            duplicateSuppressionLogged = false;
            log = null;
        }
    }

    private static bool LoadAvailableLocalesPrefix(string[] __0)
    {
        string fingerprint = BuildFingerprint(__0);

        lock (Sync)
        {
            if (string.IsNullOrEmpty(successfulSourceFingerprint) ||
                !string.Equals(
                    successfulSourceFingerprint,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!duplicateSuppressionLogged)
            {
                duplicateSuppressionLogged = true;
                log?.LogInfo(
                    "RWImGUI duplicate localization reload suppressed after repeated RainWorld.OnModsInit.");
            }

            return false;
        }
    }

    private static void LoadAvailableLocalesPostfix(string[] __0)
    {
        // Harmony postfixes are not executed when the original throws. Recording the fingerprint
        // here therefore means only a fully successful first localization load can suppress a
        // later identical request.
        string fingerprint = BuildFingerprint(__0);
        lock (Sync)
        {
            successfulSourceFingerprint = fingerprint;
            duplicateSuppressionLogged = false;
        }
    }

    private static string BuildFingerprint(string[] directories)
    {
        if (directories == null || directories.Length == 0)
            return "<empty>";

        return string.Join(
            "|",
            directories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        string normalized = (path ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .TrimEnd('/');

        return normalized;
    }

    private static Type FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            catch
            {
            }
        }

        return null;
    }

    private static Exception Unwrap(Exception error)
    {
        while (error is TargetInvocationException invocation &&
               invocation.InnerException != null)
        {
            error = invocation.InnerException;
        }

        return error ?? new InvalidOperationException("Unknown reflection failure.");
    }
}
