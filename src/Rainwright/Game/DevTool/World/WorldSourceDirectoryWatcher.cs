using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Windows directory notifications without Mono's DefaultWatcher, whose recursive initial
/// snapshot walks every asset synchronously. The kernel supplies changes without a tree scan.
/// This adapter owns only notification handles; it never reads or writes author contents.
/// </summary>
internal sealed class WorldSourceDirectoryWatcher : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct OverlappedData
    {
        internal IntPtr Internal, InternalHigh;
        internal uint Offset, OffsetHigh;
        internal IntPtr Event;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateEventW")]
    private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ResetEvent")]
    private static extern bool ResetEvent(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadDirectoryChangesW")]
    private static extern bool ReadChanges(IntPtr handle, IntPtr buffer, uint length, bool subtree, uint filter, out uint returned, IntPtr overlapped, IntPtr completion);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetOverlappedResult")]
    private static extern bool GetResult(IntPtr handle, IntPtr overlapped, out uint returned, bool wait);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CancelIoEx")]
    private static extern bool CancelIo(IntPtr handle, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WaitForSingleObject")]
    private static extern uint Wait(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    private static extern bool Close(IntPtr handle);

    private const int BufferSize = 32768;
    private readonly object gate = new();
    private readonly string root;
    private readonly FileSystemEventHandler changed;
    private readonly ErrorEventHandler failed;
    private IntPtr directory, signal, buffer, overlapped;
    private bool pending;
    private int stopped;

    internal WorldSourceDirectoryWatcher(string root, FileSystemEventHandler changed, ErrorEventHandler failed)
    {
        this.root = root; this.changed = changed; this.failed = failed;
        try
        {
            directory = CreateFile(root, 1, 7, IntPtr.Zero, 3, 0x42000000, IntPtr.Zero);
            if (directory == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            signal = CreateEvent(IntPtr.Zero, true, false, null);
            if (signal == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            buffer = Marshal.AllocHGlobal(BufferSize);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OverlappedData)));
            Arm(); // Registered before the caller resolves sources; no notification gap.
            new Thread(Run) { IsBackground = true, Name = "RainWright source changes" }.Start();
        }
        catch { Release(); throw; }
    }

    private void Arm()
    {
        if (!ResetEvent(signal)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Marshal.StructureToPtr(new OverlappedData { Event = signal }, overlapped, false);
        if (!ReadChanges(directory, buffer, BufferSize, true, 0x11F, out _, overlapped, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 997) throw new Win32Exception(error);
        }
        pending = true;
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                if (Wait(signal, uint.MaxValue) != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                bool complete = GetResult(directory, overlapped, out uint bytes, false);
                int error = complete ? 0 : Marshal.GetLastWin32Error();
                byte[] data = null;
                lock (gate)
                {
                    pending = !complete && error == 996;
                    if (stopped != 0) break;
                    if (!complete) throw new Win32Exception(error);
                    if (bytes > BufferSize) throw new IOException("Invalid directory notification length.");
                    data = new byte[bytes];
                    if (bytes > 0) Marshal.Copy(buffer, data, 0, (int)bytes);
                    Arm();
                }
                if (data.Length == 0)
                    failed(this, new ErrorEventArgs(new InternalBufferOverflowException("World source directory notifications overflowed.")));
                else Dispatch(data);
            }
        }
        catch (Exception error)
        {
            if (Volatile.Read(ref stopped) == 0) failed(this, new ErrorEventArgs(error));
        }
        finally { Release(); }
    }

    private void Dispatch(byte[] data)
    {
        for (int offset = 0; offset <= data.Length - 12 && Volatile.Read(ref stopped) == 0;)
        {
            uint next = BitConverter.ToUInt32(data, offset);
            uint action = BitConverter.ToUInt32(data, offset + 4);
            uint length = BitConverter.ToUInt32(data, offset + 8);
            if ((length & 1) != 0 || length > data.Length - offset - 12)
                throw new IOException("Invalid directory notification name length.");
            string name = System.Text.Encoding.Unicode.GetString(data, offset + 12, (int)length);
            WatcherChangeTypes kind = action == 1 || action == 5 ? WatcherChangeTypes.Created
                : action == 2 || action == 4 ? WatcherChangeTypes.Deleted : WatcherChangeTypes.Changed;
            changed(this, new FileSystemEventArgs(kind, root, name));
            if (next == 0) break;
            if (next < 12 || next > data.Length - offset) throw new IOException("Invalid directory notification record offset.");
            offset += (int)next;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (stopped != 0) return;
            Volatile.Write(ref stopped, 1);
            // Cancellation completes the outstanding OVERLAPPED and signals the worker. Its
            // finally owns buffers/handles; the game thread never waits for a directory worker.
            if (pending && !CancelIo(directory, overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1168) Core.EditorDiagnostics.Warning("World source notification cancellation failed.", new Win32Exception(error));
            }
        }
    }

    private void Release()
    {
        lock (gate) Volatile.Write(ref stopped, 1);
        // Only construction failure or the worker reaches this boundary. Pending I/O must finish
        // before its unmanaged OVERLAPPED/buffer can be freed, including cancellation/error paths.
        if (pending && directory != IntPtr.Zero && directory != new IntPtr(-1))
        {
            CancelIo(directory, overlapped);
            GetResult(directory, overlapped, out _, true);
            pending = false;
        }
        if (directory != IntPtr.Zero && directory != new IntPtr(-1)) Close(directory);
        if (signal != IntPtr.Zero) Close(signal);
        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
    }
}
