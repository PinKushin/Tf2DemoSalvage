using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFPlayer::FireBullet`'s water test (`tf_player_shared.cpp:10513`): a shot that starts out of water and stops in it takes
/// `ImpactWaterTrace` — a `tf_gunshotsplash` where it entered the water — and none of the regular impact effects.
/// </summary>
/// <remarks>The water here is everything below z = 0.</remarks>
public sealed class BulletWaterConformanceTests
{
    private const int Water = 0x20;

    private static int Contents(float x, float y, float z) => z < 0f ? Water : 0;

    [Test]
    public void Mark_AShotFromTheAirIntoWater_SplashesWhereItEntered()
    {
        ShotImpact into = Impact((0f, 0f, 100f), (100f, 0f, -100f));

        ShotImpact marked = BulletWater.Mark([into], static _ => 0, Contents)[0];

        marked.WaterEntry.ShouldNotBeNull();
        marked.WaterEntry.Value.Z.ShouldBe(0f, 0.01f);
        marked.WaterEntry.Value.X.ShouldBe(50f, 0.01f);
        marked.Splash.ShouldBe("water_bulletsplash01");
    }

    /// <remarks>`tf_gunshotsplash_minigun` for the minigun, whose particle system is `water_bulletsplash01_minigun`.</remarks>
    [Test]
    public void Mark_AMinigunShotIntoWater_TakesTheMinigunsSplash() =>
        BulletWater.Mark([Impact((0f, 0f, 100f), (100f, 0f, -100f))], static _ => BulletWater.MinigunId, Contents)[0]
            .Splash.ShouldBe("water_bulletsplash01_minigun");

    [Test]
    public void Mark_AShotFromUnderwater_IsARegularImpact() =>
        BulletWater.Mark([Impact((0f, 0f, -10f), (100f, 0f, -100f))], static _ => 0, Contents)[0].WaterEntry.ShouldBeNull();

    [Test]
    public void Mark_AShotThatStaysDry_IsARegularImpact() =>
        BulletWater.Mark([Impact((0f, 0f, 100f), (100f, 0f, 10f))], static _ => 0, Contents)[0].WaterEntry.ShouldBeNull();

    private static ShotImpact Impact((float X, float Y, float Z) start, (float X, float Y, float Z) end) =>
        new(0, 0, 10, 5, 2, start, end, end, 0);
}
