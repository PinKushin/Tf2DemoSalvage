using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A client bullet a player stops — `FX_FireBullets` into `CTFPlayer::FireBullet`'s trace (tf_fx_shared.cpp:158,
/// tf_player_shared.cpp:10276) — over players this file put in a demo (D38, D213).
/// </summary>
/// <remarks>
/// A shooter at the origin, a scout 300 units along +x, a dead man beside him, and a map with nothing in it. The bullet
/// runs along +x at chest height; the world stops it at 500 of a 1000-unit range. The posed entities are a fake that
/// answers the hitbox trace with a fraction of the ray, so the end is arithmetic: 1000 times the fraction.
/// </remarks>
public sealed class BulletStrikesStruckTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int Shooter = 1;
    private const int Scout = 2;
    private const int Corpse = 3;
    private const int ShotTick = 100;

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticEffects.Demo(
        [
            new(Shooter, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, Red)),
            new(Scout, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(300f, 0f, 0f, Blue)),
            new(
                Corpse,
                SyntheticEffects.PlayerClass,
                new Dictionary<string, PropertyValue>(SyntheticEffects.Standing(200f, 0f, 0f, Blue), StringComparer.Ordinal)
                {
                    ["m_lifeState"] = PropertyValue.FromInt(1),
                }),
        ],
        [string.Empty, "Impact"],
        [],
        [],
        new SyntheticEffects.Effect(90, "CTETFBlood", new Dictionary<string, PropertyValue>(StringComparer.Ordinal)),
        new SyntheticEffects.Effect(
            91,
            "CTEFireBullets",
            new Dictionary<string, PropertyValue>(StringComparer.Ordinal) { ["m_iPlayer"] = PropertyValue.FromInt(Shooter - 1) }),
        new SyntheticEffects.Effect(
            92,
            "CTEEffectDispatch",
            new Dictionary<string, PropertyValue>(StringComparer.Ordinal) { ["m_iEffectName"] = PropertyValue.FromInt(1) })));

    private static readonly BulletScene Scene = new(Timeline, Empty, null, ShotTick, true);

    private static ShotImpact Bullet(int bullet = 0, int shooter = Shooter) =>
        new(0, bullet, ShotTick, shooter, Red, (0f, 0f, 40f), (500f, 0f, 40f), (1000f, 0f, 40f), -1);

    /// <summary>Posed entities whose hitbox trace stops the ray on the scout at 290 units and on the corpse at 190.</summary>
    private static (BulletStrikes Strikes, FakePosedEntities Models) Posed()
    {
        FakePosedEntities models = new()
        {
            Posed = { Scout, Corpse },
            Fractions = { [Scout] = 0.29f, [Corpse] = 0.19f },
        };

        return (new BulletStrikes(models, new RecordingLogger()), models);
    }

    [Test]
    public void End_ABulletWhoseRayMeetsALivingPlayersHitboxes_StopsThereAndNamesHim()
    {
        Timeline.PlayersAt(ShotTick).Select(static player => player.EntityIndex).ShouldBe([Shooter, Scout, Corpse], ignoreOrder: true);

        (BulletStrikes strikes, _) = Posed();

        ((float X, float Y, float Z) end, bool judged, int? struck) = strikes.End(Bullet(), Scene);

        struck.ShouldBe(Scout, "the dead man nearer the gun is passed over, and so is the shooter");
        end.X.ShouldBe(290f, 0.001f);
        end.Y.ShouldBe(0f);
        end.Z.ShouldBe(40f);
        judged.ShouldBeTrue();
    }

    [Test]
    public void End_ABulletWhosePlayersNoPassPosed_IsTheWorldsEndAndNotYetJudged()
    {
        BulletStrikes strikes = new(new FakePosedEntities(), new RecordingLogger());

        // Unposed, the scout's collision box answers for him: the ray enters it at 276 of 1000.
        ((float X, float Y, float Z) end, bool judged, int? struck) = strikes.End(Bullet(), Scene);

        struck.ShouldBe(Scout);
        end.X.ShouldBe(276f, 0.001f);
        judged.ShouldBeFalse("nobody the bullet could meet has hitboxes to ask yet");
    }

    [Test]
    public void End_ABulletFiredByThePlayerInItsPath_PassesThroughHim()
    {
        (BulletStrikes strikes, FakePosedEntities models) = Posed();

        // The man at the origin is now a target the ray starts inside; posed and missed, he is out of it too.
        models.Posed.Add(Shooter);

        strikes.End(Bullet(shooter: Scout), Scene).ShouldBe(((500f, 0f, 40f), true, null), "CTraceFilterSimple( this, … )");
    }

    [Test]
    public void StruckPlayer_ABulletAPlayerStoppedOnItsOwnTick_IsRememberedAfterHeMoves()
    {
        (BulletStrikes strikes, FakePosedEntities models) = Posed();

        strikes.StruckPlayer(Bullet(), ShotTick, Scene).ShouldBeTrue();

        // He steps out of the way; the decal and the effect must still agree about this bullet.
        models.Fractions.Clear();
        models.Posed.Clear();

        strikes.StruckPlayer(Bullet(), ShotTick + 50, Scene with { Timeline = null }).ShouldBeTrue("remembered by shot and pellet");
        strikes.StruckPlayer(Bullet(bullet: 1), ShotTick + 50, Scene with { Timeline = null }).ShouldBeFalse("another pellet is another question");

        strikes.Clear();

        strikes.StruckPlayer(Bullet(), ShotTick + 50, Scene with { Timeline = null }).ShouldBeFalse("a new map forgets");
    }

    [Test]
    public void StruckPlayer_ABulletFirstMetTicksAfterItWasFired_IsTheWorlds()
    {
        (BulletStrikes strikes, _) = Posed();

        strikes.StruckPlayer(Bullet(), ShotTick + 2, Scene).ShouldBeFalse("this frame's bones are not the ones it met");
        strikes.StruckPlayer(Bullet(), ShotTick + 1, Scene).ShouldBeTrue("the control: one tick late is still its own");
    }

    [Test]
    public void StruckPlayer_AnImpactTheServerTraced_IsNeverJudged()
    {
        (BulletStrikes strikes, _) = Posed();

        strikes.StruckPlayer(Bullet() with { Shot = -1 }, ShotTick, Scene).ShouldBeFalse("the server already traced it");
    }

    [Test]
    public void StruckPlayer_ABoltTracedAgainstBrushesAlone_IsNeverJudged()
    {
        (BulletStrikes strikes, _) = Posed();

        strikes.StruckPlayer(Bullet() with { BrushOnly = true }, ShotTick, Scene).ShouldBeFalse("MASK_SOLID_BRUSHONLY holds no player");
    }

    [Test]
    public void ShotOrder_AClientShot_IsItsFireBulletsPlaceInTheStreamThenThePellet()
    {
        int place = Timeline.Shots.All.ShouldHaveSingleItem().TempEntity;

        place.ShouldBeGreaterThan(0, "the control: a blood event precedes it, so its place is not the default");
        BulletStrikes.ShotOrder(Timeline, 0, 5).ShouldBe(new ClientSoundOrder(ClientSoundPhase.TempEntities, place, 5));
    }

    [Test]
    public void ShotOrder_AServerImpact_IsItsDispatchsPlaceInTheStream()
    {
        int place = Timeline.Dispatches.All.ShouldHaveSingleItem().TempEntity;

        place.ShouldBeGreaterThan(Timeline.Shots.All[0].TempEntity, "the control: it follows the shot");
        BulletStrikes.ShotOrder(Timeline, -1, 0).ShouldBe(new ClientSoundOrder(ClientSoundPhase.TempEntities, place, 0));
    }

    [TestCase(1, TestName = "ShotOrder_AShotPastTheFeed_IsPlaceZero")]
    [TestCase(-2, TestName = "ShotOrder_ADispatchPastTheFeed_IsPlaceZero")]
    public void ShotOrder_AnIndexTheFeedDoesNotHold_IsPlaceZero(int shot) =>
        BulletStrikes.ShotOrder(Timeline, shot, 0).ShouldBe(new ClientSoundOrder(ClientSoundPhase.TempEntities, 0, 0));
}
