using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>What a `CTEFireBullets` carries, and who fired it (B415).</summary>
/// <remarks>
/// **`DT_TEFireBullets`** (`c_tf_fx.cpp:36`), and the demo's own schema agrees — measured on
/// `demostf-cp_process_f12`, where `m_vecOrigin` arrives as ONE vector, unlike the explosion's three floats:
///
/// <code>
/// m_vecOrigin, m_vecAngles[0], m_vecAngles[1], m_iWeaponID, m_iMode, m_iSeed, m_iPlayer, m_flSpread, m_bCritical
/// </code>
///
/// **`m_iPlayer + 1`**: `C_TEFireBullets::PostDataUpdate` calls `FX_FireBullets( NULL, m_iPlayer + 1, … )`, so the wire
/// carries the player one below the entity index.
/// </remarks>
public sealed class ShotFeedConformanceTests
{
    [Test]
    public void Record_AFireBullets_ReadsEveryFieldAndTheShooterIsOneAboveTheWire()
    {
        ShotFeed feed = new();

        feed.Record(ShotFeed.EventClassName, Shot(), tick: 500, Shooters(new ShotShooter(3, 40, 13))).ShouldBeTrue();

        SceneShot shot = feed.All[0];

        shot.Tick.ShouldBe(500);
        shot.Shooter.ShouldBe(9);
        shot.Origin.ShouldBe((696f, 1140f, 667f));
        shot.Pitch.ShouldBe(19.843f);
        shot.Yaw.ShouldBe(263.622f);
        shot.WeaponId.ShouldBe(75);
        shot.Mode.ShouldBe(1);
        shot.Seed.ShouldBe(86);
        shot.Spread.ShouldBe(0.039f);
        shot.Critical.ShouldBeTrue();
        shot.By.ShouldBe(new ShotShooter(3, 40, 13));
    }

    /// <remarks>
    /// An unsent field is the client's default: `m_iMode` 0 (`TF_WEAPON_PRIMARY_MODE`), `m_bCritical` false. f12's
    /// shots send neither on most bullets, so reading absence as anything else would change every tracer.
    /// </remarks>
    [Test]
    public void Record_ABareDelta_TakesTheClientsDefaults()
    {
        ShotFeed feed = new();

        feed.Record(ShotFeed.EventClassName, Bare(), tick: 1, Shooters(null));

        SceneShot shot = feed.All[0];

        shot.Shooter.ShouldBe(1);
        shot.Mode.ShouldBe(0);
        shot.Critical.ShouldBeFalse();
    }

    /// <remarks>
    /// **`FX_FireBullets` returns at once when `ToTFPlayer( GetBaseEntity( iPlayer ) )` is null**, so a shot from nobody
    /// draws nothing. It is still recorded, with no shooter, so the decision is made where the effect is.
    /// </remarks>
    [Test]
    public void Record_AShooterNotInTheList_HasNoShooter()
    {
        ShotFeed feed = new();

        feed.Record(ShotFeed.EventClassName, Shot(), tick: 1, Shooters(null));

        feed.All[0].By.ShouldBeNull();
    }

    [Test]
    public void Record_AnotherClass_IsNotAShot()
    {
        ShotFeed feed = new();

        feed.Record("CTETFExplosion", Shot(), tick: 1, Shooters(null)).ShouldBeFalse();

        feed.All.Count.ShouldBe(0);
    }

    [Test]
    public void Between_AWindow_ReturnsOnlyTheShotsInsideItWithTheirIndices()
    {
        ShotFeed feed = new();

        foreach (int tick in new[] { 10, 20, 20, 30 })
        {
            feed.Record(ShotFeed.EventClassName, Bare(), tick, Shooters(null));
        }

        List<(int Index, SceneShot Shot)> found = [];

        feed.Between(20, 25, found);

        found.Count.ShouldBe(2);
        found[0].Index.ShouldBe(1);
        found[1].Index.ShouldBe(2);
    }

    /// <summary>A shooter lookup that answers the same for every index.</summary>
    private static Func<int, ShotShooter?> Shooters(ShotShooter? answer) => _ => answer;

    /// <summary>The first `CTEFireBullets` of f12's trace, with a mode and a crit added.</summary>
    private static DecodedTempEntity Shot() =>
        new(
            ClassId: 152,
            DelaySeconds: 0f,
            Properties:
            [
                Vector("m_vecOrigin", (696f, 1140f, 667f)),
                Float("m_vecAngles[0]", 19.843f),
                Float("m_vecAngles[1]", 263.622f),
                Int("m_iWeaponID", 75),
                Int("m_iMode", 1),
                Int("m_iSeed", 86),
                Int("m_iPlayer", 8),
                Float("m_flSpread", 0.039f),
                Int("m_bCritical", 1),
            ]);

    /// <summary>A delta that sends nothing.</summary>
    private static DecodedTempEntity Bare() => new(ClassId: 152, DelaySeconds: 0f, Properties: []);

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
                OwnerTable: "DT_TEFireBullets",
                ArrayElement: null),
            Value: value);
}
