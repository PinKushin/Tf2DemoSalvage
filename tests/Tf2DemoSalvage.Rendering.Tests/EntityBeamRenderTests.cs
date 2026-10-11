using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A real <c>CBeam</c>, out of a real demo, drawn by the real renderer.</summary>
/// <remarks>
/// **The output-level half of the beam suites.** `BeamStateConformanceTests` prove the decode, `EntityBeamsConformanceTests`
/// the strip; neither can see whether the pieces are wired — whether a beam the timeline hands over reaches a batch with
/// a sheet the renderer can draw. This runs the whole chain the viewer runs: <see cref="DemoTimeline.Build"/>,
/// <see cref="DemoTimeline.PropsAt"/>, the sprite materials <see cref="DemoModels.Sprites"/> asks the map load for,
/// <see cref="EntityBeams.Build"/>, and <see cref="OffscreenTarget.DrawSprites"/>.
///
/// **The specimen is the committed 2011 <c>koth_viaduct</c> SourceTV demo**, whose 68 live <c>point_spotlight</c> beams at
/// mid-match are each a 100-unit vertical shaft of <c>sprites/glow_test02</c> (`effect-entities` probe, 2026-10-04). The
/// camera is placed from the data (`docs/memory/point-the-camera-from-the-data.md`): 200 units off the first beam's
/// middle, level, facing it, so the shaft stands across the middle of the picture and the corners see nothing but the
/// cleared black.
/// </remarks>
public sealed class EntityBeamRenderTests
{
    private const string DemoName = "tf2-2011-build4604-stv-koth_viaduct.dem";

    private const int Size = 64;

    private const float StandOff = 200f;

    /// <remarks>
    /// **The shaft lights the middle and nothing else.** At 200 units with a 90° view, the 102-unit beam spans the middle
    /// quarter of the picture across and its 100-unit length the middle quarter down, so the centre pixel is on the shaft
    /// and every corner is a hundred units clear of it.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Render_ASpotlightBeamFromARealDemo_LightsItsShaftAndNotTheCorners()
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

        SceneProp beam = props.First(prop => prop.Pose.Beam is not null);
        (float endX, float endY, float endZ) = beam.Pose.Beam.ShouldNotBeNull().EndPosition;
        Vector3 middle = new((beam.Pose.X + endX) / 2f, (beam.Pose.Y + endY) / 2f, (beam.Pose.Z + endZ) / 2f);

        FreeCamera camera = new()
        {
            Origin = (middle.X + StandOff, middle.Y, middle.Z),
            Angles = (0f, 180f, 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        IReadOnlyList<ParticleBatch> batches = Build(beam, camera, assets.SpriteMaterials, out int drawn);

        drawn.ShouldBe(1, "the beam must be built for the picture to mean anything");

        StripPicture.Draw(target, camera.ToMatrix(), assets, batches);

        (int red, int green, int blue) = target.PixelAt(Size / 2, Size / 2);

        // Measured (25, 24, 24) on 2026-10-04 — a spotlight shaft is faint by design — and (1, 0, 0) with the strip's
        // half-width sabotaged to zero. Half the healthy sum is the floor.
        (red + green + blue).ShouldBeGreaterThan(36, $"the shaft's middle, measured ({red}, {green}, {blue})");

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(1, 1), (Size - 2, 1), (1, Size - 2), (Size - 2, Size - 2)])
        {
            target.PixelAt(x, y).ShouldBe((0, 0, 0), $"corner ({x}, {y}) is a hundred units clear of the shaft");
        }
    }

    /// <remarks>
    /// **The lamp over cp_process_f12's indoor pipes, from the camera B521 was measured at, on the materials the game
    /// ships.** Entity 676 of `demostf-cp_process_f12-2026-08-08-2207` at tick 60000 as the `beam-view` probe printed it
    /// (*measured*, 2026-10-11) — the wire values are written out here because that demo is not committed; the two
    /// materials are read from the install, which is the half that was wrong.
    ///
    /// **What the engine multiplies the shaft's texture by** — *read from published source*, `view_beams.cpp:1777-1865`
    /// and `beamdraw.cpp:312-374`, through a Sprite material whose <c>$ignorevertexcolors</c> nobody set and is
    /// therefore zero (`SpriteMaterialConformanceTests`):
    ///
    /// <code>
    /// color      = rendercolor / 255 * brightness / 255        = 64 / 255          = 0.25098
    /// distToLine = eye to the beam's axis                      = 190.805
    /// dotScale   = RemapVal( 190.805, 4w, w, 1, 0 ), w 64.037  = 0.65986
    /// lamp end   = color * dotScale * ( 1 - 0 / fadeFraction ) = 0.16561
    /// far end    = color * dotScale * ( 1 - 1 / 1 )            = 0
    /// </code>
    ///
    /// by arithmetic done outside this project (awk, from the probe's wire values). Before B521 every corner carried
    /// (1, 1, 1): the shaft drew six times too bright at the lamp and never faded, which is the near-opaque white cone.
    ///
    /// **The halo is untouched and is the control**: <c>kRenderGlow</c> always took the vertex colour, the source colour
    /// at full strength (fade <c>2 · 0.5644</c>, squared and clamped to one), sized
    /// <c>RemapVal( 190.805, 4w, w / 2, 1, 2 ) · 60 = 77.49</c> units either side of the lamp.
    /// </remarks>
    [Test]
    public void Build_ProcessLamp676OnItsShippedMaterials_ShadesTheShaftAndKeepsTheHalo()
    {
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_viaduct.bsp"));

        MapAssets assets = MapAssets.Load(
            map, GameArchives.Open(tf), maximumTextureSize: 64, spriteMaterials: [LampShaft, LampHalo]);

        FreeCamera camera = new()
        {
            Origin = (146f, -1831.6f, 728f),
            Angles = (-28.7f, -36.5f, 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        IReadOnlyList<ParticleBatch> batches = Build(Lamp676(), camera, assets.SpriteMaterials, out int drawn);

        drawn.ShouldBe(1);

        List<DetailSpriteVertex> shaft = [.. batches.Single(batch => batch.Material.Depth != SpriteDepth.Off).Corners];

        shaft.Count.ShouldBe(6);

        foreach (DetailSpriteVertex corner in shaft)
        {
            // A strip's vertex colour is four BYTES (`BeamSegDraw`): 0.16561 · 255 = 42.23, stored as 42.
            float expected = corner.Z > 800f ? 42f / 255f : 0f;

            corner.Red.ShouldBe(expected, 0.00001f, $"the corner at height {corner.Z}");
            corner.Green.ShouldBe(expected, 0.00001f);
            corner.Blue.ShouldBe(expected, 0.00001f);
            corner.Alpha.ShouldBe(1f);
        }

        ParticleBatch halo = batches.Single(batch => batch.Material.Depth == SpriteDepth.Off);

        halo.Corners.Select(corner => (corner.Red, corner.Green, corner.Blue)).Distinct().ShouldBe([(1f, 1f, 1f)]);

        Vector3 lamp = new(283.03125f, -1976.5352f, 844.7832f);

        // A square of half-side s about the lamp has its corners s·√2 away.
        foreach (DetailSpriteVertex corner in halo.Corners)
        {
            Vector3.Distance(new Vector3(corner.X, corner.Y, corner.Z), lamp).ShouldBe(77.4928f * MathF.Sqrt(2f), 0.01f);
        }
    }

    private const string LampShaft = "sprites/glow_test02.vmt";

    private const string LampHalo = "sprites/light_glow03.vmt";

    /// <summary><c>INVALID_NETWORKED_EHANDLE_VALUE</c>.</summary>
    private const int NoHandle = (1 << 21) - 1;

    /// <summary>cp_process_f12's <c>point_spotlight</c> at (283, −1976.55, 844.77), as its <c>CBeam</c> is networked.</summary>
    private static SceneProp Lamp676() => new(
        EntityIndex: 676,
        ModelPath: LampShaft,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose
        {
            X = 283.03125f,
            Y = -1976.5352f,
            Z = 844.7832f,
            RenderMode = RenderModes.TransTexture,
            RenderAlpha = 64,
            RenderColor = ((byte)255, (byte)255, (byte)255),
            Beam = new SceneBeam(
                Type: EntityBeams.BeamPoints,
                Flags: BeamDraw.ShadeOutFlag | BeamDraw.NoTileFlag,
                EntityCount: 2,
                Ends: [.. Enumerable.Repeat((NoHandle, 0), SceneBeam.MaximumEnds)],
                HaloIndex: 1,
                HaloScale: 60f,
                Width: 64.03741f,
                EndWidth: 30.27041f,
                FadeLength: 189.19966f,
                Amplitude: 0f,
                StartFrame: 0f,
                Speed: 0f,
                FrameRate: 0f,
                HdrColourScale: 1f,
                Frame: 0f,
                EndPosition: (274.15625f, -1966.7188f, 656.03125f),
                MinDxLevel: 90) { HaloPath = LampHalo },
        },
        ClassName: "CBeam");

    /// <summary>The viewer's beam pass for one prop, with a camera the halo gate may always see past.</summary>
    private static IReadOnlyList<ParticleBatch> Build(
        SceneProp beam, FreeCamera camera, IReadOnlyDictionary<string, EngineSprite> sprites, out int drawn)
    {
        ((float X, float Y, float Z) forward, (float X, float Y, float Z) right, (float X, float Y, float Z) up) =
            camera.Basis();

        BeamView view = new(
            new Vector3(camera.Origin.X, camera.Origin.Y, camera.Origin.Z),
            new Vector3(forward.X, forward.Y, forward.Z),
            new Vector3(right.X, right.Y, right.Z),
            new Vector3(up.X, up.Y, up.Z));

        EntityBeams beams = new();

        IReadOnlyList<ParticleBatch> batches = beams.Build(
            [beam],
            view,
            sprites,
            prop => ParentChain.Absolute(prop, _ => null),
            (_, _, _) => null,
            _ => true,
            currentTime: 0f,
            frameTime: 0f);

        drawn = beams.Drawn;

        return batches;
    }
}
