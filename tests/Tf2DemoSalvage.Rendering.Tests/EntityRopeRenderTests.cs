using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A real <c>C_RopeKeyframe</c>, hung and drawn out of a real demo by the real renderer (B477).</summary>
/// <remarks>
/// **The output-level half of `EntityRopesConformanceTests`**, through the chain the viewer runs: <see cref="DemoTimeline"/>
/// admitting the rope by its material, <see cref="DemoModels.Sprites"/> asking the map load for <c>cable/cable</c> and its
/// <c>_back</c>, <see cref="EntityRopes.Build"/> hanging it, and <see cref="OffscreenTarget.DrawSprites"/>.
///
/// **The specimen is the committed 2011 <c>koth_viaduct</c> SourceTV demo's first simulated rope**: 35 rope keyframes,
/// 27 of them <c>ROPE_SIMULATE</c> (`entity-census`). **TF2's cable is black** — <c>cable/cable</c> is the <c>Cable</c>
/// shader over a one-texel black base — so the picture is cleared to grey and the rope must DARKEN it. The camera is
/// placed from the data: level, 20 units to the side of the hung rope's middle strip point, facing it, so the two-unit
/// cable crosses the centre and the corners see only grey.
/// </remarks>
public sealed class EntityRopeRenderTests
{
    private const string DemoName = "tf2-2011-build4604-stv-koth_viaduct.dem";

    private const int Size = 64;

    private const float StandOff = 20f;

    private const int SlotMask = (1 << 11) - 1;

    private const float Grey = 0.5f;

    /// <summary>A shipped <c>Cable</c> material with a light base and the same normal map as <c>cable/cable</c>.</summary>
    private const string LightRope = "cable/rope.vmt";

    /// <remarks>
    /// **The cable darkens the centre and leaves the corners grey.** At 20 units with a 90° view the 2-unit cable is about
    /// three pixels thick where it crosses the middle.
    /// </remarks>
    [Test]
    public void Render_AViaductCableFromARealDemo_DarkensItsLineAndNotTheCorners()
    {
        (OffscreenTarget picture, MapAssets assets, EntityRopes ropes, List<SceneProp> scene, List<SceneProp> props,
            Vector3 eye, Vector3 toward, float[] matrix) = Setup(StandOff);

        using OffscreenTarget target = picture;

        IReadOnlyList<ParticleBatch> batches = Build(ropes, scene, props, new RopeView(eye, toward, Size), assets);

        ropes.Drawn.ShouldBeGreaterThanOrEqualTo(1, "the rope must be built for the picture to mean anything");

        StripPicture.Draw(target, matrix, assets, batches, background: Grey);

        int darkest = Enumerable.Range(0, Size)
            .Select(row => target.PixelAt(Size / 2, row))
            .Min(pixel => pixel.Red + pixel.Green + pixel.Blue);

        // Measured 0 on 2026-10-04 against a grey of 381 (B477): the opaque black cable over its translucent back pass.
        darkest.ShouldBeLessThan(64, "the cable crosses the centre column");
        batches.Select(batch => batch.Material.Blend).ShouldBe([SpriteBlend.Translucent, SpriteBlend.Opaque]);

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(1, 1), (Size - 2, 1), (1, Size - 2), (Size - 2, Size - 2)])
        {
            (int red, int _, int _) = target.PixelAt(x, y);
            red.ShouldBeGreaterThan(100, $"corner ({x}, {y}) is clear of the cable");
        }
    }

    /// <remarks>
    /// **The back pass is darkened by <c>cable/cablenormalmap</c>** (`cable_ps2x.fxc:40-49`, B478): its blue squared, which
    /// is at most one. Eight units from the cable — one past the near plane — the strip is eight pixels down the centre
    /// column. The control is the same batch without its normal map, drawn by the same renderer. **What was expected and
    /// not seen**: B478 filed the term as shading the strip "like a cylinder"; at this size the edge-to-middle ratio is the
    /// same with and without it, so the measured effect is an overall darkening, not a profile.
    /// </remarks>
    [Test]
    public void Render_AViaductCablesBackPass_IsDarkenedByItsNormalMap()
    {
        (OffscreenTarget target, MapAssets assets, EntityRopes ropes, List<SceneProp> scene, List<SceneProp> props,
            Vector3 eye, Vector3 toward, float[] matrix) = Setup(standOff: 8f);

        using (target)
        {
            ParticleBatch back = Build(ropes, scene, props, new RopeView(eye, toward, Size), assets)
                .Single(batch => batch.Material.Blend == SpriteBlend.Translucent);

            back.Material.CableBump.ShouldNotBeNull("cable_back names cable\\cablenormalmap");

            // `cable/rope` — the same normal map over a light texture — so the shading is resolvable in eight bits;
            // `cable_back`'s own base is nearly black.
            EngineSprite rope = assets.SpriteMaterials[LightRope];
            ParticleBatch lit = back with { Material = rope.Material with { Blend = SpriteBlend.Translucent } };

            (int bumpedTotal, double bumped) = Column(target, matrix, assets, lit);
            (int flatTotal, double flat) = Column(target, matrix, assets, lit with { Material = lit.Material with { CableBump = null } });

            // Measured 2026-10-06: 1760 against 2056 down the centre column, the edge-to-middle ratio 1 either way.
            bumpedTotal.ShouldBeLessThan(flatTotal * 9 / 10, "b² is at most one, and below it over the map");
            bumped.ShouldBe(flat, 0.05, "no across-the-width profile is resolved at eight pixels");
        }
    }

    /// <remarks>
    /// **At Christmas every strip point of a drawn rope carries a bulb** (`BuildRope`, `c_rope.cpp:1709-1752`), made the frame
    /// after by <c>CreateHolidayLight</c> — the first one red, (255, 0, 0) — and drawn as <c>effects/christmas_bulb</c>
    /// (`tf_fx_christmaslights.cpp`). The local player stands at the camera; a red pixel must appear where the grey had
    /// none.
    /// </remarks>
    [Test]
    public void Render_AViaductCableAtChristmas_HangsColouredBulbs()
    {
        (OffscreenTarget target, MapAssets assets, EntityRopes ropes, List<SceneProp> scene, List<SceneProp> props,
            Vector3 eye, Vector3 toward, float[] matrix) = Setup(standOff: StandOff);

        using (target)
        {
            assets.SpriteMaterials.ShouldContainKey(RopeHolidayLights.BulbMaterial);

            Build(ropes, scene, props, new RopeView(eye, toward, Size), assets, holidayStyle: 0);

            RopeHolidayLights lights = new();

            foreach (RopeHolidayDispatch dispatch in ropes.HolidayLights)
            {
                lights.Add(dispatch, eye, new Vector3(0f, 0f, -100000f));
            }

            lights.Update(curtime: 0f, style: 0);

            lights.Lights.Count.ShouldBe(ropes.HolidayLights.Count);

            (Vector3 right, Vector3 up) = Axes(toward);
            StripPicture.Draw(target, matrix, assets, lights.Batches(assets.SpriteMaterials, toward, right, up), background: Grey);

            int red = 0;

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    (int r, int g, int b) = target.PixelAt(x, y);

                    if (r > g + 60 && r > b + 60)
                    {
                        red++;
                    }
                }
            }

            red.ShouldBeGreaterThan(0, "a red bulb on the grey");
        }
    }

    /// <summary>
    /// One batch drawn alone on black: the centre column's total, and its darker inner edge against its brightest pixel.
    /// </summary>
    private static (int Total, double EdgeToMiddle) Column(OffscreenTarget target, float[] matrix, MapAssets assets, ParticleBatch batch)
    {
        StripPicture.Draw(target, matrix, assets, [batch]);

        List<int> lit = [.. Enumerable.Range(0, Size)
            .Select(row => target.PixelAt(Size / 2, row))
            .Select(pixel => pixel.Red + pixel.Green + pixel.Blue)
            .Where(sum => sum > 0)];

        lit.Count.ShouldBeGreaterThan(4, "the strip covers the centre column");

        return (lit.Sum(), (double)Math.Min(lit[1], lit[^2]) / lit.Max());
    }

    /// <summary>The camera beside the first long simulated rope of the viaduct demo, as the first test places it.</summary>
    private static (OffscreenTarget Target, MapAssets Assets, EntityRopes Ropes, List<SceneProp> Scene, List<SceneProp> Props,
        Vector3 Eye, Vector3 Toward, float[] Matrix) Setup(float standOff)
    {
        string demo = CommittedDemo.Require(DemoName);
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_viaduct.bsp"));

        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        MapAssets assets = MapAssets.Load(
            map, GameArchives.Open(tf), maximumTextureSize: 64, spriteMaterials: [.. DemoModels.Sprites(timeline), LightRope]);

        List<SceneProp> props = [];
        timeline.PropsAt(timeline.Frames[timeline.Frames.Count / 2].Tick, props);

        // A long span: the first simulated rope, entity 94, is a 26-unit vertical drop from a pole to the cable, which
        // has no side to look at it from.
        SceneProp rope = props.First(prop =>
            prop.Pose.Rope is { } parameters && (parameters.Flags & EntityRopes.SimulateFlag) != 0 && parameters.Length >= 200);
        List<SceneProp> scene = [rope];

        // Hang it once from anywhere to find where it is — the back pass carries every strip point however far the
        // camera — then look at it from the side.
        EntityRopes ropes = new();
        List<DetailSpriteVertex> strip =
            [.. Build(ropes, scene, props, new RopeView(Vector3.Zero, Vector3.UnitX, Size), assets)[0].Corners];

        // The middle quad: its first two corners straddle a strip point, and its third is the next point's.
        int middle = strip.Count / 12 * 6;
        Vector3 point = (At(strip[middle]) + At(strip[middle + 1])) / 2f;
        Vector3 along = Vector3.Normalize(At(strip[middle + 2]) - At(strip[middle]));
        Vector3 side = Vector3.Normalize(Vector3.Cross(along, Vector3.UnitZ));
        Vector3 eye = point + (side * standOff);
        Vector3 toward = Vector3.Normalize(point - eye);

        FreeCamera camera = new()
        {
            Origin = (eye.X, eye.Y, eye.Z),
            Angles = (0f, float.RadiansToDegrees(MathF.Atan2(toward.Y, toward.X)), 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        return (target, assets, ropes, scene, props, eye, toward, camera.ToMatrix());
    }

    /// <summary>The view's right and up for a level forward.</summary>
    private static (Vector3 Right, Vector3 Up) Axes(Vector3 forward)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));

        return (right, Vector3.Cross(right, forward));
    }

    /// <summary>The viewer's rope pass, with each end resolved through the moment's props and a white light.</summary>
    private static IReadOnlyList<ParticleBatch> Build(
        EntityRopes ropes,
        List<SceneProp> scene,
        List<SceneProp> props,
        RopeView view,
        MapAssets assets,
        int? holidayStyle = null) =>
        ropes.Build(
            scene,
            view,
            assets.SpriteMaterials,
            (handle, _, _) => props.FirstOrDefault(prop => prop.EntityIndex == (handle & SlotMask)) is { } end
                ? new RopeEndPoint(Position(end, props), Vector3.UnitX)
                : null,
            _ => Vector3.One,
            (_, _) => new RopeHit(1f, Vector3.Zero, 0f, false),
            frameTime: 0f,
            holidayStyle: holidayStyle);

    private static Vector3 Position(SceneProp prop, List<SceneProp> props)
    {
        ScenePose placed = ParentChain.Absolute(prop, entity => props.FirstOrDefault(other => other.EntityIndex == entity));

        return new Vector3(placed.X, placed.Y, placed.Z);
    }

    private static Vector3 At(DetailSpriteVertex corner) => new(corner.X, corner.Y, corner.Z);
}
