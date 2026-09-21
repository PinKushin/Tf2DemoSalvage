using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>What a `CTETFExplosion` carries, and what the client makes of it (B415).</summary>
/// <remarks>
/// **`DT_TETFExplosion`** (`tf_fx_explosions.cpp:236-246`), and the demo's own schema agrees field for field:
///
/// <code>
/// RecvPropFloat ( RECVINFO( m_vecOrigin[0] ) ),
/// RecvPropFloat ( RECVINFO( m_vecOrigin[1] ) ),
/// RecvPropFloat ( RECVINFO( m_vecOrigin[2] ) ),
/// RecvPropVector( RECVINFO( m_vecNormal ) ),
/// RecvPropInt   ( RECVINFO( m_iWeaponID ) ),
/// RecvPropInt   ( "entindex", 0, SIZEOF_IGNORE, 0, RecvProxy_ExplosionEntIndex ),
/// RecvPropInt   ( RECVINFO( m_nDefID ) ),
/// RecvPropInt   ( RECVINFO( m_nSound ) ),
/// RecvPropInt   ( RECVINFO( m_iCustomParticleIndex ) ),
/// </code>
///
/// **The origin arrives as three separate floats and the normal as one vector**, which is not a detail to smooth
/// over: a reader that expected `m_vecOrigin` as a vector finds no such property and places every blast at the world
/// origin, with nothing to report.
///
/// **The ANGLES are not computed here and that is a layering decision, not an omission.** `TFExplosionCallback`
/// turns the normal into `angExplosion` with `VectorAngles`, which this project has as
/// `Tf2DemoSalvage.Scene.AngleVectors.Angles` — one copy, with its own conformance tests. Core cannot reach Scene,
/// so what is decoded here is the raw normal and the one predicate the angle choice hangs on (`InAir`); the
/// conversion itself is asserted where the function lives.
/// </remarks>
public sealed class ExplosionFeedConformanceTests
{
    /// <remarks>
    /// **The whole record, because every field of it steers something.** The origin and normal place and orient the
    /// effect, the weapon id picks which script names it, the entity index decides `bIsPlayer`, and the custom index
    /// overrides all of that.
    /// </remarks>
    [Test]
    public void Record_ATfExplosion_ReadsEveryFieldOfTheSendTable()
    {
        ExplosionFeed feed = new();

        feed.Record(
            ExplosionFeed.EventClassName,
            Explosion(x: 1024f, y: -512f, z: 96f, normal: (0f, 0f, 1f), weapon: 22, entity: 7, custom: 31),
            tick: 4200,
            NoPlayers).ShouldBeTrue();

        SceneExplosion blast = feed.All.ShouldHaveSingleItem();

        blast.Tick.ShouldBe(4200);
        blast.X.ShouldBe(1024f);
        blast.Y.ShouldBe(-512f);
        blast.Z.ShouldBe(96f);
        blast.Normal.ShouldBe((0f, 0f, 1f));
        blast.WeaponId.ShouldBe(22);
        blast.Entity.ShouldBe(7);
        blast.CustomParticleIndex.ShouldBe(31);
    }

    /// <remarks>
    /// **A blast in mid air is recognised by the NORMAL's magnitude, not by a trace**
    /// (`tf_fx_explosions.cpp:75-84`):
    ///
    /// <code>
    /// // Cannot use zeros here because we are sending the normal at a smaller bit size.
    /// if ( fabs( vecNormal.x ) &lt; 0.05f &amp;&amp; fabs( vecNormal.y ) &lt; 0.05f &amp;&amp; fabs( vecNormal.z ) &lt; 0.05f )
    /// {
    ///     bInAir = true;
    ///     angExplosion.Init();
    /// }
    /// </code>
    ///
    /// Valve's comment is the reason the test is a magnitude rather than an equality: the server cannot send an
    /// exact zero through the quantised vector, so it sends something small and the client reads the smallness.
    /// </remarks>
    [Test]
    public void InAir_ANormalTooSmallToSurvivePacking_IsTrue()
    {
        Recorded(normal: (0.04f, -0.04f, 0.049f)).InAir.ShouldBeTrue();
        Recorded(normal: (0f, 0f, 0f)).InAir.ShouldBeTrue();
    }

    /// <remarks>
    /// The control: <b>all three</b> components must be small. A blast against a wall has a unit normal, and one
    /// component alone being tiny is the common case rather than the in-air one.
    /// </remarks>
    [Test]
    public void InAir_ASurfaceNormal_IsFalse()
    {
        Recorded(normal: (0f, 0f, 1f)).InAir.ShouldBeFalse("straight up is a floor, not mid air");
        Recorded(normal: (0.04f, 0.04f, 0.999f)).InAir.ShouldBeFalse("only two of three are small");
    }

    /// <remarks>
    /// **`INVALID_STRING_INDEX` is 65535 and not −1**, because `networkstringtabledefs.h:17` declares it
    /// <c>(unsigned short)-1</c> and the field it lands in is an `int` sent through a signed 32-bit
    /// `SendPropInt`. Read −1 as the sentinel and 89% of one real match's explosions claim a custom particle they
    /// do not have — a wrong answer that is plausible, has no exception, and reads as a finding about modern TF2.
    /// </remarks>
    [Test]
    public void HasCustomParticle_TheEnginesOwnInvalidIndex_IsFalse()
    {
        Recorded(custom: 65535).HasCustomParticle.ShouldBeFalse("INVALID_STRING_INDEX is (unsigned short)-1");
        Recorded(custom: 31).HasCustomParticle.ShouldBeTrue();

        // **−1 is a REAL index as far as this field is concerned** — it is not the sentinel, and treating it as
        // one is the mistake this pins. Nothing sends it, which is exactly why nothing would report the error.
        Recorded(custom: -1).HasCustomParticle.ShouldBeTrue();
    }

    /// <remarks>
    /// **The wire has two encodings for "no entity" and `RecvProxy_ExplosionEntIndex` accepts both**
    /// (`tf_fx_explosions.cpp:222-229`): 2047 today, −1 in older demos, with Valve's own comment saying so. A reader
    /// that knew only the modern one would hand an old demo's blast −1 to look up; one that knew only −1 would look
    /// up edict 2047. Both then pick the wrong branch of `bIsPlayer` and draw the wrong effect.
    /// </remarks>
    [Test]
    public void Record_EitherEncodingOfNoEntity_IsNoEntity()
    {
        Recorded(entity: SceneExplosion.InvalidEntityIndex).HasEntity.ShouldBeFalse("2047 is the new encoding");
        Recorded(entity: -1).HasEntity.ShouldBeFalse("-1 is what old demos and replays carry");
    }

    /// <remarks>
    /// The control: worldspawn is entity ZERO and a blast really can name it, so a reader that treated the absent
    /// case as zero would be indistinguishable from one that got it right until a blast hit the world.
    /// </remarks>
    [Test]
    public void Record_ARealEntityIndex_IsKept()
    {
        Recorded(entity: 0).HasEntity.ShouldBeTrue("zero is worldspawn, not 'nothing'");
        Recorded(entity: 12).Entity.ShouldBe(12);
    }

    /// <remarks>
    /// **`bIsPlayer` is asked of the client's entity list when the blast arrives** (`tf_fx_explosions.cpp:62-70`):
    ///
    /// <code>
    /// C_BaseEntity *pEntity = C_BaseEntity::Instance( hEntity );
    /// if ( pEntity &amp;&amp; pEntity->IsPlayer() ) bIsPlayer = true;
    /// </code>
    ///
    /// It is the one thing on a blast that is NOT on the wire, and it is what makes a direct hit draw the weapon's
    /// `ExplosionPlayerEffect` instead of its wall effect.
    /// </remarks>
    [Test]
    public void Record_ABlastNamingAPlayer_StruckAPlayer()
    {
        ExplosionFeed feed = new();

        feed.Record(ExplosionFeed.EventClassName, Explosion(weapon: 22, entity: 5), tick: 1, isPlayer: index => index == 5);

        feed.All[0].StruckPlayer.ShouldBeTrue();
    }

    /// <remarks>The control: an entity that is not a player — a sentry, a door — is not a player hit.</remarks>
    [Test]
    public void Record_ABlastNamingAnotherEntity_DidNotStrikeAPlayer()
    {
        ExplosionFeed feed = new();

        feed.Record(ExplosionFeed.EventClassName, Explosion(weapon: 22, entity: 300), tick: 1, isPlayer: index => index == 5);

        feed.All[0].StruckPlayer.ShouldBeFalse();
    }

    /// <remarks>
    /// **"No entity" is never looked up**, in either encoding — `RecvProxy_ExplosionEntIndex` turns both into
    /// `INVALID_EHANDLE`, whose `Get()` is null. Asked of a table that would say yes to anything, a reader that looked
    /// up −1 or 2047 anyway would call it a player hit.
    /// </remarks>
    [TestCase(SceneExplosion.InvalidEntityIndex)]
    [TestCase(-1)]
    public void Record_ABlastNamingNoEntity_AsksNothing(int sent)
    {
        ExplosionFeed feed = new();
        int asked = 0;

        feed.Record(
            ExplosionFeed.EventClassName,
            Explosion(weapon: 22, entity: sent),
            tick: 1,
            isPlayer: _ =>
            {
                asked++;
                return true;
            });

        feed.All[0].StruckPlayer.ShouldBeFalse();
        asked.ShouldBe(0);
    }

    /// <remarks>
    /// **Every other temp entity class is ignored here rather than mis-read.** The feed is offered every decoded
    /// effect in the packet — blood, decals, dust — and a reader that took them all would place a dust puff's fields
    /// into an explosion's record and draw a blast.
    /// </remarks>
    [Test]
    public void Record_AnotherTempEntityClass_IsIgnored()
    {
        ExplosionFeed feed = new();

        feed.Record("CTETFBlood", Explosion(weapon: 22), tick: 10, NoPlayers).ShouldBeFalse();

        feed.All.ShouldBeEmpty();
    }

    /// <remarks>
    /// **Blasts are kept in tick order so a viewer can ask what fired recently.** They arrive in order, and the
    /// search below is what the renderer uses to replay a one-shot forward from where it started.
    /// </remarks>
    [Test]
    public void Between_AWindowOfTicks_IsEveryBlastInsideItAndNoOther()
    {
        ExplosionFeed feed = new();

        foreach (int tick in new[] { 100, 250, 250, 251, 900 })
        {
            feed.Record(ExplosionFeed.EventClassName, Explosion(weapon: 22), tick, NoPlayers);
        }

        List<(int Index, SceneExplosion Blast)> window = [];
        feed.Between(250, 251, window);

        window.Count.ShouldBe(3, "two at 250 and one at 251, with 100 and 900 outside");
        window[0].Blast.Tick.ShouldBe(250);
        window[2].Blast.Tick.ShouldBe(251);

        // **The index is into `All`, which is what makes it a stable identity between frames.** Off by one and
        // two blasts share a key, so one explosion of a pair never draws.
        window.Select(one => one.Index).ShouldBe([1, 2, 3]);
    }

    /// <remarks>An empty window is empty rather than the nearest blast — the search is a range, not a lookup.</remarks>
    [Test]
    public void Between_AWindowWithNoBlasts_IsEmpty()
    {
        ExplosionFeed feed = new();

        feed.Record(ExplosionFeed.EventClassName, Explosion(weapon: 22), tick: 100, NoPlayers);

        List<(int Index, SceneExplosion Blast)> window = [];
        feed.Between(200, 300, window);

        window.ShouldBeEmpty();
    }

    /// <summary>One blast, recorded and handed straight back.</summary>
    private static SceneExplosion Recorded(
        (float X, float Y, float Z) normal = default,
        int entity = SceneExplosion.NoEntity,
        int custom = SceneExplosion.NoCustomParticle)
    {
        ExplosionFeed feed = new();

        feed.Record(
            ExplosionFeed.EventClassName,
            Explosion(normal: normal, weapon: 22, entity: entity, custom: custom),
            tick: 1,
            NoPlayers);

        return feed.All[0];
    }

    /// <summary>An entity list with no players in it.</summary>
    private static readonly Func<int, bool> NoPlayers = static _ => false;

    /// <summary>A decoded <c>CTETFExplosion</c> with the fields the send table declares.</summary>
    private static DecodedTempEntity Explosion(
        float x = 0f,
        float y = 0f,
        float z = 0f,
        (float X, float Y, float Z) normal = default,
        int weapon = 0,
        int entity = SceneExplosion.NoEntity,
        int custom = SceneExplosion.NoCustomParticle) =>
        new(
            ClassId: 172,
            DelaySeconds: 0f,
            Properties:
            [
                Float("m_vecOrigin[0]", x),
                Float("m_vecOrigin[1]", y),
                Float("m_vecOrigin[2]", z),
                Vector("m_vecNormal", normal),
                Int("m_iWeaponID", weapon),
                Int("entindex", entity),
                Int("m_nDefID", -1),
                Int("m_nSound", 0),
                Int("m_iCustomParticleIndex", custom),
            ]);

    private static DecodedProperty Int(string name, int value) =>
        Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Float(string name, float value) =>
        Declared(name, SendPropType.Float, PropertyValue.FromFloat(value));

    private static DecodedProperty Vector(string name, (float X, float Y, float Z) value) =>
        Declared(name, SendPropType.Vector, PropertyValue.FromVector(value.X, value.Y, value.Z));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(
                new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0),
                OwnerTable: "DT_TETFExplosion",
                ArrayElement: null),
            Value: value);
}
