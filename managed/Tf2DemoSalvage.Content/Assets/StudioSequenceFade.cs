using System;
using System.Buffers.Binary;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How long a sequence takes to cross-fade in and out — <c>fadeintime</c> and <c>fadeouttime</c>.
/// </summary>
/// <remarks>
/// **These are the whole of `MaintainSequenceTransitions`' timing.** When an entity's sequence
/// changes, `CSequenceTransitioner::CheckForSequenceChange` (`sequence_Transitioner.cpp:46`) keeps
/// the OUTGOING one alive and fading:
///
/// <code>
///   currentblend->m_flLayerFadeOuttime = MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime );
/// </code>
///
/// so it takes one field from the sequence being left and one from the sequence being entered, and
/// neither alone is the answer. Without them every sequence change is a cut: a player who stops
/// running snaps from the run pose to the idle in one frame.
///
/// **Valve's default for both is 0.2 seconds** (`studio.h:854`), but the value is per sequence and
/// authored, so it is read rather than assumed — a sequence that says zero is asking not to be
/// faded, and `GetFadeout` returns zero weight for it immediately.
/// </remarks>
public static class StudioSequenceFade
{
    /// <summary>How long this sequence takes to blend in, in seconds.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="sequence">Which local sequence.</param>
    /// <returns>Its <c>fadeintime</c>, or zero when it cannot be read.</returns>
    public static float In(ReadOnlyMemory<byte> file, int sequence) =>
        Read(file, sequence, SequenceFadeInOffset);

    /// <summary>How long this sequence takes to blend out, in seconds.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="sequence">Which local sequence.</param>
    /// <returns>Its <c>fadeouttime</c>, or zero when it cannot be read.</returns>
    public static float Out(ReadOnlyMemory<byte> file, int sequence) =>
        Read(file, sequence, SequenceFadeOutOffset);

    /// <summary>The weight an outgoing sequence still has — <c>C_AnimationLayer::GetFadeout</c>.</summary>
    /// <param name="elapsed">Seconds since the sequence stopped being current.</param>
    /// <param name="fadeOut">How long it was given to fade, from <see cref="Out"/>.</param>
    /// <returns>Its weight, zero to one, and zero once it is finished.</returns>
    /// <remarks>
    /// **<c>animationlayer.h:84</c>**, splined rather than linear:
    ///
    /// <code>
    ///   s = 1.0 - (flCurTime - m_flLayerAnimtime) / m_flLayerFadeOuttime;
    ///   if (s &gt; 0 &amp;&amp; s &lt;= 1.0) s = 3 * s * s - 2 * s * s * s;
    ///   else if ( s &gt; 1.0f ) s = 1.0f;
    /// </code>
    ///
    /// **The <c>s &gt; 1</c> clamp is Valve guarding against its own clock**, with the comment
    /// *"Shouldn't happen, but maybe curtime is behind animtime?"* — and a demo viewer that can
    /// scrub backwards makes it happen routinely, so it is reproduced rather than trimmed.
    ///
    /// **A zero or negative fade is zero weight**, not an instant one: the engine's first branch
    /// sets `s = 0` outright, so a sequence authored with no fade-out disappears at once.
    /// </remarks>
    public static float Fadeout(double elapsed, float fadeOut)
    {
        if (fadeOut <= 0f)
        {
            return 0f;
        }

        double remaining = 1d - (elapsed / fadeOut);

        if (remaining > 1d)
        {
            return 1f;
        }

        return remaining > 0d
            ? (float)((3d * remaining * remaining) - (2d * remaining * remaining * remaining))
            : 0f;
    }

    /// <summary>Reads one float out of a sequence descriptor.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="sequence">Which local sequence.</param>
    /// <param name="offset">The field's byte offset within the descriptor.</param>
    /// <returns>The value, or zero when the sequence cannot be reached.</returns>
    private static float Read(ReadOnlyMemory<byte> file, int sequence, int offset)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (sequence < 0 || bytes.Length < HeaderSequenceIndexOffset + 4)
        {
            return 0f;
        }

        if (sequence >= BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceCountOffset..]))
        {
            return 0f;
        }

        int start = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceIndexOffset..]) +
            (sequence * SequenceStride);

        return start < 0 || start + SequenceStride > bytes.Length
            ? 0f
            : BinaryPrimitives.ReadSingleLittleEndian(bytes[(start + offset)..]);
    }
}
