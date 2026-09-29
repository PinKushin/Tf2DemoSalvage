using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The level screen fade on a real map, through the production load and draw (B432).</summary>
/// <remarks>
/// **`koth_overcast_final` is one of the two installed maps whose `worldspawn` enables it**
/// (`static-prop-fades` census: `minpropscreenwidth 2`, `maxpropscreenwidth 4`), so every static prop
/// there fades out below four pixels. The view is set so the first placed prop's sphere projects to
/// 2000 / 645 = 3.1008 pixels (`ProjectionY` 1, 1000 tall: `px = 2r · 1000 / d`, with `d = 645 r`), and
/// `FUN_1801cb810` gives `(3.1008 − 2) · 255 / 2 = 140.35 → 140` — clear of an integer boundary, which a
/// 3.2-pixel choice sat on (153.0, read as 152). The control is the same prop at 5 pixels, opaque.
/// </remarks>
public sealed class StaticPropScreenFadeWiringTests
{
    [TestCase(645f, 140)]
    [TestCase(400f, 255)]
    public void Instances_OnKothOvercast_AStaticPropFadesByItsScreenWidth(float distancePerRadius, int expected)
    {
        if (GameInstall.Find(Path.Combine("maps", "koth_overcast_final.bsp")) is not { } mapPath)
        {
            Assert.Ignore("koth_overcast_final.bsp or the TF2 install is not on this machine.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(mapPath);
        MapAssets assets = MapAssets.Load(bytes, GameArchives.Open(GameInstall.Require()), maximumTextureSize: 64);

        SceneProp prop = assets.StaticModels.First(candidate => candidate.StaticFade is null);
        StaticPropScreen screen = prop.StaticScreen.ShouldNotBeNull("every static prop carries its sphere");
        screen.Radius.ShouldBeGreaterThan(0f);
        screen.ForcedFadeScale.ShouldBe(1f);

        (float min, float max) = BspEntities.PropScreenWidths(BspEntities.ReadFrom(bytes));
        (min, max).ShouldBe((2f, 4f));

        (float X, float Y, float Z) eye = (prop.Pose.X - (distancePerRadius * screen.Radius), prop.Pose.Y, prop.Pose.Z);
        EntityModelSet models = new()
        {
            ViewOrigin = eye,
            ScreenView = new ScreenFadeView(eye, (1f, 0f, 0f), (0f, 0f, 1f), 1f, 1000f),
            LevelScreenFade = ScreenFadeRange.Set(min, max),
        };
        List<ModelInstance> instances = [];
        SceneProp[] props = [prop];

        models.Add(props, assets.Geometry);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem().Alpha.ShouldBe(expected);
    }
}
