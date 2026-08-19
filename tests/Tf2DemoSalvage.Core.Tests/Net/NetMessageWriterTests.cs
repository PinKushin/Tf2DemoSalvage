using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// <c>NetMessageWriter</c> against synthetic messages, with no demo involved.
/// </summary>
/// <remarks>
/// **Written because the writer's only coverage was the corpus, and that is a coverage gap rather
/// than a coverage style.** `CorpusMessageRoundTripTests` re-encodes real demos and is the stronger
/// evidence about real data — but it can only exercise the messages and the VALUES that ten
/// recordings happen to contain, and it needs 20 MB of Git LFS to run at all. Measured 2026-08-18:
/// `NetMessageWriter` carried 83 mutants that no test in `Core.Tests` reached, which is why the
/// nightly mutation run scored it as uncovered. See `docs/MEASUREMENT-PLAN.md`.
///
/// **The property is object → write → read → object, not bits → bits.** Building the expected bits
/// by hand would test this project's encoder against my own reading of the wire format, and
/// hand-built fixtures have caused more bugs here than the decoders have
/// (`docs/memory/fixtures-are-the-weak-point.md`). Going through the object round trip instead
/// means a field the writer drops comes back changed, with nothing hand-encoded anywhere.
///
/// **The values are chosen to be adversarial rather than typical**, which is the whole advantage
/// of a synthetic test over a corpus one: zero, the maximum a field can hold, and the sign
/// boundaries. Real demos carry ordinary values, and ordinary values agree with a broken width
/// (`docs/memory/real-data-hides-bugs-small-inputs-expose.md`).
/// </remarks>
public sealed class NetMessageWriterTests
{
    /// <summary>A modern protocol, where the message type field is six bits.</summary>
    private const int ModernProtocol = 24;

    [Test]
    public void Write_Prefetch_RoundTripsAtZeroAndAtItsWidestIndex()
    {
        // Zero and the widest value the field holds: a truncated width still reproduces small
        // indices, so a typical one cannot tell a 13-bit field from a 14-bit one.
        RoundTrip(new PrefetchMessage(0)).SoundIndex.ShouldBe(0);
        RoundTrip(new PrefetchMessage(8191)).SoundIndex.ShouldBe(8191);
    }

    [Test]
    public void Write_SetView_RoundTripsTheEntityIndex()
    {
        RoundTrip(new SetViewMessage(1)).EntityIndex.ShouldBe(1);

        // MAX_EDICTS - 1, the highest slot that exists.
        RoundTrip(new SetViewMessage(2047)).EntityIndex.ShouldBe(2047);
    }

    [Test]
    public void Write_SignOnState_RoundTripsBothFields()
    {
        SignOnStateMessage read = RoundTrip(new SignOnStateMessage(State: 6, SpawnCount: 12345));

        read.State.ShouldBe(6);

        // The two fields are different widths, so swapping them survives equal values and only
        // shows up when they differ - which is why they differ here.
        read.SpawnCount.ShouldBe(12345);
    }

    [Test]
    public void Write_FixAngle_RoundTripsItsFlagAndThreeAngles()
    {
        FixAngleMessage read = RoundTrip(new FixAngleMessage(IsRelative: true, 45f, -90f, 180f));

        read.IsRelative.ShouldBeTrue();

        // Angles travel as 16-bit fractions of a circle, so they do not come back exactly. The
        // tolerance is one step of that encoding (360/65536), not an arbitrary epsilon.
        const float AngleStep = 360f / 65536f;

        read.Pitch.ShouldBe(45f, AngleStep);

        // **-90 comes back as 270, and that is the encoding rather than a defect.** `WriteAngle` is
        // `Round(degrees * 65536/360) & 0xFFFF`, so the wire carries an unsigned fraction of a
        // circle with nowhere to put a sign. The angle is the same; its representative is not.
        // Asserting -90 here was this test's own wrong prediction, kept as a comment because
        // "the value changed" and "the value is wrong" look identical without it.
        read.Yaw.ShouldBe(270f, AngleStep);
        read.Roll.ShouldBe(180f, AngleStep);
    }

    [Test]
    public void Write_ANegativePitch_SurvivesInsteadOfCollapsingToZero()
    {
        // **The regression test for B113, which this file found.** `WriteAngle` cast the scaled
        // float straight to `uint`, and .NET's float-to-unsigned conversions SATURATE — so every
        // negative angle was written as exactly 0 and a player looking up was flattened to level.
        // Valve's own bf_write::WriteBitAngle casts to a SIGNED int and masks, which wraps instead.
        //
        // The corpus round trip cannot catch this and never could: `ReadAngle` is
        // `raw * 360/65536`, so a demo-sourced angle is always 0..360 and never re-enters the
        // writer negative. Only a caller constructing a message — a synthetic test, or the
        // text-to-demo compiler — can reach it.
        const float AngleStep = 360f / 65536f;

        // -30 degrees of pitch is a player looking up, and 330 is the same direction.
        RoundTrip(new FixAngleMessage(IsRelative: false, -30f, 0f, 0f))
            .Pitch.ShouldBe(330f, AngleStep);

        // The control: the positive representative of the same angle must land identically, or
        // the assertion above is measuring the wrap rather than the value.
        RoundTrip(new FixAngleMessage(IsRelative: false, 330f, 0f, 0f))
            .Pitch.ShouldBe(330f, AngleStep);
    }

    [Test]
    public void Write_FixAngleWhenAbsolute_KeepsItsFlagClear()
    {
        // The control for the test above: with only the `true` case asserted, a writer that
        // hardcoded the bit would pass.
        RoundTrip(new FixAngleMessage(IsRelative: false, 0f, 0f, 0f)).IsRelative.ShouldBeFalse();
    }

    [Test]
    public void Write_GetCvarValue_RoundTripsItsCookieAndName()
    {
        GetCvarValueMessage read = RoundTrip(new GetCvarValueMessage(0xDEADBEEF, "sv_cheats"));

        read.Cookie.ShouldBe(0xDEADBEEF);
        read.CvarName.ShouldBe("sv_cheats");
    }

    [Test]
    public void Write_File_RoundTripsItsTransferIdNameAndDirection()
    {
        FileMessage read = RoundTrip(new FileMessage(7u, "maps/cp_badlands.bsp", IsRequested: true));

        read.TransferId.ShouldBe(7u);
        read.FileName.ShouldBe("maps/cp_badlands.bsp");
        read.IsRequested.ShouldBeTrue();

        RoundTrip(new FileMessage(0u, "x", IsRequested: false)).IsRequested.ShouldBeFalse();
    }

    [Test]
    public void Write_AString_SurvivesNonAsciiCharacters()
    {
        // Every decoder here is UTF-8 and ASCII corrupts a name into a plausible one rather than
        // failing - docs/memory/international-names-are-required.md. A corpus of English demos
        // cannot catch that, so the synthetic test is where it belongs.
        RoundTrip(new GetCvarValueMessage(1u, "Ω_переменная_名前"))
            .CvarName.ShouldBe("Ω_переменная_名前");
    }

    [Test]
    public void Write_TheTypeField_IsSixBitsAbove15AndFiveAtOrBelow()
    {
        // Writing six bits unconditionally would shift every message after the first on an old
        // demo, and a single-message round trip would still pass - so this asserts the WIDTH by
        // measuring the encoded size of a body-less message rather than by reading it back.
        // net_NOP is pure padding: the type field and nothing else.
        WrittenBits(NetEmptyMessage.Instance, protocol: 24).ShouldBe(6);
        WrittenBits(NetEmptyMessage.Instance, protocol: 15).ShouldBe(5);
    }

    [Test]
    public void Write_TwoMessagesInOneStream_BothComeBack()
    {
        // A single message cannot show that the writer left the stream correctly positioned: any
        // trailing-bit error is invisible until something follows it.
        NetDecodeState state = State(ModernProtocol);
        BitWriter writer = new();

        NetMessageWriter.TryWrite(writer, new SetViewMessage(3), state).ShouldBeTrue();
        NetMessageWriter.TryWrite(writer, new PrefetchMessage(77), state).ShouldBeTrue();

        IReadOnlyList<INetMessage> read =
            NetMessageReader.Read(writer.Build(), State(ModernProtocol)).Messages;

        read.Count.ShouldBe(2);
        read[0].ShouldBeOfType<SetViewMessage>().EntityIndex.ShouldBe(3);
        read[1].ShouldBeOfType<PrefetchMessage>().SoundIndex.ShouldBe(77);
    }

    [Test]
    public void Write_ARefusedMessage_WritesNothingAtAll()
    {
        // "Checked before the type field is written, so a refusal leaves the stream untouched
        // rather than half-written." A refusal that had already emitted its type field would
        // desynchronise a caller that kept going.
        NetDecodeState state = State(ModernProtocol);
        BitWriter writer = new();

        // A game event that arrived before its definition decodes to an id and nothing else, so
        // it cannot be rebuilt - the writer's own documented unwritable case.
        GameEventMessage undefined = new(
            EventId: 1, Name: null, Values: new Dictionary<string, object?>());

        NetMessageWriter.TryWrite(writer, undefined, state).ShouldBeFalse();
        NetMessageWriter.CanWrite(undefined).ShouldBeFalse();
        writer.Build().Length.ShouldBe(0);
    }

    /// <summary>Writes a message, reads it back, and returns the decoded result.</summary>
    private static TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, INetMessage
    {
        BitWriter writer = new();

        NetMessageWriter.TryWrite(writer, message, State(ModernProtocol))
            .ShouldBeTrue($"the writer refused {typeof(TMessage).Name}");

        NetMessageReadResult result = NetMessageReader.Read(writer.Build(), State(ModernProtocol));

        // **Trailing padding decodes as net_NOP, and that is not an extra message the writer
        // emitted.** A stream is written in bits and read from whole bytes, so a body that does not
        // land on a byte boundary leaves up to seven zero bits behind — and a zero type field IS
        // net_NOP, so any message whose total is 17..18 or 25..26 bits gains a phantom Empty on the
        // end. `svc_SetView` (6 + 11 = 17) does; `svc_Prefetch` (6 + 13 = 19) does not, which is
        // exactly why only one of them failed when this asserted a count of one. The corpus round
        // trip never sees it because it compares bits rather than re-reading.
        result.Messages.Count.ShouldBeGreaterThan(0, "the message did not come back at all");

        foreach (INetMessage trailing in result.Messages.Skip(1))
        {
            trailing.ShouldBeOfType<NetEmptyMessage>(
                "only net_NOP padding may follow the message under test");
        }

        return result.Messages[0].ShouldBeOfType<TMessage>();
    }

    /// <summary>How many bits a message occupies when written at a given protocol.</summary>
    private static int WrittenBits(INetMessage message, int protocol)
    {
        BitWriter writer = new();

        NetMessageWriter.TryWrite(writer, message, State(protocol)).ShouldBeTrue();

        return writer.BitCount;
    }

    private static NetDecodeState State(int protocol) => new() { NetworkProtocol = (ushort)protocol };
}
