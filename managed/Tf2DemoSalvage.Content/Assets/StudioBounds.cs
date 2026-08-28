using System;
using System.Buffers.Binary;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>An axis-aligned box, in whatever space its producer was working in.</summary>
/// <param name="MinX">Lower corner.</param>
/// <param name="MinY">Lower corner.</param>
/// <param name="MinZ">Lower corner.</param>
/// <param name="MaxX">Upper corner.</param>
/// <param name="MaxY">Upper corner.</param>
/// <param name="MaxZ">Upper corner.</param>
public readonly record struct StudioBox(
    float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
{
    /// <summary>A box with no extent, which is what an unreadable model answers.</summary>
    public static StudioBox Empty => default;

    /// <summary>Whether either corner is away from the origin, Valve's own test.</summary>
    /// <remarks>
    /// **An OR across both corners, not an AND.** `GetRenderBounds` asks
    /// <c>!VectorCompare( vec3_origin, view_bbmin() ) || !VectorCompare( vec3_origin, view_bbmax() )</c>
    /// — so a clipping box whose lower corner sits exactly at the origin is still authored, and
    /// still wins over the hull. Reading it as an AND falls through to the hull for a whole class
    /// of ordinary models and nothing says so.
    /// </remarks>
    public bool IsAuthored =>
        MinX != 0f || MinY != 0f || MinZ != 0f || MaxX != 0f || MaxY != 0f || MaxZ != 0f;

    /// <summary>The smallest box containing both, as <c>VectorMin</c>/<c>VectorMax</c> give it.</summary>
    /// <param name="other">The box to take in.</param>
    /// <returns>The union.</returns>
    public StudioBox Union(StudioBox other) =>
        new(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Min(MinZ, other.MinZ),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY),
            Math.Max(MaxZ, other.MaxZ));

    /// <summary>The longest of the three axes — Valve's <c>fDimension</c>.</summary>
    /// <remarks>
    /// `MAX( MAX( fabs(dims.x), fabs(dims.y) ), fabs(dims.z) )` where `dims = absMaxs - absMins`.
    /// **The longest axis, not a volume or a diagonal**, so a tall thin lamp post counts as large:
    /// what fills the depth buffer is the silhouette's extent, and a post occludes a whole column
    /// of the screen.
    /// </remarks>
    public float LongestAxis =>
        Math.Max(Math.Max(Math.Abs(MaxX - MinX), Math.Abs(MaxY - MinY)), Math.Abs(MaxZ - MinZ));
}

/// <summary>
/// The bounds the engine draws a studio model by, which are authored fields rather than its mesh.
/// </summary>
/// <remarks>
/// **`C_BaseAnimating::GetRenderBounds` (`c_baseanimating.cpp:4533`), transcribed:**
///
/// <code>
/// if (!VectorCompare( vec3_origin, view_bbmin() ) || !VectorCompare( vec3_origin, view_bbmax() ))
///     theMins = view_bbmin(); theMaxs = view_bbmax();   // clipping bounding box
/// else
///     theMins = hull_min();  theMaxs = hull_max();      // movement bounding box
///
/// mstudioseqdesc_t &amp;seqdesc = pStudioHdr-&gt;pSeqdesc( GetSequence() );
/// VectorMin( seqdesc.bbmin, theMins, theMins );
/// VectorMax( seqdesc.bbmax, theMaxs, theMaxs );
/// </code>
///
/// **These are not the vertex extent, and the difference is the point.** The plan here was to take
/// each model's vertex bounds once when it is packed and call that its size — which is a different
/// number, cannot change with the animation, and is not what the engine asks. The owner stopped it:
/// *"do not simplify valve unless i give you permission and you explain why"*.
///
/// **The sequence union is why one number per model will not do.** A running player is bounded
/// differently from a crouched one, so the size a model buckets at depends on what it is doing.
///
/// **Model scale is not applied here.** Valve finishes with `theMaxs *= flScale` from
/// `GetModelScale()`, which is `m_flModelScale` on the entity — a networked property this project
/// does not decode. Left out rather than assumed to be one: a scaled model would bucket by its
/// unscaled size, which is a known gap rather than a silent approximation, and TF2 uses model
/// scaling on very little.
/// </remarks>
public static class StudioBounds
{
    /// <summary>The render bounds of a model in one sequence, in model space.</summary>
    /// <param name="file">The <c>.mdl</c> bytes.</param>
    /// <param name="sequence">Which sequence is playing; negative or out of range unions nothing.</param>
    /// <returns>The bounds, or <see cref="StudioBox.Empty"/> when the file cannot be read.</returns>
    /// <remarks>
    /// **Empty rather than a guess when the header is short.** A truncated model that answered a
    /// plausible box would bucket somewhere plausible and draw in the wrong order, which is
    /// invisible; an empty box buckets smallest and is at least consistent.
    /// </remarks>
    public static StudioBox RenderBounds(ReadOnlyMemory<byte> file, int sequence)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderViewBoundsMaxOffset + 12)
        {
            return StudioBox.Empty;
        }

        StudioBox clipping = Box(bytes, HeaderViewBoundsMinOffset, HeaderViewBoundsMaxOffset);

        StudioBox bounds = clipping.IsAuthored
            ? clipping
            : Box(bytes, HeaderHullMinOffset, HeaderHullMaxOffset);

        return SequenceBounds(bytes, sequence) is { } playing ? bounds.Union(playing) : bounds;
    }

    /// <summary>The movement hull, whether or not <see cref="RenderBounds"/> would use it.</summary>
    /// <param name="file">The <c>.mdl</c> bytes.</param>
    /// <returns>The hull box, or <see cref="StudioBox.Empty"/> for a short file.</returns>
    /// <remarks>
    /// **Exposed because otherwise it is untestable on real data.** Shifting
    /// <c>HeaderHullMinOffset</c> by four bytes was caught only by the SDK field-order check; every
    /// test against a real model stayed green, because the scout has an authored clipping box and
    /// its hull is never consulted. That is the wrong-condition failure — an input for which
    /// correct and broken predict the same observation — and the fix is a test that reads the hull
    /// directly rather than a stronger assertion on the box that happens to win.
    /// </remarks>
    public static StudioBox MovementHull(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < HeaderHullMaxOffset + 12
            ? StudioBox.Empty
            : Box(bytes, HeaderHullMinOffset, HeaderHullMaxOffset);
    }

    /// <summary>The clipping box, which wins when the modeller authored one.</summary>
    /// <param name="file">The <c>.mdl</c> bytes.</param>
    /// <returns>The clipping box, or <see cref="StudioBox.Empty"/> when none was authored.</returns>
    public static StudioBox ClippingBox(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < HeaderViewBoundsMaxOffset + 12
            ? StudioBox.Empty
            : Box(bytes, HeaderViewBoundsMinOffset, HeaderViewBoundsMaxOffset);
    }

    /// <summary>One sequence's own box, or null when there is no such sequence.</summary>
    /// <remarks>
    /// Null rather than an empty box, because an empty box would UNION to the origin and drag a
    /// model's bounds out to include it — a box centred on nothing, quietly enlarging every model
    /// whose sequence could not be read.
    /// </remarks>
    private static StudioBox? SequenceBounds(ReadOnlySpan<byte> bytes, int sequence)
    {
        if (sequence < 0 || bytes.Length < HeaderSequenceIndexOffset + 4)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceCountOffset..]);

        if (sequence >= count)
        {
            return null;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceIndexOffset..]);
        int start = at + (sequence * SequenceStride);

        if (start < 0 || start + SequenceStride > bytes.Length)
        {
            return null;
        }

        return Box(
            bytes, start + SequenceBoundsMinOffset, start + SequenceBoundsMaxOffset);
    }

    private static StudioBox Box(ReadOnlySpan<byte> bytes, int minimum, int maximum) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(bytes[minimum..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(minimum + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(minimum + 8)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[maximum..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(maximum + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(maximum + 8)..]));
}
