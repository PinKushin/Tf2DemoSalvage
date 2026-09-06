using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The three initial conditions a TF2 corpse is simulated from (B58, B368).
/// </summary>
/// <remarks>
/// **`DT_TFRagdoll` sends initial conditions and nothing else**, because a TF2 corpse is simulated
/// by the CLIENT — 158 of 159 corpses in a measured match receive exactly one update. Three of those
/// conditions are the physics ones, declared as consecutive entries on the table
/// (`c_tf_player.cpp:518-521`):
///
/// <code>
/// RecvPropVector( RECVINFO(m_vecRagdollOrigin) ),
/// RecvPropEHandle( RECVINFO( m_hPlayer ) ),
/// RecvPropVector( RECVINFO(m_vecForce) ),
/// RecvPropVector( RECVINFO(m_vecRagdollVelocity) ),
/// RecvPropInt( RECVINFO( m_nForceBone ) ),
/// </code>
///
/// **This project decoded the origin and none of the other three**, so a solver fed by it would
/// drop every corpse straight down however it was killed. The force is what makes a rocket death
/// look unlike a bullet death.
///
/// **Authored rather than measured, and stronger for it** (D38): the test puts the values on the
/// wire itself, so it has ground truth where a corpus test could only compare two readings of the
/// same file. It exercises the whole path — schema into a `dem_datatables`, entity into a real
/// `svc_PacketEntities` body, `DemoTimeline.Build` reading it back — so a decode that read the
/// property and dropped it between `Ragdoll()` and `SceneRagdoll` fails here and would pass a
/// property-level test.
/// </remarks>
public sealed class RagdollForceSpecimenTests
{
    /// <remarks>
    /// **The force and the velocity are different vectors and must not be swapped.** They are
    /// adjacent on the table, they are the same type, and they mean opposite things: one is the
    /// kill's impulse and scales by each body's mass share, the other is the player's own motion
    /// applied whole. The values below share no component, so a reader that crossed them fails on
    /// every axis rather than on none.
    /// </remarks>
    [Test]
    public void Build_ForACorpseCarryingItsInitialConditions_KeepsForceAndVelocityApart()
    {
        SceneRagdoll corpse = Only(Demo(forceBone: 5));

        corpse.Force.ShouldNotBeNull();
        corpse.Force.Value.X.ShouldBe(100f);
        corpse.Force.Value.Y.ShouldBe(-200f);
        corpse.Force.Value.Z.ShouldBe(300f);

        corpse.Velocity.ShouldNotBeNull();
        corpse.Velocity.Value.X.ShouldBe(-7f);
        corpse.Velocity.Value.Y.ShouldBe(11f);
        corpse.Velocity.Value.Z.ShouldBe(-13f);
    }

    /// <remarks>
    /// **The bone index selects an ELEMENT of the ragdoll**, not a bone of the skeleton, by the time
    /// the solver reads it: `ragdoll.list[forceBone]` (`ragdoll_shared.cpp:660`). It decides which
    /// body takes the centre-of-mass push before the rest get the offset spread.
    /// </remarks>
    [Test]
    public void Build_ForACorpseWithAForceBone_CarriesTheIndex()
    {
        Only(Demo(forceBone: 5)).ForceBone.ShouldBe(5);
    }

    /// <remarks>
    /// **Negative is a real value and means "no bone".** Valve's guard is
    /// `if ( forceBone &gt;= 0 &amp;&amp; forceBone &lt; ragdoll.listCount )`, so a negative index simply skips
    /// the centre-of-mass push and leaves the mass-scaled spread — and the `Assert` above that guard
    /// is compiled out of a release build.
    ///
    /// **So this must survive the decode unclamped.** A reader that treated a negative as "absent"
    /// would report null and lose the difference between "the server said no bone" and "the server
    /// said nothing", which is the distinction the control below rests on.
    /// </remarks>
    [Test]
    public void Build_ForACorpseWhoseForceBoneIsNegative_KeepsTheNegative()
    {
        Only(Demo(forceBone: -1)).ForceBone.ShouldBe(-1);
    }

    /// <remarks>
    /// **The control, and it is the one that stops a constant passing every test above.** A corpse
    /// on a table that declares none of the three answers null to all of them — not zero, and not an
    /// origin-shaped vector. Absent and zero are different facts here: a zero force is a death with
    /// no push, and an absent one is an era or a writer that sent nothing
    /// (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
    /// </remarks>
    [Test]
    public void Build_ForACorpseOnATableWithoutThem_ReportsNoneRatherThanZero()
    {
        SceneRagdoll corpse = Only(Demo(forceBone: null));

        corpse.Force.ShouldBeNull();
        corpse.Velocity.ShouldBeNull();
        corpse.ForceBone.ShouldBeNull();

        // The control on the control: the corpse itself decoded, so a null above is about the three
        // fields and not about the specimen failing to produce a corpse at all.
        corpse.X.ShouldBe(64f);
    }

    /// <summary>The one corpse the specimen contains.</summary>
    /// <param name="demo">The authored demo.</param>
    /// <returns>Its corpse.</returns>
    private static SceneRagdoll Only(byte[] demo)
    {
        DemoTimeline timeline = DemoTimeline.Build(demo);

        timeline.Corpses.Count.ShouldBe(1, "the specimen carries exactly one CTFRagdoll");

        return timeline.Corpses[0];
    }

    /// <summary>A demo whose single entity is a corpse with the initial conditions on it.</summary>
    /// <param name="forceBone">The bone index to send, or null to send none of the three.</param>
    /// <returns>The demo bytes.</returns>
    private static byte[] Demo(int? forceBone)
    {
        DemoSchema schema = Schema(forceBone is not null);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(RagdollClassId);

        List<DecodedProperty> properties =
        [
            Property(flat, "m_iClass", PropertyValue.FromInt(5)),
            Property(flat, "m_iTeam", PropertyValue.FromInt(SceneTeams.Red)),

            // An origin, or the corpse is decoded and then declined for having no place to be.
            Property(flat, "m_vecRagdollOrigin", PropertyValue.FromVectorXY(64f, 32f)),
            Property(flat, "m_vecRagdollOrigin[2]", PropertyValue.FromFloat(8f)),
        ];

        if (forceBone is { } bone)
        {
            properties.Add(Property(flat, "m_vecForce", PropertyValue.FromVectorXY(100f, -200f)));
            properties.Add(Property(flat, "m_vecForce[2]", PropertyValue.FromFloat(300f)));

            properties.Add(
                Property(flat, "m_vecRagdollVelocity", PropertyValue.FromVectorXY(-7f, 11f)));
            properties.Add(
                Property(flat, "m_vecRagdollVelocity[2]", PropertyValue.FromFloat(-13f)));

            properties.Add(Property(flat, "m_nForceBone", PropertyValue.FromInt(bone)));
        }

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

    /// <summary><c>DT_TFRagdoll</c> as the engine declares it, minus what this specimen ignores.</summary>
    /// <param name="withForce">Whether to declare the three physics fields at all.</param>
    /// <returns>The schema.</returns>
    /// <remarks>
    /// **`m_nForceBone` is declared SIGNED — flags 0, not 1** — because a negative index is a real
    /// value the engine admits. Declaring it unsigned the way `m_iClass` is would make the negative
    /// case untestable and would be a fixture asserting a table TF2 does not have.
    /// </remarks>
    private static DemoSchema Schema(bool withForce)
    {
        List<SendProperty> properties =
        [
            new SendProperty(SendPropType.Int, "m_iClass", 1, string.Empty, 0f, 0f, 4, 0),
            new SendProperty(SendPropType.Int, "m_iTeam", 1, string.Empty, 0f, 0f, 3, 0),
            new SendProperty(
                SendPropType.VectorXY, "m_vecRagdollOrigin", 1, string.Empty,
                -16384f, 16384f, 32, 0),
            new SendProperty(
                SendPropType.Float, "m_vecRagdollOrigin[2]", 1, string.Empty,
                -16384f, 16384f, 32, 0),
        ];

        if (withForce)
        {
            properties.Add(new SendProperty(
                SendPropType.VectorXY, "m_vecForce", 1, string.Empty, -16384f, 16384f, 32, 0));
            properties.Add(new SendProperty(
                SendPropType.Float, "m_vecForce[2]", 1, string.Empty, -16384f, 16384f, 32, 0));

            properties.Add(new SendProperty(
                SendPropType.VectorXY, "m_vecRagdollVelocity", 1, string.Empty,
                -16384f, 16384f, 32, 0));
            properties.Add(new SendProperty(
                SendPropType.Float, "m_vecRagdollVelocity[2]", 1, string.Empty,
                -16384f, 16384f, 32, 0));

            properties.Add(new SendProperty(
                SendPropType.Int, "m_nForceBone", 0, string.Empty, 0f, 0f, 8, 0));
        }

        return new DemoSchema(
            [new SendTable("DT_TFRagdoll", NeedsDecoder: true, properties)],
            [new ServerClass(RagdollClassId, "CTFRagdoll", "DT_TFRagdoll")]);
    }

    /// <summary>One property, resolved to the flattened index the encoder needs.</summary>
    /// <param name="flat">The flattened property list.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="value">Its value.</param>
    /// <returns>The property.</returns>
    /// <exception cref="KeyNotFoundException">The fixture schema declares no such property.</exception>
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
