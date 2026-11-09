using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Palette;

internal sealed class PaletteFileListing
{
    internal sealed class Result
    {
        internal string Directory;
        internal string[] Files = Array.Empty<string>();
        internal bool Truncated;
        internal Exception Error;
    }
    private sealed class Request { internal string Directory; }
    private readonly object gate = new();
    private Request latest, pending;
    private Result completed;
    private bool running;
    internal bool Busy { get { lock (gate) return running; } }
    internal void Refresh(string directory)
    {
        lock (gate)
        {
            latest = pending = new Request { Directory = directory }; completed = null;
            if (running) return;
            running = true;
            try { if (!ThreadPool.QueueUserWorkItem(_ => Run())) throw new InvalidOperationException("Cannot start palette folder listing."); }
            catch { running = false; throw; }
        }
    }
    internal bool TryTake(out Result result)
    {
        lock (gate) { result = completed; completed = null; return result != null; }
    }
    private void Run()
    {
        for (;;)
        {
            Request request;
            lock (gate) { request = pending; pending = null; if (request == null) { running = false; return; } }
            var result = new Result { Directory = request.Directory };
            try
            {
                if (!string.IsNullOrEmpty(request.Directory))
                {
                    string[] names = Directory.EnumerateFiles(request.Directory, "*.png", SearchOption.TopDirectoryOnly)
                        .Take(1025).Select(Path.GetFileName).ToArray();
                    result.Truncated = names.Length > 1024;
                    result.Files = names.Take(1024).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            catch (Exception error) { result.Error = error; }
            lock (gate) { if (ReferenceEquals(latest, request)) completed = result; }
        }
    }
}
