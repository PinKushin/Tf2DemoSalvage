using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene;

/// <summary>Texture files read by material resolves, by path, counted where each read happens (finding 80).</summary>
public sealed class TextureReadTally
{
    private readonly ConcurrentDictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One path's reads.</summary>
    /// <param name="Reads">Every read of it.</param>
    /// <param name="Bytes">The file's length.</param>
    /// <param name="FrameReads">Reads for an animated texture's frame past the first.</param>
    public readonly record struct Entry(int Reads, long Bytes, int FrameReads);

    /// <summary>Every path read, with its counts.</summary>
    public IReadOnlyDictionary<string, Entry> ByPath => _byPath;

    /// <summary>Counts one read.</summary>
    /// <param name="path">The file read.</param>
    /// <param name="bytes">Its length.</param>
    /// <param name="laterFrame">Whether it was read for a frame past the first.</param>
    public void Record(string path, long bytes, bool laterFrame) =>
        _byPath.AddOrUpdate(
            path,
            _ => new Entry(1, bytes, laterFrame ? 1 : 0),
            (_, was) => new Entry(was.Reads + 1, bytes, was.FrameReads + (laterFrame ? 1 : 0)));

    /// <summary>All bytes read, the bytes of reads after a path's first, and those that were later frames.</summary>
    /// <returns>The three totals.</returns>
    public (long All, long Repeats, long LaterFrames) Totals() => (
        _byPath.Values.Sum(entry => entry.Bytes * entry.Reads),
        _byPath.Values.Sum(entry => entry.Bytes * (entry.Reads - 1)),
        _byPath.Values.Sum(entry => entry.Bytes * entry.FrameReads));
}
