using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Portable bounded LRU of detached source snapshots. The caller supplies a
/// reader for game/mod data; Rainwright owns fingerprint validation and
/// cache lifetime without ever referring to room geometry or Unity.
/// </summary>
internal sealed class CartographySourceFingerprintCache<TSource>
{
    private sealed class Entry
    {
        internal string Fingerprint;
        internal TSource Source;
        internal long Used;
    }

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly int capacity;
    private long clock;

    internal CartographySourceFingerprintCache(int capacity = 4) =>
        this.capacity = Math.Max(1, capacity);

    internal int Count => entries.Count;

    /// <summary>
    /// Called by one background loader at a time. A cache hit never invokes
    /// decode. Cache entries are committed only when source files are stable.
    /// Context is passed explicitly to avoid closure allocations per lookup.
    /// </summary>
    internal TSource Load<TContext>(string identity, CartographyRegionFiles files,
        bool refresh, Func<CartographyRegionFiles, TContext, TSource> decode,
        TContext context, out bool hit)
    {
        string fingerprint = files.Fingerprint();
        hit = !refresh && entries.TryGetValue(identity, out Entry cached) &&
              cached.Fingerprint == fingerprint;
        if (hit)
        {
            Entry entry = entries[identity];
            entry.Used = ++clock;
            return entry.Source;
        }

        TSource source = decode(files, context);
        if (files.Fingerprint() != fingerprint)
            throw new IOException("Region files changed during loading; refresh again / 区域文件在读取期间发生变化，请刷新重试。");

        entries[identity] = new Entry { Fingerprint = fingerprint, Source = source, Used = ++clock };
        while (entries.Count > capacity)
            entries.Remove(entries.OrderBy(pair => pair.Value.Used).First().Key);
        return source;
    }
}
