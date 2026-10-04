using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The strip geometry of <c>beamdraw.cpp</c> and the noise of <c>view_beams.cpp</c>, branch by branch.</summary>
/// <remarks>
/// **Each test puts the inputs there and so knows the answer.** A beam a hundred units long down the Z axis, seen from
/// +X, with the noise table zeroed unless the test is about noise — so a point's position is its fraction of the delta,
/// and only the branch under test moves anything.
/// </remarks>
public sealed class BeamDrawConformanceTests
{
    private const float Close = 1e-4f;

    private static readonly BeamView SideOn =
        new(new Vector3(500f, 0f, 50f), -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    private static readonly float[] Quiet = new float[BeamDraw.NoiseDivisions + 1];

    /// <remarks>
    /// **<c>FBEAM_SHADEOUT</c> darkens toward the far end over the fade length, clamped** — <c>brightness = 1 −
    /// fraction / fadeFraction</c> (`beamdraw.cpp:359-362`). Fade over the whole length and the three points are 1, ½, 0.
    /// </remarks>
    [Test]
    public void DrawSegs_ShadeOutOverTheWholeLength_DarkensToTheEnd()
    {
        List<BeamSegment> points = Segs(segments: 3, flags: BeamDraw.ShadeOutFlag, fadeLength: 100f, width: 1f);

        points.ConvertAll(point => point.Colour.X).ShouldBe([1f, 0.5f, 0f]);
    }

    /// <remarks>
    /// **And over a SHORTER fade it is dark well before the end** — fade 50 of 100, so the middle point is already at
    /// <c>1 − 0.5 / 0.5 = 0</c>.
    /// </remarks>
    [Test]
    public void DrawSegs_ShadeOutOverHalfTheLength_IsDarkByTheMiddle()
    {
        List<BeamSegment> points = Segs(segments: 3, flags: BeamDraw.ShadeOutFlag, fadeLength: 50f, width: 1f);

        points.ConvertAll(point => point.Colour.X).ShouldBe([1f, 0f, 0f]);
    }

    /// <remarks>
    /// **Both shades peak at the middle** — <c>2 · fraction / fadeFraction</c> below one half, <c>2 · ( 1 − fraction /
    /// fadeFraction )</c> above (`:344-354`).
    /// </remarks>
    [Test]
    public void DrawSegs_ShadeInAndOut_PeaksInTheMiddle()
    {
        List<BeamSegment> points = Segs(
            segments: 5, flags: BeamDraw.ShadeInFlag | BeamDraw.ShadeOutFlag, fadeLength: 100f, width: 1f);

        points.ConvertAll(point => point.Colour.X).ShouldBe([0f, 0.5f, 1f, 0.5f, 0f]);
    }

    /// <remarks>
    /// **The width doubles on the way into the strip** — <c>curSeg.m_flWidth = startWidth * 2</c> (`:400`) — and tapers
    /// linearly between the two widths when they differ.
    /// </remarks>
    [Test]
    public void DrawSegs_AWidth_IsDoubledAndTaperedAlongTheBeam()
    {
        List<BeamSegment> points = Segs(segments: 3, width: 1f, endWidth: 3f);

        points.ConvertAll(point => point.Width).ShouldBe([2f, 4f, 6f]);
    }

    /// <remarks>
    /// **Too many segments for the width are cut back so the strip does not fold over itself** —
    /// <c>if ( length*div &lt; flMaxWidth * 1.414 ) segments = (int)(length / (flMaxWidth * 1.414)) + 1</c> (`:254-262`).
    /// A width of 64 has a half-width of 32: twelve points 9.1 apart would overlap, and 100 / 45.25 rounds down to 2,
    /// so three points survive.
    /// </remarks>
    [Test]
    public void DrawSegs_TooManySegmentsForAWideBeam_AreCutBack()
    {
        Segs(segments: 12, width: 64f).Count.ShouldBe(3);
    }

    /// <remarks>
    /// **`FBEAM_NOTILE` stretches the texture once over the beam; without it the texture repeats every hundred units** —
    /// <c>vStep = div</c> against <c>vStep = length * 0.01 * div</c> (`:274-283`). A 100-unit beam in three points steps
    /// V by ½ either way, so the control is a 400-unit beam, which tiles four times.
    /// </remarks>
    [TestCase(BeamDraw.NoTileFlag, 100f, 0.5f)]
    [TestCase(0, 400f, 2f)]
    [TestCase(BeamDraw.NoTileFlag, 400f, 0.5f)]
    public void DrawSegs_TheTextureStep_FollowsNoTile(int flags, float length, float step)
    {
        List<BeamSegment> points = [];

        BeamDraw.DrawSegs(
            Quiet, Vector3.Zero, new Vector3(0f, 0f, length), 1f, 1f, 0f, 0f, 0f, 3, flags, Vector3.One, length,
            SideOn, points);

        (points[1].TexCoord - points[0].TexCoord).ShouldBe(step, Close);
    }

    /// <remarks>
    /// **The texture scrolls by <c>fmod( freq · speed, 1 )</c>** — the first point's V. 2.7 seconds at a speed of 1.5 is
    /// 4.05, so the strip starts at 0.05.
    /// </remarks>
    [Test]
    public void DrawSegs_AScrollingBeam_StartsItsTextureAtTheFractionalScroll()
    {
        List<BeamSegment> points = [];

        BeamDraw.DrawSegs(
            Quiet, Vector3.Zero, new Vector3(0f, 0f, 100f), 1f, 1f, 0f, 2.7f, 1.5f, 3, 0, Vector3.One, 100f,
            SideOn, points);

        points[0].TexCoord.ShouldBe(0.05f, 1e-3f);
    }

    /// <remarks>
    /// **Sine noise needs sixteen points** and raises fewer to it (`:288-297`), where the overlap correction above only
    /// ever lowers.
    /// </remarks>
    [Test]
    public void DrawSegs_SineNoise_RaisesTheSegmentsToSixteen()
    {
        Segs(segments: 3, flags: BeamDraw.SineNoiseFlag, width: 1f).Count.ShouldBe(16);
    }

    /// <remarks>
    /// **Noise pushes a point along the perpendicular to the beam and the view** — <c>cross( CurrentViewForward(), beam
    /// direction )</c>, scaled by <c>noise · amplitude · length / 100</c>. With the table at one everywhere, an amplitude
    /// of 2 on a 100-unit beam pushes every point 2 units; the view looks down −X and the beam runs up +Z, and
    /// <c>( −1, 0, 0 ) × ( 0, 0, 1 )</c> is +Y.
    /// </remarks>
    [Test]
    public void DrawSegs_Noise_PushesAlongThePerpendicular()
    {
        float[] ones = new float[BeamDraw.NoiseDivisions + 1];
        System.Array.Fill(ones, 1f);
        List<BeamSegment> points = [];

        BeamDraw.DrawSegs(
            ones, Vector3.Zero, new Vector3(0f, 0f, 100f), 1f, 1f, 2f, 0f, 0f, 3, 0, Vector3.One, 100f, SideOn, points);

        foreach (BeamSegment point in points)
        {
            point.Position.Y.ShouldBe(2f, Close);
        }
    }

    /// <remarks>
    /// **The fractal fills the upper half first, from <c>beamRandom</c>** — <c>noise[div2] = (noise[0] + noise[divs]) ·
    /// 0.5 + scale · RandomFloat( −1, 1 )</c>, then <c>Noise( &amp;noise[div2], … )</c> before <c>Noise( noise, … )</c>
    /// (`view_beams.cpp:186-202`). With the ends at zero the middle entry is the stream's first draw, and the upper
    /// quarter its second.
    /// </remarks>
    [Test]
    public void Noise_FromAZeroedTable_FillsTheUpperHalfFirst()
    {
        float[] noise = new float[BeamDraw.NoiseDivisions + 1];
        UniformRandomStream random = new();
        UniformRandomStream control = new();

        random.SetSeed(7);
        control.SetSeed(7);

        BeamDraw.Noise(noise, BeamDraw.NoiseDivisions, 1f, random);

        float first = control.RandomFloat(-1f, 1f);
        float second = control.RandomFloat(-1f, 1f);

        noise[64].ShouldBe(first);
        noise[96].ShouldBe((float)(((noise[64] + noise[128]) * 0.5) + (0.5f * second)));
    }

    [Test]
    public void SineNoise_OverTheTable_IsHalfASineWave()
    {
        float[] noise = new float[BeamDraw.NoiseDivisions + 1];

        BeamDraw.SineNoise(noise, BeamDraw.NoiseDivisions);

        noise[0].ShouldBe(0f);
        noise[64].ShouldBe(1f, Close);
    }

    /// <remarks>
    /// **A spline beam's shade is computed and never applied** — the segment colour is set once per span before the
    /// loop that recomputes <c>brightness</c> (`beamdraw.cpp:796-840`), so every point carries the span's STARTING
    /// brightness: all lit, even under <c>FBEAM_SHADEOUT</c>.
    /// </remarks>
    [Test]
    public void DrawSplineSegs_UnderShadeOut_IsUniformlyLit()
    {
        List<BeamSegment> points = [];

        BeamDraw.DrawSplineSegs(
            Quiet,
            [Vector3.Zero, new Vector3(0f, 0f, 100f), new Vector3(0f, 100f, 100f)],
            1f, 1f, 0f, 0f, 0f, 4, BeamDraw.ShadeOutFlag, Vector3.One, SideOn, points);

        points.Count.ShouldBe(6, "three points after the first, per span, over two spans");

        foreach (BeamSegment point in points)
        {
            point.Colour.ShouldBe(Vector3.One);
        }
    }

    /// <remarks>**The halo of a spline goes at the end of its last span.**</remarks>
    [Test]
    public void DrawSplineSegs_TheHalo_IsAtTheLastPoint()
    {
        List<BeamSegment> points = [];

        (Vector3 haloAt, Vector3 _) = BeamDraw.DrawSplineSegs(
            Quiet,
            [Vector3.Zero, new Vector3(0f, 0f, 100f), new Vector3(0f, 100f, 100f)],
            1f, 1f, 0f, 0f, 0f, 4, 0, Vector3.One, SideOn, points);

        haloAt.ShouldBe(new Vector3(0f, 100f, 100f));
    }

    /// <remarks>**A Catmull-Rom span runs from its second control point to its third.**</remarks>
    [Test]
    public void CatmullRom_AtItsEnds_IsTheMiddleTwoPoints()
    {
        Vector3 p1 = new(-1f, 0f, 0f);
        Vector3 p2 = new(0f, 1f, 0f);
        Vector3 p3 = new(1f, 1f, 0f);
        Vector3 p4 = new(2f, 0f, 0f);

        BeamDraw.CatmullRom(p1, p2, p3, p4, 0f).ShouldBe(p2);
        Vector3.Distance(BeamDraw.CatmullRom(p1, p2, p3, p4, 1f), p3).ShouldBeLessThan(Close);
    }

    /// <remarks>
    /// **The halo is a square of the view's own axes**, its corners and texture coordinates in <c>DrawHalo</c>'s order
    /// (`beamdraw.cpp:90-116`) and its alpha one — <c>Color3fv</c> sets no alpha.
    /// </remarks>
    [Test]
    public void DrawHalo_AtAPoint_IsAViewFacingSquareOfItsScale()
    {
        List<DetailSpriteVertex> corners = [];

        BeamDraw.DrawHalo(new Vector3(10f, 0f, 0f), 2f, new Vector3(1f, 0.5f, 0f), SideOn, corners);

        corners.Count.ShouldBe(6);

        // bottom-left: source − up·s − right·s, at (0, 1). Right is −Y here, so −right is +Y.
        (corners[0].X, corners[0].Y, corners[0].Z).ShouldBe((10f, 2f, -2f));
        (corners[0].U, corners[0].V).ShouldBe((0f, 1f));
        corners[0].Alpha.ShouldBe(1f);
        corners[0].Green.ShouldBe(128 / 255f, "packed to a byte, like every vertex colour");
    }

    /// <remarks><c>RemapVal</c>'s guard: an empty source range answers whichever end the value is past.</remarks>
    [TestCase(5f, 1f)]
    [TestCase(3f, 0f)]
    public void RemapVal_AnEmptyRange_AnswersAnEnd(float value, float expected)
    {
        BeamDraw.RemapVal(value, 4f, 4f, 0f, 1f).ShouldBe(expected);
    }

    private static List<BeamSegment> Segs(
        int segments, float width, int flags = 0, float fadeLength = 100f, float? endWidth = null)
    {
        List<BeamSegment> points = [];

        BeamDraw.DrawSegs(
            Quiet, Vector3.Zero, new Vector3(0f, 0f, 100f), width, endWidth ?? width, 0f, 0f, 0f, segments, flags,
            Vector3.One, fadeLength, SideOn, points);

        return points;
    }
}
