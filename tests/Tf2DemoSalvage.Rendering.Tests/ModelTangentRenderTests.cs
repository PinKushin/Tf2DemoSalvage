using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's normal map lit through its tangent frame, drawn on a real device (B170's tangent leftover).
/// </summary>
/// <remarks>
/// **One identity carries every test here**: the bumped normal is a pure function of the texel and the frame
/// (`Vec3TangentToWorld`, common_fxc.h:1135; binormal `cross( N, T ) * w`, common_vs_fxc.h:740 — quoted in
/// <c>ModelTangentConformanceTests</c>), and every lit term reads only that normal. So a quad drawn with a frame
/// must draw exactly what the same quad draws with no frame and that normal as its VERTEX normal — which the
/// test computes from the texel it reads out of the shipped normal map. No knowledge of the lighting is needed.
///
/// **One texel, read exactly**: every corner takes the same coordinate, so the sampler has no derivative, reads
/// mip 0, and at a texel's centre returns that texel. The camera is <c>PhongLocalLightRenderTests</c>'s: the eye at
/// (0, −300, 0) looking along +Y at a quad in the XZ plane.
/// </remarks>
public sealed class ModelTangentRenderTests
{
    private const int Size = 65;
    private const int Centre = 32;

    /// <summary>Facing the eye.</summary>
    private static readonly (float X, float Y, float Z) Facing = (0f, -1f, 0f);

    /// <summary>A sun from up, right and in front, so both the diffuse and the highlight move with the normal.</summary>
    private static readonly SunLight Sun = Light((-0.5f, 0.6f, -0.62f));

    private static readonly AmbientCube Dim =
        new((0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f),
            (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f));

    [TestCase(1f)]
    [TestCase(-1f)]
    public void Draw_ABumpThroughTheFrame_DrawsTheNormalTheFramePredicts(float sign)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject() is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the game is not installed");
            return;
        }

        (MapAssets assets, int material, (float U, float V) texel, (float X, float Y, float Z) bumped) = subject;

        // T along +X; B = cross( N, T ) * w = (0, 0, 1) * w for N toward the eye.
        (float X, float Y, float Z) predicted = Frame(bumped, Facing, (1f, 0f, 0f), sign);

        (int R, int G, int B) framed = Draw(target, assets, material, texel, Facing, (1f, 0f, 0f, sign));
        (int R, int G, int B) reference = Draw(target, assets, material, texel, predicted, default);
        (int R, int G, int B) mirrored = Draw(target, assets, material, texel, Facing, (1f, 0f, 0f, -sign));

        TestContext.Out.WriteLine(
            $"texel {texel} decodes {bumped}, w {sign}: framed {framed}, predicted normal {predicted} draws {reference}, other sign {mirrored}");

        Sum(Minus(framed, mirrored)).ShouldBeGreaterThan(12, "the control: the binormal's sign moves this pixel");
        Near(framed, reference, 2, "the frame turns the texel into the predicted normal");
    }

    [Test]
    public void Draw_ATangentSkinnedByABone_TurnsWithIt()
    {
        // A bone turning 90 degrees about the view axis (Y): the quad and its normal stay put, the tangent turns from
        // +Z to +X. `worldTangentS = mul3x3( modelTangentS, blendMatrix )` (common_vs_fxc.h:738) — the same matrix as
        // the position. Unskinned, the same model-space tangent would leave the frame on +Z.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject() is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the game is not installed");
            return;
        }

        (MapAssets assets, int material, (float U, float V) texel, _) = subject;

        // R(x, y, z) = (z, y, -x), so the model-space quad and tangent are R⁻¹ of where they are drawn: (-z, y, x).
        float[] bone = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f];

        (int R, int G, int B) skinned = Draw(
            target, assets, material, texel, Facing, (0f, 0f, 1f, 1f), bones: [bone]);
        (int R, int G, int B) expected = Draw(target, assets, material, texel, Facing, (1f, 0f, 0f, 1f));
        (int R, int G, int B) unturned = Draw(target, assets, material, texel, Facing, (0f, 0f, 1f, 1f));

        TestContext.Out.WriteLine($"skinned {skinned}, the frame on +X {expected}, left on +Z {unturned}");

        Sum(Minus(expected, unturned)).ShouldBeGreaterThan(12, "the control: the two frames draw differently");
        Near(skinned, expected, 2, "the bone turns the tangent with the quad");
    }

    [Test]
    public void Draw_AVertexWithNoTangent_DrawsItsVertexNormal()
    {
        // w zero is "no frame" — brushwork, and a .vvd with no tangent array (studio.h:1969). The direction alone
        // must not count: a tangent with w zero draws what no tangent draws.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject() is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the game is not installed");
            return;
        }

        (MapAssets assets, int material, (float U, float V) texel, _) = subject;

        Draw(target, assets, material, texel, Facing, (1f, 0f, 0f, 0f))
            .ShouldBe(Draw(target, assets, material, texel, Facing, default));
    }

    /// <summary>The scout's body, and a texel of its normal map whose decoded normal leans well off the surface.</summary>
    private static (MapAssets Assets, int Material, (float U, float V) Texel, (float X, float Y, float Z) Bumped)? Subject()
    {
        if (GameInstall.Root is null)
        {
            return null;
        }

        MapAssets assets = MapCache.Load(entityModels: ["models/player/scout.mdl"]);
        int material = Enumerable.Range(0, assets.Materials.Count).FirstOrDefault(
            index => assets.Materials[index].Name.EndsWith("scout/scout_red", StringComparison.OrdinalIgnoreCase), -1);

        material.ShouldBeGreaterThanOrEqualTo(0, "the scout names scout_red");
        assets.Phong[material]!.Value.MaskedByBaseAlpha.ShouldBeFalse("the control: the normal map is not flattened");

        MapBump bump = assets.Bumps[material]!.Value;
        bump.IsSelfShadowing.ShouldBeFalse();

        MapTexture map = bump.Texture;
        byte[] rgba = map.Image.ToRgba(map.Width, map.Height).ToArray();

        // The texel leaning furthest along BOTH tangent axes, so T and the binormal's sign each move the answer.
        (float X, float Y, float Z) Decoded(int texel) => (
            (rgba[texel * 4] / 255f * 2f) - 1f,
            (rgba[(texel * 4) + 1] / 255f * 2f) - 1f,
            (rgba[(texel * 4) + 2] / 255f * 2f) - 1f);

        int best = Enumerable.Range(0, rgba.Length / 4)
            .Where(texel => Decoded(texel).Z > 0.3f)
            .MaxBy(texel => MathF.Min(MathF.Abs(Decoded(texel).X), MathF.Abs(Decoded(texel).Y)));

        (float X, float Y, float Z) leaning = Decoded(best);
        MathF.Min(MathF.Abs(leaning.X), MathF.Abs(leaning.Y)).ShouldBeGreaterThan(0.15f, "a texel that leans both ways");

        return (assets, material,
            (((best % map.Width) + 0.5f) / map.Width, ((best / map.Width) + 0.5f) / map.Height),
            leaning);
    }

    /// <summary><c>normalize( t.x T + t.y (N × T) w + t.z N )</c>.</summary>
    private static (float X, float Y, float Z) Frame(
        (float X, float Y, float Z) t, (float X, float Y, float Z) n, (float X, float Y, float Z) tangent, float w)
    {
        (float X, float Y, float Z) b = (
            ((n.Y * tangent.Z) - (n.Z * tangent.Y)) * w,
            ((n.Z * tangent.X) - (n.X * tangent.Z)) * w,
            ((n.X * tangent.Y) - (n.Y * tangent.X)) * w);

        return Unit((
            (t.X * tangent.X) + (t.Y * b.X) + (t.Z * n.X),
            (t.X * tangent.Y) + (t.Y * b.Y) + (t.Z * n.Y),
            (t.X * tangent.Z) + (t.Y * b.Z) + (t.Z * n.Z)));
    }

    /// <summary>A quad facing the eye, every corner on one texel; the centre pixel in linear 1/255.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float U, float V) texel,
        (float X, float Y, float Z) normal,
        (float X, float Y, float Z, float W) tangent,
        IReadOnlyList<float[]>? bones = null)
    {
        WorldVertex Corner(float x, float z)
        {
            // Skinned, the corner is stored where the bone's inverse puts it: R⁻¹(x, 0, z) = (−z, 0, x).
            (float X, float Z) at = bones is null ? (x, z) : (-z, x);

            return new(at.X, 0f, at.Z, texel.U, texel.V, 0f, 0f, 0f)
            {
                NormalX = normal.X,
                NormalY = normal.Y,
                NormalZ = normal.Z,
                TangentX = tangent.X,
                TangentY = tangent.Y,
                TangentZ = tangent.Z,
                TangentW = tangent.W,
                WeightA = bones is null ? 0f : 1f,
            };
        }

        List<WorldVertex> vertices =
        [
            Corner(-64f, -64f), Corner(64f, -64f), Corner(64f, 64f),
            Corner(-64f, -64f), Corner(64f, 64f), Corner(-64f, 64f),
        ];

        float[] identity =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(material, 0, vertices.Count)],
            camera,
            identity,
            assets,
            light: Dim,
            bothSides: true,
            sun: Sun,
            bones: bones);

        (int r, int g, int b) = target.PixelAt(Centre, Centre);

        static int Linear(int stored) => (int)Math.Round(SrgbTarget.Decode(stored) * 255.0);

        return (Linear(r), Linear(g), Linear(b));
    }

    private static SunLight Light((float X, float Y, float Z) travels)
    {
        (float X, float Y, float Z) unit = Unit(travels);

        return new SunLight(1f, 1f, 1f, unit.X, unit.Y, unit.Z);
    }

    private static (float X, float Y, float Z) Unit((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        return (v.X / length, v.Y / length, v.Z / length);
    }

    private static (int R, int G, int B) Minus((int R, int G, int B) a, (int R, int G, int B) b) =>
        (Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B));

    private static int Sum((int R, int G, int B) pixel) => pixel.R + pixel.G + pixel.B;

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, int within, string because)
    {
        actual.R.ShouldBeInRange(expected.R - within, expected.R + within, because);
        actual.G.ShouldBeInRange(expected.G - within, expected.G + within, because);
        actual.B.ShouldBeInRange(expected.B - within, expected.B + within, because);
    }
}
