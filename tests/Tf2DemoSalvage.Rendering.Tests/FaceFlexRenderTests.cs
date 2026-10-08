using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The flex stream drawn on a real device (B513): a delta in the stream draws exactly what the same delta baked into
/// the vertex draws.
/// </summary>
/// <remarks>
/// **One identity carries every test**: <c>ApplyMorph</c> (common_vs_fxc.h:384-387) adds the position delta to the
/// position and the normal delta to both the normal and the tangent, in model space before skinning. So a skinned quad
/// with a delta in the stream must draw what the quad draws with that delta already added to its vertices and no
/// stream. The camera and texel are <c>ModelTangentRenderTests</c>'s.
/// </remarks>
public sealed class FaceFlexRenderTests
{
    private const int Size = 65;
    private const int Centre = 32;

    private static readonly float[] IdentityBone = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    [Test]
    public void Draw_APositionDelta_MovesTheVerticesAsIfTheyStoodThere()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject() is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the game is not installed");
            return;
        }

        // A quad a third of the view wide, pushed 20 units right by the stream — or by its vertices.
        int[] flexed = Picture(target, subject, offset: 0f, normal: (0f, -1f, 0f), stream: (20f, 0f, 0f, 0f, 0f, 0f));
        int[] moved = Picture(target, subject, offset: 20f, normal: (0f, -1f, 0f), stream: null);
        int[] still = Picture(target, subject, offset: 0f, normal: (0f, -1f, 0f), stream: null);
        int shifted = flexed.Zip(still).Count(pair => Math.Abs(pair.First - pair.Second) > 6);

        TestContext.Out.WriteLine($"{shifted} pixels change when the stream moves the quad");

        shifted.ShouldBeGreaterThan(200, "the control: about 6 columns of 21 rows each side");
        flexed.Zip(moved).Count(pair => Math.Abs(pair.First - pair.Second) > 2).ShouldBe(0, "the stream's delta is the vertex's");
    }

    [Test]
    public void Draw_ANormalDelta_TurnsTheNormalAndTheTangentTogether()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject() is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the game is not installed");
            return;
        }

        // N = (0, -1, 0) and T = (1, 0, 0) take d = (0.6, 0, 0.5): the stream must draw N + d with T + d.
        (float X, float Y, float Z) d = (0.6f, 0f, 0.5f);

        int flexed = Picture(target, subject, 0f, (0f, -1f, 0f), (0f, 0f, 0f, d.X, d.Y, d.Z), tangent: (1f, 0f, 0f))[Middle];
        int baked = Picture(target, subject, 0f, (d.X, -1f + d.Y, d.Z), null, tangent: (1f + d.X, d.Y, d.Z))[Middle];
        int normalOnly = Picture(target, subject, 0f, (d.X, -1f + d.Y, d.Z), null, tangent: (1f, 0f, 0f))[Middle];
        int still = Picture(target, subject, 0f, (0f, -1f, 0f), null, tangent: (1f, 0f, 0f))[Middle];

        TestContext.Out.WriteLine($"flexed {flexed}, baked {baked}, normal alone {normalOnly}, still {still}");

        Math.Abs(flexed - still).ShouldBeGreaterThan(12, "the control: the delta changes the lit pixel");
        Math.Abs(baked - normalOnly).ShouldBeGreaterThan(6, "the control: turning the tangent too changes it again");
        flexed.ShouldBeInRange(baked - 3, baked + 3, "the tangent takes the normal's delta (common_vs_fxc.h:387)");
    }

    private const int Middle = (Centre * Size) + Centre;

    private static (MapAssets Assets, int Material, (float U, float V) Texel)? Subject()
    {
        if (GameInstall.Root is null)
        {
            return null;
        }

        MapAssets assets = MapCache.Load(entityModels: ["models/player/scout.mdl"]);
        int material = Enumerable.Range(0, assets.Materials.Count).FirstOrDefault(
            index => assets.Materials[index].Name.EndsWith("scout/scout_red", StringComparison.OrdinalIgnoreCase), -1);

        material.ShouldBeGreaterThanOrEqualTo(0, "the scout names scout_red");

        // The texel ModelTangentRenderTests picks: leaning furthest along BOTH tangent axes, so a turned frame shows.
        MapTexture map = assets.Bumps[material]!.Value.Texture;
        byte[] rgba = map.Image.ToRgba(map.Width, map.Height).ToArray();

        (float X, float Y, float Z) Decoded(int texel) => (
            (rgba[texel * 4] / 255f * 2f) - 1f,
            (rgba[(texel * 4) + 1] / 255f * 2f) - 1f,
            (rgba[(texel * 4) + 2] / 255f * 2f) - 1f);

        int best = Enumerable.Range(0, rgba.Length / 4)
            .Where(texel => Decoded(texel).Z > 0.3f)
            .MaxBy(texel => MathF.Min(MathF.Abs(Decoded(texel).X), MathF.Abs(Decoded(texel).Y)));

        return (assets, material, (((best % map.Width) + 0.5f) / map.Width, ((best / map.Width) + 0.5f) / map.Height));
    }

    /// <summary>A skinned quad 64 units wide on one texel, each pixel's channel sum.</summary>
    private static int[] Picture(
        OffscreenTarget target,
        (MapAssets Assets, int Material, (float U, float V) Texel) subject,
        float offset,
        (float X, float Y, float Z) normal,
        (float, float, float, float, float, float)? stream,
        (float X, float Y, float Z)? tangent = null)
    {
        (float X, float Y, float Z) t = tangent ?? (1f, 0f, 0f);

        WorldVertex Corner(float x, float z) => new(x + offset, 0f, z, subject.Texel.U, subject.Texel.V, 0f, 0f, 0f)
        {
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
            TangentX = t.X,
            TangentY = t.Y,
            TangentZ = t.Z,
            TangentW = 1f,
            WeightA = 1f,
        };

        List<WorldVertex> vertices =
        [
            Corner(-32f, -32f), Corner(32f, -32f), Corner(32f, 32f),
            Corner(-32f, -32f), Corner(32f, 32f), Corner(-32f, 32f),
        ];

        float[]? flex = stream is { } s
            ? [.. Enumerable.Range(0, vertices.Count).SelectMany(_ => new[] { s.Item1, s.Item2, s.Item3, s.Item4, s.Item5, s.Item6 })]
            : null;

        float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];
        // Close enough that a 20-unit move is several pixels: the view is about 200 units across 65.
        float[] camera = new FreeCamera { Origin = (0f, -100f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(subject.Material, 0, vertices.Count)],
            camera,
            identity,
            subject.Assets,
            light: new AmbientCube((0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f),
                (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f)),
            bothSides: true,
            sun: new SunLight(1f, 1f, 1f, -0.48f, 0.58f, -0.66f),
            bones: [IdentityBone],
            flex: flex);

        int[] sums = new int[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                (int r, int g, int b) = target.PixelAt(x, y);
                sums[(y * Size) + x] = r + g + b;
            }
        }

        return sums;
    }
}
