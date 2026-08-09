namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Controls how much of a demo the trace writes out.
/// </summary>
public sealed class DemoTraceOptions
{
    /// <summary>
    /// Whether to expand each entity snapshot into its entities and their changed properties.
    /// </summary>
    /// <remarks>
    /// **Off by default, and the reason is scale rather than taste.** A demo carries millions of
    /// entity updates — 14.8 million across the nine measured, with 94 million property values —
    /// so expanding them turns a 39 MB demo into a multi-gigabyte text file. Defaulting to that
    /// would make the ordinary case unusable.
    ///
    /// Turning it on is what a *full* decompile means, in the sense `lmpc` uses: everything the
    /// file contains, in order, as text. Worth it when investigating one demo, or one stretch of
    /// one demo, and not otherwise.
    /// </remarks>
    public bool IncludeEntities { get; init; }

    /// <summary>
    /// Whether to list each entity's changed properties, rather than just the entity.
    /// </summary>
    /// <remarks>
    /// Only has effect when <see cref="IncludeEntities"/> is set. Separating the two makes the
    /// middle setting available: which entities a snapshot touched, without the property values
    /// that make up most of the volume.
    /// </remarks>
    public bool IncludeEntityProperties { get; init; } = true;

    /// <summary>Stop after this many entity snapshots, or zero for all of them.</summary>
    /// <remarks>
    /// The practical way to inspect a demo's entity stream without writing all of it: the first
    /// few hundred snapshots answer most questions about whether decoding is working.
    /// </remarks>
    public int EntitySnapshotLimit { get; init; }
}
