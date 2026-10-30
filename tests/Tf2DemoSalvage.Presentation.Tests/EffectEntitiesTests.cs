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
/// What a frame's beams, sprite trails and ropes are built against, as it left the view (D214): the entity an
/// <c>EHANDLE</c> names now (<c>CBaseHandle::Get</c>), <c>ComputeBeamEntPosition</c> for a beam end,
/// <c>CSpriteTrail::GetRenderOrigin</c> (`SpriteTrail.cpp:537-553`) and <c>CalculateEndPointAttachment</c>
/// (`c_rope.cpp:1923-1967`).
/// </summary>
/// <remarks>
/// The demo is `SyntheticRocket`'s (D38, D213): a rocket in slot 40 flying +x at 64 up, at (150, 0, 64) on tick 110, and
/// a bystander in slot 7; the player tests use a `SyntheticEffects` demo with a `CTFPlayer` in slot 1. **Building a strip is Scene's and has its own suites**
/// (`EntityBeamsConformanceTests`, `EntityTrailsConformanceTests`, `EntityRopesConformanceTests`); asserted here is where
/// the presenter says each end is, and that a beam is built from this frame's camera and this map's materials.
///
/// **Not here:** the frame time a rope and a beam's noise are stepped by, a rope's impulses and shakes and the holiday
/// lights, which need a rope that simulates; and a dormant rope end, which needs a demo that leaves an entity's PVS.
/// </remarks>
public sealed class EffectEntitiesTests
{
    private const int Tick = 110;
    private const int Rocket = SyntheticRocket.RocketEntityIndex;
    private const int Player = 1;
    private const int NoHandle = (1 << 21) - 1;
    private const string Shaft = "sprites/glow_test02.vmt";

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
        0.015f, SyntheticRocket.Model, [(100, 0f, 0f, 64f), (110, 150f, 0f, 64f), (120, 300f, 0f, 64f)]));

    /// <summary>A demo with a player in it, whose track is the players' own and not a prop's.</summary>
    private static readonly DemoTimeline Players = DemoTimeline.Build(SyntheticEffects.Demo(
        [new(Player, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(10f, 20f, 30f, 2))],
        [],
        [],
        [],
        new SyntheticEffects.Effect(200, "CTETFBlood", new Dictionary<string, Core.Schema.PropertyValue>(StringComparer.Ordinal))));

    private static readonly FreeCamera Camera = new() { Origin = (500f, 0f, 50f), Angles = (0f, 180f, 0f) };

    private static readonly SceneProp RocketProp = new(
        Rocket, SyntheticRocket.ModelPath, SceneModelKind.Studio, new ScenePose { X = 150f, Y = 0f, Z = 64f });

    private static readonly SceneProp PlayerProp = new(
        Player, "models/player/soldier.mdl", SceneModelKind.Studio, new ScenePose { X = 10f, Y = 20f, Z = 30f });

    /// <summary>The handle that names a slot's occupant at the tick: its serial above the eleven bits of its index.</summary>
    private static int Handle(int slot, int serialOff = 0) =>
        (((slot == Player ? Players : Timeline).TrackFor(slot, Tick).ShouldNotBeNull().SerialNumber + serialOff) << 11) | slot;

    private static EffectEntityFrame Frame(params SceneProp[] drawn) =>
        new(drawn.Contains(PlayerProp) ? Players : Timeline, null, Tick, Camera, drawn, 1280, 0);

    private static EffectEntityMap Map(bool sprites = true) => new(
        sprites
            ? new Dictionary<string, EngineSprite>(StringComparer.OrdinalIgnoreCase)
            {
                [Shaft] = new EngineSprite(
                    new ParticleMaterial(new MapTexture(128, 128, 128, 128, TextureImage.None, IsTransparent: true), [], SpriteBlend.Translucent),
                    128,
                    128,
                    SpriteOrientation.Parallel,
                    SpriteExtents.Of(128, 128, origin: null),
                    (0.5f, 0.5f, 0.5f, 0.75f),
                    IgnoresVertexColors: true),
            }
            : null,
        LevelLighting.Unlit(new RecordingLogger()),
        Empty);

    /// <summary>A presenter that has built one frame over the drawn props, so its ends can be asked for.</summary>
    private static (EffectEntities Effects, FakePosedEntities Models) Built(params SceneProp[] drawn)
    {
        FakePosedEntities models = new();
        EffectEntities effects = new(models, new RecordingLogger());
        (Vector3 forward, Vector3 right, Vector3 up) = Axes();

        effects.BuildOver(Frame(drawn), Map(), forward, right, up, static _ => 1f);

        return (effects, models);
    }

    private static (Vector3 Forward, Vector3 Right, Vector3 Up) Axes() => (-Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    private static SceneProp Spotlight() => new(
        EntityIndex: 530,
        ModelPath: Shaft,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose
        {
            Z = 100f,
            RenderMode = RenderModes.TransTexture,
            RenderAlpha = 64,
            RenderColor = ((byte)255, (byte)255, (byte)255),
            Beam = new SceneBeam(
                Type: EntityBeams.BeamPoints,
                Flags: BeamDraw.ShadeOutFlag | BeamDraw.NoTileFlag,
                EntityCount: 2,
                Ends: [.. Enumerable.Repeat((NoHandle, 0), SceneBeam.MaximumEnds)],
                HaloIndex: 0,
                HaloScale: 60f,
                Width: 64f,
                EndWidth: 30f,
                FadeLength: 100f,
                Amplitude: 0f,
                StartFrame: 0f,
                Speed: 0f,
                FrameRate: 0f,
                HdrColourScale: 1f,
                Frame: 0f,
                EndPosition: (0f, 0f, 0f),
                MinDxLevel: 90),
        },
        ClassName: "CBeam");

    [Test]
    public void BuildOver_ABeamInTheMoment_IsBuiltFromTheFramesCameraAndTheMapsSprites()
    {
        EffectEntities effects = new(new FakePosedEntities(), new RecordingLogger());
        (Vector3 forward, Vector3 right, Vector3 up) = Axes();
        EffectEntityMap map = Map();

        IReadOnlyList<ParticleBatch> built = effects.BuildOver(Frame(Spotlight()), map, forward, right, up, static _ => 1f);

        IReadOnlyList<ParticleBatch> beside = new EntityBeams().Build(
            [Spotlight()],
            new BeamView(new Vector3(500f, 0f, 50f), forward, right, up),
            map.SpriteMaterials.ShouldNotBeNull(),
            static one => one.Pose,
            static (_, _, _) => null,
            static _ => 1f,
            Tick * 0.015f,
            0f);

        beside.SelectMany(static batch => batch.Corners).ShouldNotBeEmpty("the control: the spotlight draws from here");
        built.SelectMany(static batch => batch.Corners).ShouldBe(beside.SelectMany(static batch => batch.Corners));
    }

    /// <remarks>
    /// **Built every frame, so built without garbage** (B599): the callbacks were new delegates per frame (the parent
    /// lookup one per prop placed), the holiday check a closure, the props walked through a boxed enumerator per builder,
    /// and the result a fresh array. Warmed once, then built again for the same frame with a trail (which places through the
    /// parent lookup and draws nothing yet): nothing allocated. The render log has Debug off, as the viewer runs.
    ///
    /// **A beam still allocates 176 bytes a frame somewhere in its own build, not located** (2026-10-29); the test below
    /// holds a beam's corners, not its bytes.
    /// </remarks>
    [Test]
    public void BuildOver_ATrailFrameBuiltAgain_AllocatesNothing()
    {
        EffectEntities effects = new(new FakePosedEntities(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        (Vector3 forward, Vector3 right, Vector3 up) = Axes();
        EffectEntityMap map = Map();
        EffectEntityFrame frame = Frame(Trail(NoHandle));

        _ = effects.BuildOver(frame, map, forward, right, up, Sees);

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = effects.BuildOver(frame, map, forward, right, up, Sees);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        bytes.ShouldBe(0L);

        static float Sees(GlowQuery query) => 1f;
    }

    /// <remarks>The result list is reused (B599): a second build of the same frame must hand back the same corners.</remarks>
    [Test]
    public void BuildOver_ABeamBuiltAgain_DrawsTheSameCorners()
    {
        EffectEntities effects = new(new FakePosedEntities(), new RecordingLogger());
        (Vector3 forward, Vector3 right, Vector3 up) = Axes();
        EffectEntityMap map = Map();
        EffectEntityFrame frame = Frame(Spotlight());

        List<DetailSpriteVertex> first = [.. effects.BuildOver(frame, map, forward, right, up, static _ => 1f).SelectMany(static batch => batch.Corners)];
        List<DetailSpriteVertex> again = [.. effects.BuildOver(frame, map, forward, right, up, static _ => 1f).SelectMany(static batch => batch.Corners)];

        first.ShouldNotBeEmpty("the control: the spotlight draws from here");
        again.ShouldBe(first);
    }


    [Test]
    public void BeamEnd_AHandleWhoseSerialIsTheSlotsOccupants_IsThatEntitysOrigin()
    {
        (EffectEntities effects, _) = Built(RocketProp);

        effects.BeamEnd(Handle(Rocket), 0, hitboxes: false).ShouldBe(new Vector3(150f, 0f, 64f));
    }

    [Test]
    public void BeamEnd_AHandleWithAnotherSerial_IsNotFound()
    {
        (EffectEntities effects, _) = Built(RocketProp);

        effects.BeamEnd(Handle(Rocket, serialOff: 1), 0, hitboxes: false).ShouldBeNull("CBaseHandle::Get: the serial decides");
        effects.BeamEnd(NoHandle, 0, hitboxes: false).ShouldBeNull();
    }

    [Test]
    public void BeamEnd_AnEntityTheFrameDidNotDraw_IsNotFound()
    {
        (EffectEntities effects, _) = Built(Spotlight());

        effects.BeamEnd(Handle(Rocket), 0, hitboxes: false).ShouldBeNull();
    }

    [Test]
    public void BeamEnd_AnAttachmentThePosedEntityHas_IsTheAttachment()
    {
        (EffectEntities effects, FakePosedEntities models) = Built(RocketProp);

        models.Numbered[(Rocket, 2)] = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));

        effects.BeamEnd(Handle(Rocket), 2, hitboxes: false).ShouldBe(new Vector3(1f, 2f, 3f));
    }

    [Test]
    public void BeamEnd_OnHitboxes_IsThePointNearestWhereTheCameraWas()
    {
        (EffectEntities effects, FakePosedEntities models) = Built(RocketProp);

        models.Numbered[(Rocket, 2)] = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));
        models.Nearest[(Rocket, 2)] = new Vector3(7f, 8f, 9f);

        effects.BeamEnd(Handle(Rocket), 2, hitboxes: true).ShouldBe(new Vector3(7f, 8f, 9f), "not the attachment of that number");
        models.LastViewer.ShouldBe(new Vector3(500f, 0f, 50f), "MainViewOrigin()");
    }

    [Test]
    public void BeamEnd_APlayer_IsTheMiddleOfHisHull()
    {
        (EffectEntities effects, _) = Built(PlayerProp);

        effects.BeamEnd(Handle(Player), 0, hitboxes: false)
            .ShouldBe(new Vector3(10f, 20f, 30f + PlayerHull.CenterHeight(null)), "player origins are at their feet");
    }

    private static SceneProp Trail(int attachedTo, int attachment = 0, int? parent = null) => new(
        600,
        "sprites/trail.vmt",
        SceneModelKind.Sprite,
        new ScenePose
        {
            X = 5f,
            Y = 6f,
            Z = 7f,
            SpriteTrail = new SceneSpriteTrail(1f, 8f, 0f, 0f, 1f, 0f, (0f, 0f, 0f), 1f, attachedTo, attachment),
        },
        AttachedTo: parent);

    [Test]
    public void TrailOrigin_ATrailAttachedToADrawnEntity_IsItsAttachmentOrItsOrigin()
    {
        (EffectEntities effects, FakePosedEntities models) = Built(RocketProp);

        effects.TrailOrigin(Trail(Handle(Rocket), 1)).ShouldBe(new Vector3(150f, 0f, 64f), "an attachment that does not exist");

        models.Numbered[(Rocket, 1)] = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));

        effects.TrailOrigin(Trail(Handle(Rocket), 1)).ShouldBe(new Vector3(1f, 2f, 3f));
    }

    [Test]
    public void TrailOrigin_ATrailWhoseEntityIsNotInTheMoment_HasNoHead()
    {
        (EffectEntities effects, _) = Built(Spotlight());

        effects.TrailOrigin(Trail(Handle(Rocket))).ShouldBeNull("the head stops where the entity was last");
    }

    [Test]
    public void TrailOrigin_ATrailAttachedToNothing_IsItsOwnPlaceUnlessItsParentIsGone()
    {
        (EffectEntities effects, _) = Built(RocketProp);

        effects.TrailOrigin(Trail(NoHandle)).ShouldBe(new Vector3(5f, 6f, 7f));
        effects.TrailOrigin(Trail(NoHandle, parent: 77)).ShouldBeNull("its move parent was not drawn");
    }

    [Test]
    public void RopeEnd_AnEntityWithTheAttachment_IsTheAttachmentAndTheEntitysForward()
    {
        SceneProp turned = RocketProp with { Pose = RocketProp.Pose with { Yaw = 90f } };
        (EffectEntities effects, FakePosedEntities models) = Built(turned);

        models.Numbered[(Rocket, 3)] = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));

        RopeEndPoint end = effects.RopeEnd(Handle(Rocket), 3, playerWeapon: false).ShouldNotBeNull();

        end.Position.ShouldBe(new Vector3(1f, 2f, 3f));
        Vector3.Distance(end.Forward, Vector3.UnitY).ShouldBeLessThan(1e-5f, "AngleVectors of the entity's own angles");
    }

    [Test]
    public void RopeEnd_APlayer_IsTheMiddleOfHisHull()
    {
        (EffectEntities effects, _) = Built(PlayerProp);

        effects.RopeEnd(Handle(Player), 0, playerWeapon: false).ShouldNotBeNull().Position
            .ShouldBe(new Vector3(10f, 20f, 30f + PlayerHull.CenterHeight(null)), "WorldSpaceCenter()");
    }

    [Test]
    public void RopeEnd_OnAPlayersWeapon_IsTheActiveWeaponsBuffAttachmentOrNothing()
    {
        SceneProp weapon = new(
            90, "models/weapons/c_models/c_buffbanner.mdl", SceneModelKind.Studio, new ScenePose(), OwnedBy: Player, WeaponState: 2);
        (EffectEntities effects, FakePosedEntities models) = Built(PlayerProp, weapon);

        effects.RopeEnd(Handle(Player), 0, playerWeapon: true).ShouldBeNull("ROPE_PLAYER_WPN_ATTACH with no buff_attach");

        models.Named[(90, "buff_attach")] = ParticleControlPoint.Unoriented(new Vector3(4f, 5f, 6f));

        effects.RopeEnd(Handle(Player), 0, playerWeapon: true).ShouldNotBeNull().Position.ShouldBe(new Vector3(4f, 5f, 6f));
    }

    [Test]
    public void RopeEnd_AHandleWithAnotherSerial_IsNotFound()
    {
        (EffectEntities effects, _) = Built(RocketProp);

        effects.RopeEnd(Handle(Rocket, serialOff: 1), 0, playerWeapon: false).ShouldBeNull();
    }

    [Test]
    public void RopeLight_WithNoMap_IsWhiteAndUnderAnUnlitOneIsBlack()
    {
        FakePosedEntities models = new();
        EffectEntities effects = new(models, new RecordingLogger());
        (Vector3 forward, Vector3 right, Vector3 up) = Axes();

        effects.BuildOver(Frame(), null, forward, right, up, static _ => 1f);
        effects.RopeLight(Vector3.Zero).ShouldBe(Vector3.One);

        effects.BuildOver(Frame(), Map(), forward, right, up, static _ => 1f);
        effects.RopeLight(Vector3.Zero).ShouldBe(Vector3.Zero, "six faces of no light, averaged");
    }

    [Test]
    public void RopeCollide_WithNothingInTheWay_RunsItsWholeLength()
    {
        (EffectEntities effects, _) = Built();

        effects.RopeCollide(Vector3.Zero, new Vector3(100f, 0f, 0f)).Fraction.ShouldBe(1f);
    }

    [Test]
    public void RopeEndDormantWithModel_AnEntityInTheRecordersView_IsNot()
    {
        (EffectEntities effects, _) = Built(RocketProp);

        effects.RopeEndDormantWithModel(Handle(Rocket)).ShouldBeFalse("the rocket is not dormant");
        effects.RopeEndDormantWithModel(NoHandle).ShouldBeFalse();
    }
}
