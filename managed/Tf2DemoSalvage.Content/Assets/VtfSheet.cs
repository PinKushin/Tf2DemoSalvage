using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One frame of a sprite sheet sequence: how long it shows, and where it is.</summary>
/// <param name="Duration">How long this frame lasts, in the sequence's own units.</param>
/// <param name="U0">Left edge, 0..1.</param>
/// <param name="V0">Top edge, 0..1.</param>
/// <param name="U1">Right edge.</param>
/// <param name="V1">Bottom edge.</param>
public readonly record struct SheetFrame(float Duration, float U0, float V0, float U1, float V1);

/// <summary>One sequence of frames a particle can play.</summary>
/// <param name="Id">Which sequence this is — what <c>sequence_number</c> selects.</param>
/// <param name="Clamp">Whether the sequence holds on its last frame rather than looping.</param>
/// <param name="TotalTime">The sum of the frame durations, as the file states it.</param>
/// <param name="Frames">The frames, in order.</param>
public sealed record SheetSequence(
    int Id, bool Clamp, float TotalTime, IReadOnlyList<SheetFrame> Frames);

/// <summary>
/// The sprite sheet a particle texture carries — VTF resource <c>0x10</c> (B373).
/// </summary>
/// <remarks>
/// **The container is published and the payload is not**, so this is half read-from-source and half
/// measured, and the two halves are marked separately.
///
/// **Read from source** (`src/public/vtf/vtf.h`): a 7.3 header carries `numResources`, followed by
/// `ResourceEntryInfo { uint32 eType; uint32 resData; }` entries. The sheet's id is
/// `VTF_RSRC_SHEET = MK_VTF_RSRC_ID( 0x10, 0, 0 )` and **the high byte of `eType` is FLAGS**
/// (`MK_VTF_RSRCF`), so `RSRCF_HAS_NO_DATA_CHUNK` (`0x02 &lt;&lt; 24`) means `resData` IS the data
/// rather than an offset to it — reading the offset unconditionally returns garbage.
///
/// **Measured** on `materials/effects/smoke/smokelit.vtf`, because `CSheet` is forward-declared at
/// `particles.h:41` and defined nowhere in the SDK:
///
/// <code>
/// int32 size, int32 version, int32 sequenceCount
///   per sequence: int32 id, int32 clamp, int32 frameCount, float totalTime
///   per frame:    float duration, then FOUR sets of (u0, v0, u1, v1)
/// </code>
///
/// **The control is the tiling.** That reading gives `smokelit` four sequences of five frames, and
/// all twenty land on exact 128-pixel boundaries of a 4×2 grid in its 512×256 texture — the top row
/// at (1,1), (129,1), (257,1), (385,1) and one below at (1,129). The four sequences are four
/// different PERMUTATIONS of that same five-tile set, which is what stops neighbouring puffs of one
/// trail animating in lockstep. A wrong structure or a wrong stride cannot produce that: it produces
/// overlapping tiles, coordinates outside 0..1, or floats of 3e+38, all of which were seen while the
/// offsets were wrong.
///
/// **A differential control from a second file read by a second reader:** `rockettrail.pcf` declares
/// `Sequence Random` with `sequence_min = 0` and `sequence_max = 3`. Four sequences in the texture,
/// drawn 0..3 by the definition.
///
/// **Four UV sets per frame, not one**, which is what makes the stride 68 bytes: `spritecard.cpp:271`
/// gives the vertex format eight texcoords — frame 0's bounds, frame 1's, a second texture's, and a
/// second sequence's. Only the first set is read here, and that is a stated limit rather than a
/// finished job (B373).
///
/// **The wrong turn worth keeping:** the probe that measured this printed `Math.Min(frames, 4)`
/// while the header said `frames 5`, and that mismatch was written down as an open question about
/// the FORMAT. The fifth frame was simply not printed. The cap was in the instrument
/// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
/// </remarks>
public static class VtfSheet
{
    /// <summary>The sheet resource's id, from <c>VTF_RSRC_SHEET</c>.</summary>
    private const uint SheetResource = 0x10;

    /// <summary>Set when a resource's <c>resData</c> IS its data, from <c>RSRCF_HAS_NO_DATA_CHUNK</c>.</summary>
    private const uint NoDataChunk = 0x02;

    /// <summary>Where <c>numResources</c> sits, past the 7.2 fields and three pad bytes.</summary>
    private const int ResourceCountOffset = 0x44;

    /// <summary>Where the resource entries begin.</summary>
    private const int ResourceEntriesOffset = 0x50;

    /// <summary>Bytes per frame: a duration and four UV sets.</summary>
    private const int FrameStride = 4 + (4 * 4 * 4);

    /// <summary>Reads the sequences a texture's sheet declares.</summary>
    /// <param name="vtf">The whole <c>.vtf</c>.</param>
    /// <returns>Its sequences, or empty when it carries no sheet.</returns>
    /// <remarks>
    /// **Empty is the ordinary answer**, not a failure: most textures are not sheets, and a particle
    /// drawn from one that is not simply takes the whole image — which is what this project did
    /// before the sheet was read at all.
    /// </remarks>
    public static IReadOnlyList<SheetSequence> Read(ReadOnlySpan<byte> vtf)
    {
        if (vtf.Length < ResourceEntriesOffset ||
            BitConverter.ToInt32(vtf[8..]) < 3)
        {
            return [];
        }

        int resources = BitConverter.ToInt32(vtf[ResourceCountOffset..]);

        if (resources is < 0 or > 32)
        {
            return [];
        }

        for (int index = 0; index < resources; index++)
        {
            int at = ResourceEntriesOffset + (index * 8);

            if (at + 8 > vtf.Length)
            {
                return [];
            }

            uint type = BitConverter.ToUInt32(vtf[at..]);
            uint payload = BitConverter.ToUInt32(vtf[(at + 4)..]);

            if ((type & 0x00FFFFFF) != SheetResource)
            {
                continue;
            }

            // A sheet with no data chunk would have to fit in four bytes, which no sequence does.
            return ((type >> 24) & NoDataChunk) != 0 || payload + 12 > (uint)vtf.Length
                ? []
                : Sequences(vtf, (int)payload);
        }

        return [];
    }

    /// <summary>The whole texture, for a particle whose material carries no sheet.</summary>
    public static SheetFrame Whole => new(0f, 0f, 0f, 1f, 1f);

    /// <summary>Which two frames a particle is between, and how far.</summary>
    /// <param name="sequence">The sequence the particle was born into.</param>
    /// <param name="age">How long it has been alive, in seconds.</param>
    /// <param name="rate">
    /// The renderer's <c>animation rate</c>, in frames per second when
    /// <paramref name="asFramesPerSecond"/> is set and in sequences per second otherwise.
    /// </param>
    /// <param name="asFramesPerSecond">The renderer's <c>use animation rate as FPS</c>.</param>
    /// <param name="lifetime">
    /// How long the particle lives, used only when <paramref name="fitLifetime"/> is set.
    /// </param>
    /// <param name="fitLifetime">The renderer's <c>animation_fit_lifetime</c>.</param>
    /// <returns>The frame showing, the one being mixed toward, and the mix.</returns>
    /// <remarks>
    /// **Three clocks, because `render_animated_sprites` declares three parameters and they are not
    /// interchangeable.** Measured on `rockettrail`, which is the trail actually on screen:
    /// `animation_fit_lifetime = 0`, `use animation rate as FPS = 1`, `animation rate = 3`. So the
    /// frame comes from AGE — three frames a second, for as long as the particle lasts — and not
    /// from how far through its life it is. Reading it as a life fraction gives a five-frame
    /// animation stretched over 0.8 to 1.2 seconds instead of the 1.7 loops the engine plays, which
    /// looks like a slow morph rather than billowing smoke.
    ///
    /// **`fitLifetime` is still honoured** because other systems set it, and a renderer parameter
    /// that exists in the file and nowhere in the code is the shape
    /// `docs/memory/a-schema-key-nobody-reads-is-a-lead.md` names.
    ///
    /// **Wrapping unless the sequence says clamp** — `clamp` is the sequence's own field in the
    /// sheet, and `smokelit`'s four sequences all say false.
    ///
    /// **The mix is not optional.** `BLENDFRAMES` defaults to `1` (`spritecard.cpp:143`) and
    /// `rocketrailsmoke.vmt` does not set it, so the engine crossfades between consecutive frames:
    /// `lerp( baseTex0, baseTex1, i.blendfactor0.x )` (`spritecard_ps2x.fxc:77`). Returning one
    /// frame would step through five stills where the engine shows a continuous plume.
    /// </remarks>
    public static (SheetFrame Frame, SheetFrame Next, float Blend) At(
        SheetSequence sequence,
        float age,
        float rate,
        bool asFramesPerSecond,
        float lifetime,
        bool fitLifetime)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        int count = sequence.Frames.Count;

        if (count == 0)
        {
            return (Whole, Whole, 0f);
        }

        // Three readings of `animation rate`, in the order the renderer's own flags decide.
        float position;

        if (fitLifetime)
        {
            position = (lifetime > 0f ? age / lifetime : 0f) * count;
        }
        else if (asFramesPerSecond)
        {
            position = age * rate;
        }
        else
        {
            position = age * rate * count;
        }

        int at = (int)MathF.Floor(position);
        float blend = position - at;

        int first = Wrapped(at, count, sequence.Clamp);
        int second = Wrapped(at + 1, count, sequence.Clamp);

        return (sequence.Frames[first], sequence.Frames[second], blend);
    }

    /// <summary>Turns an unbounded frame number into one the sequence has.</summary>
    private static int Wrapped(int frame, int count, bool clamp) =>
        clamp
            ? Math.Clamp(frame, 0, count - 1)
            : ((frame % count) + count) % count;

    /// <summary>Reads every sequence at a payload offset.</summary>
    private static List<SheetSequence> Sequences(ReadOnlySpan<byte> vtf, int at)
    {
        // The size word is not needed to walk the structure and is not trusted as a bound: the
        // spans below are checked against the FILE, which cannot be overstated by a field in it.
        int count = BitConverter.ToInt32(vtf[(at + 8)..]);

        if (count is < 0 or > 64)
        {
            return [];
        }

        int cursor = at + 12;

        List<SheetSequence> sequences = new(count);

        for (int index = 0; index < count; index++)
        {
            if (cursor + 16 > vtf.Length)
            {
                break;
            }

            int id = BitConverter.ToInt32(vtf[cursor..]);
            bool clamp = BitConverter.ToInt32(vtf[(cursor + 4)..]) != 0;
            int frames = BitConverter.ToInt32(vtf[(cursor + 8)..]);
            float total = BitConverter.ToSingle(vtf[(cursor + 12)..]);

            cursor += 16;

            if (frames is < 0 or > 4096 || cursor + (frames * FrameStride) > vtf.Length)
            {
                break;
            }

            List<SheetFrame> read = new(frames);

            for (int frame = 0; frame < frames; frame++)
            {
                read.Add(new SheetFrame(
                    BitConverter.ToSingle(vtf[cursor..]),
                    BitConverter.ToSingle(vtf[(cursor + 4)..]),
                    BitConverter.ToSingle(vtf[(cursor + 8)..]),
                    BitConverter.ToSingle(vtf[(cursor + 12)..]),
                    BitConverter.ToSingle(vtf[(cursor + 16)..])));

                cursor += FrameStride;
            }

            sequences.Add(new SheetSequence(id, clamp, total, read));
        }

        return sequences;
    }
}
