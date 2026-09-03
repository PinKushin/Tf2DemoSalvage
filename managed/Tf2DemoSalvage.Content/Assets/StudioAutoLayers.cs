using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One sequence layered automatically over another — <c>mstudioautolayer_t</c>.
/// </summary>
/// <param name="Sequence">Which sequence to layer, RELATIVE to the parent's model group.</param>
/// <param name="PoseParameter">Which pose parameter drives it, when <c>STUDIO_AL_POSE</c> is set.</param>
/// <param name="Flags">The <c>STUDIO_AL_*</c> bits, which decide how the envelope is shaped.</param>
/// <param name="Start">Where influence begins.</param>
/// <param name="Peak">Where full influence begins.</param>
/// <param name="Tail">Where full influence ends.</param>
/// <param name="End">Where influence ends.</param>
/// <remarks>
/// **A sequence can automatically play OTHER sequences over itself**, each with its own envelope
/// across the parent's cycle: ramping in between <see cref="Start"/> and <see cref="Peak"/>, held
/// between <see cref="Peak"/> and <see cref="Tail"/>, ramping out to <see cref="End"/>.
/// <c>AccumulatePose</c> applies them right after the main blend (<c>bone_setup.cpp:2449</c>).
///
/// **Measured before being implemented, and unlike the procedural rules this one is used**: 1 of 76
/// sequences on `koth_harvest_final` and 6 of 142 on `cp_fulgur` declare autolayers, where all four
/// unimplemented `CalcProceduralBone` rules measured zero on the same demos (B294).
///
/// **<see cref="Start"/> and the rest are in the PARENT's cycle, not the layer's** — except under
/// <c>STUDIO_AL_POSE</c>, where they are in the pose parameter's own range. The same four numbers
/// mean two different things depending on one flag, which is the detail most likely to be read
/// past.
/// </remarks>
public readonly record struct StudioAutoLayer(
    int Sequence,
    int PoseParameter,
    int Flags,
    float Start,
    float Peak,
    float Tail,
    float End)
{
    /// <summary>Whether the ramp is a spline rather than a straight line.</summary>
    public bool IsSpline => (Flags & StudioAutoLayerFlags.Spline) != 0;

    /// <summary>Whether the ramp-out is pre-biased for a parent that is not at full weight.</summary>
    public bool CrossFades => (Flags & StudioAutoLayerFlags.CrossFade) != 0;

    /// <summary>Whether the layer ignores the parent's weight entirely.</summary>
    public bool IgnoresWeight => (Flags & StudioAutoLayerFlags.NoBlend) != 0;

    /// <summary>Whether this layer belongs to the sequence's own local pass.</summary>
    public bool IsLocal => (Flags & StudioAutoLayerFlags.Local) != 0;

    /// <summary>Whether a pose parameter drives the envelope instead of the parent's cycle.</summary>
    public bool DrivenByPose => (Flags & StudioAutoLayerFlags.Pose) != 0;
}

/// <summary>
/// The <c>STUDIO_AL_*</c> bits, from <c>studio.h:3093</c>.
/// </summary>
/// <remarks>
/// **Eight of the sixteen bit positions are left blank in Valve's own header**, written out as bare
/// comments with no name. Reproduced as the five that are named plus <c>STUDIO_AL_POST</c>, so a
/// model setting an unnamed bit is visibly unhandled rather than silently folded into a neighbour.
/// </remarks>
public static class StudioAutoLayerFlags
{
    /// <summary><c>STUDIO_AL_POST</c> — declared, and read by nothing in the SDK.</summary>
    /// <remarks>
    /// **Shares the value of <c>STUDIO_POST</c> and is a different flag on a different structure.**
    /// A sequence's `STUDIO_POST` picks which side a delta composes on; this is an autolayer's, and
    /// neither `AddSequenceLayers` nor `AddLocalLayers` tests it. Named here so the coincidence is
    /// on the record rather than discovered later as a suspected bug.
    /// </remarks>
    public const int Post = 0x0010;

    /// <summary><c>STUDIO_AL_SPLINE</c> — ease the ramp instead of running it straight.</summary>
    public const int Spline = 0x0040;

    /// <summary><c>STUDIO_AL_XFADE</c> — pre-bias the ramp for a parent below full weight.</summary>
    /// <remarks>
    /// Valve's comment: *"pre-bias the ramp curve to compense for a non-1 weight, assuming a second
    /// layer is also going to accumulate"*. The formula is
    /// <c>( s * flWeight ) / ( 1 - flWeight + s * flWeight )</c>, which is one at <c>s == 1</c>
    /// whatever the parent weighs — so two layers cross-fading do not both fade toward nothing.
    /// **It applies only past the tail**, on the ramp OUT.
    /// </remarks>
    public const int CrossFade = 0x0080;

    /// <summary><c>STUDIO_AL_NOBLEND</c> — the layer's weight is the ramp alone.</summary>
    public const int NoBlend = 0x0200;

    /// <summary><c>STUDIO_AL_LOCAL</c> — belongs to the sequence's own local pass.</summary>
    /// <remarks>
    /// **Which of the two passes a layer belongs to, and they are mutually exclusive.**
    /// `AddSequenceLayers` skips a layer carrying this and `AddLocalLayers` skips one without it, so
    /// every autolayer is handled by exactly one of them. The local pass composes into the
    /// sequence's OWN pose at weight one, before that pose is blended into the accumulator; the
    /// other composes onto the accumulator afterwards at the parent's weight.
    /// </remarks>
    public const int Local = 0x1000;

    /// <summary><c>STUDIO_AL_POSE</c> — a pose parameter drives the envelope, not the cycle.</summary>
    public const int Pose = 0x4000;
}

/// <summary>
/// Reads a sequence's autolayer array.
/// </summary>
/// <remarks>
/// **The offsets are relative to the SEQUENCE**, as every index inside a studio structure is
/// relative to the structure holding it: <c>pAutolayer(i)</c> is
/// <c>((byte *)this) + autolayerindex</c> then indexed by element (<c>studio.h:873</c>).
/// </remarks>
public static class StudioAutoLayers
{
    /// <summary>Bytes per <c>mstudioautolayer_t</c>.</summary>
    /// <remarks>
    /// **Two shorts then five four-byte fields**: <c>iSequence</c> and <c>iPose</c> are
    /// <c>short</c>, not <c>int</c>, which is the one place this structure is not a run of
    /// four-byte values. Reading them as ints would take the flags word as part of the sequence
    /// number and shift everything after it.
    /// </remarks>
    public const int Stride = 24;

    /// <summary>The autolayers a sequence declares, in file order.</summary>
    /// <param name="model">The whole <c>.mdl</c> file.</param>
    /// <param name="sequence">Which sequence, by its index within THIS model.</param>
    /// <returns>Its autolayers, or empty when it declares none or the read cannot be trusted.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> is negative.</exception>
    /// <remarks>
    /// **A LOCAL sequence index, not a merged one.** The offsets are into this file's own sequence
    /// table, so a caller holding a merged number has to resolve it to a group and a local index
    /// first — the same rule the bone controllers and the jiggle bones follow, and the one that has
    /// produced a plausible wrong answer twice in this project.
    /// </remarks>
    public static IReadOnlyList<StudioAutoLayer> Read(ReadOnlyMemory<byte> model, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        ReadOnlySpan<byte> bytes = model.Span;

        if (bytes.Length < StudioLayout.HeaderSequenceIndexOffset + sizeof(int))
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderSequenceCountOffset..]);

        int table = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderSequenceIndexOffset..]);

        if (sequence >= count ||
            table < 0 ||
            (long)table + ((long)count * StudioLayout.SequenceStride) > bytes.Length)
        {
            return [];
        }

        int start = table + (sequence * StudioLayout.SequenceStride);

        int layers = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.SequenceAutoLayerCountOffset)..]);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.SequenceAutoLayerIndexOffset)..]);

        // **Bounded before it is trusted**, because `numautolayers` is a number from a file: a
        // corrupt or unexpected count would otherwise walk off the end of the model.
        if (layers <= 0 || layers > StudioReaderLimits.MaximumAutoLayers)
        {
            return [];
        }

        long at = (long)start + index;

        if (index == 0 || at < 0 || at + ((long)layers * Stride) > bytes.Length)
        {
            return [];
        }

        List<StudioAutoLayer> read = new(layers);

        for (int layer = 0; layer < layers; layer++)
        {
            ReadOnlySpan<byte> entry = bytes.Slice((int)at + (layer * Stride), Stride);

            read.Add(new StudioAutoLayer(
                BinaryPrimitives.ReadInt16LittleEndian(entry),
                BinaryPrimitives.ReadInt16LittleEndian(entry[2..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[12..]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[16..]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[20..])));
        }

        return read;
    }
}
