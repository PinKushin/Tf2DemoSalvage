using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `ImpactSoundGroup` (`tf_fx_shared.cpp:40`): within one shot's pellets, an impact sound is not played again within 300
/// units of the same sound already played. f12 from gummo's eyes, 13770-14100: TF2 started 16 impact sounds, we 26.
/// </summary>
[TestFixture]
public sealed class ShotSoundGroupTests
{
    [Test]
    public void Allows_TheSameSoundWithin300OfOneAlreadyPlayed_IsRefused()
    {
        ShotSoundGroup group = new();
        group.Start(1);

        group.Allows("Concrete.BulletImpact", new Vector3(0, 0, 0)).ShouldBeTrue();
        group.Allows("Concrete.BulletImpact", new Vector3(299, 0, 0)).ShouldBeFalse();
    }

    [Test]
    public void Allows_AnotherSoundOrFartherThan300_IsPlayed()
    {
        ShotSoundGroup group = new();
        group.Start(1);
        group.Allows("Concrete.BulletImpact", new Vector3(0, 0, 0));

        group.Allows("Flesh.BulletImpact", new Vector3(10, 0, 0)).ShouldBeTrue("a different sound is not grouped");
        group.Allows("Concrete.BulletImpact", new Vector3(301, 0, 0)).ShouldBeTrue("the engine's test is strictly within 300");
    }

    [Test]
    public void Start_ANewShot_ForgetsTheLastShotsSounds()
    {
        // `EndGroupingSounds` purges the list after every FX_FireBullets call.
        ShotSoundGroup group = new();
        group.Start(1);
        group.Allows("Concrete.BulletImpact", new Vector3(0, 0, 0));

        group.Start(2);

        group.Allows("Concrete.BulletImpact", new Vector3(0, 0, 0)).ShouldBeTrue();
    }

    [Test]
    public void Allows_TheNameIsComparedIgnoringCase_AsQStricmpDoes()
    {
        ShotSoundGroup group = new();
        group.Start(1);
        group.Allows("Concrete.BulletImpact", Vector3.Zero);

        group.Allows("concrete.bulletimpact", Vector3.One).ShouldBeFalse();
    }
}
