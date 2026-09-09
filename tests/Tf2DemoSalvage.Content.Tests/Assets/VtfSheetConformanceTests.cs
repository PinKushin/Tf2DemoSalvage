using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The sprite sheet a particle texture carries — VTF resource <c>0x10</c> (B373).
/// </summary>
/// <remarks>
/// **Synthetic, because a synthetic fixture HAS ground truth** (D38): the numbers asserted below are
/// the numbers this file wrote, where a test against `smokelit.vtf` could only compare one reading of
/// it against another. The real file is measured by the `particles sheet` probe, which is where the
/// structure came from and where a wrong reading shows up as tiles that do not tile.
/// </remarks>
[TestFixture]
public sealed class VtfSheetConformanceTests
{
    /// <summary>Where <c>numResources</c> sits in a 7.3 header.</summary>
    private const int CountAt = 0x44;

    /// <summary>Where the resource entries begin.</summary>
    private const int EntriesAt = 0x50;

    /// <summary>The sheet resource's id — <c>VTF_RSRC_SHEET</c>.</summary>
    private const uint Sheet = 0x10;

    [Test]
    public void Read_ASheetResource_ReturnsTheSequencesItDeclares()
    {
        // Two sequences of two frames each, at coordinates chosen so every field is distinguishable
        // from every other: a reader off by one field returns 0.25 where 0.75 is expected rather
        // than something that merely looks wrong.
        byte[] file = Vtf(Sheet, flags: 0, Payload(
        [
            (Id: 4, Clamp: 1, Frames: new[]
            {
                (Duration: 1f, U0: 0f, V0: 0f, U1: 0.25f, V1: 0.5f),
                (Duration: 2f, U0: 0.25f, V0: 0f, U1: 0.5f, V1: 0.5f),
            }),
            (Id: 9, Clamp: 0, Frames: new[]
            {
                (Duration: 3f, U0: 0.5f, V0: 0.5f, U1: 0.75f, V1: 1f),
                (Duration: 4f, U0: 0.75f, V0: 0.5f, U1: 1f, V1: 1f),
            }),
        ]));

        IReadOnlyList<SheetSequence> read = VtfSheet.Read(file);

        read.Count.ShouldBe(2);

        read[0].Id.ShouldBe(4);
        read[0].Clamp.ShouldBeTrue();
        read[0].Frames.Count.ShouldBe(2);
        read[0].Frames[1].U0.ShouldBe(0.25f);
        read[0].Frames[1].U1.ShouldBe(0.5f);
        read[0].Frames[1].Duration.ShouldBe(2f);

        read[1].Id.ShouldBe(9);
        read[1].Clamp.ShouldBeFalse();
        read[1].Frames[0].V0.ShouldBe(0.5f);
        read[1].Frames[1].U1.ShouldBe(1f);
    }

    [Test]
    public void Read_AResourceWhoseDataIsInline_IsNotFollowedAsAnOffset()
    {
        // **The trap the container's own header sets.** `RSRCF_HAS_NO_DATA_CHUNK` (0x02 in the HIGH
        // byte of `eType`) means `resData` IS the four bytes of data, so following it as a file
        // offset reads whatever happens to be at 0x40000000. `smokelit` ships two such resources —
        // its CRC and its LOD — and a reader that ignores the flag walks off into the pixels.
        byte[] file = Vtf(Sheet, flags: 0x02, Payload(
        [
            (Id: 0, Clamp: 0, Frames: new[] { (1f, 0f, 0f, 1f, 1f) }),
        ]));

        VtfSheet.Read(file).ShouldBeEmpty();
    }

    [Test]
    public void Read_ATextureBelow73_CarriesNoResourcesAtAll()
    {
        // The control for every "no sheet" answer: a 7.2 file has no resource table, so returning
        // empty must be a decision rather than a failed parse of a table that was never there.
        byte[] file = Vtf(Sheet, flags: 0, Payload(
        [
            (Id: 0, Clamp: 0, Frames: new[] { (1f, 0f, 0f, 1f, 1f) }),
        ]));

        BitConverter.GetBytes(2).CopyTo(file, 8);

        VtfSheet.Read(file).ShouldBeEmpty();
    }

    [Test]
    public void Read_AResourceThatIsNotTheSheet_IsSkipped()
    {
        // A 7.3 VTF's resources are mostly not sheets — the high-res image, the low-res image, the
        // CRC. Returning the first resource's payload as a sheet would give plausible garbage.
        byte[] file = Vtf(type: 0x30, flags: 0, Payload(
        [
            (Id: 0, Clamp: 0, Frames: new[] { (1f, 0f, 0f, 1f, 1f) }),
        ]));

        VtfSheet.Read(file).ShouldBeEmpty();
    }

    [Test]
    public void At_AtThreeFramesPerSecond_AdvancesOneFramePerThirdOfASecond()
    {
        // **`rockettrail`'s actual clock**: `use animation rate as FPS = 1`, `animation rate = 3`,
        // `animation_fit_lifetime = 0`. So a particle a third of a second old is on frame 1 whatever
        // its lifetime is — and the lifetime passed here is deliberately long enough that a
        // fraction-based reading would still say frame 0.
        SheetSequence sequence = Sequence(clamp: false, frames: 4);

        VtfSheet.At(sequence, 0f, 3f, true, 10f, false).Frame.Duration.ShouldBe(0f);
        VtfSheet.At(sequence, 1f / 3f, 3f, true, 10f, false).Frame.Duration.ShouldBe(1f);
        VtfSheet.At(sequence, 2f / 3f, 3f, true, 10f, false).Frame.Duration.ShouldBe(2f);
    }

    [Test]
    public void At_BetweenTwoFrames_ReportsBothAndTheFractionBetweenThem()
    {
        // `BLENDFRAMES` defaults to on, so the pair and the fraction are the output, not one frame.
        (SheetFrame frame, SheetFrame next, float blend) =
            VtfSheet.At(Sequence(clamp: false, frames: 4), 0.5f / 3f, 3f, true, 10f, false);

        frame.Duration.ShouldBe(0f);
        next.Duration.ShouldBe(1f);
        blend.ShouldBe(0.5f, 0.0001d);
    }

    [Test]
    public void At_PastTheLastFrame_WrapsUnlessTheSequenceSaysClamp()
    {
        // Four frames at three a second: after two seconds the position is 6, which is frame 2 of a
        // wrapping sequence and frame 3 of a clamping one. Both are asserted, because a reader that
        // only ever clamps passes a wrapping assertion for the first loop.
        VtfSheet.At(Sequence(clamp: false, frames: 4), 2f, 3f, true, 10f, false)
            .Frame.Duration.ShouldBe(2f);

        VtfSheet.At(Sequence(clamp: true, frames: 4), 2f, 3f, true, 10f, false)
            .Frame.Duration.ShouldBe(3f);
    }

    [Test]
    public void At_WithFitLifetime_StretchesTheSequenceAcrossTheLifeInstead()
    {
        // The other clock, honoured because other systems set it. Half way through a life, a
        // four-frame sequence is on frame 2 — and the RATE is ignored, which is what distinguishes
        // this branch from the one above.
        VtfSheet.At(Sequence(clamp: false, frames: 4), 1f, 999f, true, 2f, fitLifetime: true)
            .Frame.Duration.ShouldBe(2f);
    }

    [Test]
    public void At_ASequenceWithNoFrames_TakesTheWholeTexture()
    {
        (SheetFrame frame, _, float blend) =
            VtfSheet.At(new SheetSequence(0, false, 0f, []), 1f, 3f, true, 1f, false);

        frame.ShouldBe(VtfSheet.Whole);
        blend.ShouldBe(0f);
    }

    /// <summary>A sequence whose frames are told apart by their durations, 0, 1, 2, …</summary>
    private static SheetSequence Sequence(bool clamp, int frames)
    {
        List<SheetFrame> made = new(frames);

        for (int index = 0; index < frames; index++)
        {
            made.Add(new SheetFrame(index, 0f, 0f, 1f, 1f));
        }

        return new SheetSequence(0, clamp, frames, made);
    }

    /// <summary>The bytes a sheet payload is made of.</summary>
    private static byte[] Payload(
        IReadOnlyList<(int Id, int Clamp,
            (float Duration, float U0, float V0, float U1, float V1)[] Frames)> sequences)
    {
        List<byte> bytes = [];

        void Whole(int value) => bytes.AddRange(BitConverter.GetBytes(value));
        void Real(float value) => bytes.AddRange(BitConverter.GetBytes(value));

        Whole(0);                        // size, which the reader deliberately does not trust
        Whole(1);                        // version
        Whole(sequences.Count);

        foreach ((int id,
                  int clamp,
                  (float Duration, float U0, float V0, float U1, float V1)[] frames) in sequences)
        {
            Whole(id);
            Whole(clamp);
            Whole(frames.Length);
            Real(frames.Length);         // total time

            foreach ((float duration, float u0, float v0, float u1, float v1) in frames)
            {
                Real(duration);

                // **Four coordinate sets per frame, which is what makes the stride 68 rather than
                // 20** — `spritecard.cpp:271` gives the vertex format eight texcoords. Only the
                // first is read, so the other three are written as values a reader that took the
                // wrong one would return.
                for (int set = 0; set < 4; set++)
                {
                    Real(set == 0 ? u0 : -1f);
                    Real(set == 0 ? v0 : -1f);
                    Real(set == 0 ? u1 : -1f);
                    Real(set == 0 ? v1 : -1f);
                }
            }
        }

        return [.. bytes];
    }

    /// <summary>A 7.3 header with one resource entry pointing at a payload.</summary>
    private static byte[] Vtf(uint type, uint flags, byte[] payload)
    {
        byte[] file = new byte[EntriesAt + 8 + payload.Length];

        BitConverter.GetBytes(3).CopyTo(file, 8);           // version minor
        BitConverter.GetBytes(1).CopyTo(file, CountAt);     // numResources

        BitConverter.GetBytes(type | (flags << 24)).CopyTo(file, EntriesAt);
        BitConverter.GetBytes((uint)(EntriesAt + 8)).CopyTo(file, EntriesAt + 4);

        payload.CopyTo(file, EntriesAt + 8);

        return file;
    }
}
