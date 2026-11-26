using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>What the build hands `OnNewModel` for a model path, read from the model's own header (B870).</summary>
public sealed class StudioModelFactsTests
{
    private const string Sentry = "models/buildables/sentry1.mdl";

    /// <summary>
    /// **The pose parameters' count and loop flags, and the static-prop bit, as the header states them** — a hand-built
    /// header with `aim_pitch` (no loop) and `aim_yaw` (loop 360), flagged `STUDIOHDR_FLAGS_STATIC_PROP`.
    /// </summary>
    [Test]
    public void FactsOf_AHeaderWithTwoParametersAndTheStaticPropBit_ReportsBoth()
    {
        StudioModelFacts facts = new(path => path == Sentry ? Header(StudioModelFlags.StaticProp, ("aim_pitch", 0f), ("aim_yaw", 360f)) : null);

        ModelFacts read = facts.FactsOf(Sentry).ShouldNotBeNull();

        read.PoseParameterLoops.ShouldBe([false, true]);
        read.StaticProp.ShouldBeTrue();
        read.SequenceLoops.ShouldNotBeNull()(0).ShouldBeFalse("a model with no sequences loops nothing");
    }

    /// <summary>
    /// **Each merged sequence's `STUDIO_LOOPING`, asked of the sequence the model PLAYS**: sequence 0 loops and 1 does not;
    /// −1 and one past the end play 0 (`c_baseanimating.cpp:1967-1970`), so they loop too.
    /// </summary>
    [Test]
    public void AsPlayed_TwoSequencesOneLooping_AnswersByThePlayedSequence()
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, [new StudioSequence(0, StudioFlags.SequenceLooping), new StudioSequence(0, 0)])];

        Func<int, bool> loops = StudioModelFacts.AsPlayed(
            StudioModelFacts.LoopingFlags(StudioSequenceTable.Merge(groups), groups));

        (loops(0), loops(1), loops(-1), loops(5)).ShouldBe((true, false, true, true));
    }

    /// <summary>
    /// **A sequence from an included model is looked up in ITS group**: merged sequence 1 is group 1's local 0, which loops,
    /// while group 0's only sequence does not.
    /// </summary>
    [Test]
    public void LoopingFlags_ASequenceFromAnIncludedModel_ReadsItsOwnGroup()
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
        [
            (0, [new StudioSequence(0, 0, Label: "idle")]),
            (1, [new StudioSequence(0, StudioFlags.SequenceLooping, Label: "spin")]),
        ];

        StudioModelFacts.LoopingFlags(StudioSequenceTable.Merge(groups), groups).ShouldBe([false, true]);
    }

    /// <summary>The control: no flags and no parameters read as none, not as a default model.</summary>
    [Test]
    public void FactsOf_AHeaderWithNoParametersOrFlags_ReportsNone()
    {
        StudioModelFacts facts = new(_ => Header(0));

        ModelFacts read = facts.FactsOf(Sentry).ShouldNotBeNull();

        read.PoseParameterLoops.ShouldBeEmpty();
        read.StaticProp.ShouldBeFalse();
    }

    /// <summary>
    /// **No file, or one that is not a studio model, is the engine's no-header answer**: null, so the build resizes
    /// nothing (`c_baseanimating.cpp:1042-1072`).
    /// </summary>
    [Test]
    public void FactsOf_AMissingOrUnreadableModel_IsNull()
    {
        new StudioModelFacts(_ => null).FactsOf("*12").ShouldBeNull();
        new StudioModelFacts(_ => new byte[512]).FactsOf(Sentry).ShouldBeNull();
        new StudioModelFacts(_ => throw new System.IO.IOException("locked")).FactsOf(Sentry).ShouldBeNull();
    }

    /// <summary>
    /// **A failed read is remembered too** (B870 review 4): an unreadable or invalid model is read once, so every build
    /// of the session — the whole timeline and a window walk — gets the same answer.
    /// </summary>
    /// <param name="locked">Whether the read fails with `IOException` rather than returning bytes that are no model.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void FactsOf_AFailedReadTwice_ReadsTheFileOnce(bool locked)
    {
        int reads = 0;
        StudioModelFacts facts = new(_ =>
        {
            reads++;
            return locked ? throw new System.IO.IOException("locked") : new byte[512];
        });

        (facts.FactsOf(Sentry), facts.FactsOf(Sentry)).ShouldBe((null, null));
        reads.ShouldBe(1);
    }

    /// <summary>A model is read once however often the recording sets it.</summary>
    [Test]
    public void FactsOf_TheSamePathTwice_ReadsTheFileOnce()
    {
        int reads = 0;
        StudioModelFacts facts = new(_ =>
        {
            reads++;
            return Header(0, ("aim_pitch", 0f));
        });

        facts.FactsOf(Sentry).ShouldBeSameAs(facts.FactsOf(Sentry));
        reads.ShouldBe(1);
    }

    /// <summary>
    /// A 512-byte studio header: `IDST`, version 48, the flags word, and `numlocalposeparameters` /
    /// `localposeparamindex` at 300 and 304 pointing at `mstudioposeparamdesc_t` entries (name, flags, start, end, loop).
    /// </summary>
    private static byte[] Header(int flags, params (string Name, float Loop)[] parameters)
    {
        const int entries = 320;
        const int stride = 20;
        const int names = entries + (8 * stride);

        byte[] file = new byte[512];
        "IDST"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 48);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderFlagsOffset), flags);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(300), parameters.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(304), parameters.Length > 0 ? entries : 0);

        int name = names;

        for (int index = 0; index < parameters.Length; index++)
        {
            int at = entries + (index * stride);

            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), name - at);
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(at + 8), -1f);
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(at + 12), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(at + 16), parameters[index].Loop);

            System.Text.Encoding.ASCII.GetBytes(parameters[index].Name).CopyTo(file, name);
            name += parameters[index].Name.Length + 1;
        }

        return file;
    }
}
