using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `IStudioRender::ComputeLighting` (`studiorender.dll` `0x180020bd0`, setup `0x180020c80`): the light at a point along a
/// normal, which `GetModelMaterialColorAndLighting` (`engine.dll` `0x1801c9f10`) asks for at a static prop's hit (B415).
/// </summary>
/// <remarks>
/// <code>
/// light = Σ axis ( n_axis² · cube[ n_axis &gt; 0 ? +axis : −axis ] )
///       + Σ local ( colour · max( n · Δ/|Δ|, 0 ) · falloff )     falloff = 1 / ( c + l·|Δ| + q·|Δ|² ), zero past range
/// </code>
/// </remarks>
public sealed class StudioPointLightingConformanceTests
{
    private static readonly AmbientCube Cube = new((1f, 0f, 0f), (2f, 0f, 0f), (3f, 0f, 0f), (4f, 0f, 0f), (5f, 0f, 0f), (6f, 0f, 0f));

    [Test]
    public void At_ANormalStraightUp_TakesTheUpperFaceAlone()
    {
        StudioPointLighting.At(PointLighting.Bounce(Cube), null, Vector3.Zero, Vector3.UnitZ).X.ShouldBe(5f);
    }

    /// <remarks>0.6 along +x and −0.8 along z: 0.36 of +x's 1 and 0.64 of −z's 6. A zero component takes the negative face at no weight.</remarks>
    [Test]
    public void At_ASlantedNormal_WeighsEachFaceByItsComponentSquared()
    {
        StudioPointLighting.At(PointLighting.Bounce(Cube), null, Vector3.Zero, new Vector3(0.6f, 0f, -0.8f)).X
            .ShouldBe((0.36f * 1f) + (0.64f * 6f), 1e-5f);
    }

    /// <remarks>A quadratic light 100 units overhead: 1 / 100², so 10,000 of red arrives as 1.</remarks>
    [Test]
    public void At_ALightOverhead_AddsItsColourOverItsFalloff()
    {
        StudioPointLighting.At(Lit(new LocalLight(0f, 0f, 100f, 10000f, 0f, 0f, 0f, 0f, 1f, 0f)), null, Vector3.Zero, Vector3.UnitZ).X
            .ShouldBe(1f, 1e-4f);
    }

    [Test]
    public void At_ALightBehindTheSurface_AddsNothing()
    {
        StudioPointLighting.At(Lit(new LocalLight(0f, 0f, -100f, 10000f, 0f, 0f, 1f, 0f, 0f, 0f)), null, Vector3.Zero, Vector3.UnitZ).X
            .ShouldBe(0f);
    }

    /// <remarks>`0x18004f800`: kept while `|Δ|² ≤ range²` — on the boundary, still lit.</remarks>
    [Test]
    public void At_ALightExactlyAtItsRange_StillLights()
    {
        StudioPointLighting.At(Lit(new LocalLight(0f, 0f, 100f, 2f, 0f, 0f, 1f, 0f, 0f, 100f)), null, Vector3.Zero, Vector3.UnitZ).X
            .ShouldBe(2f, 1e-5f);
    }

    [Test]
    public void At_ALightPastItsRange_AddsNothing()
    {
        StudioPointLighting.At(Lit(new LocalLight(0f, 0f, 100f, 2f, 0f, 0f, 1f, 0f, 0f, 99f)), null, Vector3.Zero, Vector3.UnitZ).X
            .ShouldBe(0f);
    }

    /// <remarks>The sun travels down at 60° from vertical: an upward normal meets it at cos 60°.</remarks>
    [Test]
    public void At_TheSun_AddsItsColourByTheCosine()
    {
        SunLight sun = new(4f, 0f, 0f, 0f, -0.8660254f, -0.5f);

        StudioPointLighting.At(PointLighting.None, sun, Vector3.Zero, Vector3.UnitZ).X.ShouldBe(2f, 1e-5f);
    }

    private static PointLighting Lit(LocalLight light) => new(default, [light]);
}
