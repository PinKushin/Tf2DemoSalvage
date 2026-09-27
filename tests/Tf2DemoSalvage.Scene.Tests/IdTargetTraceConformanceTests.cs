using System.Numerics;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`C_TFPlayer::UpdateIDTarget` / `GetIDTarget()` (c_tf_player.cpp:7025, :7041).</summary>
/// <remarks>The tracer is entity 1, RED (team 2); a target at entity 2 unless noted otherwise.</remarks>
public sealed class IdTargetTraceConformanceTests
{
    private static readonly Vector3 Origin = new(0f, 0f, 64f);
    private static readonly Vector3 Forward = new(1f, 0f, 0f);

    [Test]
    public void GetIdTarget_SolidHitsAPlayer_ReturnsThatPlayer() =>
        Trace(Solid(2, isPlayer: true, team: 3)).ShouldBe(2);

    [Test]
    public void GetIdTarget_SolidHitsTheWorld_ReturnsZero() =>
        Trace(World()).ShouldBe(0);

    [Test]
    public void GetIdTarget_SolidHitsSelf_ReturnsZero() =>
        Trace(Solid(1, isPlayer: true, team: 2)).ShouldBe(0);

    [Test]
    public void GetIdTarget_StartSolidAgainstAFriendly_ReturnsZero() =>
        Trace(Solid(2, isPlayer: true, team: 2, startSolid: true)).ShouldBe(0);

    [Test]
    public void GetIdTarget_StartSolidAgainstAnEnemy_ReturnsThatPlayer() =>
        Trace(Solid(2, isPlayer: true, team: 3, startSolid: true)).ShouldBe(2);

    [Test]
    public void GetIdTarget_ShotTraceHitsADifferentPlayer_PrefersTheShotTrace() =>
        Trace(Solid(2, isPlayer: true, team: 3), shot: Solid(3, isPlayer: true, team: 3)).ShouldBe(3);

    [Test]
    public void GetIdTarget_ShotTraceMissesEveryone_KeepsTheSolidTrace() =>
        Trace(Solid(2, isPlayer: true, team: 3), shot: World()).ShouldBe(2);

    [Test]
    public void GetIdTarget_SolidHitsANonPlayerEntity_ReturnsIt() =>
        Trace(Solid(5, isPlayer: false, team: 0)).ShouldBe(5);

    [Test]
    public void GetIdTarget_Observing_IgnoresTheObserverTargetNotSelf() =>
        Trace(Solid(9, isPlayer: true, team: 3), isObserver: true, observerTarget: 9, ignoreExpected: 9).ShouldBe(9);

    private static int Trace(IdTraceHit solid, IdTraceHit? shot = null, bool isObserver = false, int observerTarget = 0, int? ignoreExpected = null) =>
        IdTargetTrace.GetIdTarget(
            Origin,
            Forward,
            selfEntity: 1,
            selfTeam: 2,
            isObserver: isObserver,
            observerTarget: observerTarget,
            solidTrace: (_, _, ignore) =>
            {
                if (ignoreExpected is { } expected)
                {
                    ignore.ShouldBe(expected);
                }

                return solid;
            },
            shotTrace: (_, _, _) => shot ?? solid);

    private static IdTraceHit Solid(int entity, bool isPlayer, int team, bool startSolid = false) =>
        new(entity, isPlayer, team, startSolid, DidHitNonWorldEntity: true);

    private static IdTraceHit World() => new(null, false, 0, false, DidHitNonWorldEntity: false);
}
