using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A frame's particles as `MainForm.DrawParticles` decided them and as they left it (D214): which projectile carries a
/// trail (`CTFBaseRocket::CreateTrails`, `PATTACH_POINT_FOLLOW`), the order the batches are joined in, and the glow
/// occlusion gate the sprites and the strips pass through (`PixelVisibility_FractionVisible`, c_pixel_visibility.cpp).
/// </summary>
/// <remarks>
/// **Not here:** the quads `ParticleEffects.Build` makes and their count, which need a system that emits; the gate's
/// arithmetic, `PixelVisibilityConformanceTests`'; and a sprite's quad, `EntitySpriteBatchesTests`'. Asserted here is
/// what is joined to what, in which order, and what the device is asked and told.
/// </remarks>
public sealed class FrameParticlesTests
{
    private const string Glow = "materials/sprites/light_glow03.vmt";
    private const int GlowEntity = 42;

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly ParticleSystem Trail = new(
        "rockettrail", [], [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    /// <summary>Straight down from five hundred units up, which a viewplane-parallel glow at the origin faces.</summary>
    private static readonly FreeCamera Overhead = new() { Origin = (0f, 0f, 500f), Angles = (90f, 0f, 0f) };

    private static readonly DemoTimeline Rocket = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
        0.015f, (100, 0f, 0f, 64f), (110, 150f, 0f, 64f), (120, 300f, 0f, 64f)));

    private static readonly ParticleBatch Strip =
        new([new DetailSpriteVertex(7f, 7f, 7f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f)], default);

    private static ParticleMap Map(bool trail = true, bool sprite = false) => new(
        trail ? Trail : null,
        null,
        null,
        sprite
            ? new Dictionary<string, EngineSprite>(StringComparer.OrdinalIgnoreCase)
            {
                [Glow] = new EngineSprite(
                    new ParticleMaterial(new MapTexture(128, 128, 128, 128, TextureImage.None, IsTransparent: true), [], SpriteBlend.Additive),
                    128,
                    128,
                    SpriteOrientation.Parallel,
                    SpriteExtents.Of(128, 128, origin: null),
                    (1f, 1f, 1f, 1f),
                    IgnoresVertexColors: true),
            }
            : null,
        Empty);

    private static readonly SceneProp GlowProp = new(
        EntityIndex: GlowEntity,
        ModelPath: Glow,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose { RenderMode = RenderModes.WorldGlow, Sprite = SceneSprite.Default });

    private static ParticleFrame Frame(int tick, IReadOnlyList<SceneProp>? drawn = null, FreeCamera? viewing = null, bool pixelVisibility = false) =>
        new(Rocket, null, tick, viewing ?? Overhead, drawn ?? [], 0.05f, 720, pixelVisibility);

    private static List<SceneProp> DrawnAt(int tick)
    {
        List<SceneProp> drawn = [];

        Rocket.PropsAt(tick, drawn);

        return drawn;
    }

    private static IReadOnlyList<ParticleBatch> NoStrips(Vector3 forward, Vector3 right, Vector3 up, Func<GlowQuery, float> sees) => [];

    [Test]
    public void TrailsOver_ARocketTheFrameDrew_StartsItsTrail()
    {
        ParticleEffects particles = new();
        FrameParticles frame = new(particles, new RecordingLogger());
        List<SceneProp> drawn = DrawnAt(110);

        drawn.ShouldContain(static prop => prop.ClassName == ParticleEffects.RocketClass, "the control: the demo's rocket is drawn at this tick");

        frame.TrailsOver(Frame(110, drawn), Map()).ShouldBeTrue();

        particles.TrailEffect(SyntheticRocket.RocketEntityIndex).ShouldNotBeNull();
        particles.TrailEffect(SyntheticRocket.OwnerEntityIndex).ShouldBeNull("its owner is no projectile");
        particles.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A window's track is trimmed** (D217 step 3): its first keyframes are gone, and the trail's birth is the track's
    /// kept first tick and pose, not index 0 — which throws once anything is dropped.
    /// </remarks>
    [Test]
    public void TrailsOver_ARocketWhoseTrackWasTrimmed_StartsItsTrail()
    {
        DemoTimeline rocket = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
            0.015f, (100, 0f, 0f, 64f), (110, 150f, 0f, 64f), (120, 300f, 0f, 64f), (130, 450f, 0f, 64f)));
        ScenePropTrack track = rocket.Props.Single(prop => prop.EntityIndex == SyntheticRocket.RocketEntityIndex);
        track.TrimBefore(130);
        track.HeldKeyframeCount.ShouldBeLessThan(track.KeyframeCount, "the control: the trim dropped the first keyframes");
        List<SceneProp> drawn = [];
        rocket.PropsAt(125, drawn);
        ParticleEffects particles = new();

        new FrameParticles(particles, new RecordingLogger())
            .TrailsOver(new ParticleFrame(rocket, null, 125, Overhead, drawn, 0.05f, 720, false), Map()).ShouldBeTrue();

        particles.TrailEffect(SyntheticRocket.RocketEntityIndex).ShouldNotBeNull();
    }

    [Test]
    public void TrailsOver_AMapWithNoRocketTrail_StepsNothingAndSaysSo()
    {
        ParticleEffects particles = new();
        FrameParticles frame = new(particles, new RecordingLogger());

        frame.TrailsOver(Frame(110, DrawnAt(110)), Map(trail: false)).ShouldBeFalse("no .pcf, which is every CI run");
        frame.TrailsOver(Frame(110, DrawnAt(110)), null).ShouldBeFalse();

        particles.Count.ShouldBe(0);
    }

    [Test]
    public void TrailsOver_ARocketNoLongerDrawn_LosesItsTrail()
    {
        ParticleEffects particles = new();
        FrameParticles frame = new(particles, new RecordingLogger());

        frame.TrailsOver(Frame(110, DrawnAt(110)), Map());
        frame.TrailsOver(Frame(111, []), Map());

        particles.TrailEffect(SyntheticRocket.RocketEntityIndex).ShouldBeNull("offered every frame: a rocket not offered is gone");
    }

    [Test]
    public void BuildOver_WithNothingButStrips_HandsTheStripsTheCamerasAxesAndReturnsThem()
    {
        FrameParticles frame = new(new ParticleEffects(), new RecordingLogger());
        FakeFrameDevice device = new();
        FreeCamera level = new() { Origin = (0f, 0f, 0f), Angles = (0f, 90f, 0f) };
        (Vector3 Forward, Vector3 Right, Vector3 Up) given = default;

        IReadOnlyList<ParticleBatch> batches = frame.BuildOver(
            Frame(110, viewing: level),
            Map(),
            device,
            (forward, right, up, _) =>
            {
                given = (forward, right, up);

                return [Strip];
            });

        batches.ShouldHaveSingleItem().ShouldBe(Strip);
        Vector3.Distance(given.Forward, Vector3.UnitY).ShouldBeLessThan(1e-5f, "AngleVectors of pitch 0, yaw 90");
        Vector3.Distance(given.Right, Vector3.UnitX).ShouldBeLessThan(1e-5f);
        Vector3.Distance(given.Up, Vector3.UnitZ).ShouldBeLessThan(1e-5f);
        frame.Quads.ShouldBe(0, "the effects built none; a strip is not counted among them");
        device.Proxies.ShouldHaveSingleItem().ShouldBeEmpty("PixelVisibility_EndCurrentView, once a frame, with nothing asked");
    }

    [Test]
    public void BuildOver_ASpriteAndAStrip_JoinsTheSpriteFirstAndTheStripAfter()
    {
        FrameParticles frame = new(new ParticleEffects(), new RecordingLogger());

        // `r_dopixelvisibility 0`: the gate is the line of sight, and nothing stands in it.
        frame.LevelInit(doPixelVisibility: false);

        IReadOnlyList<ParticleBatch> batches = frame.BuildOver(
            Frame(110, [GlowProp]), Map(sprite: true), new FakeFrameDevice(), static (_, _, _, _) => [Strip]);

        batches.Count.ShouldBe(2);
        batches[0].Corners.Count.ShouldBe(6, "the entity sprite's quad");
        batches[1].ShouldBe(Strip);
    }

    [Test]
    public void BuildOver_ASpriteWithNoStrips_IsTheSpriteAlone()
    {
        FrameParticles frame = new(new ParticleEffects(), new RecordingLogger());

        frame.LevelInit(doPixelVisibility: false);

        frame.BuildOver(Frame(110, [GlowProp]), Map(sprite: true), new FakeFrameDevice(), NoStrips)
            .ShouldHaveSingleItem().Corners.Count.ShouldBe(6);
    }

    [Test]
    public void BuildOver_AGlowAskedOfTheGate_IsCountedByTheDeviceAndFadesInOverTheFramesDuration()
    {
        FrameParticles frame = new(new ParticleEffects(), new RecordingLogger());
        FakeFrameDevice device = new() { Counts = { [GlowEntity] = (100, 100) } };
        GlowQuery glow = new(GlowEntity, Vector3.Zero, 16f, 1f);
        List<float> seen = [];

        IReadOnlyList<ParticleBatch> Asking(Vector3 forward, Vector3 right, Vector3 up, Func<GlowQuery, float> sees)
        {
            seen.Add(sees(glow));

            return [];
        }

        frame.LevelInit(doPixelVisibility: true);
        frame.BuildOver(Frame(110, pixelVisibility: true), Map(), device, Asking);

        seen[0].ShouldBe(0f, "never issued: nothing has counted it yet");
        device.Proxies[0].ShouldHaveSingleItem().Key.ShouldBe(GlowEntity, "the proxy this frame's view counts");
        device.Asked.ShouldBeEmpty();
        frame.GlowsSettled.ShouldBeFalse();

        frame.BuildOver(Frame(111, pixelVisibility: true), Map(), device, Asking);

        device.Asked.ShouldBe([GlowEntity], "read back by the next frame");
        seen[1].ShouldBe(0.8f, 0.0001f, "all of it visible, approached at 0.05 s over the 0.0625 s fade");
        frame.GlowsUnsettled().ShouldContain("entity 42");
    }
}
