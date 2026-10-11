using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A <c>CSpriteTrail</c> as a recording networks it, stepped tick by tick and drawn by the real renderer (B475).</summary>
/// <remarks>
/// **The output-level half of `EntityTrailsConformanceTests`**, through the chain the viewer runs after the timeline:
/// <see cref="EntityTrails.Build"/> each tick so the ring fills as it does in playback, the trail's material as the map
/// load reads it from the install, and <see cref="OffscreenTarget.DrawSprites"/>.
///
/// **The trail is written down, not read from a recording** (D213): z1800's entity 806, a Rescue Ranger bolt's
/// <c>effects/repair_claw_trail_blue</c>, and the bolt it is attached to, entity 794, as `moment-dump z1800 4326 806 794`
/// and `… 4345 …` printed them on 2026-10-11. Between those two ticks the bolt goes from (984, −17, 68) to
/// (588, −13.546875, 71.75) — 20.84 units a tick — and here it flies that line at that speed for twenty ticks. The
/// camera is placed from the data (`docs/memory/point-the-camera-from-the-data.md`): level, 100 units to the side of
/// the middle of the last eight ticks of flight and facing it, so the five-unit ribbon crosses the picture's centre
/// column and the corners see only the cleared black.
/// </remarks>
public sealed class EntityTrailRenderTests
{
    private const int TrailEntity = 806;

    private const int Bolt = 794;

    /// <summary>The trail's <c>m_hAttachedToEntity</c> as networked: entity 794 under its serial number.</summary>
    private const int BoltHandle = 1530650;

    private const int FirstTick = 4326;

    private const int Ticks = 20;

    private const float IntervalPerTick = 0.015f;

    private const int Size = 128;

    private const float StandOff = 100f;

    private const int SlotMask = (1 << 11) - 1;

    private static readonly Vector3 Launched = new(984f, -17f, 68f);

    /// <summary>The bolt's travel in one tick: (588, −13.546875, 71.75) less <see cref="Launched"/>, over nineteen.</summary>
    private static readonly Vector3 PerTick = (new Vector3(588f, -13.546875f, 71.75f) - Launched) / 19f;

    /// <remarks>
    /// **The ribbon crosses the centre column; nothing reaches the corners.** The bolt moves about twenty units a tick and
    /// its trail lives 0.3 seconds — twenty ticks — so the strip runs the width of a 200-unit view; at five units wide it
    /// is three or four pixels tall where it crosses the middle.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Render_ARescueRangerBoltsTrail_CrossesThePictureAndNotTheCorners()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        MapAssets assets = MapCache.Effects();

        SceneProp trail = new(
            EntityIndex: TrailEntity,
            ModelPath: "effects/repair_claw_trail_blue.vmt",
            Kind: SceneModelKind.Sprite,
            Pose: new ScenePose
            {
                RenderMode = RenderModes.TransAlpha,
                RenderAlpha = 255,
                RenderColor = ((byte)255, (byte)255, (byte)255),
                SpriteTrail = new SceneSpriteTrail(
                    LifeTime: 0.3f,
                    StartWidth: 5f,
                    EndWidth: -1f,
                    StartWidthVariance: 0f,
                    TextureRes: 0.010416667f,
                    MinFadeLength: 0f,
                    SkyboxOrigin: (0f, 0f, 0f),
                    SkyboxScale: 1f,
                    AttachedTo: BoltHandle,
                    Attachment: 0),
            },
            ClassName: "CSpriteTrail");

        EntityTrails trails = new();
        List<Vector3> heads = [];
        IReadOnlyList<ParticleBatch> batches = [];
        Vector3 camera = Vector3.Zero;

        for (int tick = FirstTick; tick < FirstTick + Ticks; tick++)
        {
            Vector3 at = Launched + (PerTick * (tick - FirstTick));

            List<SceneProp> props =
            [
                trail,
                new SceneProp(
                    EntityIndex: Bolt,
                    ModelPath: "models/weapons/w_models/w_repair_claw.mdl",
                    Kind: SceneModelKind.Studio,
                    Pose: new ScenePose { X = at.X, Y = at.Y, Z = at.Z, Pitch = 360f, Yaw = 177.14285f },
                    ClassName: "CTFProjectile_Arrow"),
            ];

            Vector3? head = Head(trail, props);
            heads.Add(head.ShouldNotBeNull("the trail's handle resolves to the bolt through its slot bits"));

            if (tick == FirstTick + Ticks - 1)
            {
                camera = Camera(heads[^1], heads[^9]);
            }

            batches = trails.Build(
                props, camera, assets.SpriteMaterials, prop => Head(prop, props), tick * IntervalPerTick);
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

        // Measured 142 on 2026-10-04 (B475) on the recording itself; half of it is the floor.
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
