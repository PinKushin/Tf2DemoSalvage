using System;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// <c>democmdinfo_t</c>, the camera the engine plays a demo through.
/// </summary>
/// <remarks>
/// **Every packet in every demo carries the recorder's view, and this project has been discarding
/// it.** The reader consumes 76 bytes of prologue and keeps them as opaque bytes so the file can be
/// written back — which is right, and is also why nobody noticed there was a camera in there.
///
/// <c>public/demofile/demoformat.h</c>:
///
/// <code>
/// int     flags;
///
/// // original origin/viewangles
/// Vector  viewOrigin;
/// QAngle  viewAngles;
/// QAngle  localViewAngles;
///
/// // Resampled origin/viewangles
/// Vector  viewOrigin2;
/// QAngle  viewAngles2;
/// QAngle  localViewAngles2;
/// </code>
///
/// Four bytes of flags and six three-float structures is 76, which is exactly the constant the
/// container reader has always skipped.
///
/// **The flags are not decoration — they select which copy is live**, and the SDK's own accessors
/// are the specification:
///
/// <code>
/// const Vector&amp; GetViewOrigin()
/// {
///     if ( flags &amp; FDEMO_USE_ORIGIN2 ) { return viewOrigin2; }
///     return viewOrigin;
/// }
/// </code>
///
/// So a reader that always took the first copy would be right on most demos and wrong on the ones
/// that were resampled — a difference that shows up as a camera in the wrong place rather than as
/// an error, which is this project's recurring failure mode.
///
/// <c>FDEMO_NOINTERP</c> is the third flag and it means "don't interpolate between this and the
/// last view". A camera cut, in other words: honouring it is the difference between a hard switch
/// and the camera flying across the map over one interpolation window.
/// </remarks>
public sealed class RecordedViewConformanceTests
{
    /// <summary>The SDK's <c>FDEMO_USE_ORIGIN2</c>.</summary>
    private const int UseOrigin2 = 1 << 0;

    /// <summary>The SDK's <c>FDEMO_USE_ANGLES2</c>.</summary>
    private const int UseAngles2 = 1 << 1;

    /// <summary>The SDK's <c>FDEMO_NOINTERP</c>.</summary>
    private const int NoInterpolation = 1 << 2;

    [Test]
    public void Parse_TheStructure_Is76BytesTheReaderAlreadySkips()
    {
        // The arithmetic that identifies the layout, stated as a test so a change to either number
        // has to be deliberate. 4 + (6 * 3 * 4) = 76.
        RecordedView.SizeBytes.ShouldBe(76);
    }

    [Test]
    public void Parse_APrologueWithNoFlags_ReadsTheOriginalCopy()
    {
        // The ordinary case: flags zero, so the first origin and angles are the live ones.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: 0,
            origin: (64f, -128f, 256f),
            angles: (10f, 20f, 0f),
            origin2: (1f, 2f, 3f),
            angles2: (4f, 5f, 6f)));

        view.Origin.ShouldBe((64f, -128f, 256f));
        view.Angles.ShouldBe((10f, 20f, 0f));
    }

    [Test]
    public void Parse_TheUseOrigin2Flag_TakesTheResampledOriginOnly()
    {
        // **Origin and angles are selected by SEPARATE flags**, so a reader that switched both on
        // either flag would be right whenever a demo sets both and wrong whenever it sets one.
        // The fixture sets only FDEMO_USE_ORIGIN2 and asserts the angles did NOT move.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: UseOrigin2,
            origin: (64f, -128f, 256f),
            angles: (10f, 20f, 0f),
            origin2: (1f, 2f, 3f),
            angles2: (4f, 5f, 6f)));

        view.Origin.ShouldBe((1f, 2f, 3f));
        view.Angles.ShouldBe((10f, 20f, 0f));
    }

    [Test]
    public void Parse_TheUseAngles2Flag_TakesTheResampledAnglesOnly()
    {
        // The complement, and the half that catches the two flags being swapped.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: UseAngles2,
            origin: (64f, -128f, 256f),
            angles: (10f, 20f, 0f),
            origin2: (1f, 2f, 3f),
            angles2: (4f, 5f, 6f)));

        view.Origin.ShouldBe((64f, -128f, 256f));
        view.Angles.ShouldBe((4f, 5f, 6f));
    }

    [Test]
    public void Parse_TheNoInterpFlag_IsReportedSoACutIsNotSmoothed()
    {
        // A camera cut. Without this the view interpolates from wherever it was, which on a
        // spectator switching players is a flight across the map — smooth, plausible, and not what
        // the recording says happened.
        RecordedView.Parse(Prologue(flags: NoInterpolation)).IsCut.ShouldBeTrue();
        RecordedView.Parse(Prologue(flags: 0)).IsCut.ShouldBeFalse();
    }

    [Test]
    public void Parse_APrologueWithNoFlags_ReadsTheOriginalLocalAngles()
    {
        // `localViewAngles` sits between `viewAngles` and the resampled copies, 28..39 (B56): the local player's own
        // `pl.v_angle`, which `C_TFPlayer::UpdateClientSideAnimation` animates him from. A read at the wrong offset takes
        // `viewAngles` or `viewOrigin2`, both of which the fixture fills with different numbers.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: 0,
            angles: (10f, 20f, 0f),
            localAngles: (30f, 40f, 50f),
            origin2: (1f, 2f, 3f),
            localAngles2: (60f, 70f, 80f)));

        view.LocalAngles.ShouldBe((30f, 40f, 50f));
    }

    [Test]
    public void Parse_TheUseAngles2Flag_TakesTheResampledLocalAnglesToo()
    {
        // `GetLocalViewAngles` tests FDEMO_USE_ANGLES2 — the angles' flag, not a third one (demoformat.h:129-136).
        RecordedView view = RecordedView.Parse(Prologue(
            flags: UseAngles2,
            localAngles: (30f, 40f, 50f),
            localAngles2: (60f, 70f, 80f)));

        view.LocalAngles.ShouldBe((60f, 70f, 80f));
    }

    [Test]
    public void Parse_TheUseOrigin2Flag_LeavesTheLocalAnglesOriginal()
    {
        // The complement: the origin's flag must not move the local angles.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: UseOrigin2,
            localAngles: (30f, 40f, 50f),
            localAngles2: (60f, 70f, 80f)));

        view.LocalAngles.ShouldBe((30f, 40f, 50f));
    }

    [Test]
    public void Parse_TheFlags_AreKeptAsWritten()
    {
        RecordedView.Parse(Prologue(flags: UseOrigin2 | NoInterpolation)).Flags.ShouldBe(UseOrigin2 | NoInterpolation);
    }

    [Test]
    public void Reset_AResampledView_ReadsItsOriginalsWithNoFlags()
    {
        // `democmdinfo_t::Reset` (demoformat.h:138-144): flags cleared, every resampled copy overwritten by its
        // original. `CDemoPlayer::InterpolateViewpoint` applies it to the copies it searches with (0x180071fd0), so a
        // search that finds no pair reads the ORIGINALS of the current view whatever its flags chose.
        RecordedView view = RecordedView.Parse(Prologue(
            flags: UseOrigin2 | UseAngles2 | NoInterpolation,
            origin: (64f, -128f, 256f),
            angles: (10f, 20f, 0f),
            localAngles: (30f, 40f, 50f),
            origin2: (1f, 2f, 3f),
            angles2: (4f, 5f, 6f),
            localAngles2: (60f, 70f, 80f)));

        RecordedView reset = view.Reset();

        reset.Flags.ShouldBe(0);
        reset.Origin.ShouldBe((64f, -128f, 256f));
        reset.Angles.ShouldBe((10f, 20f, 0f));
        reset.LocalAngles.ShouldBe((30f, 40f, 50f));
        reset.ViewOrigin2.ShouldBe((64f, -128f, 256f));
    }

    [Test]
    public void IsDefault_AZeroedStructure_IsTrue()
    {
        // `InterpolateViewpoint` sets no view at all while the current `democmdinfo_t` is still its constructor's:
        // FDEMO_NORMAL and the three originals at vec3_origin and vec3_angle (0x1800721e7..0x1800722a3). That is a
        // SourceTV demo's every packet.
        RecordedView.Parse(Prologue(flags: 0)).IsDefault.ShouldBeTrue();
    }

    [Test]
    public void IsDefault_AFlagAlone_IsFalse()
    {
        RecordedView.Parse(Prologue(flags: NoInterpolation)).IsDefault.ShouldBeFalse();
    }

    [Test]
    public void IsDefault_AnOriginalLocalAngle_IsFalse()
    {
        // The third original counts: a view whose only non-zero value is the local roll is not the default.
        RecordedView.Parse(Prologue(flags: 0, localAngles: (0f, 0f, 1f))).IsDefault.ShouldBeFalse();
    }

    [Test]
    public void IsDefault_OnlyTheResampledCopiesSet_IsStillTrue()
    {
        // The engine compares the originals and the flags and nothing else, so a resampled copy nothing selects
        // leaves the structure default.
        RecordedView.Parse(Prologue(
            flags: 0,
            origin2: (1f, 2f, 3f),
            angles2: (4f, 5f, 6f),
            localAngles2: (7f, 8f, 9f))).IsDefault.ShouldBeTrue();
    }

    [Test]
    public void IsDefault_ANaNOriginal_ComparesEqualToZero()
    {
        // `UCOMISS` then `JNZ`: an unordered comparison sets ZF, so NaN passes for vec3_origin and the test reads
        // the structure as default (0x1800721ef, no JP beside the JNZ).
        RecordedView.Parse(Prologue(flags: 0, origin: (float.NaN, 0f, 0f))).IsDefault.ShouldBeTrue();
    }

    [Test]
    public void Parse_APrologueShorterThanTheStructure_IsRefused()
    {
        // A prologue this short means the caller handed over the wrong bytes — a usercmd's
        // prologue, or a truncated file. Reading it would produce a camera somewhere arbitrary.
        Should.Throw<ArgumentException>(() => RecordedView.Parse(new byte[75]));
    }

    [Test]
    public void Parse_ALongerPrologue_ReadsOnlyItsOwnBytes()
    {
        // A packet's prologue is democmdinfo_t PLUS two sequence numbers, so the span handed in is
        // 84 bytes rather than 76. Reading past the structure would take a sequence number as a
        // coordinate.
        byte[] whole = new byte[76 + 8];
        Prologue(flags: 0, origin: (7f, 8f, 9f)).CopyTo(whole, 0);
        BitConverter.GetBytes(1234).CopyTo(whole, 76);

        RecordedView.Parse(whole).Origin.ShouldBe((7f, 8f, 9f));
    }

    /// <summary>Builds a <c>democmdinfo_t</c> with the given fields and zero elsewhere.</summary>
    /// <remarks>
    /// Written out field by field rather than by round-tripping this project's own writer: a
    /// fixture built by the code under test agrees with it by construction, which is the failure
    /// <c>docs/memory/fixtures-are-the-weak-point.md#put-the-real-file-in-the-fixture</c> records.
    /// </remarks>
    private static byte[] Prologue(
        int flags,
        (float X, float Y, float Z) origin = default,
        (float Pitch, float Yaw, float Roll) angles = default,
        (float Pitch, float Yaw, float Roll) localAngles = default,
        (float X, float Y, float Z) origin2 = default,
        (float Pitch, float Yaw, float Roll) angles2 = default,
        (float Pitch, float Yaw, float Roll) localAngles2 = default)
    {
        byte[] bytes = new byte[76];
        BitConverter.GetBytes(flags).CopyTo(bytes, 0);

        Write(bytes, 4, origin.X, origin.Y, origin.Z);
        Write(bytes, 16, angles.Pitch, angles.Yaw, angles.Roll);
        Write(bytes, 28, localAngles.Pitch, localAngles.Yaw, localAngles.Roll);
        Write(bytes, 40, origin2.X, origin2.Y, origin2.Z);
        Write(bytes, 52, angles2.Pitch, angles2.Yaw, angles2.Roll);
        Write(bytes, 64, localAngles2.Pitch, localAngles2.Yaw, localAngles2.Roll);

        return bytes;
    }

    private static void Write(byte[] into, int at, float first, float second, float third)
    {
        BitConverter.GetBytes(first).CopyTo(into, at);
        BitConverter.GetBytes(second).CopyTo(into, at + 4);
        BitConverter.GetBytes(third).CopyTo(into, at + 8);
    }
}
