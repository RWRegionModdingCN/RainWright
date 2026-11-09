using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DryCycle.DevUI.DevTool.RWImGui;

// The Windows shell owns navigation and enumeration. One STA worker keeps its modal message
// loop away from Unity/ImGui; only an immutable result crosses back to the editor.
internal static class NativeFolderPicker
{
    internal sealed class Result { internal string Path; internal Exception Error; }
    private static Result result;
    private static int busy, generation;
    internal static bool Supported => Environment.OSVersion.Platform == PlatformID.Win32NT;
    internal static bool Busy => Volatile.Read(ref busy) != 0;
    internal static void Abandon() { Interlocked.Increment(ref generation); Interlocked.Exchange(ref result, null); }
    internal static bool TryTake(out Result completed) { completed = Interlocked.Exchange(ref result, null); return completed != null; }

    internal static void Open(string initial, string title)
    {
        if (!Supported) throw new PlatformNotSupportedException("The system folder dialog requires Windows.");
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        int request = Volatile.Read(ref generation);
        IntPtr owner = GetForegroundWindow(); GetWindowThreadProcessId(owner, out uint process);
        if (process != GetCurrentProcessId()) owner = IntPtr.Zero;
        try
        {
            var thread = new Thread(() =>
            {
                var completed = new Result();
                try { using var dialog = new WindowsFolderDialog(initial, title); completed.Path = dialog.Show(owner); }
                catch (Exception error) { completed.Error = error; }
                finally
                {
                    if (request == Volatile.Read(ref generation)) Interlocked.Exchange(ref result, completed);
                    Volatile.Write(ref busy, 0);
                }
            }) { IsBackground = true, Name = "RainWright folder selection" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        catch { Volatile.Write(ref busy, 0); throw; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
}

// Raw COM ABI calls also work in the game's Mono runtime, without System.Windows.Forms
// or a managed COM-interoperability dependency. Every acquired pointer stays on its STA.
internal sealed class WindowsFolderDialog : IDisposable
{
    private IntPtr dialog;
    private bool initialized;
    private static readonly Guid FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid OpenDialogInterface = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    internal WindowsFolderDialog(string initial, string title)
    {
        Check(CoInitializeEx(IntPtr.Zero, 2)); initialized = true;
        try
        {
            Guid clsid = FileOpenDialog, iid = OpenDialogInterface;
            Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out dialog));
            Check(Method<SetOptions>(dialog, 9)(dialog, 0x20u | 0x40u | 0x800u | 0x8u | 0x02000000u));
            Check(Method<SetText>(dialog, 17)(dialog, title ?? "Select folder"));
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            {
                Guid shell = ShellItemInterface;
                Check(SHCreateItemFromParsingName(Path.GetFullPath(initial), IntPtr.Zero, ref shell, out IntPtr item));
                try { Check(Method<SetItem>(dialog, 12)(dialog, item)); }
                finally { Release(item); }
            }
        }
        catch { Dispose(); throw; }
    }

    internal string Show(IntPtr owner)
    {
        int hr = Method<ShowDialog>(dialog, 3)(dialog, owner);
        if (hr == unchecked((int)0x800704C7)) return null;
        Check(hr); return ReadFolder(true);
    }

    private string ReadFolder(bool selected)
    {
        Check(Method<GetItem>(dialog, selected ? 20 : 13)(dialog, out IntPtr item));
        try
        {
            Check(Method<GetName>(item, 5)(item, 0x80058000, out IntPtr name));
            try { return Marshal.PtrToStringUni(name); }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Release(item); }
    }

    public void Dispose()
    {
        if (dialog != IntPtr.Zero) { Release(dialog); dialog = IntPtr.Zero; }
        if (initialized) { CoUninitialize(); initialized = false; }
    }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static T Method<T>(IntPtr instance, int slot) where T : class =>
        (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T));
    private static void Release(IntPtr instance) { if (instance != IntPtr.Zero) Method<ReleaseObject>(instance, 2)(instance); }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseObject(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetOptions(IntPtr self, uint options);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int SetText(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetItem(IntPtr self, IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetItem(IntPtr self, out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ShowDialog(IntPtr self, IntPtr owner);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetName(IntPtr self, uint kind, out IntPtr text);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr result);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, out IntPtr item);
}
