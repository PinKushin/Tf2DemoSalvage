using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The viewer's answers to `UpdateIDTarget`'s two traces: `MASK_SOLID` stops on the world or a player's collision hull,
/// `MASK_SHOT` on the world or a player's hitboxes.
/// </summary>
/// <remarks>A ray from the origin along +X. Player 2 (team 3) stands at x 100, player 3 (team 2) at x 200.</remarks>
public sealed class IdTargetTracesTests
{
    private static readonly Vector3 Start = new(0f, 0f, 40f);
    private static readonly Vector3 End = new(1000f, 0f, 40f);

    [Test]
    public void Solid_TwoPlayersInLine_HitsTheNearerHull()
    {
        IdTraceHit hit = Traces(worldFraction: 1f).Solid(Start, End, 0);

        (hit.Entity, hit.IsPlayer, hit.Team, hit.DidHitNonWorldEntity, hit.StartSolid).ShouldBe((2, true, 3, true, false));
    }

    [Test]
    public void Solid_TheIgnoredPlayer_IsPassedOver() =>
        Traces(worldFraction: 1f).Solid(Start, End, 2).Entity.ShouldBe(3);

    [Test]
    public void Solid_AWallBeforeThePlayers_IsTheWorld()
    {
        IdTraceHit hit = Traces(worldFraction: 0.05f).Solid(Start, End, 0);

        (hit.Entity, hit.DidHitNonWorldEntity).ShouldBe(((int?)null, false));
    }

    [Test]
    public void Solid_FromInsideAHull_StartsSolid() =>
        Traces(worldFraction: 1f).Solid(new Vector3(100f, 0f, 40f), End, 0).StartSolid.ShouldBeTrue();

    [Test]
    public void Shot_AHullWithoutAHitboxHit_PassesThroughToTheNext()
    {
        // Player 2's hitboxes miss; player 3's hit — the shot trace is by hitboxes, not hulls.
        IdTraceHit hit = Traces(worldFraction: 1f, hitboxes: entity => entity == 3 ? 0.19f : 2f).Shot(Start, End, 0);

        hit.Entity.ShouldBe(3);
    }

    [Test]
    public void Solid_ABuildingBeforeThePlayers_HitsItsBox()
    {
        // A sentry's SOLID_BBOX, x 50..70: `CONTENTS_SOLID`, which both masks stop on.
        IdTraceHit hit = Traces(worldFraction: 1f, withSentry: true).Solid(Start, End, 0);

        (hit.Entity, hit.IsPlayer, hit.Team, hit.DidHitNonWorldEntity).ShouldBe((55, false, 2, true));
    }

    [Test]
    public void Shot_ABuildingBeforeThePlayers_HitsItsBoxToo() =>
        Traces(worldFraction: 1f, withSentry: true).Shot(Start, End, 0).Entity.ShouldBe(55);

    [Test]
    public void Solid_ABuildingAboveTheRay_IsMissed() =>
        Traces(worldFraction: 1f, withSentry: true, sentryBottom: 50f).Solid(Start, End, 0).Entity.ShouldBe(2);

    [Test]
    public void Solid_AVphysicsWeapon_IsClippedAgainstItsHullNotItsBox()
    {
        // The hull is the one-ledge box at x 100 yawed 90° (±9.84 along world x); the bounding box sits elsewhere entirely.
        StaticPropCollision hull = StaticPropCollision.From(
            [new Content.Bsp.BspStaticProp("models/w.mdl", 100f, 0f, 40f, 0f, 90f, 0f, 1f, Solid: 6)],
            _ => StaticPropCollisionConformanceTests.Collide(),
            _ => -1);
        IdTargetBox weapon = new(60, 0, new Vector3(500f, 500f, 500f), new Vector3(510f, 510f, 510f), hull);
        IdTargetTraces traces = new([], new Dictionary<int, int>(), (_, _) => 1f, (_, _, _) => null, [weapon]);

        (traces.Solid(new Vector3(0f, 0f, 40f), new Vector3(200f, 0f, 40f), 0).Entity).ShouldBe(60);
        traces.Solid(new Vector3(0f, 12f, 40f), new Vector3(200f, 12f, 40f), 0).Entity.ShouldBe(60, "39.37 wide along y once yawed");
        traces.Solid(new Vector3(0f, 45f, 40f), new Vector3(200f, 45f, 40f), 0).Entity.ShouldBeNull("past the hull");
    }

    private static IdTargetTraces Traces(float worldFraction, System.Func<int, float?>? hitboxes = null, bool withSentry = false, float sentryBottom = 0f) =>
        new(
            [new BulletTarget(2, new Vector3(100f, 0f, 0f), false), new BulletTarget(3, new Vector3(200f, 0f, 0f), false)],
            new Dictionary<int, int> { [2] = 3, [3] = 2 },
            (_, _) => worldFraction,
            (entity, _, _) => hitboxes?.Invoke(entity) ?? (entity == 2 ? 0.09f : 0.19f),
            withSentry ? [new IdTargetBox(55, 2, new Vector3(50f, -20f, sentryBottom), new Vector3(70f, 20f, 66f))] : null);
}
