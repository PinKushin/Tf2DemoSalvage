using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A <c>Refract</c> strip, drawn by the real renderer over the frame it copies (B476).</summary>
/// <remarks>
/// **The camera of the synthetic tests is the identity**, so a position is clip space: the frame is cleared black with
/// a lit wall over its right half (x &gt; 0), read before the strip is drawn, and the strip is one full-screen quad in
/// front of it. The normal map is
/// one texel, (255, 128, 255, 255) — a normal of x = 1, y = 1/255, alpha 1 — so <c>refract_ps2x.fxc</c> moves every
/// sample <c>vNormal.xy · vNormal.a · $refractamount</c> to the right: at 0.25, a quarter of the frame
/// (<c>vRefractTexCoord *= scale; vRefractTexCoord += vRefractTexCoordNoWarp</c>). The y offset, a thousandth of the
/// frame, moves nothing a pixel.
/// </remarks>
public sealed class RefractTrailRenderTests
{
    private const int Size = 64;

    /// <remarks>
    /// **The warp reads the frame a quarter to the right.** Column 20 (u ≈ 0.32) is black under the strip and samples
    /// u ≈ 0.57, the wall, so it becomes what column 36 was; column 4 (u ≈ 0.07) samples u ≈ 0.32, still black. The
    /// control is the same strip at <c>$refractamount</c> 0, which samples straight through: column 20 stays black.
    /// </remarks>
    [TestCase(0.25f, true)]
    [TestCase(0f, false)]
    public void Render_ARefractStripOverAHalfLitFrame_ShowsTheFrameOffsetByTheNormal(float amount, bool showsWall)
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        (int, int, int) wall = DrawFrameThenStrip(target, Refract(amount), (1f, 1f, 1f, 1f), 36);

        wall.ShouldNotBe((0, 0, 0), "the control: the wall must be lit for the warp to show anything");
        Near(target.PixelAt(20, 32), showsWall ? wall : (0, 0, 0));
        target.PixelAt(4, 32).ShouldBe((0, 0, 0), "a quarter right of column 4 is still the black half");
    }

    /// <remarks>
    /// **<c>$vertexcolormodulate</c> multiplies the warped colour by the vertex colour**:
    /// <c>refractTintColor *= i.ColorModulate.rgb; colorWarp *= refractTintColor</c>. Over the wall at column 48 a
    /// vertex colour of (1, 0.5, 0) keeps red, halves green IN LINEAR LIGHT and removes blue: the frame is read and
    /// written through the sRGB curve, as the shader's <c>EnableSRGBRead</c> and <c>EnableSRGBWrite</c> ask, and the
    /// offscreen target's view is the window's <c>B8G8R8A8_UNORM_SRGB</c> (<c>Device3D.CreateBackBufferView</c>).
    /// </remarks>
    [Test]
    public void Render_AVertexColourWithColorModulate_TintsTheWarpedFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        (int Red, int Green, int Blue) wall = DrawFrameThenStrip(target, Refract(0f), (1f, 0.5f, 0f, 1f), 48);

        Near(target.PixelAt(48, 32), (wall.Red, SrgbTarget.Encode(0.5 * SrgbTarget.Decode(wall.Green)), 0));
    }

    /// <remarks>
    /// **A fogged Refract strip blends toward the fog colour by the squared factor**: <c>FinalOutput</c> with
    /// <c>CalcPixelFogFactor( PIXELFOGTYPE, … )</c> (`refract_ps2x.fxc`), the range fog the world's pixel shader runs.
    /// With the identity camera the strip's projected z is 0.5; a fog from 0 to 0.25 at density 1 is fully in, so every
    /// pixel is the fog colour, linear (1, 0, 0) — written (255, 0, 0). The control is the same strip with
    /// <c>$nofog</c>, which shows the wall.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Render_ARefractStripInFullFog_IsTheFogColourUnlessNoFog(bool fogged)
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        (int, int, int) wall = DrawFrameThenStrip(
            target, Refract(0f) with { Fogged = fogged }, (1f, 1f, 1f, 1f), 48, new SceneFog(0f, 0.25f, 1f, 0f, 0f, 1f));

        Near(target.PixelAt(48, 32), fogged ? (255, 0, 0) : wall);
    }

    private static void Near((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected)
    {
        Math.Abs(actual.Red - expected.Red).ShouldBeLessThanOrEqualTo(1, $"red {actual} against {expected}");
        Math.Abs(actual.Green - expected.Green).ShouldBeLessThanOrEqualTo(1, $"green {actual} against {expected}");
        Math.Abs(actual.Blue - expected.Blue).ShouldBeLessThanOrEqualTo(1, $"blue {actual} against {expected}");
    }

    /// <remarks>
    /// **The output-level assertion: a real trail from a real match** (D38's only-real-bytes half is the wiring, not the
    /// arithmetic above). `pass_sanctum_a2a`'s entity 355 is an <c>effects/beam001_white</c> trail on player 7 from tick
    /// 6262 (`trails` probe, 2026-10-05). The head here is the player's origin raised 40 units — the test's geometry,
    /// not <c>GetRenderOrigin</c>'s attachment, which the viewer resolves through the player's model; what is under test
    /// is that the strip the trail pass builds is drawn by the refract pass at all.
    ///
    /// **Over a uniform grey the warp is invisible and the tint is not**: <c>$refracttinttexture effects/white</c> makes
    /// the tint <c>2 · white</c>, so where the strip is opaque the grey doubles. The corners see only the grey.
    /// </remarks>
    [Test]
    public void Render_ABeam001TrailFromARealMatch_BrightensTheGreyItCrosses()
    {
        const int Player = 7;
        const int TrailEntity = 355;
        const int FirstTick = 6262;
        const int Ticks = 40;
        const float Grey = 0.25f;

        string demo = CommittedDemo.RequireLocal("demostf-pass_sanctum_a2a-1491285.dem");
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_harvest_final.bsp"));

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size * 2, Size * 2), "no Direct3D on this machine");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        MapAssets assets = MapAssets.Load(
            map, GameArchives.Open(tf), maximumTextureSize: 64, spriteMaterials: DemoModels.Sprites(timeline));

        assets.SpriteMaterials.TryGetValue("effects/beam001_white.vmt", out EngineSprite beam)
            .ShouldBeTrue("beam001_white ships with TF2 and the demo names it");
        beam.Material.Refract.ShouldNotBeNull("the material loads as a Refract draw");

        EntityTrails trails = new();
        List<SceneProp> props = [];
        List<Vector3> heads = [];
        IReadOnlyList<ParticleBatch> batches = [];

        for (int tick = FirstTick; tick < FirstTick + Ticks; tick++)
        {
            timeline.PropsAt(tick, props);

            props.Single(prop => prop.EntityIndex == TrailEntity).Pose.SpriteTrail
                .ShouldNotBeNull("the specimen must still be the trail this test was written against");

            ScenePlayer player = timeline.PlayersAt(tick).Single(one => one.EntityIndex == Player);
            Vector3 head = new(player.X, player.Y, player.Z + 40f);

            heads.Add(head);

            batches = trails.Build(
                [.. props.Where(prop => prop.EntityIndex == TrailEntity)],
                Camera(heads),
                assets.SpriteMaterials,
                _ => head,
                tick * timeline.IntervalPerTick);
        }

        Vector3.Distance(heads[0], heads[^1]).ShouldBeGreaterThan(50f, "the player must move for the trail to have length");
        trails.Drawn.ShouldBe(1);
        batches.ShouldAllBe(batch => batch.Material.Refract != null);

        Vector3 eye = Camera(heads);
        Vector3 middle = Middle(heads);
        Vector3 toward = Vector3.Normalize(middle - eye);

        FreeCamera view = new()
        {
            Origin = (eye.X, eye.Y, eye.Z),
            Angles = (0f, float.RadiansToDegrees(MathF.Atan2(toward.Y, toward.X)), 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        StripPicture.Draw(target, view.ToMatrix(), assets, batches, Grey);

        int background = Sum(target.PixelAt(1, 1));
        int brightest = Enumerable.Range(0, Size * 2).Select(row => Sum(target.PixelAt(Size, row))).Max();

        TestContext.Out.WriteLine($"BEAM001 brightest {brightest} against background {background}");

        // Measured 465 against a background of 411 on 2026-10-05: the strip's alpha fades along it, so the doubling is
        // partial. About half the measured lift of 54 is the floor.
        brightest.ShouldBeGreaterThan(background + 29, "the strip doubles the grey where it is opaque");

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(Size * 2 - 2, 1), (1, Size * 2 - 2), (Size * 2 - 2, Size * 2 - 2)])
        {
            Sum(target.PixelAt(x, y)).ShouldBe(background, $"corner ({x}, {y}) is clear of the strip");
        }
    }

    private static int Sum((int Red, int Green, int Blue) pixel) => pixel.Red + pixel.Green + pixel.Blue;

    /// <summary>The middle of the last ten heads.</summary>
    private static Vector3 Middle(List<Vector3> heads) => (heads[^1] + heads[Math.Max(0, heads.Count - 10)]) / 2f;

    /// <summary>Level, 150 units to the side of the middle of the last stretch of the path.</summary>
    private static Vector3 Camera(List<Vector3> heads)
    {
        Vector3 along = heads[^1] - heads[Math.Max(0, heads.Count - 10)];

        if (along.LengthSquared() < 1f)
        {
            return heads[^1] + new Vector3(0f, 150f, 0f);
        }

        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(along), Vector3.UnitZ));

        return Middle(heads) + (side * 150f);
    }

    private static RefractMaterial Refract(float amount) => new(
        new MapTexture(1, 1, 1, 1, TextureImage.Rgba(new byte[] { 255, 128, 255, 255 }), IsTransparent: false),
        amount,
        (1f, 1f, 1f),
        RefractTintTexture: null,
        BlurAmount: 0,
        VertexColorModulate: true,
        WritesDepth: true);

    /// <summary>Clears black, draws the lit right half, reads the frame at one column, then draws the strip over everything.</summary>
    /// <returns>The frame at <paramref name="column"/>, row 32, before the strip.</returns>
    private static (int Red, int Green, int Blue) DrawFrameThenStrip(
        OffscreenTarget target,
        RefractMaterial refract,
        (float Red, float Green, float Blue, float Alpha) colour,
        int column,
        SceneFog? fog = null)
    {
        List<WorldVertex> wall =
        [
            new(0f, -1f, 0.9f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.9f, 1f, 1f, 0f, 0f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.9f, 1f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
            new(0f, -1f, 0.9f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
            new(0f, 1f, 0.9f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.9f, 1f, 1f, 0f, 0f, 0f, 1f, 1f, 1f),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [new WorldBatch(0, 0, wall.Count)], Identity, MapCache.Load(), surfaceColours: true, translucent: false);

        (int, int, int) before = target.PixelAt(column, 32);
        (float r, float g, float b, float a) = colour;

        DetailSpriteVertex Corner(float x, float y) => new(x, y, 0.5f, 0f, 0f, r, g, b, a, 0f, 0f, 0f);

        List<DetailSpriteVertex> strip =
        [
            Corner(-1f, -1f), Corner(1f, 1f), Corner(1f, -1f),
            Corner(-1f, -1f), Corner(-1f, 1f), Corner(1f, 1f),
        ];

        target.DrawSprites(
            new ParticleBatch(
                strip,
                new ParticleMaterial(refract.NormalMap, [], SpriteBlend.Translucent, Depth: SpriteDepth.TestAndWrite)
                {
                    Refract = refract,
                }),
            Identity,
            fog);

        return before;
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
