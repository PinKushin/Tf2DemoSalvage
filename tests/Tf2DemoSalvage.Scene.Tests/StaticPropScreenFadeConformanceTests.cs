using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A static prop's SCREEN-size fades, as `engine.dll` and `materialsystem.dll` compute them (B432).</summary>
/// <remarks>
/// - **`FUN_180202c60`'s screen-space branch, `engine.dll` `0x180202e98`** — for a prop flagged
///   `STATIC_PROP_SCREEN_SPACE_FADE` (0x20): `px = ComputePixelWidthOfSphere(origin, radius)`; alpha 0
///   at `px ≤ entry.max`, 255 at `entry.min &lt; 0` or `px ≥ entry.min`, else
///   `clamp((int)((px − max) · scale), 0, 255)`.
/// - **The level fade, `FUN_1801cb810`** (reached through the modelinfo thunks `0x1801c9ed0`/`0x1801c9ef0`)
///   — 255 when `range.min ≤ 0` or the forced fade scale `≤ 0`; `px' = px / forcedScale`; 0 at
///   `px' ≤ min`, 255 at `max &lt; 0` or `px' ≥ max`, else `(px' − min) · scale`. Its range setter,
///   `FUN_1801c9e50`, stores `{min, max, 255/(max−min)}`, or `{min, min, 255}` when `max ≤ min`.
/// - **The min** — `FUN_180202c60` lowers the prop's alpha to the smaller of the level and view fades
///   only when that is lower (`0x180202fe5`..`0x180202ffd`), and applies them to EVERY static prop,
///   with or without a fade entry.
/// - **`ComputePixelWidthOfSphere`, `materialsystem.dll` `FUN_180012710`** = 2 ×
///   `FUN_180030f90`: `center ± radius · up` (the view matrix's row 1) through view-projection, each
///   `y / w` (or `y · 1000` when `w &lt; 0.001`, constants at `0x1800bd540`/`0x1800bd550`), then
///   `|Δ| · viewportHeight · 0.5`.
///
/// The fixture view: eye at the origin looking down +X with +Z up, `ProjectionY` 1 (a 90° vertical
/// field), 1000 pixels tall. A sphere of radius 10 at 100 units spans NDC ±0.1, so
/// `2 · 0.2 · 1000 · 0.5` = 200 pixels.
/// </remarks>
public sealed class StaticPropScreenFadeConformanceTests
{
    private const int Fades = 0x1;
    private const int ScreenSpace = 0x20;

    private static readonly ScreenFadeView View = new((0f, 0f, 0f), (1f, 0f, 0f), (0f, 0f, 1f), 1f, 1000f);

    [Test]
    public void PixelWidthOfSphere_InFrontOfTheEye_IsTwiceTheHalfHeightSpan() =>
        View.PixelWidthOfSphere((100f, 0f, 0f), 10f).ShouldBe(200f, 0.001f);

    [Test]
    public void PixelWidthOfSphere_OffAxis_ProjectsAlongTheCameraUp() =>
        View.PixelWidthOfSphere((100f, 50f, 20f), 10f).ShouldBe(200f, 0.001f, "(20 ± 10)/100 still differ by 0.2");

    /// <remarks>w = −100 is below 0.001, so each y is multiplied by 1000: y = ±10 → Δ 20,000 → 2 × 20,000 × 500.</remarks>
    [Test]
    public void PixelWidthOfSphere_BehindTheEye_TakesTheThousandfoldFallback() =>
        View.PixelWidthOfSphere((-100f, 0f, 0f), 10f).ShouldBe(20_000_000f, 1f);

    [Test]
    public void ScreenView_FromACamera_AgreesWithItsOwnMatrix()
    {
        FreeCamera camera = new() { Origin = (10f, 20f, 30f), Angles = (15f, 40f, 0f), FieldOfView = 90f, Aspect = 16f / 9f };
        float[] matrix = camera.ToMatrix();
        (float X, float Y, float Z) centre = (400f, 380f, 80f);
        const float Radius = 25f;

        (float X, float Y, float Z) up = camera.Basis().Up;
        float Ndc((float X, float Y, float Z) p) =>
            ((p.X * matrix[1]) + (p.Y * matrix[5]) + (p.Z * matrix[9]) + matrix[13])
            / ((p.X * matrix[3]) + (p.Y * matrix[7]) + (p.Z * matrix[11]) + matrix[15]);

        float top = Ndc((centre.X + (up.X * Radius), centre.Y + (up.Y * Radius), centre.Z + (up.Z * Radius)));
        float bottom = Ndc((centre.X - (up.X * Radius), centre.Y - (up.Y * Radius), centre.Z - (up.Z * Radius)));

        camera.ScreenView(720f).PixelWidthOfSphere(centre, Radius)
            .ShouldBe(System.MathF.Abs(top - bottom) * 720f, 0.01f);
    }

    [Test]
    public void ScreenFadeRange_Set_StoresTheLerpScale()
    {
        ScreenFadeRange range = ScreenFadeRange.Set(10f, 20f);

        range.Minimum.ShouldBe(10f);
        range.Maximum.ShouldBe(20f);
        range.Scale.ShouldBe(25.5f);
    }

    [Test]
    public void ScreenFadeRange_SetWithMaxBelowMin_CollapsesToAHardCut() =>
        ScreenFadeRange.Set(10f, -1f).ShouldBe(new ScreenFadeRange(10f, 10f, 255f));

    [TestCase(5f, 1f, 0)]
    [TestCase(10f, 1f, 0)]
    [TestCase(15f, 1f, 127)]
    [TestCase(20f, 1f, 255)]
    [TestCase(30f, 2f, 127, TestName = "ScreenFadeRange_Alpha_DividesByTheForcedScale")]
    public void ScreenFadeRange_Alpha_IsTheLerpOverTheWidth(float pixels, float forced, int expected) =>
        ScreenFadeRange.Set(10f, 20f).Alpha(pixels, forced).ShouldBe((byte)expected);

    [Test]
    public void ScreenFadeRange_AlphaWithAZeroMinimum_IsOpaque() =>
        ScreenFadeRange.Set(0f, -1f).Alpha(0f, 1f).ShouldBe((byte)255, "the 232 maps that write min 0, max −1");

    [Test]
    public void ScreenFadeRange_AlphaWithANegativeForcedScale_IsOpaque() =>
        ScreenFadeRange.Set(10f, 20f).Alpha(1f, -1f).ShouldBe((byte)255, "forcedScale ≤ 0 returns before dividing");

    /// <remarks>Hammer's screen-space band runs large → small: min 300 px opaque, max 100 px gone; scale 255/200.</remarks>
    [TestCase(50f, 0)]
    [TestCase(100f, 0)]
    [TestCase(200f, 127)]
    [TestCase(300f, 255)]
    [TestCase(400f, 255)]
    public void ScreenAlpha_AlongTheBand_IsTheLinearLerp(float pixels, int expected) =>
        StaticPropFade.For(Fades | ScreenSpace, 300f, 100f).ShouldNotBeNull().ScreenAlpha(pixels).ShouldBe((byte)expected);

    [Test]
    public void Opacity_AScreenSpaceEntry_UsesTheProjectedWidth() =>
        StaticPropFade.Opacity(
            StaticPropFade.For(Fades | ScreenSpace, 300f, 100f), new StaticPropScreen(5f, 1f),
            (100f, 0f, 0f), (0f, 0f, 0f), 1f, View, default).ShouldBe((byte)0, "a 100-pixel sphere is at max");

    [Test]
    public void Opacity_NoEntryUnderAnEnabledLevelFade_TakesTheLevelFade() =>
        StaticPropFade.Opacity(
            null, new StaticPropScreen(0.75f, 1f), (100f, 0f, 0f), (0f, 0f, 0f), 1f, View, ScreenFadeRange.Set(10f, 20f))
            .ShouldBe((byte)127, "radius 0.75 at 100 units is 15 pixels");

    [Test]
    public void Opacity_TheLowerOfDistanceAndLevel_Wins() =>
        StaticPropFade.Opacity(
            StaticPropFade.For(Fades, 100f, 300f), new StaticPropScreen(2f, 1f), (200f, 0f, 0f), (0f, 0f, 0f), 1f,
            View, ScreenFadeRange.Set(10f, 20f)).ShouldBe((byte)159, "distance 159 against a 20-pixel sphere's 255");

    [Test]
    public void Opacity_WithANegativeFactor_IsOpaqueWhateverTheLevelFade() =>
        StaticPropFade.Opacity(
            null, new StaticPropScreen(0.01f, 1f), (100f, 0f, 0f), (0f, 0f, 0f), -1f, View, ScreenFadeRange.Set(10f, 20f))
            .ShouldBe((byte)255);

    [Test]
    public void Instances_AStaticPropUnderTheLevelFade_CarriesItsAlpha()
    {
        EntityModelSet models = new()
        {
            ViewOrigin = (0f, 0f, 0f), ScreenView = View, LevelScreenFade = ScreenFadeRange.Set(10f, 20f),
        };
        List<ModelInstance> instances = [];
        SceneProp[] props = [Static(new StaticPropScreen(0.75f, 1f))];

        models.Add(props, ModelFramesFixture.OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Alpha.ShouldBe(127);
    }

    /// <remarks>The control: an ENTITY at the same place and size is not a static prop and takes no screen fade.</remarks>
    [Test]
    public void Instances_AnEntityUnderTheLevelFade_IsOpaque()
    {
        EntityModelSet models = new()
        {
            ViewOrigin = (0f, 0f, 0f), ScreenView = View, LevelScreenFade = ScreenFadeRange.Set(10f, 20f),
        };
        List<ModelInstance> instances = [];
        SceneProp[] props = [Static(null) with { EntityIndex = 5, ClassName = "prop_dynamic" }];

        models.Add(props, ModelFramesFixture.OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Alpha.ShouldBe(255);
    }

    private static SceneProp Static(StaticPropScreen? screen) =>
        new(
            PropModels.FirstStaticPropEntityIndex,
            "models/props/crate.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f },
            ClassName: "prop_static",
            StaticScreen: screen);
}
