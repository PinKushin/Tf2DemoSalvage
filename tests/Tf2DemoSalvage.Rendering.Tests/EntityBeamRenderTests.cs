using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A <c>CBeam</c> as a recording networks it, on the materials the game ships, drawn by the real renderer.</summary>
/// <remarks>
/// **The output-level half of the beam suites.** `BeamStateConformanceTests` prove the decode, `EntityBeamsConformanceTests`
/// the strip; neither can see whether the pieces are wired — whether a beam reaches a batch with a sheet the renderer
/// can draw. This runs the chain the viewer runs after the timeline: the sprite materials the map load reads from the
/// install, <see cref="EntityBeams.Build"/>, and <see cref="OffscreenTarget.DrawSprites"/>.
///
/// **The beams are written down, not read from a recording** (D213). The first is entity 233 of the committed 2011
/// <c>koth_viaduct</c> SourceTV demo at tick 7879 — one of its 68 <c>point_spotlight</c> shafts, a 100-unit vertical
/// strip of <c>sprites/glow_test02</c> — as `moment-dump` printed it on 2026-10-11. That the timeline produces such a
/// prop from the wire is `BeamStateConformanceTests`' and the corpus suite's to say.
/// </remarks>
public sealed class EntityBeamRenderTests
{
    private const int Size = 64;

    private const float StandOff = 200f;

    /// <remarks>
    /// **The shaft lights the middle and nothing else.** At 200 units with a 90° view, the 102-unit beam spans the middle
    /// quarter of the picture across and its 100-unit length the middle quarter down, so the centre pixel is on the shaft
    /// and every corner is a hundred units clear of it.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Render_AViaductSpotlightBeam_LightsItsShaftAndNotTheCorners()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        MapAssets assets = MapCache.Effects();

        SceneProp beam = Spotlight(
            entity: 233,
            start: new Vector3(-2351.9736f, -1983.9727f, 400.03125f),
            end: new Vector3(-2352f, -1984f, 300f),
            width: 102.3f,
            endWidth: 102.3f,
            fadeLength: 100f,
            colour: (230, 227, 200));
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

        // A spotlight shaft is faint by design, and fainter than this test first recorded. The engine's arithmetic for
        // the middle of this shaft from 200 units: colour (230, 227, 200) / 255, times brightness 64 / 255, times
        // dotScale RemapVal( 200, 4w, w, 1, 0 ) = 0.318 at w 102.3, times the shade-out's 0.5 half way down — 0.036,
        // 0.036, 0.031 — onto the STORED frame (B521's gamma path): 9, 9 and 8 of 255 at a white texel.
        // Measured (9, 8, 6) on 2026-10-11. It was (25, 24, 24) on 2026-10-04, when the vertex colour was dropped and
        // the texel added in linear light, and (1, 0, 0) with the strip's half-width sabotaged to zero. Half the
        // healthy sum is the floor.
        (red + green + blue).ShouldBeGreaterThan(11, $"the shaft's middle, measured ({red}, {green}, {blue})");
        red.ShouldBeLessThanOrEqualTo(10, "no brighter than the engine's 0.036 of a white texel");

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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Build_ProcessLamp676OnItsShippedMaterials_ShadesTheShaftAndKeepsTheHalo()
    {
        MapAssets assets = MapCache.Effects();

        FreeCamera camera = new()
        {
            Origin = (146f, -1831.6f, 728f),
            Angles = (-28.7f, -36.5f, 0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        SceneProp lamp676 = Spotlight(
            entity: 676,
            start: new Vector3(283.03125f, -1976.5352f, 844.7832f),
            end: new Vector3(274.15625f, -1966.7188f, 656.03125f),
            width: 64.03741f,
            endWidth: 30.27041f,
            fadeLength: 189.19966f,
            colour: (255, 255, 255));

        IReadOnlyList<ParticleBatch> batches = Build(lamp676, camera, assets.SpriteMaterials, out int drawn);

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

    /// <remarks>
    /// **The halo's stored texel is ADDED TO THE STORED FRAME — in gamma space — because the Sprite shader reads and
    /// writes without the sRGB curve** (B521; `SpriteMaterialConformanceTests` has the four lines). Lamp 676's halo, on
    /// the shipped <c>sprites/light_glow03</c>, seen from 300 units at a quarter of the way round from the beam's axis:
    /// <c>dot = 0.25</c>, so the halo's colour is <c>clamp( ( 2 · 0.25 )², 0, 1 ) = 0.25</c> of white
    /// (`view_beams.cpp:1855-1860`).
    ///
    /// **So what the halo adds to a pixel's STORED value does not depend on what was there**:
    /// <c>stored' = stored + alpha · colour · texel</c>, the same over a dark frame as over a mid grey one. Added in
    /// linear light it is nothing like the same — the curve is steep where the frame is dark — and that is the control,
    /// measured before the fix on these two frames: +53 over 0.02 (stored 39) and +22 over 0.2 (stored 124). The
    /// background is read from a corner the quad does not reach, so nothing here is the renderer's own arithmetic.
    ///
    /// **The wrong turn, kept**: the first form of this test predicted the centre pixel from the texture's centre
    /// texel, and PASSED against the linear path — 92 drawn, 93 predicted — by an accident of a half this test's author
    /// had not accounted for. A prediction that needs the texture's own filtering is the renderer's arithmetic again;
    /// the invariance needs none.
    ///
    /// *Measured* against TF2 (the golden comparison's indoor pair, 2026-10-11): predicting TF2's pixels from the
    /// viewer's by this rule lands within 5 of 255 across the first lamp's glow, where the two pictures differed by 45.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Render_Lamp676sHaloOverTwoFrames_AddsTheSameStoredAmountToBoth()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        MapAssets assets = MapCache.Effects();

        Vector3 lamp = new(283.03125f, -1976.5352f, 844.7832f);
        Vector3 end = new(274.15625f, -1966.7188f, 656.03125f);
        Vector3 along = Vector3.Normalize(end - lamp);
        Vector3 across = Vector3.Normalize(Vector3.Cross(along, Vector3.UnitZ));

        // A unit vector a quarter along the beam: dot( beamDir, toEye ) is 0.25.
        Vector3 toEye = (0.25f * along) + (MathF.Sqrt(1f - 0.0625f) * across);
        Vector3 eye = lamp + (300f * toEye);
        Vector3 look = -toEye;

        FreeCamera camera = new()
        {
            Origin = (eye.X, eye.Y, eye.Z),
            Angles = (
                -float.RadiansToDegrees(MathF.Asin(look.Z)),
                float.RadiansToDegrees(MathF.Atan2(look.Y, look.X)),
                0f),
            FieldOfView = 90f,
            Aspect = 1f,
        };

        SceneProp lamp676 = Spotlight(676, lamp, end, 64.03741f, 30.27041f, 189.19966f, (255, 255, 255));
        ParticleBatch halo = Build(lamp676, camera, assets.SpriteMaterials, out _)
            .Single(batch => batch.Material.Depth == SpriteDepth.Off);

        float colour = halo.Corners[0].Red;
        colour.ShouldBe(0.25f, 0.005f, "the instrument: the halo's fade at a quarter round");

        int Added(float frame, int stored)
        {
            StripPicture.Draw(target, camera.ToMatrix(), assets, [halo], background: frame);

            (int background, int _, int _) = target.PixelAt(1, 1);
            (int red, int _, int _) = target.PixelAt(Size / 2, Size / 2);

            background.ShouldBeInRange(stored - 1, stored + 1, $"{frame} in linear light is stored as {stored}");

            return red - background;
        }

        int overDark = Added(0.02f, 39);
        int overGrey = Added(0.2f, 124);

        TestContext.Out.WriteLine($"HALO colour {colour}: +{overDark} over the dark frame, +{overGrey} over the grey");

        overDark.ShouldBeGreaterThan(10, "the instrument: the glow reaches the centre pixel");
        overGrey.ShouldBeInRange(overDark - 1, overDark + 1, "the stored texel is added to the stored frame, whatever the frame");
    }

    private const string LampShaft = "sprites/glow_test02.vmt";

    private const string LampHalo = "sprites/light_glow03.vmt";

    /// <summary><c>INVALID_NETWORKED_EHANDLE_VALUE</c>.</summary>
    private const int NoHandle = (1 << 21) - 1;

    /// <summary>
    /// A <c>point_spotlight</c>'s <c>CBeam</c> as it is networked: every field both recordings' spotlights share, and the
    /// six that differ between them.
    /// </summary>
    private static SceneProp Spotlight(
        int entity, Vector3 start, Vector3 end, float width, float endWidth, float fadeLength, (byte, byte, byte) colour) => new(
        EntityIndex: entity,
        ModelPath: LampShaft,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose
        {
            X = start.X,
            Y = start.Y,
            Z = start.Z,
            RenderMode = RenderModes.TransTexture,
            RenderAlpha = 64,
            RenderColor = colour,
            Beam = new SceneBeam(
                Type: EntityBeams.BeamPoints,
                Flags: BeamDraw.ShadeOutFlag | BeamDraw.NoTileFlag,
                EntityCount: 2,
                Ends: [.. Enumerable.Repeat((NoHandle, 0), SceneBeam.MaximumEnds)],
                HaloIndex: 1,
                HaloScale: 60f,
                Width: width,
                EndWidth: endWidth,
                FadeLength: fadeLength,
                Amplitude: 0f,
                StartFrame: 0f,
                Speed: 0f,
                FrameRate: 0f,
                HdrColourScale: 1f,
                Frame: 0f,
                EndPosition: (end.X, end.Y, end.Z),
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
            _ => 1f,
            currentTime: 0f,
            frameTime: 0f);

        drawn = beams.Drawn;

        return batches;
    }
}
