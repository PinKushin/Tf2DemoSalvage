using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Tests for walking a <c>svc_PacketEntities</c> body: entity indices, update types, and the
/// property indices that address the flattened schema.
/// </summary>
/// <remarks>
/// Unlike the value decoders, none of this can be verified by round trip — there is no encoder
/// to disagree with, and a wrong index reads a real value into the wrong field rather than
/// failing. So every fixture here is hand-built from the SDK's write path, and the assertions
/// name exact properties rather than checking that something plausible came out.
///
/// Both delta encodings add one, which is the trap: <c>index = previous + delta + 1</c>. A
/// decoder that omits the <c>+1</c> still produces monotonic indices addressing real
/// properties, so every fixture below uses at least two consecutive items — with one, the two
/// behaviours are indistinguishable.
/// </remarks>
public sealed class EntityDecoderTests
{
    private const int ChangesOften = 1 << 10;
    private const int Unsigned = 1 << 0;

    /// <summary>Six properties, none changes-often, so the flattened order is the declared one.</summary>
    private static DemoSchema Schema()
    {
        List<SendProperty> properties =
        [
            new(SendPropType.Int, "m_iHealth", Unsigned, string.Empty, 0f, 0f, 10, 0),
            new(SendPropType.Int, "m_iTeamNum", Unsigned, string.Empty, 0f, 0f, 3, 0),
            new(SendPropType.Int, "m_iAmmo", Unsigned, string.Empty, 0f, 0f, 8, 0),
            new(SendPropType.Int, "m_iClass", Unsigned, string.Empty, 0f, 0f, 4, 0),
            new(SendPropType.Int, "m_iScore", Unsigned, string.Empty, 0f, 0f, 12, 0),
            new(SendPropType.String, "m_szName", 0, string.Empty, 0f, 0f, 0, 0),
        ];

        return new DemoSchema(
            [new SendTable("DT_Test", true, properties)],
            [new ServerClass(0, "CTest", "DT_Test"), new ServerClass(1, "COther", "DT_Test")]);
    }

    /// <summary>Class ids are sized from the class count, so two classes means two bits.</summary>
    private const int ClassBits = 2;

    private static EntityDecoder Decoder() => new(Schema(), ClassBits);

    private static PacketEntitiesMessage Header(int updated, bool delta = false) =>
        new(2048, delta, delta ? 100 : null, false, updated, 0, false, System.ReadOnlyMemory<byte>.Empty);
    [TestCase(2, 2)]
    [TestCase(3, 2)]
    [TestCase(4, 3)]
    [TestCase(362, 9)]
    [TestCase(363, 9)]
    [TestCase(512, 10)]
    public void ClassIdWidth_IsFloorLogTwoPlusOne(int classCount, int expected)
    {
        // floor, not ceil. The two agree on exact powers of two and on two classes, which is
        // why every fixture in this file agreed with a ceiling implementation - the bug only
        // surfaced against a real demo's 362 classes, where ceil gives 10 and the wire uses 9.
        // 3 and 363 are the rows that separate them.
        EntityDecoder.ClassIdBits(classCount).ShouldBe(expected);
    }

    [Test]
    public void TempEntities_DecodeTheirDelayClassAndProperties()
    {
        // svc_TempEntities is the largest undeciphered part of the codec - 761,828 of z1800's
        // 1,226,354 opaque payload bits - and it is what a viewer needs for explosions, tracers
        // and impacts. Its body was consumed by length and discarded.
        //
        // Layout, from demostf/parser's tempentities.rs rather than guessed: per effect, a bit
        // saying whether a fire delay follows (8 bits, hundredths of a second), a bit saying
        // whether a class id follows (ClassIdBits wide and stored ONE HIGHER than the real id),
        // then the same property list a PacketEntities update carries. An effect with no class
        // bit repeats the previous effect's class.
        BitWriter writer = new();

        writer.Write(1, 1).Write(25, 8);           // delay present: 0.25s
        writer.Write(1, 1).Write(2, ClassBits);    // class id 1, stored as 2
        writer.Write(1, 1).UBitVar(0).Write(300, 10);   // m_iHealth = 300
        writer.Write(0, 1);                        // no more properties

        writer.Write(0, 1);                        // second effect: no delay
        writer.Write(0, 1);                        // and no class - repeats the first
        writer.Write(0, 1);                        // no properties

        byte[] body = writer.Build();
        IReadOnlyList<DecodedTempEntity> effects =
            Decoder().DecodeTempEntities(body, 2, writer.BitCount);

        effects.Count.ShouldBe(2);

        effects[0].ClassId.ShouldBe(1);
        effects[0].DelaySeconds.ShouldBe(0.25f, 0.001f);
        effects[0].Properties.ShouldHaveSingleItem()
            .Definition.Property.Name.ShouldBe("m_iHealth");

        // The repeat. Without carrying the previous class forward, this effect has no schema to
        // read against and the whole body desynchronises from here on.
        effects[1].ClassId.ShouldBe(1);
        effects[1].DelaySeconds.ShouldBe(0f);
        effects[1].Properties.ShouldBeEmpty();
    }
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void TempEntities_CountOfZeroMeansOneReliableEffect(int wireCount, bool expected)
    {
        // A count byte of zero does not mean an empty message - it means exactly one effect, sent
        // reliably. The engine spends the count byte's zero value on the case it can infer, and a
        // decoder that loops `count` times therefore drops a real effect and leaves the body
        // unread, with nothing anywhere reporting a problem.
        //
        // Not reachable from the corpus: 11,192 svc_TempEntities messages across every era carry a
        // nonzero count, so the input that separates right from wrong does not occur there. The
        // encoder half of demostf/parser is what settles it - it writes 0 for a single reliable
        // event, which only round-trips against a reader that reads it back that way.
        //
        // The count-1 row is the control. Same bytes, same single effect, and the reliability flag
        // must NOT come back set - otherwise this passes on a decoder that ignores the count.
        BitWriter writer = new();
        writer.Write(0, 1).Write(1, 1).Write(2, ClassBits).Write(0, 1);

        DecodedTempEntity effect = Decoder()
            .DecodeTempEntities(writer.Build(), wireCount, writer.BitCount)
            .ShouldHaveSingleItem();

        effect.ClassId.ShouldBe(1);
        effect.IsReliable.ShouldBe(expected);
    }

    [Test]
    public void TempEntities_WithABodyThatDoesNotFit_AreRefusedRatherThanGuessed()
    {
        // The self-check that makes a researched layout safe: the message states its body length,
        // and a correct reading consumes exactly that. Claiming more effects than the body holds
        // is what a wrong layout looks like from inside.
        BitWriter writer = new();
        writer.Write(0, 1).Write(1, 1).Write(2, ClassBits).Write(0, 1);   // one complete effect

        Should.Throw<System.IO.EndOfStreamException>(
            () => Decoder().DecodeTempEntities(writer.Build(), 40, writer.BitCount));
    }

    [Test]
    public void TempEntities_WithNoClassOnTheFirstEffect_AreRefused()
    {
        // The class is optional per effect and repeats the previous one, so the FIRST effect
        // having none leaves nothing to read its properties against. Guessing a class here would
        // decode real bits against the wrong schema and produce plausible nonsense.
        BitWriter writer = new();
        writer.Write(0, 1).Write(0, 1).Write(0, 1);

        Should.Throw<System.IO.InvalidDataException>(
            () => Decoder().DecodeTempEntities(writer.Build(), 1, writer.BitCount));
    }

    [Test]
    public void EnteringEntity_CarriesItsClassAndSerialNumber()
    {
        BitWriter writer = new();
        writer.UBitVar(0);                    // entity 0
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(1, ClassBits);           // class 1
        writer.Write(42, 10);                 // serial number
        writer.Write(0, 1);                   // no properties follow

        IReadOnlyList<DecodedEntity> entities =
            Decoder().Decode(writer.Build(), Header(1), writer.BitCount);

        DecodedEntity entity = entities.ShouldHaveSingleItem();
        entity.EntityIndex.ShouldBe(0);
        entity.ClassId.ShouldBe(1);
        entity.SerialNumber.ShouldBe(42);
        entity.UpdateType.ShouldBe(EntityUpdateType.Enter);
        entity.Properties.ShouldBeEmpty();
    }

    [Test]
    public void ConsecutiveEntities_AreOneApartNotZero()
    {
        // The +1 in "index = previous + delta + 1". With a single entity a decoder missing it
        // still reports index 0; two consecutive entities is the smallest fixture where the
        // right and wrong answers differ.
        BitWriter writer = new();
        foreach (int _ in Enumerable.Range(0, 3))
        {
            writer.UBitVar(0);
            writer.Write((uint)EntityUpdateType.Enter, 2);
            writer.Write(0, ClassBits);
            writer.Write(0, 10);
            writer.Write(0, 1);
        }

        IReadOnlyList<DecodedEntity> entities =
            Decoder().Decode(writer.Build(), Header(3), writer.BitCount);

        entities.Select(e => e.EntityIndex).ShouldBe([0, 1, 2]);
    }

    [Test]
    public void EntityIndexDelta_SkipsTheStatedNumberOfSlots()
    {
        BitWriter writer = new();
        writer.UBitVar(5);                    // 0 + 5 + 1 = 6
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(0, 1);
        writer.UBitVar(300);                  // 6 + 300 + 1 = 307, and 300 needs 12 bits
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(0, 1);

        IReadOnlyList<DecodedEntity> entities =
            Decoder().Decode(writer.Build(), Header(2), writer.BitCount);

        // 300 exercises UBitVar's 12-bit payload; 5 exercises its 4-bit one. A decoder that
        // read a fixed width would agree on the first index and not the second.
        entities.Select(e => e.EntityIndex).ShouldBe([5, 306]);
    }

    [Test]
    public void PropertyIndices_AddressTheFlattenedList()
    {
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);

        writer.Write(1, 1).UBitVar(0).Write(125, 10);   // property 0: m_iHealth = 125
        writer.Write(1, 1).UBitVar(0).Write(3, 3);      // property 1: m_iTeamNum = 3
        writer.Write(1, 1).UBitVar(1).Write(9, 4);      // property 3: m_iClass = 9
        writer.Write(0, 1);

        DecodedEntity entity = Decoder()
            .Decode(writer.Build(), Header(1), writer.BitCount)
            .ShouldHaveSingleItem();

        // Names, not indices. An off-by-one in the property delta reads a real value into the
        // adjacent field, which asserting on indices alone would not reveal.
        entity.Properties.Select(p => p.Definition.Property.Name)
            .ShouldBe(["m_iHealth", "m_iTeamNum", "m_iClass"]);
        entity.Properties.Select(p => p.Value.AsInt).ShouldBe([125, 3, 9]);
    }

    [Test]
    public void PropertyValues_DecodeAccordingToTheirOwnDefinition()
    {
        // Each property has a different width, so a decoder using one width for all of them
        // desynchronises rather than returning a wrong number.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(1, 1).UBitVar(4).Write(4095, 12);  // property 5 skipped? no: 0+4+1 = 5
        writer.Write(0, 1);

        DecodedEntity entity = Decoder()
            .Decode(writer.Build(), Header(1), writer.BitCount)
            .ShouldHaveSingleItem();

        entity.Properties.ShouldHaveSingleItem()
            .Definition.Property.Name.ShouldBe("m_iScore");
        entity.Properties[0].Value.AsInt.ShouldBe(4095);
    }

    [Test]
    public void DeltaUpdate_ReusesTheClassLearnedWhenTheEntityEntered()
    {
        // A delta carries no class id. The decoder has to remember it from the enter, which is
        // why this class holds state across snapshots rather than being a static method.
        EntityDecoder decoder = Decoder();

        BitWriter enter = new();
        enter.UBitVar(3);
        enter.Write((uint)EntityUpdateType.Enter, 2);
        enter.Write(1, ClassBits);
        enter.Write(7, 10);
        enter.Write(0, 1);
        decoder.Decode(enter.Build(), Header(1), enter.BitCount);

        BitWriter update = new();
        update.UBitVar(3);
        update.Write((uint)EntityUpdateType.Delta, 2);
        update.Write(1, 1).UBitVar(0).Write(66, 10);
        update.Write(0, 1);
        update.Write(0, 1);                   // no removed entities

        DecodedEntity entity = decoder
            .Decode(update.Build(), Header(1, delta: true), update.BitCount)
            .ShouldHaveSingleItem();

        entity.ClassId.ShouldBe(1);
        entity.UpdateType.ShouldBe(EntityUpdateType.Delta);
        entity.Properties.ShouldHaveSingleItem().Value.AsInt.ShouldBe(66);
    }

    [Test]
    public void LeaveAndDelete_CarryNoPropertiesAndConsumeNoFurtherBits()
    {
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Leave, 2);
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Delete, 2);
        writer.Write(0, 1);                   // no removed entities

        IReadOnlyList<DecodedEntity> entities =
            Decoder().Decode(writer.Build(), Header(2, delta: true), writer.BitCount);

        entities.Select(e => e.UpdateType)
            .ShouldBe([EntityUpdateType.Leave, EntityUpdateType.Delete]);
        entities.ShouldAllBe(e => e.Properties.Count == 0);
    }

    [Test]
    public void RemovedEntities_AreListedAfterTheUpdatesOnADelta()
    {
        // A trailing flag-and-index list, present only on delta snapshots. Reading it on a full
        // snapshot would consume bits that are not there.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Leave, 2);
        writer.Write(1, 1).Write(11, 11);
        writer.Write(1, 1).Write(1500, 11);
        writer.Write(0, 1);

        EntityDecoder decoder = Decoder();
        decoder.Decode(writer.Build(), Header(1, delta: true), writer.BitCount);

        decoder.RemovedEntities.ShouldBe([11, 1500]);
    }

    [Test]
    public void FullSnapshot_DoesNotReadARemovedEntityList()
    {
        // The mirror of the test above. If the decoder read the list unconditionally it would
        // consume the sentinel below and report a removal that was never sent.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Leave, 2);
        writer.Write(1, 1).Write(11, 11);     // would be read as a removal if the guard is wrong

        EntityDecoder decoder = Decoder();
        decoder.Decode(writer.Build(), Header(1), writer.BitCount);

        decoder.RemovedEntities.ShouldBeEmpty();
    }

    [Test]
    public void EntityIndexBeyondTheEntityLimit_IsRejected()
    {
        // MAX_EDICTS is 2048. An index past it means the stream desynchronised, and continuing
        // would read noise as entities rather than stopping.
        BitWriter writer = new();
        writer.UBitVar(5000);
        writer.Write((uint)EntityUpdateType.Leave, 2);

        Should.Throw<System.IO.InvalidDataException>(() =>
            Decoder().Decode(writer.Build(), Header(1), writer.BitCount));
    }

    [Test]
    public void PropertyIndexPastTheEndOfTheClass_IsRejected()
    {
        // The schema above flattens to six properties. Index 20 cannot be addressed, and
        // guessing past the end would attribute a value to no property at all.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(1, 1).UBitVar(20).Write(0, 10);

        Should.Throw<System.IO.InvalidDataException>(() =>
            Decoder().Decode(writer.Build(), Header(1), writer.BitCount));
    }

    [Test]
    public void DeltaForAnEntityNeverSeenEntering_IsRejected()
    {
        // Without a prior enter there is no class, so there is no flattened list to index and
        // no way to know how wide the next value is.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Delta, 2);
        writer.Write(0, 1);

        Should.Throw<System.IO.InvalidDataException>(() =>
            Decoder().Decode(writer.Build(), Header(1, delta: true), writer.BitCount));
    }

    [Test]
    public void PropertyIndices_FollowFlattenedOrderNotDeclaredOrder()
    {
        // RISKS B4 made concrete. m_iScore is declared fifth but marked changes-often, so
        // flattening moves it to the front and index 0 addresses it rather than m_iHealth.
        // Both are integers, so getting this wrong reads a real number into the wrong field -
        // the exact silent failure the flattener exists to prevent.
        List<SendProperty> properties =
        [
            new(SendPropType.Int, "m_iHealth", Unsigned, string.Empty, 0f, 0f, 10, 0),
            new(SendPropType.Int, "m_iScore", Unsigned | ChangesOften, string.Empty, 0f, 0f, 12, 0),
        ];
        DemoSchema schema = new(
            [new SendTable("DT_Test", true, properties)],
            [new ServerClass(0, "CTest", "DT_Test")]);

        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(1, 1).UBitVar(0).Write(4000, 12);   // index 0, read at m_iScore's width
        writer.Write(0, 1);

        DecodedEntity entity = new EntityDecoder(schema, ClassBits)
            .Decode(writer.Build(), Header(1), writer.BitCount)
            .ShouldHaveSingleItem();

        entity.Properties.ShouldHaveSingleItem().Definition.Property.Name.ShouldBe("m_iScore");
        entity.Properties[0].Value.AsInt.ShouldBe(4000);
    }

    [Test]
    public void NullSchema_IsRejectedAtConstruction()
    {
        // The decoder holds the schema for its whole life, so a null one would surface much
        // later as a null reference inside a decode rather than at the mistake.
        Should.Throw<System.ArgumentNullException>(() => new EntityDecoder(null!, ClassBits))
            .ParamName.ShouldBe("schema");
    }

    [Test]
    public void NullHeader_IsRejected()
    {
        Should.Throw<System.ArgumentNullException>(() =>
                Decoder().Decode(new byte[4], null!, 32))
            .ParamName.ShouldBe("header");
    }

    [Test]
    public void RemovalList_StopsAtTheDeclaredBodyLengthEvenWithoutATerminator()
    {
        // The loop is bounded by the body length as well as by the terminator flag. Here the
        // body ends exactly after one removal, with no terminator and a byte of unrelated data
        // beyond it - a decoder bounded only by the flag would read that byte as a second
        // removal. Fixing the bound to <= would do the same.
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Leave, 2);
        writer.Write(1, 1).Write(11, 11);
        int bodyBits = writer.BitCount;
        writer.Write(0xFF, 8);                // beyond the body; must not be read

        EntityDecoder decoder = Decoder();
        decoder.Decode(writer.Build(), Header(1, delta: true), bodyBits);

        decoder.RemovedEntities.ShouldBe([11]);
    }

    [Test]
    public void RemovedEntities_AreClearedBetweenSnapshots()
    {
        // The list is reused across calls, so a stale entry would be reported as a removal in
        // a snapshot that never mentioned it.
        EntityDecoder decoder = Decoder();

        BitWriter first = new();
        first.UBitVar(0);
        first.Write((uint)EntityUpdateType.Leave, 2);
        first.Write(1, 1).Write(11, 11);
        first.Write(0, 1);
        decoder.Decode(first.Build(), Header(1, delta: true), first.BitCount);
        decoder.RemovedEntities.ShouldBe([11]);

        BitWriter second = new();
        second.UBitVar(0);
        second.Write((uint)EntityUpdateType.Leave, 2);
        second.Write(0, 1);
        decoder.Decode(second.Build(), Header(1, delta: true), second.BitCount);

        decoder.RemovedEntities.ShouldBeEmpty();
    }

    [Test]
    public void StringProperty_ReadsThroughTheSameFlattenedList()
    {
        BitWriter writer = new();
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(0, ClassBits);
        writer.Write(0, 10);
        writer.Write(1, 1).UBitVar(5).Write(3, 9);      // property 5: m_szName, 3 bytes
        foreach (byte b in "abc"u8)
        {
            writer.Write(b, 8);
        }

        writer.Write(0, 1);

        DecodedEntity entity = Decoder()
            .Decode(writer.Build(), Header(1), writer.BitCount)
            .ShouldHaveSingleItem();

        entity.Properties.ShouldHaveSingleItem().Value.AsString.ShouldBe("abc");
    }

    [Test]
    public void Baseline_DecodesAsAnOrdinaryPropertyList()
    {
        // A class baseline is encoded exactly like an entity delta - the same continuation-flag
        // property loop, starting at index 0 - which is why no new codec is needed. Confirmed by
        // reading demostf/parser, which decodes it by calling its own entity-update reader.
        EntityDecoder decoder = Decoder();

        BitWriter writer = new();
        writer.Write(1, 1).UBitVar(0).Write(125, 10);       // property 0: m_iHealth = 125
        writer.Write(1, 1).UBitVar(1).Write(3, 8);          // property 2 (0 + 1 + 1): m_iAmmo
        writer.Write(0, 1);                                 // end of list

        decoder.SetBaseline(0, writer.Build());

        IReadOnlyList<DecodedProperty> baseline = decoder.Baseline(0).ShouldNotBeNull();
        baseline.Count.ShouldBe(2);
        baseline[0].Definition.Property.Name.ShouldBe("m_iHealth");
        baseline[0].Value.AsInt.ShouldBe(125);
        baseline[1].Definition.Property.Name.ShouldBe("m_iAmmo");
        baseline[1].Value.AsInt.ShouldBe(3);
    }

    [Test]
    public void Baseline_ForAnUnknownClass_IsNull()
    {
        // Distinguishable from "a class with an empty baseline", which is why this returns null
        // rather than an empty list: an entity of a class with no baseline must not be seeded
        // with silence that looks like a decoded answer.
        Decoder().Baseline(0).ShouldBeNull();
    }

    [Test]
    public void RewritingABaseline_ReplacesTheDecodedOne()
    {
        // Baselines are rewritten mid-match through svc_UpdateStringTable, so a decoded copy
        // cached against a class id has to be dropped when the entry changes. The oracle solves
        // this the same way - it deletes the memo on write rather than versioning it.
        EntityDecoder decoder = Decoder();

        BitWriter first = new();
        first.Write(1, 1).UBitVar(0).Write(125, 10).Write(0, 1);
        decoder.SetBaseline(0, first.Build());
        decoder.Baseline(0).ShouldNotBeNull()[0].Value.AsInt.ShouldBe(125);

        BitWriter second = new();
        second.Write(1, 1).UBitVar(0).Write(42, 10).Write(0, 1);
        decoder.SetBaseline(0, second.Build());

        // Reading before the rewrite is what populates the memo, so this fails on a cache that
        // is never invalidated - which is the whole reason the test reads twice.
        decoder.Baseline(0).ShouldNotBeNull()[0].Value.AsInt.ShouldBe(42);
    }

}
