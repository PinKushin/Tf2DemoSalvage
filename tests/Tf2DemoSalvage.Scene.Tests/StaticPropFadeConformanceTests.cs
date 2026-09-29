using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A static prop's distance fade, as `engine.dll` builds and applies it (B430).</summary>
/// <remarks>
/// Two engine functions, read in disassembly and decompilation:
///
/// - **`UnserializeModels`, `0x180206590`** — only a prop whose lump flags carry
///   `STATIC_PROP_FLAG_FADES` (0x1, `public/gamebspfile.h:126`) gets an entry in the list at
///   `mgr+0x78`, `{ int prop; float min; float max; float scale }`. Unless
///   `STATIC_PROP_SCREEN_SPACE_FADE` (0x20) is set, min and max are stored SQUARED. The scale is 255
///   when they are equal, else `255 / (max - min)` (squared) or `255 / (min - max)` (screen space).
/// - **`FUN_180202c60`** — the per-prop opacity. `ComputePropOpacity` itself (`0x180203030`, slot 5 of
///   the `IStaticPropMgrClient` table at `0x1803b7100`) only stores the view origin and the factor.
///   The prop's alpha is 255 when the factor is negative (`cl_leveloverview`) or the prop has no
///   entry; otherwise, with `d² = |(origin - view) · factor|²`, it is 0 at or past `max`, 255 at or
///   inside `min` (or when `min &lt; 0`), and `clamp((int)((max - d²) · scale), 0, 255)` between.
///
/// Numbers chosen so the falloff lands where it can be predicted: a 100 → 300 band is 10,000 →
/// 90,000 squared, a scale of 255 / 80,000, and at 200 units (40,000) the alpha is 159.375 → 159.
/// </remarks>
public sealed class StaticPropFadeConformanceTests
{
    private const int Fades = 0x1;
    private const int ScreenSpace = 0x20;

    [Test]
    public void For_WithoutTheFadesFlag_IsNoEntry() =>
        StaticPropFade.For(0x2, 100f, 300f).ShouldBeNull("only STATIC_PROP_FLAG_FADES earns an entry");

    [Test]
    public void For_ADistanceFade_StoresBothBoundsSquared()
    {
        StaticPropFade fade = StaticPropFade.For(Fades, 100f, 300f).ShouldNotBeNull();

        fade.Minimum.ShouldBe(10_000f);
        fade.Maximum.ShouldBe(90_000f);
        fade.Scale.ShouldBe(255f / 80_000f);
        fade.ScreenSpace.ShouldBeFalse();
    }

    [Test]
    public void For_AScreenSpaceFade_StoresTheBoundsAsWrittenAndTheReversedScale()
    {
        StaticPropFade fade = StaticPropFade.For(Fades | ScreenSpace, 100f, 300f).ShouldNotBeNull();

        fade.Minimum.ShouldBe(100f);
        fade.Maximum.ShouldBe(300f);
        fade.Scale.ShouldBe(255f / -200f);
        fade.ScreenSpace.ShouldBeTrue();
    }

    [Test]
    public void For_EqualBounds_TakesAScaleOf255() =>
        StaticPropFade.For(Fades, 300f, 300f).ShouldNotBeNull().Scale.ShouldBe(255f);

    [TestCase(50f, 255)]
    [TestCase(100f, 255)]
    [TestCase(200f, 159)]
    [TestCase(300f, 0)]
    [TestCase(400f, 0)]
    public void Alpha_AlongTheBand_IsTheSquaredLerp(float x, int expected) =>
        Band().Alpha((x, 0f, 0f), (0f, 0f, 0f), 1f).ShouldBe((byte)expected);

    [Test]
    public void Alpha_MeasuresFromTheViewOrigin() =>
        Band().Alpha((1000f, 0f, 0f), (800f, 0f, 0f), 1f).ShouldBe((byte)159);

    [Test]
    public void Alpha_WithAFovFactor_ScalesTheDistanceBeforeSquaring() =>
        Band().Alpha((400f, 0f, 0f), (0f, 0f, 0f), 0.5f).ShouldBe((byte)159, "400 × 0.5 is 200 units");

    [Test]
    public void Alpha_WithANegativeFactor_IsOpaque() =>
        Band().Alpha((400f, 0f, 0f), (0f, 0f, 0f), -1f).ShouldBe((byte)255, "cl_leveloverview disables it");

    [TestCase(200f, 255)]
    [TestCase(300f, 0)]
    public void Alpha_WithEqualBounds_IsAHardCut(float x, int expected) =>
        StaticPropFade.For(Fades, 300f, 300f).ShouldNotBeNull()
            .Alpha((x, 0f, 0f), (0f, 0f, 0f), 1f).ShouldBe((byte)expected);

    [Test]
    public void Instances_AFadingStaticPropInsideItsBand_CarriesThePartialAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];
        SceneProp[] props = [Static(x: 200f, Band())];

        models.Add(props, ModelFramesFixture.OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Alpha.ShouldBe(159);
    }

    /// <remarks>The control: the same prop with no entry is opaque at the same distance.</remarks>
    [Test]
    public void Instances_AStaticPropWithNoEntry_IsOpaque()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];
        SceneProp[] props = [Static(x: 400f, fade: null)];

        models.Add(props, ModelFramesFixture.OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Alpha.ShouldBe(255);
    }

    /// <remarks>
    /// **The output-level check: real lump bytes, the production `StaticModel`, the production draw.**
    /// `koth_harvest_final` places `lightbulb001.mdl` at (−944 −400 208) with a 1200 → 1400 fade and
    /// the flag set (`static-prop-fades` probe: 377 of 652 props fade). From 1300 units out:
    /// (1400² − 1300²) · 255 / (1400² − 1200²) = 270,000 · 255 / 520,000 = 132.4 → 132. The control
    /// is a prop with no entry, from the same distance, drawing opaque.
    /// </remarks>
    [Test]
    public void Instances_OnKothHarvest_FadeTheLightbulbAndNotAPropWithoutAnEntry()
    {
        string path = Skip.Unless(
            GameInstall.Find(Path.Combine("maps", "koth_harvest_final.bsp")), GameInstall.Missing);
        IReadOnlyList<BspStaticProp> placements = BspStaticProps.Read(File.ReadAllBytes(path));

        int bulb = Enumerable.Range(0, placements.Count).First(index =>
            placements[index].Model == "models/props_2fort/lightbulb001.mdl"
            && MathF.Abs(placements[index].X + 944f) < 1f && MathF.Abs(placements[index].Y + 400f) < 1f);
        int plain = Enumerable.Range(0, placements.Count).First(index => (placements[index].Flags & 0x1) == 0);

        Alpha(PropModels.StaticModel(placements[bulb], bulb), 1300f).ShouldBe(132);
        Alpha(PropModels.StaticModel(placements[plain], plain), 1300f).ShouldBe(255);
    }

    /// <summary>The alpha the model draw gives a prop seen from <paramref name="distance"/> units along +X.</summary>
    private static int Alpha(SceneProp prop, float distance)
    {
        EntityModelSet models = new() { ViewOrigin = (prop.Pose.X + distance, prop.Pose.Y, prop.Pose.Z) };
        List<ModelInstance> instances = [];
        SceneProp[] props = [prop];

        models.Add(props, ModelFramesFixture.OneTriangle);
        models.Instances(props, instances);

        return instances.ShouldHaveSingleItem().Alpha;
    }

    private static StaticPropFade Band() => StaticPropFade.For(Fades, 100f, 300f).ShouldNotBeNull();

    private static SceneProp Static(float x, StaticPropFade? fade) =>
        new(
            PropModels.FirstStaticPropEntityIndex,
            "models/props/crate.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = x },
            ClassName: "prop_static",
            StaticFade: fade);
}
