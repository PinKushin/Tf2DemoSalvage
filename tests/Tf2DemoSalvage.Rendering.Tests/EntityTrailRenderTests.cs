using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A real <c>CSpriteTrail</c>, sampled tick by tick out of a real demo and drawn by the real renderer (B474).</summary>
/// <remarks>
/// **The output-level half of `EntityTrailsConformanceTests`**, through the chain the viewer runs:
/// <see cref="DemoTimeline.PropsAt"/> each tick, <see cref="EntityTrails.Build"/> each tick so the ring fills as it does in
/// playback, the sprite materials <see cref="DemoModels.Sprites"/> asks the map load for, and
/// <see cref="OffscreenTarget.DrawSprites"/>.
///
/// **The specimen is z1800's entity 806**, a Rescue Ranger bolt's <c>effects/repair_claw_trail_blue</c> that flies 1,583
/// units between ticks 4326 and 4378 with its projectile present throughout (`trails` probe, 2026-10-04). After twenty
/// ticks the camera is placed from the data (`docs/memory/point-the-camera-from-the-data.md`): level, 100 units to the
/// side of the middle of the last eight ticks of flight and facing it, so the five-unit ribbon crosses the picture's
/// centre column and the corners see only the cleared black.
/// </remarks>
public sealed class EntityTrailRenderTests
{
    private const string DemoName = "z1800.dem";

    private const int TrailEntity = 806;

    private const int FirstTick = 4326;

    private const int Ticks = 20;

    private const int Size = 128;

    private const float StandOff = 100f;

    private const int SlotMask = (1 << 11) - 1;

    /// <remarks>
    /// **The ribbon crosses the centre column; nothing reaches the corners.** The bolt moves about thirty units a tick and
    /// its trail lives 0.3 seconds, so the strip runs the width of a 200-unit view; at five units wide it is three or four
    /// pixels tall where it crosses the middle.
    /// </remarks>
    [Test]
    public void Render_ARescueRangerBoltsTrailFromARealDemo_CrossesThePictureAndNotTheCorners()
    {
        string demo = CommittedDemo.Require(DemoName);
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_harvest_final.bsp"));

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        MapAssets assets = MapAssets.Load(
            map, GameArchives.Open(tf), maximumTextureSize: 64, spriteMaterials: DemoModels.Sprites(timeline));

        EntityTrails trails = new();
        List<SceneProp> props = [];
        List<Vector3> heads = [];
        IReadOnlyList<ParticleBatch> batches = [];
        Vector3 camera = Vector3.Zero;

        for (int tick = FirstTick; tick < FirstTick + Ticks; tick++)
        {
            timeline.PropsAt(tick, props);

            SceneProp trail = props.Single(prop => prop.EntityIndex == TrailEntity);
            trail.Pose.SpriteTrail.ShouldNotBeNull("the specimen must still be the trail this test was written against");

            Vector3? head = Head(trail, props);
            heads.Add(head.ShouldNotBeNull("the bolt is present for every tick of this window"));

            if (tick == FirstTick + Ticks - 1)
            {
                camera = Camera(heads[^1], heads[^9]);
            }

            batches = trails.Build(
                props, camera, assets.SpriteMaterials, prop => Head(prop, props), tick * timeline.IntervalPerTick);
        }

        trails.Drawn.ShouldBeGreaterThanOrEqualTo(1, "the trail must be drawn for the picture to mean anything");

        Vector3 middle = (heads[^1] + heads[^9]) / 2f;
        Vector3 toward = Vector3.Normalize(middle - camera);

        FreeCamera view = new()
        {
            Origin = (camera.X, camera.Y, camera.Z),
            Angles = (0f, float.RadiansToDegrees(MathF.Atan2(toward.Y, toward.X)), 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        StripPicture.Draw(target, view.ToMatrix(), assets, batches);

        int brightest = Enumerable.Range(0, Size)
            .Select(row => target.PixelAt(Size / 2, row))
            .Max(pixel => pixel.Red + pixel.Green + pixel.Blue);

        // Measured 142 on 2026-10-04 (B474); half of it is the floor.
        brightest.ShouldBeGreaterThan(71, "the ribbon crosses the centre column");

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(1, 1), (Size - 2, 1), (1, Size - 2), (Size - 2, Size - 2)])
        {
            target.PixelAt(x, y).ShouldBe((0, 0, 0), $"corner ({x}, {y}) is clear of the ribbon");
        }
    }

    /// <summary>
    /// <c>GetRenderOrigin</c> as the viewer answers it for attachment zero: the attached entity's origin through its
    /// parents, or null when that entity is not in the moment.
    /// </summary>
    private static Vector3? Head(SceneProp trail, List<SceneProp> props)
    {
        if (trail.Pose.SpriteTrail is not { } parameters ||
            props.FirstOrDefault(prop => prop.EntityIndex == (parameters.AttachedTo & SlotMask)) is not { } attached)
        {
            return null;
        }

        ScenePose placed = ParentChain.Absolute(attached, entity => props.FirstOrDefault(prop => prop.EntityIndex == entity));

        return new Vector3(placed.X, placed.Y, placed.Z);
    }

    /// <summary>Level, <see cref="StandOff"/> to the side of the middle of a stretch of flight.</summary>
    private static Vector3 Camera(Vector3 head, Vector3 earlier)
    {
        Vector3 along = Vector3.Normalize(head - earlier);
        Vector3 side = Vector3.Normalize(Vector3.Cross(along, Vector3.UnitZ));

        return ((head + earlier) / 2f) + (side * StandOff);
    }
}
