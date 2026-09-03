using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone controller: what a networked fraction means for one bone's axis.</summary>
/// <param name="Bone">Which bone it drives.</param>
/// <param name="Type">Which axis, plus the wrapping bit.</param>
/// <param name="Start">The value an encoded 0 maps to.</param>
/// <param name="End">The value an encoded 1 maps to.</param>
/// <param name="InputField">
/// Which of the entity's controller values drives this bone — <c>inputfield</c>, and the index
/// <c>CalcBoneAdj</c> reads with: <c>i = pbonecontroller-&gt;inputfield; value = controllers[i];</c>
/// (<c>bone_setup.cpp:2482</c>). Two controllers may share an input and a model's controllers are
/// not in input order, so assuming the list index is the input drives the wrong bone from the wrong
/// value on any model where they differ.
/// </param>
/// <remarks>
/// **Neither half is usable alone, which is the whole reason this has to be read.** The demo carries
/// <c>m_flEncodedController</c> as eleven bits over 0..1 (<c>baseanimating.cpp:248</c>) — a
/// fraction with no units. The model carries what that fraction spans. <c>CalcBoneAdj</c> is the
/// multiplication between them, and without this table the wire value is a number about nothing.
/// </remarks>
public readonly record struct StudioBoneController(
    int Bone,
    int Type,
    float Start,
    float End,
    int InputField = 0)
{
    /// <summary>The axis and kind this controller drives — <c>type &amp; STUDIO_TYPES</c>.</summary>
    /// <remarks>
    /// **<c>CalcBoneAdj</c> masks before it switches** (<c>bone_setup.cpp:2487</c>):
    /// <c>switch(pbonecontroller-&gt;type &amp; STUDIO_TYPES)</c>, where <c>STUDIO_TYPES</c> is
    /// <c>0x0003FFFF</c> (<c>studio.h:3074</c>). The field carries more than the axis, so testing
    /// it whole would miss a controller whose upper bits are set.
    /// </remarks>
    public int Axis => Type & StudioFlags.ControllerTypes;

    /// <summary><c>STUDIO_X</c> — translate the bone along X, in units.</summary>
    /// <remarks>
    /// **Public here because <c>StudioFlags</c> is internal to this assembly** and the code that
    /// applies a controller lives in Animation. Forwarding rather than restating keeps one
    /// definition: a second copy of <c>0x0001</c> elsewhere could drift from this one silently,
    /// and a controller applied on the wrong axis moves a bone in a plausible direction.
    /// </remarks>
    public const int TranslateX = StudioFlags.ControllerX;

    /// <summary><c>STUDIO_Y</c> — translate along Y, in units.</summary>
    public const int TranslateY = StudioFlags.ControllerY;

    /// <summary><c>STUDIO_Z</c> — translate along Z, in units.</summary>
    public const int TranslateZ = StudioFlags.ControllerZ;

    /// <summary><c>STUDIO_XR</c> — rotate about X, in DEGREES.</summary>
    /// <remarks>
    /// **Degrees, where the translations are units**, which <c>CalcBoneAdj</c> shows by converting
    /// only the rotation cases: <c>a0.Init( value * (M_PI / 180.0), 0, 0 )</c>.
    /// </remarks>
    public const int RotateX = StudioFlags.ControllerXRotation;

    /// <summary><c>STUDIO_YR</c> — rotate about Y, in degrees.</summary>
    public const int RotateY = StudioFlags.ControllerYRotation;

    /// <summary><c>STUDIO_ZR</c> — rotate about Z, in degrees.</summary>
    public const int RotateZ = StudioFlags.ControllerZRotation;

    /// <summary>The value in this controller's own units, from a normalised input.</summary>
    /// <param name="normalised">The entity's controller value, zero to one.</param>
    /// <returns>The value <c>CalcBoneAdj</c> would apply.</returns>
    /// <remarks>
    /// **<c>bone_setup.cpp:2483</c>**, clamp then lerp:
    ///
    /// <code>
    ///   if (value &lt; 0) value = 0;
    ///   if (value &gt; 1.0) value = 1.0;
    ///   value = (1.0 - value) * pbonecontroller-&gt;start + value * pbonecontroller-&gt;end;
    /// </code>
    ///
    /// The clamp comes FIRST, so an out-of-range input lands on an endpoint rather than
    /// extrapolating past it — which for a rotation controller would spin the bone past its
    /// authored limit.
    /// </remarks>
    public float Value(float normalised)
    {
        float within = Math.Clamp(normalised, 0f, 1f);

        return ((1f - within) * Start) + (within * End);
    }
}

/// <summary>
/// The bone controllers a model declares.
/// </summary>
/// <remarks>
/// **Worth reading even though TF2's player models declare none.** Measured 2026-08-24: every one of
/// the 474 controller slots across the heavy's 79 bones is −1, and the scout and soldier are the
/// same. So <c>CalcBoneAdj</c> is close to dead weight for the models this viewer draws today.
///
/// **Re-measured 2026-09-02 against a denominator that is not three player models**, because a
/// player is not what decides whether a mechanism matters — B269 had just found buildings using
/// pose parameters that players are excluded from. Every model drawn at tick 12000 of the 2013
/// SourceTV foundry demo, sixteen of them, declares **zero** bone controllers: both buildings, the
/// three player models, four pickups, three sliding doors, the capture point, its hologram, the
/// resupply locker and the builder viewmodel. Six further guesses — the payload cart among them —
/// are the same.
///
/// **The reader is proved capable before that absence is believed.**
/// <c>StudioBoneControllerTests</c> builds models that DO declare controllers and reads their
/// ranges back, and the header offsets 164/168 match <c>studiohdr_t</c>'s field order by arithmetic
/// (<c>studio.h:2165</c>). A count of zero here is the header's own answer, and the reader throws
/// rather than returning empty when the table is inconsistent — so this is a fact about TF2's
/// content rather than about the parser
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
///
/// So <c>m_flEncodedController</c> being unread and <c>CalcBoneAdj</c> unimplemented is a MEASURED
/// exclusion, not an outstanding gap. The day a model turns up that uses one, this table is already
/// loaded and the missing half is the wire value and the multiply.
///
/// It is read anyway for two reasons. The table is what makes that claim CHECKABLE rather than an
/// assumption — <c>BoneFlagContentTests</c> asserts the emptiness, so a model that does use one
/// shows up as a failing test rather than as a silent wrong pose. And the parser is upstream of
/// every stage: leaving one field unread is what turned five separate pipeline stages into "not
/// merely unwired, the data is not loaded" (B182).
/// </remarks>
public static class StudioBoneControllers
{
    /// <summary>Reads a model's bone controllers.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The controllers in file order, so a bone's slot addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more controllers than it holds.</exception>
    public static IReadOnlyList<StudioBoneController> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBoneControllerIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneControllerCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneControllerIndexOffset..]);

        if (count <= 0)
        {
            return [];
        }

        if (count > StudioReaderLimits.BoneControllers)
        {
            throw new InvalidDataException($"A model declares {count} bone controllers.");
        }

        if (at < 0 || (long)at + ((long)count * BoneControllerStride) > bytes.Length)
        {
            throw new InvalidDataException(
                $"A model's {count} bone controllers at {at} run past its own length of {bytes.Length}.");
        }

        List<StudioBoneController> controllers = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> controller =
                bytes.Slice(at + (index * BoneControllerStride), BoneControllerStride);

            controllers.Add(new StudioBoneController(
                BinaryPrimitives.ReadInt32LittleEndian(controller[BoneControllerBoneOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(controller[BoneControllerTypeOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(controller[BoneControllerStartOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(controller[BoneControllerEndOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(controller[BoneControllerInputOffset..])));
        }

        return controllers;
    }
}
