using System;
using System.Linq;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>svc_PacketEntities</c> header, through a demo.
/// </summary>
/// <remarks>
/// **Converted from <c>CorpusPacketEntitiesTests</c>, whose assertions were ranges** — the entity
/// count sits between 1 and MAX_EDICTS, the updated count between 0 and that, a delta references a
/// tick in the past. Those are what you assert when the header came off a recording and nobody
/// knows what it should say.
///
/// A written header knows. The interesting part is that several fields here are adjacent and of
/// similar magnitude, so a range check passes on every transposition of them: <c>MaxEntries</c>
/// and <c>UpdatedEntries</c> are both eleven-bit counts, and both are plausible as the other.
/// Distinct values separate them.
/// </remarks>
public sealed class PacketEntitiesHeaderTests
{
    [Test]
    public void RoundTrip_EveryHeaderField_ComesBackAsItWentIn()
    {
        // Distinct values throughout, because the range checks this replaces cannot tell
        // MaxEntries from UpdatedEntries — both are eleven-bit counts and each is plausible as the
        // other, so a transposition passes every bound.
        PacketEntitiesMessage read = Read(new PacketEntitiesMessage(
            MaxEntries: 1337,
            IsDelta: true,
            DeltaFromTick: 4242,
            BaselineIndex: true,
            UpdatedEntries: 91,
            LengthBits: 24,
            UpdateBaseline: true,
            Body: new byte[] { 0x11, 0x22, 0x33 }));

        read.MaxEntries.ShouldBe(1337);
        read.UpdatedEntries.ShouldBe(91);
        read.DeltaFromTick.ShouldBe(4242);
        read.LengthBits.ShouldBe(24);
        read.IsDelta.ShouldBeTrue();
        read.BaselineIndex.ShouldBeTrue();
        read.UpdateBaseline.ShouldBeTrue();
    }

    [Test]
    public void RoundTrip_AFullSnapshot_CarriesNoDeltaTickAtAll()
    {
        // **The delta tick is present only when the delta flag is set**, so it is a field that
        // appears and disappears rather than one that is zero. A reader that always consumed
        // thirty-two bits here would shift everything after it on every full snapshot — which is
        // the first snapshot of every demo.
        PacketEntitiesMessage read = Read(new PacketEntitiesMessage(
            MaxEntries: 64,
            IsDelta: false,
            DeltaFromTick: null,
            BaselineIndex: false,
            UpdatedEntries: 3,
            LengthBits: 16,
            UpdateBaseline: false,
            Body: new byte[] { 0xAB, 0xCD }));

        read.IsDelta.ShouldBeFalse();
        read.IsFullSnapshot.ShouldBeTrue();
        read.DeltaFromTick.ShouldBeNull();
        read.UpdatedEntries.ShouldBe(3);
    }

    [Test]
    public void RoundTrip_TheTwoFlagsBesideTheDeltaTick_AreNotTransposed()
    {
        // BaselineIndex and UpdateBaseline are single bits either side of the updated-entry count.
        // Both false and both true are the two cases a swap survives, so each is set on its own.
        Read(Snapshot(baselineIndex: true, updateBaseline: false))
            .BaselineIndex.ShouldBeTrue();

        Read(Snapshot(baselineIndex: true, updateBaseline: false))
            .UpdateBaseline.ShouldBeFalse();

        Read(Snapshot(baselineIndex: false, updateBaseline: true))
            .BaselineIndex.ShouldBeFalse();

        Read(Snapshot(baselineIndex: false, updateBaseline: true))
            .UpdateBaseline.ShouldBeTrue();
    }

    [Test]
    public void RoundTrip_TheBody_IsCarriedForExactlyItsStatedBitLength()
    {
        // The body is not byte-aligned, and the message states its length in bits. A reader taking
        // whole bytes would consume up to seven bits belonging to the next message, which stays
        // plausible until the packet ends short.
        //
        // **Bits pack LSB-first, which the first draft of this test got backwards.** The tenth bit
        // of a body is the second-lowest bit of its second byte, not the highest — so a fixture
        // that put its meaningful bits at the top of the byte asserted that zeroes came back, and
        // they did.
        byte[] body = [0b1010_1010, 0b0000_0011];

        PacketEntitiesMessage read = Read(new PacketEntitiesMessage(
            MaxEntries: 64,
            IsDelta: false,
            DeltaFromTick: null,
            BaselineIndex: false,
            UpdatedEntries: 1,
            LengthBits: 10,
            UpdateBaseline: false,
            Body: body));

        read.LengthBits.ShouldBe(10);
        read.Body.Span[0].ShouldBe((byte)0b1010_1010);

        // Only the stated ten bits are meaningful, and bits eight and nine are the second byte's
        // two lowest. Nothing above them is promised.
        (read.Body.Span[1] & 0b0000_0011).ShouldBe(0b0000_0011);
    }

    private static PacketEntitiesMessage Snapshot(bool baselineIndex, bool updateBaseline) =>
        new(
            MaxEntries: 64,
            IsDelta: false,
            DeltaFromTick: null,
            BaselineIndex: baselineIndex,
            UpdatedEntries: 1,
            LengthBits: 8,
            UpdateBaseline: updateBaseline,
            Body: new byte[] { 0x5A });

    private static PacketEntitiesMessage Read(PacketEntitiesMessage sent) =>
        SyntheticDemo.MessagesIn(SyntheticDemo.Containing(sent))
            .OfType<PacketEntitiesMessage>()
            .ShouldHaveSingleItem();
}
