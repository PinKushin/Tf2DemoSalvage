using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>koth_lakeside_final's sun, through the real query, the real material and the real renderer (B378).</summary>
/// <remarks>
/// **The output-level half of `SunGlowConformanceTests`.** Those prove the overlay's arithmetic; this runs the chain the
/// viewer runs for a frame — <see cref="PixelVisibility"/> asked by <see cref="SunGlow"/>, the proxies counted against the
/// drawn depth by the device, the next frame's quads drawn on <c>sprites/light_glow02_add_noz</c> as the install ships
/// it — and reads the pixels.
///
/// **The sun is written down, not read from a recording** (D213): entity 189 of
/// `tf2-2026-pov-koth_lakeside-triggers.dem` as `entity-census` decoded it (2026-10-14) — direction
/// (-0.469, -0.671, 0.574), rendercolor 228 202 186, sizes 32. That the timeline produces it from those bytes is the
/// corpus suite's to say.
///
/// **The control is a wall.** <c>light_glow02_add_noz</c> is <c>$ignorez</c>, so no depth test can hide the sun's quads:
/// only the occlusion query can. With a wall across the sun the query counts nothing and nothing is drawn; without it
/// the centre is lit. A pass that drew the sun ungated would light the walled centre too.
/// </remarks>
public sealed class SunGlowRenderTests
{
    private const int Size = 64;

    private const string Noz = "sprites/light_glow02_add_noz.vmt";

    private static readonly Vector3 Direction = Vector3.Normalize(new Vector3(-0.469f, -0.671f, 0.574f));

    private static readonly SceneSun Lakeside = new(
        189, (Direction.X, Direction.Y, Direction.Z), true, 32, 32, (228, 202, 186), (0, 0, 0), Noz, Noz, 1f);

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Render_LakesidesSunInOpenSky_LightsTheCentre()
    {
        (int red, int green, int blue) = Rendered(walled: false, out int drawn);

        drawn.ShouldBe(2, "the core and the glow");

        // light_glow02's centre under the core's (1, 0.886, 0.816) plus the glow's 0.75 of it, added: near white,
        // and warmer red than blue as the renormalized colour is.
        red.ShouldBeGreaterThan(128, $"centre ({red}, {green}, {blue})");
        red.ShouldBeGreaterThanOrEqualTo(blue);
    }

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Render_LakesidesSunBehindAWall_DrawsNothing()
    {
        (int red, int green, int _) = Rendered(walled: true, out int drawn);

        drawn.ShouldBe(0, "the query counted none of the proxy, so the obstruction is zero");
        red.ShouldBeLessThan(8, "only the green wall at the centre");
        green.ShouldBeGreaterThan(64, "the instrument: the wall is there");
    }

    /// <summary>Three frames of the viewer's order: ask, draw, count; the picture is the last frame's.</summary>
    private static (int Red, int Green, int Blue) Rendered(bool walled, out int drawn)
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        MapAssets assets = MapCache.Effects();

        assets.SpriteMaterials.ContainsKey(Noz).ShouldBeTrue("light_glow02_add_noz ships with TF2");

        FreeCamera camera = new()
        {
            Origin = (0f, 0f, 0f),
            Angles = (
                -float.RadiansToDegrees(System.MathF.Asin(Direction.Z)),
                float.RadiansToDegrees(System.MathF.Atan2(Direction.Y, Direction.X)),
                0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        float[] matrix = camera.ToMatrix();
        PixelVisibility visibility = new();
        SunGlow sun = new();
        IReadOnlyDictionary<int, (int Pixels, int Possible)> counts = new Dictionary<int, (int, int)>();
        IReadOnlyList<ParticleBatch> batches = [];

        visibility.LevelInit(doPixelVisibility: true);

        for (int frame = 0; frame < 3; frame++)
        {
            IReadOnlyDictionary<int, (int Pixels, int Possible)> last = counts;

            visibility.BeginFrame(1f, key => last.TryGetValue(key, out (int, int) counted) ? counted : null);
            batches = sun.Build(
                [Lakeside], Vector3.Zero, Direction, camera.FarZ, assets.SpriteMaterials, query => visibility.FractionVisible(query));

            (List<WorldVertex> wall, WorldBatch batch) = Wall(walled);

            target.Clear(0f, 0f, 0f);
            target.DrawWorld(wall, [batch], matrix, assets, surfaceColours: true, translucent: false);

            foreach (ParticleBatch drawnBatch in batches)
            {
                target.DrawSprites(drawnBatch, matrix);
            }

            counts = target.CountOcclusion(visibility.EndView(PixelVisibilityView.From(camera, Size)), matrix);
        }

        drawn = batches.Count;

        return target.PixelAt(Size / 2, Size / 2);
    }

    /// <summary>A green square 500 units out across the sun, or the same square turned away behind the eye.</summary>
    private static (List<WorldVertex> Vertices, WorldBatch Batch) Wall(bool across)
    {
        Vector3 toward = across ? Direction : -Direction;
        Vector3 right = Vector3.Normalize(Vector3.Cross(toward, Vector3.UnitZ)) * 400f;
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, toward)) * 400f;
        Vector3 centre = toward * 500f;

        WorldVertex At(Vector3 point, float u, float v) => new(point.X, point.Y, point.Z, u, v, 0f, 0f, 0f, 0f, 1f, 0f);

        List<WorldVertex> vertices =
        [
            At(centre - right - up, 0f, 0f),
            At(centre + right + up, 1f, 1f),
            At(centre + right - up, 1f, 0f),
            At(centre - right - up, 0f, 0f),
            At(centre - right + up, 0f, 1f),
            At(centre + right + up, 1f, 1f),
        ];

        return (vertices, new WorldBatch(0, 0, vertices.Count));
    }
}
