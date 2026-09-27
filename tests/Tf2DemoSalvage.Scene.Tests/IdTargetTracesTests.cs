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

    private static IdTargetTraces Traces(float worldFraction, System.Func<int, float?>? hitboxes = null) =>
        new(
            [new BulletTarget(2, new Vector3(100f, 0f, 0f), false), new BulletTarget(3, new Vector3(200f, 0f, 0f), false)],
            new Dictionary<int, int> { [2] = 3, [3] = 2 },
            (_, _) => worldFraction,
            (entity, _, _) => hitboxes?.Invoke(entity) ?? (entity == 2 ? 0.09f : 0.19f));
}
