using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.RotatedBrushTraceConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The brush clip's entry fraction for a box that starts closer to a brush than <c>DIST_EPSILON</c> (B604).
/// </summary>
/// <remarks>
/// **Read from Source's published clips, interpolated to the brush clip:** <c>IntersectRayWithBox</c> and
/// <c>IntersectRayWithOBB</c> (<c>public/collisionutils.cpp:1176-1189</c>, <c>:1624-1637</c>, called with
/// <c>flTolerance = DIST_EPSILON</c>) take an entering crossing as <c>f = d1 - tol; if (f &lt; 0) f = 0; f /= (d1-d2)</c> and
/// accept it on <c>t1 &lt; t2 &amp;&amp; t1 &gt;= 0</c> (<c>:1201</c>, <c>:1651</c>) — clamped before any comparison, and with
/// no lower bound. The engine's own <c>CM_ClipBoxToBrush</c> is in <c>engine/cmodel.cpp</c>, which the SDK does not ship;
/// Quake 2's (<c>qcommon/cmodel.c:1084-1090</c>) bounds the raw entry at −1, and that bound is NOT taken. A movement trace
/// ends <c>DIST_EPSILON</c> short of what it hits, so a player resting against a wall or a slope starts every trace inside the
/// epsilon; refused, the crossing let him pass into the brush, and <c>TryPlayerMove</c>'s unswept re-trace at the end then
/// zeroed his velocity (<c>gamemovement.cpp:2636-2649</c>) — a running recorder drawn standing (B604). The full set of
/// branches is <c>Tf2DemoSalvage.Content.Tests.Bsp.BrushClipConformanceTests</c>.
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
        // d1 = 0.01, d2 = −8: max(0.01 − 1/32, 0) / 8.01 is 0, and `t1 >= 0` keeps it.
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
    public void TraceHull_AStepShorterThanTheGapToTheFace_StopsWhereItStands()
    {
        // From d1 = 0.001 to d2 = −0.01: the raw crossing (0.001 − 1/32) / 0.011 = −2.75 was refused under Quake 2's −1 bound
        // and the box let into the slab; Source clamps the numerator first, 0 / 0.011 = 0.
        BspTrace trace = Level().TraceHull(
            (28.001f, 0f, 0f), (27.99f, 0f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 0f, 0f)]).ShouldNotBeNull();

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((1f, 0f, 0f));
    }

    /// <remarks>
    /// The footstep surface's trace (<see cref="MapLevel.GroundTrace"/>, `C_BasePlayer::GetGroundSurface`): a player at rest
    /// stands `DIST_EPSILON` above the floor, or a rounding nearer, so the cube starts inside the epsilon of the floor's top
    /// face. Exactly ε gives a raw entry of 0, which every form of the clip keeps; nearer gives a negative raw entry, which
    /// the clip before B604 refused, and the probe then ran its whole 64 units and found no surface.
    /// </remarks>
    [TestCase(0.03125f, TestName = "GroundTrace_FeetRestingAnEpsilonAboveAFloor_HitsTheFloorAtZero")]
    [TestCase(0.02f, TestName = "GroundTrace_FeetNearerTheFloorThanTheEpsilon_HitsTheFloorAtZero")]
    public void GroundTrace_FeetJustAboveAFloor_HitsTheFloorAtZero(float height)
    {
        MapLevel level = new(
            null, null, null, new Dictionary<int, string>(), TfGameMovementConformanceTests.HalfSpace(0f, 0f, 1f, 0f), null, null,
            [], [], [], [], null, default);

        BspTrace trace = level.GroundTrace((0f, 0f, height), []);

        trace.Fraction.ShouldBe(0f);
        trace.Texinfo.ShouldBe(0);
        trace.Normal.ShouldBe((0f, 0f, 1f));
    }

    [Test]
    public void GroundTrace_FeetRestingOnAPlayerClip_HitsTheClip()
    {
        // `C_BasePlayer::GetGroundSurface` traces MASK_PLAYERSOLID_BRUSHONLY (c_baseplayer.cpp:731), which carries
        // CONTENTS_PLAYERCLIP (bspflags.h:134): a player standing on a clip ledge is on it, not on whatever is beneath.
        MapLevel level = new(
            null, null, null, new Dictionary<int, string>(),
            TfGameMovementConformanceTests.HalfSpace(0f, 0f, 1f, 0f, BspLeafTree.ContentsPlayerClip), null, null,
            [], [], [], [], null, default);

        BspTrace trace = level.GroundTrace((0f, 0f, 0.03125f), []);

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((0f, 0f, 1f));
    }

    /// <remarks>
    /// The merge of one brush entity's trace into the running one, both entered at fraction 0: one inside the epsilon of a face
    /// (the slab's +x face at x = −0.01), one the point starts inside. A strict `fraction &lt;` keeps whichever came first, and
    /// the start-solid door is lost when it comes second; the engine's merge (<c>MapLevel.Nearer</c>, *interpolated:*
    /// <c>CEngineTrace::TraceRay</c>'s loop is unshipped) takes the start-solid one in either order.
    /// </remarks>
    [TestCase(false, TestName = "Trace_ADoorEnteredAtZeroThenADoorItStartsIn_IsStartSolidInTheSecond")]
    [TestCase(true, TestName = "Trace_ADoorItStartsInThenADoorEnteredAtZero_IsStartSolidInTheFirst")]
    public void Trace_TwoDoorsAtZeroOneStartedIn_IsStartSolidInEitherOrder(bool startInsideFirst)
    {
        SolidBrush touching = Slab(0f, 0f, 0f) with { Origin = new Vector3(-4.01f, 0f, 0f), Entity = 1 };
        SolidBrush around = Slab(0f, 0f, 0f) with { Origin = new Vector3(3.5f, 0f, 0f), Entity = 2 };

        BspTrace trace = Level().Trace(
            (0f, 0f, 0f), (-1f, 0f, 0f), (0f, 0f, 0f), BspLeafTree.MaskPlayerSolid,
            startInsideFirst ? [around, touching] : [touching, around]);

        trace.Fraction.ShouldBe(0f);
        trace.StartSolid.ShouldBeTrue();
        trace.BrushEntity.ShouldBe(2);
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
