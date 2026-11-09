using System.Runtime.InteropServices;

// Standalone child process, never the Unity player. Only the supplied managed probe is executed.
// Uses the documented Mono embedding API; a missing/incompatible runtime fails the build gate.
if (args.Length < 3) throw new ArgumentException("game-directory probe.exe [probe arguments...]");
string game = Path.GetFullPath(args[0]);
string runtime = Path.Combine(game, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll");
IntPtr library = NativeLibrary.Load(runtime);
T Api<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
var setDirs = Api<SetDirs>("mono_set_dirs");
var setAssemblies = Api<SetPath>("mono_set_assemblies_path");
var init = Api<Init>("mono_jit_init_version");
var open = Api<Open>("mono_domain_assembly_open");
var exec = Api<Exec>("mono_jit_exec");
setDirs(Path.Combine(game, "RainWorld_Data", "Managed"), Path.Combine(game, "MonoBleedingEdge", "etc"));
setAssemblies(Path.Combine(game, "RainWorld_Data", "Managed"));
IntPtr domain = init("RainWright-isolated-protection-probe", "v4.0.30319");
if (domain == IntPtr.Zero) throw new InvalidOperationException("Mono initialization failed.");
string probe = Path.GetFullPath(args[1]);
IntPtr assembly = open(domain, probe);
if (assembly == IntPtr.Zero) throw new InvalidOperationException("Mono could not load the standalone probe.");
var values = new[] { probe }.Concat(args.Skip(2)).Select(Marshal.StringToCoTaskMemUTF8).ToArray();
IntPtr argv = Marshal.AllocHGlobal(values.Length * IntPtr.Size);
try
{
    Marshal.Copy(values, 0, argv, values.Length);
    Environment.ExitCode = exec(domain, assembly, values.Length, argv);
}
finally
{
    Marshal.FreeHGlobal(argv); foreach (IntPtr value in values) Marshal.FreeCoTaskMem(value);
}
// No attempt to reload Mono in this process; process exit releases the isolated runtime.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetDirs([MarshalAs(UnmanagedType.LPUTF8Str)] string lib, [MarshalAs(UnmanagedType.LPUTF8Str)] string etc);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Init([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Open(IntPtr domain, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Exec(IntPtr domain, IntPtr assembly, int argc, IntPtr argv);
