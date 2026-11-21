using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The <c>Eyes</c> shader's glint: where it goes, how bright, and the 32×32 target it is drawn into (B725 item 3).</summary>
/// <remarks>
/// **Read** from TF2's x64 <c>studiorender.dll</c> (disassembled; closed): the eyeball mesh's setup (<c>0x18000f7c1</c>)
/// writes <c>$glintu</c>/<c>$glintv</c> from the view's right and up at <c>0x18001dbe0</c>; <c>0x18001e020</c> binds
/// <c>vgui/black</c> where the eye is under <c>r_eyeglintlodpixels</c> wide, else renders <c>_rt_eyeglint</c> in
/// <c>0x18001f510</c>: the glints from <c>0x18001d600</c> (each light's attenuation from <c>0x180021680</c>), one 3×3-texel
/// quad each through <c>eyeglint_vs20</c>/<c>eyeglint_ps2x.fxc</c>, added ONE/ONE. From published source,
/// <c>eyes_ps2x.fxc:56</c> adds the sample times the damping (<c>eyes_dx8_dx9_helper.cpp:445-453</c>). The derivation is
/// <c>docs/RISKS.md</c> B725.
/// </remarks>
public sealed class EyeGlintConformanceTests
{
    private const float Tolerance = 1e-5f;

    /// <remarks>
    /// <c>0x18001dbe0</c>: <c>$glintu = ( right · 0.5 / radius, 0.5 − origin · that )</c>, <c>$glintv</c> the same with up.
    /// An eye at (10, 20, 30) of radius 0.5, right (0, −1, 0), up (0, 0, 1): U = (0, −1, 0, 0.5 + 20), V = (0, 0, 1, 0.5 − 30).
    /// </remarks>
    [Test]
    public void Projection_AnEyeOfRadiusHalf_IsTheViewsRightAndUpThroughItsCentre()
    {
        ((float X, float Y, float Z, float W) u, (float X, float Y, float Z, float W) v) =
            EyeGlint.Projection((10f, 20f, 30f), 0.5f, (0f, -1f, 0f), (0f, 0f, 1f));

        u.ShouldBe((0f, -1f, 0f, 20.5f));
        v.ShouldBe((0f, 0f, 1f, -29.5f));
    }

    /// <remarks>
    /// <c>0x180021680</c>: the attenuation is chosen by the light's <c>m_Flags &amp; 7</c> — one bit per nonzero term
    /// (<c>lightdesc.cpp:11-46</c>) — through a table at <c>0x1800962d8</c>. A point light 2 units away, colour 1:
    /// <c>1 / a0</c>, <c>1 / (d·a1 + FLT_EPSILON)</c>, <c>1 / (d·a1 + a0)</c>, <c>1 / (d²·a2 + FLT_EPSILON)</c>,
    /// <c>1 / (d²·a2 + a0)</c>, <c>1 / (d·a1 + FLT_EPSILON + d²·a2)</c>, <c>1 / (d·a1 + a0 + d²·a2)</c>, and with no term
    /// <c>2²³</c> (<c>0x180080ab0</c>).
    /// </remarks>
    [TestCase(1f, 0f, 0f, 1f)]
    [TestCase(0f, 1f, 0f, 1f / (2f + 1.1920929e-07f))]
    [TestCase(1f, 1f, 0f, 1f / 3f)]
    [TestCase(0f, 0f, 1f, 1f / (4f + 1.1920929e-07f))]
    [TestCase(1f, 0f, 1f, 1f / 5f)]
    [TestCase(0f, 1f, 1f, 1f / (2f + 1.1920929e-07f + 4f))]
    [TestCase(1f, 1f, 1f, 1f / 7f)]
    [TestCase(0f, 0f, 0f, 8388608f)]
    public void LightAt_APointLightTwoUnitsAway_IsAttenuatedByItsNonzeroTerms(float a0, float a1, float a2, float expected)
    {
        GlintLight lamp = new(false, (2f, 0f, 0f), default, (1f, 1f, 1f), 0f, a0, a1, a2);

        ((float X, float Y, float Z) delta, (float R, float G, float B) colour) = EyeGlint.LightAt(lamp, (0f, 0f, 0f));

        delta.ShouldBe((2f, 0f, 0f));
        colour.R.ShouldBe(expected, expected * Tolerance);
        colour.G.ShouldBe(expected, expected * Tolerance);
    }

    /// <remarks>
    /// **The epsilon is visible only near the light**: 1e-4 units away, a linear-only light is <c>1 / (1e-4 + 1.19e-7)</c>
    /// = 9988.09 where without it 10000, and a quadratic-only one <c>1 / (1e-8 + 1.19e-7)</c> = 7.739e6 where without it 1e8.
    /// </remarks>
    [TestCase(false, 9988.093f)]
    [TestCase(true, 7739382f)]
    public void LightAt_ALightWithoutAConstantTermAlmostAtThePoint_AddsFltEpsilon(bool quadratic, float expected)
    {
        GlintLight lamp = new(false, (1e-4f, 0f, 0f), default, (1f, 1f, 1f), 0f, 0f, quadratic ? 0f : 1f, quadratic ? 1f : 0f);

        EyeGlint.LightAt(lamp, (0f, 0f, 0f)).Colour.R.ShouldBe(expected, expected * 1e-4f);
    }

    /// <remarks>
    /// Every table entry returns 0 first where the light has a range and the point is past it (<c>d² &gt; range²</c>): a
    /// range of 1.5 at 2 units is dark, one of 3 is not. A directional light's delta is <c>−direction</c> (<c>0x180021900</c>),
    /// one unit long.
    /// </remarks>
    [TestCase(1.5f, 0f)]
    [TestCase(3f, 1f / 7f)]
    public void LightAt_APointLightWithARange_IsDarkPastIt(float range, float expected)
    {
        GlintLight lamp = new(false, (2f, 0f, 0f), default, (1f, 1f, 1f), range, 1f, 1f, 1f);

        EyeGlint.LightAt(lamp, (0f, 0f, 0f)).Colour.R.ShouldBe(expected, Tolerance);
    }

    [Test]
    public void LightAt_ADirectionalLight_PointsBackAlongItsDirection()
    {
        GlintLight sun = new(true, (9f, 9f, 9f), (0f, 0f, -1f), (2f, 3f, 4f), 0f, 1f, 0f, 0f);

        ((float X, float Y, float Z) delta, (float R, float G, float B) colour) = EyeGlint.LightAt(sun, (5f, 5f, 5f));

        delta.ShouldBe((0f, 0f, 1f));
        colour.ShouldBe((2f, 3f, 4f));
    }

    /// <remarks>
    /// <c>0x18001d600</c> for an eye at the origin, forward +x, radius 0.5, seen from (100, 0, 0) with right (0, −1, 0) and
    /// up +z. The cornea sits <c>er·r − cr·(2r/3)</c> = 0.2125334 along forward (er = √(1 − ¼), cr = √(1 − (0.25 / ⅓)²)).
    /// - A sun travelling −x, colour (2, 3, 4): facing the eye, its reflection is the view, so the glint is at (0, 0); seen
    ///   from the centre it is still ahead of `er`, so no second.
    /// - A quadratic lamp at (50, −100, 20), colour (10000, 20000, 30000): from the cornea point d² = 12878.79 and
    ///   N·L = 0.4387 &gt; 0, so a glint at <c>refl · ⅔r</c> = (0.1731565, 0.0346313); from the centre N·L = 0.4402 &lt; er,
    ///   so a second at <c>refl · r</c> = (0.2597348, 0.0519470), lit by the centre's attenuation, d² = 12900.
    /// - A lamp behind the eye (−100, 0, 0): none.
    /// </remarks>
    [Test]
    public void Glints_ASunALampBesideAndALampBehind_AreTheSunsTheLampsAndTheIrisGlint()
    {
        Span<GlintLight> lights =
        [
            new(true, default, (-1f, 0f, 0f), (2f, 3f, 4f), 0f, 1f, 0f, 0f),
            new(false, (50f, -100f, 20f), default, (10000f, 20000f, 30000f), 0f, 0f, 0f, 1f),
            new(false, (-100f, 0f, 0f), default, (1f, 1f, 1f), 0f, 1f, 0f, 0f),
        ];
        Span<EyeGlintPoint> glints = stackalloc EyeGlintPoint[EyeGlint.MaxGlints];

        int count = EyeGlint.Glints(
            (0f, 0f, 0f), (1f, 0f, 0f), 0.5f, (0f, -1f, 0f), (0f, 0f, 1f), (100f, 0f, 0f), lights, glints);

        count.ShouldBe(3);
        Near(glints[0], 0f, 0f, (2f, 3f, 4f));
        Near(glints[1], 0.1731565f, 0.0346313f, (0.77647035f, 1.5529407f, 2.3294111f));
        Near(glints[2], 0.2597348f, 0.0519470f, (0.7751938f, 1.5503876f, 2.3255814f));
    }

    /// <remarks><c>0x18001d600</c> returns at its maximum (<c>cmp rsi, max; jge</c>): room for one keeps the first.</remarks>
    [Test]
    public void Glints_WithRoomForOne_StopAtIt()
    {
        Span<GlintLight> lights =
        [
            new(false, (50f, -100f, 20f), default, (10000f, 20000f, 30000f), 0f, 0f, 0f, 1f),
            new(true, default, (-1f, 0f, 0f), (2f, 3f, 4f), 0f, 1f, 0f, 0f),
        ];
        Span<EyeGlintPoint> glints = stackalloc EyeGlintPoint[1];

        EyeGlint.Glints((0f, 0f, 0f), (1f, 0f, 0f), 0.5f, (0f, -1f, 0f), (0f, 0f, 1f), (100f, 0f, 0f), lights, glints)
            .ShouldBe(1);
        glints[0].X.ShouldBe(0.1731565f, Tolerance);
    }

    /// <remarks>
    /// **The GPU pass, on the CPU** (<c>0x18001f938</c>-): centre <c>((x + 0.5)·32, (y + 0.5)·32)</c> in texels, a quad
    /// from <c>floor − 2</c> to <c>floor + 1</c> at NDC <c>2X/32 − 0.984375</c>, so under D3D9's integer pixel centres it
    /// covers texels <c>floor − 1 … floor + 1</c> with the texel coordinate <c>i − 0.25</c>; <c>eyeglint_ps2x.fxc</c>'s
    /// five Gaussians times 4/9, times the colour, added to an 8-bit target.
    /// - (0, 0), white: texel (16, 16) is <c>0.5438 · 255</c> = 139, its eight neighbours under 0.5.
    /// - (0.1, −0.2): centre (19.2, 9.6), so texels 18-20 by 8-10; (19, 10) is 35 and (20, 10) — covered only under
    ///   D3D9's centres — 10.
    /// </remarks>
    [TestCase(0f, 0f, 16, 16, 139, 17, 16, 0)]
    [TestCase(0.1f, -0.2f, 19, 10, 35, 20, 10, 10)]
    public void Rasterise_OneGlint_IsTheGaussianAtItsTexels(float x, float y, int i, int j, int value, int i2, int j2, int value2)
    {
        byte[] texels = new byte[EyeGlint.Size * EyeGlint.Size * 4];

        EyeGlint.Rasterise([new EyeGlintPoint(x, y, 1f, 1f, 1f)], texels);

        Texel(texels, i, j).ShouldBe((value, value, value));
        Texel(texels, i2, j2).ShouldBe((value2, value2, value2));
        Total(texels).ShouldBe((value + value2) * 3, "nothing else is lit");
    }

    /// <remarks>
    /// **Each quad is blended into the 8-bit target before the next** (ONE/ONE, sRGB write off): two half-white glints at
    /// (0, 0) are 69 (0.2719 · 255 = 69.3) and then <c>69/255 + 0.2719</c> → 138, where one white glint is 139; two white
    /// ones saturate at 255.
    /// </remarks>
    [TestCase(0.5f, 138)]
    [TestCase(1f, 255)]
    public void Rasterise_TwoGlintsAtOnePlace_AddInEightBits(float colour, int expected)
    {
        byte[] texels = new byte[EyeGlint.Size * EyeGlint.Size * 4];

        EyeGlint.Rasterise([new EyeGlintPoint(0f, 0f, colour, colour, colour), new EyeGlintPoint(0f, 0f, colour, colour, colour)], texels);

        Texel(texels, 16, 16).R.ShouldBe(expected);
    }

    /// <remarks>
    /// <c>0x18001e020</c>: <c>vgui/black</c> where <c>ComputePixelWidthOfSphere( origin, radius )</c> is under the config's
    /// threshold, <c>r_eyeglintlodpixels</c> 20 (<c>comiss</c>, <c>jae</c>). A unit sphere 6 units ahead through a 64-pixel
    /// view of <c>[1][1]</c> 1 is 128/6 = 21.3 pixels wide; 7 units, 18.3.
    /// </remarks>
    [TestCase(6f, true)]
    [TestCase(7f, false)]
    public void Drawn_AnEyeAroundTwentyPixelsWide_IsDrawnFromTwenty(float distance, bool expected)
    {
        ScreenFadeView view = new((0f, 0f, 0f), (1f, 0f, 0f), (0f, 0f, 1f), 1f, 64f);

        EyeGlint.Drawn(view, (distance, 0f, 0f), 1f).ShouldBe(expected);
    }

    /// <remarks>
    /// The view's right, up and projection, recovered from the view-projection the renderer holds (its columns 0, 1 and 3 are
    /// right · [0][0], up · [1][1] and forward), against the camera's own basis and <c>ScreenView</c>.
    /// </remarks>
    [Test]
    public void View_AFreeCamerasMatrix_IsItsBasisAndScreenView()
    {
        FreeCamera camera = new() { Origin = (12f, -34f, 56f), Angles = (17f, 143f, 0f), Aspect = 16f / 9f };
        (_, (float X, float Y, float Z) right, (float X, float Y, float Z) up) = camera.Basis();
        ScreenFadeView expected = camera.ScreenView(720f);

        ((float X, float Y, float Z) recovered, ScreenFadeView screen) = EyeGlint.View(camera.ToMatrix(), (12f, -34f, 56f), 720f);

        recovered.X.ShouldBe(right.X, 1e-4f);
        recovered.Y.ShouldBe(right.Y, 1e-4f);
        recovered.Z.ShouldBe(right.Z, 1e-4f);
        screen.Up.X.ShouldBe(up.X, 1e-4f);
        screen.Up.Y.ShouldBe(up.Y, 1e-4f);
        screen.Up.Z.ShouldBe(up.Z, 1e-4f);
        screen.Forward.X.ShouldBe(expected.Forward.X, 1e-4f);
        screen.Forward.Y.ShouldBe(expected.Forward.Y, 1e-4f);
        screen.Forward.Z.ShouldBe(expected.Forward.Z, 1e-4f);
        screen.ProjectionY.ShouldBe(expected.ProjectionY, 1e-4f);
        screen.ViewportHeight.ShouldBe(720f);
        screen.Eye.ShouldBe((12f, -34f, 56f));
    }

    private static void Near(EyeGlintPoint glint, float x, float y, (float R, float G, float B) colour)
    {
        glint.X.ShouldBe(x, Tolerance);
        glint.Y.ShouldBe(y, Tolerance);
        glint.Red.ShouldBe(colour.R, colour.R * Tolerance);
        glint.Green.ShouldBe(colour.G, colour.G * Tolerance);
        glint.Blue.ShouldBe(colour.B, colour.B * Tolerance);
    }

    private static (int R, int G, int B) Texel(byte[] texels, int i, int j)
    {
        int at = ((j * EyeGlint.Size) + i) * 4;

        return (texels[at], texels[at + 1], texels[at + 2]);
    }

    private static int Total(byte[] texels)
    {
        int total = 0;

        for (int at = 0; at < texels.Length; at += 4)
        {
            total += texels[at] + texels[at + 1] + texels[at + 2];
        }

        return total;
    }
}
