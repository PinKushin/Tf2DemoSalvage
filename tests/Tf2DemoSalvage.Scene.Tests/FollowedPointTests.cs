using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where a point that rides a player was on an earlier tick (B527).</summary>
public sealed class FollowedPointTests
{
    private static readonly ParticleControlPoint Muzzle =
        new(new Vector3(110f, 0f, 50f), Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    [Test]
    public void Then_APlayerWhoOnlyMoved_CarriesThePointByHowFarHeMoved()
    {
        ParticleControlPoint then = FollowedPoint.Then(
            Muzzle, new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, 0f), 0f, new Vector3(40f, 8f, -2f), 0f);

        then.ShouldBe(Muzzle with { At = new Vector3(50f, 8f, 48f) });
    }

    /// <remarks>
    /// The drawn player trails the recorded one by the interpolation delay, so the lever arm is taken from where he is
    /// DRAWN and only the movement from the recording.
    /// </remarks>
    [Test]
    public void Then_APlayerDrawnBehindHisRecordedFrame_MovesThePointFromWhereItIsDrawn()
    {
        ParticleControlPoint then = FollowedPoint.Then(
            Muzzle, new Vector3(100f, 0f, 0f), new Vector3(130f, 0f, 0f), 0f, new Vector3(120f, 0f, 0f), 0f);

        then.At.ShouldBe(new Vector3(100f, 0f, 50f));
    }

    [Test]
    public void Then_APlayerWhoTurnedAQuarterLeftSince_SwingsThePointAndItsAxesBackAboutHim()
    {
        // He faces +Y now (yaw 90) and faced +X then (yaw 0): the muzzle ten units up his +X side was ten down his −Y.
        ParticleControlPoint then = FollowedPoint.Then(
            Muzzle, new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, 0f), 90f, new Vector3(100f, 0f, 0f), 0f);

        Near(then.At, new Vector3(100f, -10f, 50f));
        Near(then.Forward, -Vector3.UnitY);
        Near(then.Right, -Vector3.UnitX);
        Near(then.Up, Vector3.UnitZ);
    }

    private static void Near(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).ShouldBeLessThan(1e-4f, $"{actual} against {expected}");
}
