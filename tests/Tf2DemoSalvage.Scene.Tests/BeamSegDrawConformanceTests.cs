using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>CBeamSegDraw</c>, read out of <c>beamsegdraw.obj</c> from the SDK's own <c>tier2.lib</c>.</summary>
/// <remarks>
/// **The class is closed**: <c>tier2/beamsegdraw.h</c> ships and its implementation is a static library linked into the
/// client. Its two functions were read in Ghidra (`D:\ghidra-proj\tier2`, <c>NextSeg</c> at `0x31290`, <c>SpecifySeg</c>
/// at `0x31a40`), and these pin what they do:
///
/// <code>
/// n   = fastnormalize( cross( m_Seg.pos − next.pos, m_Seg.pos − camera ) )
/// ave = segsDrawn &gt; 1 ? fastnormalize( ( n + normalLast ) · 0.5 ) : n;   normalLast = n
/// p1  = pos + ave · width · 0.5  at ( 0, v )      p2 = pos − ave · width · 0.5  at ( 1, v )
/// last segment: SpecifySeg( normalLast )
/// </code>
///
/// **Every geometry here is chosen so the answer is a round number**: a vertical strip seen from +X, whose normal is
/// therefore ±Y and whose corners sit exactly half a width to either side.
/// </remarks>
public sealed class BeamSegDrawConformanceTests
{
    private const float Close = 1e-4f;

    /// <remarks>
    /// **A segment's WIDTH is the whole span; each edge sits half of it from the centre.** Down the Z axis from (0,0,100)
    /// to (0,0,0), seen from (100, 0, 50): the tangent is (0,0,100) and the view vector (−100,0,50), whose cross is
    /// (0, −10000, 0) — so the first edge is at y = −5 and the second at y = +5 for a width of ten, with U 0 then 1.
    /// </remarks>
    [Test]
    public void Draw_AStraightStripSeenSideOn_SpansHalfItsWidthEitherSide()
    {
        List<DetailSpriteVertex> corners = [];

        BeamSegDraw.Draw(
            [Segment(0f, 0f, 100f, width: 10f), Segment(0f, 0f, 0f, width: 10f)],
            new Vector3(100f, 0f, 50f),
            corners);

        corners.Count.ShouldBe(6, "one quad between two points, as two triangles");

        // lastOne, lastTwo, nextOne / nextOne, lastTwo, nextTwo.
        At(corners[0]).ShouldBe(new Vector3(0f, -5f, 100f));
        At(corners[1]).ShouldBe(new Vector3(0f, 5f, 100f));
        At(corners[2]).ShouldBe(new Vector3(0f, -5f, 0f));
        At(corners[5]).ShouldBe(new Vector3(0f, 5f, 0f));

        (corners[0].U, corners[1].U).ShouldBe((0f, 1f), "the + normal edge is U 0, the − one U 1");
    }

    /// <remarks>
    /// **A bend is mitred by the average of the two pairs' normals, and the last point takes the last pair's raw one.**
    /// Three points: (0,0,100), (0,0,0), (0,100,0) seen from (100, 50, 50). The first pair's normal is
    /// normalize( cross( (0,0,100), (−100,−50,50) ) ) = (0.4472, −0.8944, 0); the second's is
    /// normalize( cross( (0,−100,0), (−100,−50,−50) ) ) = (0.4472, 0, −0.8944). The middle point's edge follows their
    /// normalized average, and the end point's follows the SECOND normal unaveraged.
    /// </remarks>
    [Test]
    public void Draw_ABentStrip_MitresTheMiddleAndEndsOnTheLastRawNormal()
    {
        List<DetailSpriteVertex> corners = [];

        BeamSegDraw.Draw(
            [Segment(0f, 0f, 100f, width: 2f), Segment(0f, 0f, 0f, width: 2f), Segment(0f, 100f, 0f, width: 2f)],
            new Vector3(100f, 50f, 50f),
            corners);

        corners.Count.ShouldBe(12);

        Vector3 first = Vector3.Normalize(Vector3.Cross(new Vector3(0f, 0f, 100f), new Vector3(-100f, -50f, 50f)));
        Vector3 second = Vector3.Normalize(Vector3.Cross(new Vector3(0f, -100f, 0f), new Vector3(-100f, -50f, -50f)));
        Vector3 mitre = Vector3.Normalize((first + second) * 0.5f);

        // The middle point is nextOne of the first quad (index 2) and lastOne of the second (index 6).
        Distance(At(corners[2]), mitre).ShouldBeLessThan(Close);
        Distance(At(corners[6]), mitre).ShouldBeLessThan(Close);

        // The end point, nextOne of the second quad.
        Distance(At(corners[8]), new Vector3(0f, 100f, 0f) + second).ShouldBeLessThan(Close);
    }

    /// <remarks>
    /// **A segment pointing straight at the camera has no width** — the cross of two parallel vectors is zero, and the
    /// engine's <c>VectorNormalizeFast</c> adds 1e-10 under its root rather than dividing by zero, so the edge collapses
    /// onto the centre line instead of becoming NaN.
    /// </remarks>
    [Test]
    public void Draw_AStripAimedAtTheCamera_CollapsesToItsCentreLine()
    {
        List<DetailSpriteVertex> corners = [];

        BeamSegDraw.Draw(
            [Segment(0f, 0f, 100f, width: 10f), Segment(0f, 0f, 0f, width: 10f)],
            new Vector3(0f, 0f, 500f),
            corners);

        foreach (DetailSpriteVertex corner in corners)
        {
            (corner.X, corner.Y).ShouldBe((0f, 0f));
            float.IsNaN(corner.Z).ShouldBeFalse();
        }
    }

    /// <remarks>
    /// **Fewer than two points is no strip** — <c>CBeamSegDraw::Start</c> asserts two, and every caller returns first.
    /// </remarks>
    [Test]
    public void Draw_OnePoint_DrawsNothing()
    {
        List<DetailSpriteVertex> corners = [];

        BeamSegDraw.Draw([Segment(0f, 0f, 0f, width: 10f)], Vector3.UnitX, corners);

        corners.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A colour is packed to a byte the way <c>SpecifySeg</c> packs it**: rounded, then the low byte kept. A half lands
    /// on 128 (127.5 rounds to even), and a channel over one WRAPS — 1.5 is 382, whose low byte is 126 — where a clamp
    /// would have held it at 255.
    /// </remarks>
    [TestCase(0.5f, 128)]
    [TestCase(1f, 255)]
    [TestCase(1.5f, 126)]
    public void Packed_AChannel_IsTheLowByteOfItsRoundedValue(float channel, int expected)
    {
        BeamSegDraw.Packed(channel).ShouldBe(expected / 255f);
    }

    private static BeamSegment Segment(float x, float y, float z, float width) =>
        new(new Vector3(x, y, z), Vector3.One, 0f, width, 1f);

    private static Vector3 At(DetailSpriteVertex corner) => new(corner.X, corner.Y, corner.Z);

    private static float Distance(Vector3 first, Vector3 second) => Vector3.Distance(first, second);
}
