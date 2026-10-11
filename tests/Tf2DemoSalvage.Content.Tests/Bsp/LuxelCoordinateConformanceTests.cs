using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Where a world position lands in a brush face's own lightmap (B521): luxel <c>k</c> is the light AT luxel coordinate
/// <c>k</c>, so a face's coordinate runs from its first luxel's centre to its last one's — over <c>count − 1</c>.
/// </summary>
/// <remarks>
/// **What the compiler does** — *read from published source*, `utils/vrad/lightmap.cpp:847-866`:
///
/// <code>
/// int width  = pLightInfo-&gt;face-&gt;m_LightmapTextureSizeInLuxels[0]+1;
/// …
/// for( int t = 0; t &lt; height; t++ )
///     for( int s = 0; s &lt; width; s++ )
///         LuxelSpaceToWorld( pLightInfo, s, t, pFaceLight-&gt;luxel[s+t*width] );
/// </code>
///
/// There are <c>size + 1</c> luxels and luxel <c>s</c> is evaluated at luxel-space <c>s</c> past the face's mins. The
/// renderer has to give luxel <c>s</c> back exactly there: the engine's coordinate is
/// <c>( s − mins + 0.5 ) / ( size + 1 )</c> of the face's own rectangle, which is that texel's centre. In this
/// project's terms — a coordinate of 0 at the first texel's centre and 1 at the last's, which is how
/// <c>LightmapAtlas</c> places a rectangle and how a displacement already states its own — that is
/// <c>( s − mins ) / size</c>.
///
/// **The fault this pins**: the coordinate was divided by the luxel COUNT, <c>size + 1</c>. Every brush face's light
/// was drawn squeezed toward its mins by <c>size / ( size + 1 )</c> — nothing at the first luxel, almost a whole luxel
/// (sixteen units) at the last, and a quarter of the face on a four-luxel one. *Measured* on cp_process_f12 from the
/// golden comparison's indoor camera (`pixel-face`, 2026-10-11): at five points on two walls TF2's pixel implies the
/// luxel value at the coordinate this rule gives to within twelve percent, and the viewer's implied the value half a
/// luxel back along both axes — 0.19 where the wall under the lamps should read 0.46. A flat wall hides it entirely,
/// which is why tile averages did not show it; a spotlight's pool does not.
/// </remarks>
public sealed class LuxelCoordinateConformanceTests
{
    /// <summary>One luxel every sixteen units along X and along Y, as a default-scale brush face has.</summary>
    private static LuxelMapping Mapping(int width, int height) =>
        new((1f / 16f, 0f, 0f, 0f), (0f, 1f / 16f, 0f, 0f), MinU: 0, MinV: 0, width, height);

    /// <remarks>
    /// Five luxels across: the point on luxel 0 is 0, on luxel 2 is a half, on luxel 4 is 1. Divided by the count they
    /// were 0, 0.4 and 0.8.
    /// </remarks>
    [TestCase(0f, 0f)]
    [TestCase(32f, 0.5f)]
    [TestCase(64f, 1f)]
    [TestCase(40f, 0.625f)]
    public void Project_APointOnAFiveLuxelFace_IsItsLuxelOverTheFourBetweenTheFirstCentreAndTheLast(float x, float expected)
    {
        Mapping(5, 5).Project(x, 0f, 0f).U.ShouldBe(expected, 1e-6f);
        Mapping(5, 5).Project(0f, x, 0f).V.ShouldBe(expected, 1e-6f);
    }

    /// <remarks>A face one luxel wide has no span to run over; every point reads its one luxel.</remarks>
    [Test]
    public void Project_OnAFaceOneLuxelWide_IsZero()
    {
        Mapping(1, 1).Project(8f, 8f, 0f).ShouldBe((0f, 0f));
    }

    /// <remarks>
    /// **On a real map, a face whose far corner sits on its last luxel reads exactly 1 there.** A brush wall cut on the
    /// sixteen-unit grid ends on a luxel, so its largest coordinate is the whole span — and divided by the count it
    /// could never be: the most any corner reached was <c>size / ( size + 1 )</c>. The control is that none goes past
    /// 1 by more than rounding: the span is the face's own extent.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Read_CpProcessFinalsBrushFaces_ReachOneAtACornerOnTheLastLuxelAndNeverPassIt()
    {
        string path = GameInstall.RequireFile("maps/cp_process_final.bsp");
        IReadOnlyList<BspSurface> faces =
            [.. BspSurfaces.Read(File.ReadAllBytes(path)).Where(face => !face.IsDisplacement && face.LuxelWidth > 1 && face.LuxelHeight > 1)];

        faces.Count.ShouldBeGreaterThan(5000, "the instrument: the map has brush faces with lightmaps");

        int reachOne = faces.Count(face => face.Vertices.Max(vertex => vertex.LightU) > 0.999f);
        float largest = faces.Max(face => face.Vertices.Max(vertex => System.MathF.Max(vertex.LightU, vertex.LightV)));

        TestContext.Out.WriteLine($"LUXEL {reachOne} of {faces.Count} faces reach 1 across; the largest coordinate is {largest}");

        reachOne.ShouldBeGreaterThan(faces.Count / 4);
        largest.ShouldBeLessThan(1.01f);
    }
}
