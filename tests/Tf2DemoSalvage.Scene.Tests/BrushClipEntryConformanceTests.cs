using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.RotatedBrushTraceConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CM_ClipBoxToBrush</c>'s entry fraction for a box that starts closer to a brush than <c>DIST_EPSILON</c> (B604).
/// </summary>
/// <remarks>
/// **Read from published source, the engine's ancestor:** Quake 2's <c>CM_ClipBoxToBrush</c> (<c>qcommon/cmodel.c</c>,
/// id Software's GPL release) takes an entering crossing as <c>f = (d1 - DIST_EPSILON) / (d1 - d2)</c>, which is negative
/// when the start is nearer the face than the epsilon, and keeps it: <c>if (enterfrac &gt; -1 &amp;&amp; enterfrac &lt;
/// trace-&gt;fraction) { if (enterfrac &lt; 0) enterfrac = 0; … }</c>. *Interpolated to Source*, whose <c>cmodel.cpp</c> the
/// SDK does not ship; this project's own box sweep (<see cref="MovementWorld.Sweep"/>) already followed it. A movement trace
/// ends <c>DIST_EPSILON</c> short of what it hits, so a player resting against a wall or a slope starts every trace inside the
/// epsilon; refused, the crossing let him pass into the brush, and <c>TryPlayerMove</c>'s unswept re-trace at the end then
/// zeroed his velocity (<c>gamemovement.cpp:2620-2632</c>) — a running recorder drawn standing (B604).
/// </remarks>
public sealed class BrushClipEntryConformanceTests
{
    private const float Tick = 0.015f;

    private static readonly (float X, float Y, float Z) Mins = (-24f, -24f, 0f);
    private static readonly (float X, float Y, float Z) Maxs = (24f, 24f, 82f);

    /// <summary>The slab's face at x = 4, met by a box's low side at feet x = 28; 0.01 off it is inside the epsilon.</summary>
    private const float WithinEpsilon = 28.01f;

    [Test]
    public void TraceHull_ABoxNearerABrushThanTheEpsilonMovingIntoIt_StopsWhereItStands()
    {
        // d1 = 0.01, d2 = −8: (0.01 − 1/32) / 8.01 is −0.0027, above −1, so it is kept and clamped to 0.
        BspTrace trace = Level().TraceHull(
            (WithinEpsilon, 0f, 0f), (20f, 0f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 0f, 0f)]).ShouldNotBeNull();

        trace.Fraction.ShouldBe(0f);
        trace.StartSolid.ShouldBeFalse();
        trace.Normal.ShouldBe((1f, 0f, 0f));
    }

    [Test]
    public void TraceHull_ABoxNearerABrushThanTheEpsilonMovingAway_IsClear()
    {
        // The control: d2 > d1 > 0 is in front of the face throughout, and nothing is entered.
        Level().TraceHull((WithinEpsilon, 0f, 0f), (40f, 0f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 0f, 0f)])
            .ShouldNotBeNull().Fraction.ShouldBe(1f);
    }

    [Test]
    public void ProcessMovement_InTheAirNearerAWallThanTheEpsilon_SlidesAlongItAndKeepsItsSpeedAcross()
    {
        // TryPlayerMove (gamemovement.cpp:2558-2760): the first bump stops at fraction 0 on the face x = 4, ClipVelocity takes
        // the x away (sv_bounce 0, so overbounce 1), and the second bump runs along y at 150. Refusing the crossing put the
        // box inside the slab and the stuck re-trace at its end zeroed all of it.
        MapLevel level = Level();
        PlayerTraceRay trace = (start, end, mins, maxs, mask) => level.TraceHull(
            (start.X, start.Y, start.Z), (end.X, end.Y, end.Z), (mins.X, mins.Y, mins.Z), (maxs.X, maxs.Y, maxs.Z), mask, [Slab(0f, 0f, 0f)]);
        PredictedPlayer player = TfGameMovementConformanceTests.Standing() with
        {
            Origin = new Vector3(WithinEpsilon, 0f, 500f),
            OnGround = false,
            Velocity = new Vector3(-200f, 150f, 0f),
        };

        new TfGameMovement(trace, MovementConVars.Defaults, maxClients: 24)
            .ProcessMovement(ref player, new UserCommand(1, 1, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0, 0, 0, 0, 0, 0), Tick, first: true, commandNumber: 1)
            .ShouldBeTrue();

        player.Velocity.X.ShouldBe(0f);
        player.Velocity.Y.ShouldBe(150f);
        player.Origin.X.ShouldBe(WithinEpsilon);
        player.Origin.Y.ShouldBe(150f * Tick, 1e-4f);
    }
}
