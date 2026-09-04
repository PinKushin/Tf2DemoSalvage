using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A corpse that turned to gold, and one that froze — authored, because no demo has either (B325).
/// </summary>
/// <remarks>
/// **The corpus cannot answer this and never will.** `m_bGoldRagdoll` needs a Saxxy or Golden Wrench
/// kill and `m_bIceRagdoll` a Spy-cicle backstab; measured across the two demos with the most
/// corpses, **0 of 566 carry either flag**. So the decode path for both was written and could not be
/// exercised — the exact case `docs/memory/author-the-specimen-the-corpus-lacks.md` describes, where
/// the demo writer becomes the test instrument.
///
/// **This is stronger than a real recording would be, not a substitute for one.** A corpus test does
/// not know the right answer and must compare two readings of the same file; this one HAS ground
/// truth, because the test put the flag on the wire itself (D38).
///
/// **It exercises the whole decode**, not `EntityState` in isolation: the schema is written into a
/// `dem_datatables`, the entity is encoded into a real `svc_PacketEntities` body, and
/// `DemoTimeline.Build` reads the file back. A reader that decoded the bit but dropped it between
/// `Ragdoll()` and `SceneRagdoll` would pass a property-level test and fail here.
/// </remarks>
public sealed class GoldRagdollSpecimenTests
{
    [Test]
    public void Build_ForACorpseThatTurnedToGold_CarriesTheFlagToTheTimeline()
    {
        SceneRagdoll corpse = Only(Demo(gold: true, ice: false));

        corpse.Gold.ShouldBeTrue();
        corpse.Ice.ShouldBeFalse();
    }

    [Test]
    public void Build_ForACorpseThatFroze_CarriesTheFlagToTheTimeline()
    {
        SceneRagdoll corpse = Only(Demo(gold: false, ice: true));

        corpse.Ice.ShouldBeTrue();
        corpse.Gold.ShouldBeFalse();
    }

    /// <remarks>
    /// **The control, and the reason the two above are not satisfied by a constant.** An ordinary
    /// corpse — the only kind the corpus contains — must answer false to both. Without this, a
    /// decode that returned true unconditionally would pass each of the cases above.
    /// </remarks>
    [Test]
    public void Build_ForAnOrdinaryCorpse_CarriesNeitherFlag()
    {
        SceneRagdoll corpse = Only(Demo(gold: false, ice: false));

        corpse.Gold.ShouldBeFalse();
        corpse.Ice.ShouldBeFalse();
    }

    /// <summary>The one corpse the specimen contains.</summary>
    private static SceneRagdoll Only(byte[] demo)
    {
        DemoTimeline timeline = DemoTimeline.Build(demo);

        timeline.Corpses.Count.ShouldBe(1, "the specimen carries exactly one CTFRagdoll");

        return timeline.Corpses[0];
    }

    /// <summary>A demo whose single entity is a corpse with the given flags.</summary>
    private static byte[] Demo(bool gold, bool ice)
    {
        DemoSchema schema = Schema();

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(RagdollClassId);

        List<DecodedProperty> properties =
        [
            Property(flat, "m_iClass", PropertyValue.FromInt(5)),
            Property(flat, "m_iTeam", PropertyValue.FromInt(SceneTeams.Red)),
            Property(flat, "m_bGoldRagdoll", PropertyValue.FromInt(gold ? 1 : 0)),
            Property(flat, "m_bIceRagdoll", PropertyValue.FromInt(ice ? 1 : 0)),

            // An origin, or the corpse is decoded and then declined for having no place to be.
            Property(flat, "m_vecRagdollOrigin", PropertyValue.FromVectorXY(64f, 32f)),
            Property(flat, "m_vecRagdollOrigin[2]", PropertyValue.FromFloat(8f)),
        ];

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        DecodedEntity corpse = new(
            RagdollEntityIndex,
            RagdollClassId,
            SerialNumber: 7,
            EntityUpdateType.Enter,
            properties);

        byte[] body = decoder.EncodeEntities([corpse], [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>
    /// <c>DT_TFRagdoll</c> as the engine declares it, minus what this specimen does not need.
    /// </summary>
    /// <remarks>
    /// **`NeedsDecoder` with no base table, because the real one is `NOBASE`.** `DT_TFRagdoll` is
    /// `IMPLEMENT_CLIENTCLASS_DT_NOBASE` (`c_tf_player.cpp:518`) and inherits nothing — a fixture
    /// that gave it a `DT_BaseEntity` parent would be testing a table TF2 does not have, and would
    /// hide exactly the property the corpse work exists because of.
    ///
    /// The bit widths are Valve's: `SendPropInt( SENDINFO( m_iTeam ), 3, SPROP_UNSIGNED )` and
    /// `SendPropInt( SENDINFO( m_iClass ), 4, SPROP_UNSIGNED )` (`tf_player.cpp:375-467`), and the
    /// booleans are one bit each as `SendPropBool` writes them.
    /// </remarks>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_TFRagdoll", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_iClass", 1, string.Empty, 0f, 0f, 4, 0),
                new SendProperty(SendPropType.Int, "m_iTeam", 1, string.Empty, 0f, 0f, 3, 0),
                new SendProperty(SendPropType.Int, "m_bGoldRagdoll", 1, string.Empty, 0f, 0f, 1, 0),
                new SendProperty(SendPropType.Int, "m_bIceRagdoll", 1, string.Empty, 0f, 0f, 1, 0),
                new SendProperty(
                    SendPropType.VectorXY, "m_vecRagdollOrigin", 1, string.Empty,
                    -16384f, 16384f, 32, 0),
                new SendProperty(
                    SendPropType.Float, "m_vecRagdollOrigin[2]", 1, string.Empty,
                    -16384f, 16384f, 32, 0),
            ]),
        ],
        [new ServerClass(RagdollClassId, "CTFRagdoll", "DT_TFRagdoll")]);

    /// <summary>One property, resolved to the flattened index the encoder needs.</summary>
    private static DecodedProperty Property(
        IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        for (int candidate = 0; candidate < flat.Count; candidate++)
        {
            if (flat[candidate].Property.Name == name)
            {
                return new DecodedProperty(candidate, flat[candidate], value);
            }
        }

        // Loud rather than silent: a fixture naming a property its own schema lacks is a broken
        // test, and a skipped property would look like a decode that dropped it.
        throw new KeyNotFoundException($"the fixture schema declares no '{name}'");
    }

    private const int RagdollClassId = 0;

    private const int RagdollEntityIndex = 40;
}
