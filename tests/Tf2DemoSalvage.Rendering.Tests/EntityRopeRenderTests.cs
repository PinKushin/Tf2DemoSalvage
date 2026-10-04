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

    /// <remarks>
    /// **The cable darkens the centre and leaves the corners grey.** At 20 units with a 90° view the 2-unit cable is about
    /// three pixels thick where it crosses the middle.
    /// </remarks>
    [Test]
    public void Render_AViaductCableFromARealDemo_DarkensItsLineAndNotTheCorners()
    {
        string demo = CommittedDemo.Require(DemoName);
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_viaduct.bsp"));

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        MapAssets assets = MapAssets.Load(
            map, GameArchives.Open(tf), maximumTextureSize: 64, spriteMaterials: DemoModels.Sprites(timeline));

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
        IReadOnlyList<ParticleBatch> hung = Build(ropes, scene, props, new RopeView(Vector3.Zero, Vector3.UnitX, Size), assets);
        List<DetailSpriteVertex> strip = [.. hung[0].Corners];

        // The middle quad: its first two corners straddle a strip point, and its third is the next point's.
        int middle = strip.Count / 12 * 6;
        Vector3 point = (At(strip[middle]) + At(strip[middle + 1])) / 2f;
        Vector3 along = Vector3.Normalize(At(strip[middle + 2]) - At(strip[middle]));
        Vector3 side = Vector3.Normalize(Vector3.Cross(along, Vector3.UnitZ));
        Vector3 eye = point + (side * StandOff);
        Vector3 toward = Vector3.Normalize(point - eye);

        FreeCamera camera = new()
        {
            Origin = (eye.X, eye.Y, eye.Z),
            Angles = (0f, float.RadiansToDegrees(MathF.Atan2(toward.Y, toward.X)), 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        IReadOnlyList<ParticleBatch> batches = Build(ropes, scene, props, new RopeView(eye, toward, Size), assets);

        ropes.Drawn.ShouldBeGreaterThanOrEqualTo(1, "the rope must be built for the picture to mean anything");

        StripPicture.Draw(target, camera.ToMatrix(), assets, batches, background: Grey);

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

    /// <summary>The viewer's rope pass, with each end resolved through the moment's props and a white light.</summary>
    private static IReadOnlyList<ParticleBatch> Build(
        EntityRopes ropes, List<SceneProp> scene, List<SceneProp> props, RopeView view, MapAssets assets) =>
        ropes.Build(
            scene,
            view,
            assets.SpriteMaterials,
            (handle, _, _) => props.FirstOrDefault(prop => prop.EntityIndex == (handle & SlotMask)) is { } end
                ? new RopeEndPoint(Position(end, props), Vector3.UnitX)
                : null,
            _ => Vector3.One,
            (_, _) => new RopeHit(1f, Vector3.Zero, 0f, false),
            frameTime: 0f);

    private static Vector3 Position(SceneProp prop, List<SceneProp> props)
    {
        ScenePose placed = ParentChain.Absolute(prop, entity => props.FirstOrDefault(other => other.EntityIndex == entity));

        return new Vector3(placed.X, placed.Y, placed.Z);
    }

    private static Vector3 At(DetailSpriteVertex corner) => new(corner.X, corner.Y, corner.Z);
}
