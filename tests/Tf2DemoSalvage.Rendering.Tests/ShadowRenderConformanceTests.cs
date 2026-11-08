using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A render-to-texture shadow, drawn: the caster into its atlas cell with <c>ShadowBuild</c>, then the world's shadow decal
/// with <c>Shadow</c> over the floor it lands on (B702, finding 107).
/// </summary>
/// <remarks>
/// **The prediction is Valve's arithmetic** (`shadow_ps2x.fxc:35-62`): under the caster every tap's alpha is 1 and the floor
/// is closer than the falloff start, so the coverage is 1 and the shader writes <c>$color</c> — the floor's linear value
/// times <c>pow( 64/255, 2.2 )</c> through the <c>ZERO, SRC_COLOR</c> blend. Away from the caster nothing is drawn.
///
/// **The fixture**: a grey floor at z = 0, 800 across; a 32-unit box standing on it at the origin, its shadow cast straight
/// down 50 units; the shadow's caster drawn as the box's top face; the camera 300 above looking down, 64 pixels square.
/// The box's shadow origin is its top, 32 above the floor, inside the falloff start √3072 / 2 + 16 ≈ 43.7, so no fade.
/// </remarks>
[NonParallelizable]
public sealed class ShadowRenderConformanceTests
{
    private const int Floor = 0;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(100)], ["LightmappedGeneric"]);

    private static readonly FreeCamera Above = new() { Origin = (0f, 0f, 300f), Angles = (89f, 0f, 0f), Aspect = 1f };

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    /// <summary>`SetPixelShaderConstantGammaToLinear` of the controller's default grey, 64.</summary>
    private static readonly float ShadowColour = MathF.Pow(64f / 255f, 2.2f);

    [Test]
    public void Draw_AFloorUnderABox_IsTheFloorTimesTheShadowColourUnderItAndUntouchedAway()
    {
        ((int R, int G, int B) under, (int R, int G, int B) away)? drawn = Draw(withShadow: true);
        ((int R, int G, int B) under, (int R, int G, int B) away)? bare = Draw(withShadow: false);

        if (drawn is not { } shadowed || bare is not { } control)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"WITH SHADOW under {shadowed.under} away {shadowed.away}; WITHOUT under {control.under} away {control.away}");

        // The control: without a shadow the two pixels are the same floor, so a difference below is the shadow's.
        control.under.ShouldBe(control.away, "the bare floor differs between the two pixels");
        shadowed.away.ShouldBe(control.away, "the shadow reached a pixel away from the box");

        int expected = Encode(Decode(control.under.G) * ShadowColour);

        shadowed.under.G.ShouldBeInRange(expected - 2, expected + 2, $"the floor under the box drew {shadowed.under}, not {expected}");
    }

    /// <remarks>
    /// The same decal handed over as a brush entity's: the world pass leaves it alone — its run is not the world's — and
    /// the entity pass after the models draws it, the same pixel as the world's (`FUN_1800e05d0`'s
    /// <c>RenderProjectedTextures</c>, the same <c>Shadow</c> material).
    /// </remarks>
    [Test]
    public void DrawEntityShadows_TheFloorsDecalOnABrushEntity_IsDrawnAfterTheWorldAndMatchesTheWorlds()
    {
        ((int R, int G, int B) under, (int R, int G, int B) away)? onWorld = Draw(withShadow: true);
        ((int R, int G, int B) under, (int R, int G, int B) away)? onBrush = Draw(withShadow: true, onBrush: true);

        if (onWorld is not { } world || onBrush is not { } brush)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        world.under.ShouldNotBe(world.away, "the control: the world's decal darkened the floor");
        brush.ShouldBe(world);
    }

    /// <remarks>
    /// An alpha-tested caster adds its own texture's alpha into the cell: `studiorender.dll` `FUN_1800041c0` sets
    /// <c>$translucent_material</c> for <c>IsAlphaTested()</c> as for <c>IsTranslucent()</c>, and the build pass has no
    /// threshold. So a cut-out of alpha 128 covers 128/255 of the floor under it, and the floor is lerped that far toward
    /// <c>$color</c> — not all the way, as an opaque caster's is.
    /// </remarks>
    [Test]
    public void Draw_AnAlphaTestedCaster_CoversTheFloorByItsTextureAlpha()
    {
        MapAssets cutout = MapAssets.ForMaterials(
            [ModulateToneMapConformanceTests.Grey(100), Cutout(128)], ["LightmappedGeneric", "VertexLitGeneric"]);

        ((int R, int G, int B) under, (int R, int G, int B) away)? drawn = Draw(withShadow: true, assets: cutout, casterMaterial: 1);
        ((int R, int G, int B) under, (int R, int G, int B) away)? bare = Draw(withShadow: false, assets: cutout, casterMaterial: 1);

        if (drawn is not { } shadowed || bare is not { } control)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        float coverage = 128f / 255f;
        int expected = Encode(Decode(control.under.G) * (1f + (coverage * (ShadowColour - 1f))));

        shadowed.under.G.ShouldBeInRange(expected - 2, expected + 2, $"the floor under the cut-out drew {shadowed.under}, not {expected}");
    }

    /// <summary>A 4×4 white texture of one alpha, alpha-tested (<c>$alphatest</c>), as a cut-out caster's material.</summary>
    private static MapTexture Cutout(byte alpha) => new(
        4,
        4,
        4,
        4,
        TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { (byte)255, (byte)255, (byte)255, alpha }).ToArray()),
        IsTransparent: true);

    private static ((int, int, int), (int, int, int))? Draw(
        bool withShadow, bool onBrush = false, MapAssets? assets = null, int casterMaterial = Floor)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        MapAssets drawn = assets ?? Assets;
        ShadowFrame frame = withShadow ? Frame() : ShadowFrame.None;

        if (onBrush)
        {
            frame = frame with { World = [], Brushes = frame.World };
        }

        target.Clear(0f, 0f, 0f);
        target.DrawShadowAtlas(frame, Top(), [new WorldBatch(casterMaterial, 0, 6)], Identity, drawn);
        target.DrawWorld(Square(0f, 400f), [new WorldBatch(Floor, 0, 6)], Above.ToMatrix(), drawn);

        target.ShadowDecalCornersDrawn.ShouldBe(frame.World.Count, "the world pass did not draw the world's shadow corners alone");

        target.DrawEntityShadows();
        target.ShadowDecalCornersDrawn.ShouldBe(frame.World.Count + frame.Brushes.Count, "the entity pass did not draw the brushes' corners");

        return (target.PixelAt(32, 32), target.PixelAt(4, 4));
    }

    /// <summary>The box's shadow, from the production casters and receivers.</summary>
    private static ShadowFrame Frame()
    {
        ShadowEntity box = new(1, null, ShadowCast.RenderToTexture, Vector3.Zero, (0f, 0f, 0f), new Vector3(-16f, -16f, 0f), new Vector3(16f, 16f, 32f));
        IReadOnlyList<CastShadow> casters = new ShadowCasters().Frame([box], new Vector3(0f, 0f, -1f), 50f, default, Above.ScreenView(64f));

        // Clockwise seen from above, the side it faces, as a BSP face is wound: the decal pass culls back faces.
        Vector3[] corners = [new(-400f, -400f, 0f), new(-400f, 400f, 0f), new(400f, 400f, 0f), new(400f, -400f, 0f)];
        DecalFace face = new(0, corners, Vector3.UnitZ, 0f, default, default, (0, 0), (0, 0), false, false, false, default);
        ShadowReceivers receivers = new(Wall(), new DecalWorld([], [[0], [0]], [face]), [SurfaceShadowBits.DynamicShadows]);
        List<ShadowVertex> vertices = [];

        foreach (CastShadow shadow in casters)
        {
            receivers.Project(shadow, vertices);
        }

        vertices.ShouldNotBeEmpty("the control: the box's shadow reached the floor");

        return new ShadowFrame(casters, vertices, new Vector3(ShadowColour), []);
    }

    /// <summary>One node on x = 0, front leaf 0 and back leaf 1, both empty.</summary>
    private static BspLeafTree Wall()
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        return BspLeafTree.FromLumps(node, plane, new byte[64]);
    }

    /// <summary>
    /// The box's middle, 16 above its base, the caster drawn into the cell — not its top, which lies on the shadow's
    /// origin plane, depth 0, the ortho's far clip.
    /// </summary>
    private static List<WorldVertex> Top() => Square(16f, 16f);

    private static List<WorldVertex> Square(float z, float half) =>
    [
        new(-half, -half, z, 0f, 0f, 0f, 0f, 0f),
        new(half, half, z, 1f, 1f, 0f, 0f, 0f),
        new(half, -half, z, 1f, 0f, 0f, 0f, 0f),
        new(-half, -half, z, 0f, 0f, 0f, 0f, 0f),
        new(-half, half, z, 0f, 1f, 0f, 0f, 0f),
        new(half, half, z, 1f, 1f, 0f, 0f, 0f),
    ];

    private static float Decode(int stored)
    {
        float c = stored / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static int Encode(float linear)
    {
        float c = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (int)MathF.Round(c * 255f);
    }
}
