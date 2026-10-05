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
/// a white wall over its right half (x &gt; 0), and the strip is one full-screen quad in front of it. The normal map is
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
    /// u ≈ 0.57, the wall; column 4 (u ≈ 0.07) samples u ≈ 0.32, still black. The control is the same strip at
    /// <c>$refractamount</c> 0, which samples straight through: column 20 stays black.
    /// </remarks>
    [TestCase(0.25f, 255)]
    [TestCase(0f, 0)]
    public void Render_ARefractStripOverAHalfWhiteFrame_ShowsTheFrameOffsetByTheNormal(float amount, int column20)
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        DrawFrameThenStrip(target, Refract(amount), (1f, 1f, 1f, 1f));

        target.PixelAt(20, 32).ShouldBe((column20, column20, column20));
        target.PixelAt(4, 32).ShouldBe((0, 0, 0), "a quarter right of column 4 is still the black half");
    }

    /// <remarks>
    /// **<c>$vertexcolormodulate</c> multiplies the warped colour by the vertex colour**:
    /// <c>refractTintColor *= i.ColorModulate.rgb; colorWarp *= refractTintColor</c>. Over the white wall at column 48, a
    /// vertex colour of (1, 0.5, 0) is linear (1, 0.5, 0), written through the sRGB curve as (255, 188, 0) — the
    /// target is read and written as sRGB, as the shader's <c>EnableSRGBRead</c> and <c>EnableSRGBWrite</c> ask.
    /// </remarks>
    [Test]
    public void Render_AVertexColourWithColorModulate_TintsTheWarpedFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        DrawFrameThenStrip(target, Refract(0f), (1f, 0.5f, 0f, 1f));

        (int red, int green, int blue) = target.PixelAt(48, 32);

        (red, blue).ShouldBe((255, 0));
        green.ShouldBeInRange(187, 188, "linear one half, through the sRGB curve");
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

        assets.SpriteMaterials.TryGetValue("materials/effects/beam001_white.vmt", out EngineSprite beam)
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

        brightest.ShouldBeGreaterThan(background + 60, "the strip doubles the grey where it is opaque");

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

    /// <summary>Clears black, draws the white right half, then the strip over everything.</summary>
    private static void DrawFrameThenStrip(
        OffscreenTarget target, RefractMaterial refract, (float Red, float Green, float Blue, float Alpha) colour)
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
            Identity);
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
