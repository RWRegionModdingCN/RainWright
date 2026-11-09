using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Release only atlases owned by this frontend. Failed unloads retain their
/// names so a later Bridge shutdown can retry instead of leaking native atlases.
/// </summary>
internal static class FrontendOwnedAtlasCleanup
{
    internal static bool Release(
        ICollection<string> loaded,
        Func<string, bool> exists,
        Action<string> unload,
        Action<string, Exception> report = null)
    {
        if (loaded == null) throw new ArgumentNullException(nameof(loaded));
        if (exists == null) throw new ArgumentNullException(nameof(exists));
        if (unload == null) throw new ArgumentNullException(nameof(unload));

        bool complete = true;
        foreach (string atlas in new List<string>(loaded))
        {
            try
            {
                if (exists(atlas))
                    unload(atlas);
                loaded.Remove(atlas);
            }
            catch (Exception error)
            {
                complete = false;
                try { report?.Invoke(atlas, error); }
                catch (Exception) { /* Keep original unload failure visible to caller. */ }
            }
        }
        return complete && loaded.Count == 0;
    }
}
