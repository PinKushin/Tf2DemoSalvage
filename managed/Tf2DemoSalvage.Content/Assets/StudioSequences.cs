using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One of a model's sequences.</summary>
/// <param name="Animation">The local animation it plays, for a sequence with no blending.</param>
/// <param name="Flags">Its flags, of which looping is the one that matters here.</param>
public readonly record struct StudioSequence(int Animation, int Flags);

/// <summary>
/// A model's sequences, and which animation each one plays.
/// </summary>
/// <remarks>
/// **A demo networks <c>m_nSequence</c>, and that is not an animation index.** A sequence is a
/// layer above: it names one or more animations arranged in a blend grid, and the engine picks
/// between them with pose parameters. Treating the sequence number as an animation index draws
/// some other animation of the same model — motion that looks deliberate and is wrong.
///
/// The lookup is Valve's <c>mstudioseqdesc_t::anim</c>: a short array at <c>animindexindex</c>,
/// indexed <c>y * groupsize[0] + x</c> with both clamped into the group. This reads the corner of
/// that grid, which is the whole of it for an unblended sequence — a health pack bobbing, a door
/// sliding, a capture point's hologram turning.
///
/// **Blends are not resolved, and that is stated rather than hidden.** A blended sequence needs
/// pose parameters the demo does not carry for a prop, and taking the corner is the same choice
/// the engine makes when every parameter is zero.
/// </remarks>
public static class StudioSequences
{
    /// <summary><c>studiohdr_t.numlocalseq</c> and <c>localseqindex</c>.</summary>
    /// <remarks>
    /// Immediately after <c>numlocalanim</c>/<c>localanimindex</c> at 180 and 184, and immediately
    /// before <c>activitylistversion</c> and then <c>numtextures</c> at 204 — which this project
    /// already reads and has verified against real files.
    /// </remarks>
    private const int SequenceCountOffset = 188;
    private const int SequenceIndexOffset = 192;

    /// <summary>
    /// Bytes per <c>mstudioseqdesc_t</c>, summing <c>studio.h</c>'s field list: through
    /// <c>numactivitymodifiers</c> at 188 and <c>unused[5]</c>.
    /// </summary>
    private const int SequenceStride = 212;

    private const int FlagsOffset = 12;
    private const int AnimationIndexOffset = 60;
    private const int GroupSizeOffset = 68;

    /// <summary>Most sequences a model may declare, as a guard against a malformed header.</summary>
    private const int MaximumSequences = 4096;

    /// <summary>Reads a model's sequences.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The sequences in order, so <c>m_nSequence</c> indexes this list directly.</returns>
    public static IReadOnlyList<StudioSequence> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < SequenceIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[SequenceCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[SequenceIndexOffset..]);

        if (count <= 0 || count > MaximumSequences)
        {
            return [];
        }

        if (at < 0 || (long)at + ((long)count * SequenceStride) > bytes.Length)
        {
            return [];
        }

        List<StudioSequence> sequences = new(count);

        for (int index = 0; index < count; index++)
        {
            int start = at + (index * SequenceStride);
            ReadOnlySpan<byte> sequence = bytes.Slice(start, SequenceStride);

            int flags = BinaryPrimitives.ReadInt32LittleEndian(sequence[FlagsOffset..]);
            int blends = BinaryPrimitives.ReadInt32LittleEndian(sequence[AnimationIndexOffset..]);
            int groupX = BinaryPrimitives.ReadInt32LittleEndian(sequence[GroupSizeOffset..]);
            int groupY = BinaryPrimitives.ReadInt32LittleEndian(sequence[(GroupSizeOffset + 4)..]);

            // **The offsets are relative to the sequence description, not to the file.** Every
            // index inside a studio structure is measured from the structure itself, which is the
            // convention that bites hardest because a file-relative read still lands on data.
            int table = start + blends;

            sequences.Add(new StudioSequence(
                groupX > 0 && groupY > 0 && table >= 0 && table + 2 <= bytes.Length
                    ? BinaryPrimitives.ReadInt16LittleEndian(bytes[table..])
                    : 0,
                flags));
        }

        return sequences;
    }

    /// <summary>Which frame a cycle lands on.</summary>
    /// <param name="cycle">How far through the sequence, where one is the end.</param>
    /// <param name="frames">How many frames the animation has.</param>
    /// <returns>A frame index inside the animation.</returns>
    /// <remarks>
    /// **Wrapped rather than clamped, because a cycle is a phase.** <c>m_flCycle</c> is
    /// interpolated between packets and a looping interpolation can carry it just past one;
    /// clamping there stalls every looping animation for a frame at its end, which reads as a
    /// stutter rather than as a defect.
    ///
    /// **Cycle exactly one is the end rather than the start**, which is the one value where
    /// wrapping and clamping disagree. A non-looping sequence stops there and must hold its last
    /// pose; a looping one is wrapped below one before it arrives, so nothing reaches this by the
    /// looping route.
    ///
    /// A single-frame animation answers zero without dividing, since <c>frames - 1</c> is the
    /// divisor and a static prop has exactly one frame — that division would send every vertex of
    /// it to NaN and lose the model.
    /// </remarks>
    public static int FrameFor(float cycle, int frames)
    {
        if (frames <= 1)
        {
            return 0;
        }

        if (!float.IsFinite(cycle))
        {
            return 0;
        }

        // **Exactly one is the END, not the beginning again.** A non-looping sequence finishes at
        // cycle one and has to hold its final pose; wrapping there snaps it back to its first
        // frame for one frame of playback. A looping sequence never reaches one, because the
        // interpolation wraps it below one first - so the two cases do not collide.
        float wrapped = cycle is >= 0f and <= 1f ? cycle : cycle - MathF.Floor(cycle);

        return Math.Clamp((int)MathF.Round(wrapped * (frames - 1)), 0, frames - 1);
    }
}
