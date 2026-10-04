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
