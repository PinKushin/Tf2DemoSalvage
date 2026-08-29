using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A swept box against one displacement triangle — <c>CDispCollTree::SweepAABBTriIntersect</c>.
/// </summary>
/// <remarks>
/// **Written off <c>public/dispcoll_common.cpp:1331</c> before any of it existed**, so what it
/// asserts is the engine's behaviour rather than a description of what got built.
///
/// **Note WHICH file, because there are two and the previous handoff named the wrong one.** The SDK
/// ships an older <c>public/dispcoll.cpp</c> with a <c>SweptAABBTriIntersect</c> and a newer
/// <c>public/dispcoll_common.cpp</c> with <c>SweepAABBTriIntersect</c> — one letter apart. The
/// newer file is the one carrying the AABB tree the engine's collision walks
/// (<c>AABBTree_SweepAABB</c>), so it is the one transcribed here.
///
/// **The algorithm is a swept separating-axis test over fifteen planes**, and it accumulates a
/// window rather than a single hit:
///
/// <code>
///   // Make sure objects are traveling toward one another.
///   float flDistAlongNormal = pTri->m_vecNormal.Dot( ray.m_Delta );
///   if( flDistAlongNormal > DISPCOLL_DIST_EPSILON )
///       return;
///
///   if ( !AxisPlanesXYZ( ray, pTri, &amp;helper ) ) return;   //  6 planes
///   ... EdgeCrossAxisX/Y/Z, three each ...                //  9 planes
///   if ( !FacePlane( ray, rayDir, pTri, &amp;helper ) ) return; //  1 plane
///
///   if ( ( helper.m_flStartFrac &lt; helper.m_flEndFrac ) || ( ... &lt; 0.001f ) )
///       if ( ( helper.m_flStartFrac != DISPCOLL_INVALID_FRAC ) &amp;&amp; ( helper.m_flStartFrac &lt; pTrace->fraction ) )
/// </code>
///
/// Every plane calls <c>ResolveRayPlaneIntersect</c>, which pushes <c>m_flStartFrac</c> forward on
/// an entry and pulls <c>m_flEndFrac</c> back on an exit; a plane the ray is wholly in front of
/// (both distances positive) returns false and the whole triangle is rejected.
///
/// **`DISPCOLL_DIST_EPSILON` is `0.03125f`** (`dispcoll_common.h:34`) — the same figure as the
/// brush trace's `DIST_EPSILON`, and it is subtracted on entry so a hit lands a thirty-second of a
/// unit SHORT of the surface rather than exactly on it. Every prediction below carries it, which is
/// why they are not round numbers.
///
/// **The fixture is a triangle in the z=0 plane** with vertices (0,0,0), (64,0,0) and (0,64,0), so
/// its normal is exactly (0,0,1) and its plane distance exactly 0. Chosen so the arithmetic can be
/// carried out by hand and written down here, rather than being whatever the implementation
/// produced — a prediction the code cannot have influenced.
/// </remarks>
public sealed class DisplacementSweepConformanceTests
{
    private static DisplacementTriangle Flat => DisplacementTriangle.From(
        (0f, 0f, 0f), (64f, 0f, 0f), (0f, 64f, 0f));

    [Test]
    public void From_AFlatTriangle_HasAnUpwardNormalAtDistanceZero()
    {
        // The fixture's own precondition. Every number below is derived from this normal, so a
        // triangle whose winding produced (0,0,-1) would make each of them wrong in a way that
        // looks like the sweep misbehaving.
        DisplacementTriangle triangle = Flat;

        triangle.Normal.ShouldBe((0f, 0f, 1f));
        triangle.Distance.ShouldBe(0f, 0.0001);
    }

    [Test]
    public void Against_ABoxDroppedOntoAFlatTriangle_StopsAtTheSurfaceLessTheEpsilon()
    {
        // **Worked by hand from the SDK, not read off the implementation.** The box's underside
        // starts at z = 100 - 6 = 94 and must reach the plane at z = 0 over a delta of 200, so the
        // fraction is 94/200 = 0.47 before the epsilon. `ResolveRayPlaneIntersect` takes an entry as
        //     t = ( flStart - DISPCOLL_DIST_EPSILON ) / ( flStart - flEnd )
        // which is ( 94 - 0.03125 ) / 200 = 0.46984375.
        //
        // Both the axis-plane maximum on Z and the face plane produce exactly this value; the
        // second cannot raise the first because the test is `t > m_flStartFrac`, strictly.
        float fraction = DisplacementSweep.Against(
            start: (10f, 10f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(0.46984375f, 0.000001);
    }

    [Test]
    public void Against_ARayWithNoExtents_ReachesTheSurfaceItself()
    {
        // **The control for the extents, and it is the pair that proves the box has a size.** With
        // zero extents the underside IS the start point, so the travel is the full 100 rather than
        // 94: ( 100 - 0.03125 ) / 200 = 0.4998437. A sweep that ignored `ray.m_Extents` would return
        // this number for the case above as well, and nothing else in this file would notice.
        float fraction = DisplacementSweep.Against(
            start: (10f, 10f, 100f),
            delta: (0f, 0f, -200f),
            extents: (0f, 0f, 0f),
            Flat,
            best: 1f);

        fraction.ShouldBe(0.499843750f, 0.000001);
    }

    [Test]
    public void Against_ABoxDroppedBesideTheTriangle_DoesNotHit()
    {
        // **Outside the diagonal edge**, which is the only one of the nine edge planes this
        // triangle actually defines — the other eight are axis-aligned and `Cache_EdgeCrossAxis*`
        // rejects them as zero-length normals. x + y = 200 is well beyond the edge at x + y = 64
        // even expanded by the box, so `ResolveRayPlaneIntersect` sees both distances positive and
        // returns false.
        float fraction = DisplacementSweep.Against(
            start: (100f, 100f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(1f, "the sweep passes beside the triangle and nothing stops it");
    }

    [Test]
    public void Against_ABoxBeyondTheDiagonalEdgeButInsideTheBounds_DoesNotHit()
    {
        // **The only case in this file that the nine edge planes can decide, and it was missing.**
        // The test above drops the box at (100, 100), which is outside the triangle's AXIS-ALIGNED
        // bounds — so `AxisPlanesXYZ` rejects it and the edge planes never speak. Disabling
        // `EdgePlanes` entirely left every case in this file green, which is the "wrong condition"
        // failure from the testing standards: an input where correct and broken predict the same
        // observation.
        //
        // (60, 60) is inside the bounds on both axes — 60 is under 64, and under the 70 the box
        // expands them to — and beyond the hypotenuse: the diagonal plane is x + y = 64, expanded by
        // the box to 64 + 6(0.7071 + 0.7071) = 72.49, and 120 is well past it. Only the edge plane
        // can refuse this, and with it disabled the box lands on empty air at 0.46984375.
        float fraction = DisplacementSweep.Against(
            start: (60f, 60f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(1f, "the box passes beyond the hypotenuse, inside the bounding box");
    }

    [Test]
    public void Against_ABoxJustInsideTheDiagonalEdge_Hits()
    {
        // **The control for the case above**, and it is what stops that test being satisfied by an
        // edge plane facing the wrong way. (30, 30) sums to 60, inside the hypotenuse at 64; the
        // same corner of the same bounding box, a few units the other side of the one plane under
        // test. An implementation that negated the edge normal would pass the miss and fail this.
        float fraction = DisplacementSweep.Against(
            start: (30f, 30f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(0.46984375f, 0.000001);
    }

    [Test]
    public void Against_ABoxTravellingAwayFromTheFace_DoesNotHit()
    {
        // The first line of the SDK's function, and it is a real rule rather than an optimisation:
        //   if ( pTri->m_vecNormal.Dot( ray.m_Delta ) > DISPCOLL_DIST_EPSILON ) return;
        // A surface is one-sided for a sweep, so a box already below the ground and moving up
        // passes through it. Without this a player walking up a slope would catch on its underside.
        float fraction = DisplacementSweep.Against(
            start: (10f, 10f, -100f),
            delta: (0f, 0f, 200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(1f, "the box is moving along the normal, away from the face");
    }

    [Test]
    public void Against_ABoxDroppedOntoASlope_StopsOnTheFaceNotOnItsBoundingBox()
    {
        // **Every other case here is a FLAT triangle, where the face plane and the Z axis plane are
        // literally the same plane** — so none of them can tell the two apart, and terrain is
        // sloped by definition. This one separates them by a wide margin.
        //
        // The triangle (0,0,0) (64,0,0) (0,64,64) has normal (0, -1/√2, 1/√2). Its highest vertex is
        // at z = 64, so `AxisPlanesXYZ`'s maximum on Z alone would stop the box at
        // ( (100 - 70) - 0.03125 ) / 200 = 0.14984375 — up in the air above the slope, at the height
        // of the bounding box's lid. The face plane carries it down to where the surface actually
        // is:
        //
        //   normal · start                     = 0.70710678 × 90     = 63.63961
        //   closest box corner along the normal                      = -8.48528
        //   flStart = 63.63961 + 8.48528 ... - = 55.15433
        //   flEnd   (at z = -100)                                    = -86.26703
        //   t = ( 55.15433 - 0.03125 ) / 141.42136                   = 0.38977903
        //
        // **Worth knowing for anyone extending this**: for THIS triangle the face plane and edge 2
        // crossed with X are the same plane, because edge 1 lies along X so the face normal has no X
        // component. Disabling `FacePlane` alone therefore does not redden this; disabling it
        // together with `EdgePlanes` drops the answer to the 0.14984375 above, which is how the pair
        // was verified. A fixture that separates all fifteen planes individually would need a
        // triangle with no edge parallel to any axis.
        DisplacementTriangle slope = DisplacementTriangle.From(
            (0f, 0f, 0f), (64f, 0f, 0f), (0f, 64f, 64f));

        float fraction = DisplacementSweep.Against(
            start: (10f, 10f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            slope,
            best: 1f);

        fraction.ShouldBe(0.38977903f, 0.000001);
    }

    [Test]
    public void Against_AHitBehindTheBestSoFar_LeavesTheBestAlone()
    {
        // `( helper.m_flStartFrac < pTrace->fraction )`. The caller sweeps many triangles into one
        // fraction, so a later triangle may only ever shorten it — this is what makes the result
        // independent of the order they are visited in.
        float fraction = DisplacementSweep.Against(
            start: (10f, 10f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 0.25f);

        fraction.ShouldBe(0.25f, "0.4698 is further along than a hit already recorded at 0.25");
    }

    [Test]
    public void Against_ABoxWideEnoughToOverhangTheEdge_StillHits()
    {
        // **The reason the edge planes are EXPANDED by the extents rather than tested against the
        // centre.** The centre at (66, 0) is outside the triangle's x extent of 64, and a
        // point-sized sweep would miss — but a 6-unit box overhangs onto the surface and the engine
        // stops it. `EdgeCrossAxis` subtracts the box's extent from the plane distance to model
        // exactly this, which is the swept-SAT trick the whole function is built on.
        float fraction = DisplacementSweep.Against(
            start: (66f, 2f, 100f),
            delta: (0f, 0f, -200f),
            extents: (6f, 6f, 6f),
            Flat,
            best: 1f);

        fraction.ShouldBe(
            0.46984375f,
            0.000001,
            "the box overhangs the edge, so it lands on the surface at the same height");
    }
}
