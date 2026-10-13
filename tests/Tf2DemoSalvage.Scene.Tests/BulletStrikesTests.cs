using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation.Tests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where a client bullet stops once the players are counted (B415), as it left the view (D214).</summary>
/// <remarks>
/// The branches that need no demo. A bullet a player stops, its being remembered, and the server and brush-only
/// refusals are in <see cref="BulletStrikesStruckTests"/>, over a timeline that file writes.
/// </remarks>
public sealed class BulletStrikesTests
{
    private const int Player = 7;

    private static readonly BulletScene NoDemo = new(null, null, null, 100d, true);

    private static ShotImpact Bullet() =>
        new(3, 2, 100, 1, 0, (0f, 0f, 0f), (50f, 0f, 0f), (100f, 0f, 0f), -1);

    [Test]
    public void HitboxesOrUntested_AHitbox_IsItsFractionAskedWithTheBulletMask()
    {
        FakePosedEntities models = new() { Posed = { Player }, Fractions = { [Player] = 0.25f } };
        BulletStrikes strikes = new(models, new RecordingLogger());

        strikes.HitboxesOrUntested(Player, Vector3.Zero, Vector3.UnitX).ShouldBe(0.25f);
        models.LastMask.ShouldBe(0x0200400B | 0x40000000, "MASK_SOLID | CONTENTS_HITBOX, what FireBullet traces with");
    }

    [Test]
    public void HitboxesOrUntested_APosedPlayerTheRayMisses_IsAFullLengthTrace()
    {
        BulletStrikes strikes = new(new FakePosedEntities { Posed = { Player } }, new RecordingLogger());

        strikes.HitboxesOrUntested(Player, Vector3.Zero, Vector3.UnitX).ShouldBe(1f);
    }

    [Test]
    public void HitboxesOrUntested_APlayerNoPassPosed_IsUntested()
    {
        BulletStrikes strikes = new(new FakePosedEntities(), new RecordingLogger());

        strikes.HitboxesOrUntested(Player, Vector3.Zero, Vector3.UnitX).ShouldBeNull();
    }

    [Test]
    public void End_WithNoDemo_IsTheWorldsEndJudgedAndNobodyStruck()
    {
        BulletStrikes strikes = new(new FakePosedEntities(), new RecordingLogger());

        strikes.End(Bullet(), NoDemo).ShouldBe(((50f, 0f, 0f), true, null));
    }

    [Test]
    public void StruckPlayer_ABulletNobodyStopped_IsFalse()
    {
        BulletStrikes strikes = new(new FakePosedEntities(), new RecordingLogger());

        strikes.StruckPlayer(Bullet(), 100, NoDemo).ShouldBeFalse();
    }

    [Test]
    public void ShotOrder_WithNoDemo_IsTheTempEntityPhaseAtPlaceZeroThenThePellet() =>
        BulletStrikes.ShotOrder(null, 3, 5).ShouldBe(new ClientSoundOrder(ClientSoundPhase.TempEntities, 0, 5));
}
