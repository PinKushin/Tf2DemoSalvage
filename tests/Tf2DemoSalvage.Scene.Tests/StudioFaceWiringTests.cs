using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The eye and mouth values reach the instance the renderer draws, from the entity's own bones (B725).</summary>
/// <remarks>
/// **Through `EntityModelSet.Instances`, the production pass**, on a synthetic skinned model: one bone at its origin, so
/// its bone-to-world is the entity's placement, (100, 0, 0) unrotated. An eyeball on that bone at (0, 0, 10) with up +z
/// and iris scale 1, and a mouth facing +x. The prop is neither a player nor a C_BaseFlex, so its view target is (0, 0, 0)
/// unclamped (a C_BaseAnimating's, taken as the origin) and its mouth weight zero (`LockFlexWeights`).
///
/// *Arithmetic*: the eye at (100, 0, 10) looks along `normalize( (−100, 0, −10) )`; `right = forward × up`, `up' = right ×
/// forward`, `$irisu = −right` with offset `0.5 − origin · $irisu`. The mouth's `$forward` is the bone's +x negated.
/// </remarks>
public sealed class StudioFaceWiringTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void Instances_ASkinnedModelWithAnEyeAndTeeth_CarryTheEyesProjectionAndTheMouth()
    {
        EntityModelSet models = new();
        SceneProp prop = new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { X = 100f },
            null,
            ClientSideAnimated: true);

        SceneProp[] props = [prop];
        models.Add(props, _ => Faced());

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        instances.Count.ShouldBe(1);
        StudioFace face = instances[0].Face.ShouldNotBeNull("a model with eyeballs carries their values");

        (float X, float Y, float Z) forward = Normalise((-100f, 0f, -10f));
        (float X, float Y, float Z) right = Normalise(Cross(forward, (0f, 0f, 1f)));
        (float X, float Y, float Z) irisU = (-right.X, -right.Y, -right.Z);

        face.Eyes.Length.ShouldBe(1);
        face.Eyes[0].Origin.X.ShouldBe(100f, Tolerance);
        face.Eyes[0].Origin.Z.ShouldBe(10f, Tolerance);
        face.Eyes[0].IrisU.X.ShouldBe(irisU.X, Tolerance);
        face.Eyes[0].IrisU.Y.ShouldBe(irisU.Y, Tolerance);
        face.Eyes[0].IrisU.W.ShouldBe(0.5f - ((100f * irisU.X) + (10f * irisU.Z)), 1e-3f);

        MouthShading mouth = face.Mouth.ShouldNotBeNull("a model with teeth and a mouth sets them up");
        mouth.Forward.X.ShouldBe(-1f, Tolerance);
        mouth.IllumFactor.ShouldBe(0f);
    }

    [Test]
    public void Instances_AModelWithAMouthAndNoTeeth_SetsNoMouth()
    {
        EntityModelSet models = new();
        SceneProp prop = new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { X = 100f },
            null,
            ClientSideAnimated: true);

        SceneProp[] props = [prop];
        models.Add(props, _ => Faced() with { HasTeeth = false });

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        instances[0].Face.ShouldNotBeNull().Mouth.ShouldBeNull("only a teeth material reads $forward and $illumfactor");
    }

    /// <remarks>
    /// **A non-local player looks along world +x** — its local angles are never set on the client (`m_angRotation` is
    /// excluded from its send table, `tf_player.cpp:773`; `c_tf_player.cpp:4415`) — **at the first living other player
    /// within 300 units on that side, by index, at its origin** (`UpdateLookAt`; a non-local player's view offset is not
    /// networked). The looker at (0, 0, 0) faces yaw 90 in its pose, which must not matter. Players 3 (behind, −x), 4 (dead,
    /// ahead) and 5 (ahead at (200, 50, 0)): it looks at 5. With nobody ahead, at (512, 0, 0). The eye at (0, 0, 10).
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Instances_APlayerWithOthersAround_LooksAtTheFirstLivingOneAheadOnXElse512AlongX(bool someoneAhead)
    {
        EntityModelSet models = new() { PlayerAlive = index => index != 4 };
        // The dead one aside, at (100, −50): looking at him would turn the eye off the x axis in both cases.
        List<SceneProp> props = [Player(2, 0f, 0f, yaw: 90f), Player(3, -100f, 0f), Player(4, 100f, -50f)];

        if (someoneAhead)
        {
            props.Add(Player(5, 200f, 50f));
        }

        models.Add(props, _ => Faced());

        // `MomentScene` hands over the frame's props before the passes; the players looked at are found there.
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        (float X, float Y, float Z) target = someoneAhead ? (200f, 50f, 0f) : (512f, 0f, 0f);
        (float X, float Y, float Z) forward = Normalise((target.X, target.Y, target.Z - 10f));
        (float X, float Y, float Z) right = Normalise(Cross(forward, (0f, 0f, 1f)));

        StudioFace face = instances.Find(instance => instance.EntityIndex == 2).Face.ShouldNotBeNull();
        face.Eyes[0].IrisU.X.ShouldBe(-right.X, Tolerance);
        face.Eyes[0].IrisU.Y.ShouldBe(-right.Y, Tolerance);
    }

    /// <remarks>
    /// **`UpdateLookAt` walks `UTIL_PlayerByIndex( 1..maxClients )` only** (`c_tf_player.cpp:6223-6227`): a map prop wearing a
    /// class model (index 3, which the scene does not know as a player, `PlayerAlive` null) is neither looked at nor a
    /// looker. Player 2 at the origin, the prop ahead at (100, 0, 0), player 5 at (200, 50, 0): player 2 looks at 5, which
    /// turns its eye off the x axis; taken as alive, the prop — the lower index — would be found first and the eye would
    /// stay on the axis. The prop itself looks at (0, 0, 0), not at player 5 ahead of it.
    /// </remarks>
    [Test]
    public void Instances_AMapPropWearingAPlayerModel_IsNeitherLookedAtNorALooker()
    {
        EntityModelSet models = new() { PlayerAlive = index => index == 3 ? null : true };
        List<SceneProp> props = [Player(2, 0f, 0f), Player(3, 100f, 0f), Player(5, 200f, 50f)];

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        (float X, float Y, float Z) looker = IrisUAt(instances, 2);
        (float X, float Y, float Z) expected = Rightward((0f, 0f, 10f), (200f, 50f, 0f));
        looker.X.ShouldBe(expected.X, Tolerance);
        looker.Y.ShouldBe(expected.Y, Tolerance);

        (float X, float Y, float Z) prop = IrisUAt(instances, 3);
        (float X, float Y, float Z) atOrigin = Rightward((100f, 0f, 10f), (0f, 0f, 0f));
        prop.X.ShouldBe(atOrigin.X, Tolerance);
        prop.Y.ShouldBe(atOrigin.Y, Tolerance);
    }

    /// <remarks>
    /// **By index, not by distance or by the order the scene lists them** (`for( int iClient = 1; …` with `break` at the
    /// first, `c_tf_player.cpp:6223-6248`): player 9 at (100, 0, 0), nearer and listed first, and player 5 at (200, 50, 0),
    /// both alive and ahead. Player 2 looks at 5.
    /// </remarks>
    [Test]
    public void Instances_ANearerHigherIndexPlayerListedFirst_LooksAtTheLowerIndex()
    {
        EntityModelSet models = new() { PlayerAlive = _ => true };
        List<SceneProp> props = [Player(9, 100f, 0f), Player(2, 0f, 0f), Player(5, 200f, 50f)];

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        (float X, float Y, float Z) irisU = IrisUAt(instances, 2);
        (float X, float Y, float Z) expected = Rightward((0f, 0f, 10f), (200f, 50f, 0f));
        irisU.X.ShouldBe(expected.X, Tolerance);
        irisU.Y.ShouldBe(expected.Y, Tolerance);
    }

    /// <remarks>
    /// **The eye controllers reach the clamp** (`eyeAng.x = g_flexweight[ … ]`, `c_baseflex.cpp:596-606`, through
    /// `SetViewTarget`'s deflection, `:620-628`). Player 2 alone at the origin looks 512 along +x (`UpdateLookAt`). Its model
    /// has an <c>eyes</c> attachment on bone 0 at (0, 0, 10), unrotated, and an <c>eyes_updown</c> controller ranged 20..40,
    /// which rests at 20 (zero set normalised and clamped into the range, `c_baseflex.cpp:1220-1222`).
    ///
    /// *Arithmetic*: the target in the attachment's frame is (512, 0, −10), length d; normalised, n; the deflection
    /// <c>AngleVectors( 20°, 0 )</c> with x zeroed is (0, 0, −sin 20°), added scaled by n.x²; renormalised, x stays above
    /// 0.866, so no clamp; the target is (0, 0, 10) + d · that. The eye at (0, 0, 10) then looks along it, so `$irisv` =
    /// −(right × forward) = (f.z, 0, −f.x). Undeflected, f.z would be −10/d ≈ −0.0195.
    /// </remarks>
    [Test]
    public void Instances_AnEyesAttachmentAndAnUpDownController_DeflectTheIrisProjection()
    {
        EntityModelSet models = new() { PlayerAlive = _ => true };
        List<SceneProp> props = [Player(2, 0f, 0f)];

        models.Add(props, _ => Faced() with
        {
            Attachments = [new StudioAttachment("eyes", 0u, 0, [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 10f])],
            Flex = new StudioFlexData([], [new StudioFlexController("eyes", "eyes_updown", 20f, 40f)], [], []),
            MaxEyeDeflection = 0.866f,
        });
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        float distance = MathF.Sqrt((512f * 512f) + 100f);
        (float X, float Y, float Z) n = (512f / distance, 0f, -10f / distance);
        float sin = MathF.Sin(20f * MathF.PI / 180f);
        (float X, float Y, float Z) f = Normalise((n.X, 0f, n.Z - (sin * n.X * n.X)));

        StudioFace face = instances.Find(instance => instance.EntityIndex == 2).Face.ShouldNotBeNull();
        face.Eyes[0].IrisV.X.ShouldBe(f.Z, Tolerance);
        face.Eyes[0].IrisV.Z.ShouldBe(-f.X, Tolerance);
    }

    /// <remarks>
    /// **The POV recorder looks along its view** (B725 open 1): `C_BasePlayer::PostDataUpdate` sets the LOCAL player's local
    /// angles from `engine->GetViewAngles` (`c_baseplayer.cpp:865-883`), which `UpdateLookAt` reads (`c_tf_player.cpp:6215`).
    /// Recorder 2 at the origin with its view at yaw 180: player 5 at (200, 50, 0) is behind it and player 3 at (−200, 0, 0)
    /// ahead, so it looks at 3 — along world +x, as a non-local player, it would look at 5.
    /// </remarks>
    [Test]
    public void Instances_TheRecorderViewingAlongMinusX_LooksAtThePlayerAheadOfItsView()
    {
        EntityModelSet models = new() { PlayerAlive = _ => true, LocalPlayerView = (2, 0f, 180f, 68f) };
        List<SceneProp> props = [Player(2, 0f, 0f), Player(3, -200f, 0f), Player(5, 200f, 50f)];

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        (float X, float Y, float Z) irisU = IrisUAt(instances, 2);
        (float X, float Y, float Z) expected = Rightward((0f, 0f, 10f), (-200f, 0f, 0f));
        irisU.X.ShouldBe(expected.X, Tolerance);
        irisU.Y.ShouldBe(expected.Y, Tolerance);
    }

    /// <remarks>
    /// **With nobody ahead, 512 units along its view, pitch included** (`AngleVectors( GetLocalAngles() )`, `:6215`, `:6254`):
    /// recorder 2 alone, view pitch 30 (down) and yaw 0, looks at 512 · (cos 30°, 0, −sin 30°). *Arithmetic*: the eye at
    /// (0, 0, 10) looks along f = normalise( target − eye ), and `$irisv` = (f.z, 0, −f.x) (see the deflection test).
    /// </remarks>
    [Test]
    public void Instances_TheRecorderAloneViewingDown_Looks512AlongItsViewPitchIncluded()
    {
        EntityModelSet models = new() { PlayerAlive = _ => true, LocalPlayerView = (2, 30f, 0f, 68f) };
        List<SceneProp> props = [Player(2, 0f, 0f)];

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        float pitch = 30f * MathF.PI / 180f;
        (float X, float Y, float Z) f = Normalise((512f * MathF.Cos(pitch), 0f, (-512f * MathF.Sin(pitch)) - 10f));

        EyeShading eye = instances.Find(instance => instance.EntityIndex == 2).Face.ShouldNotBeNull().Eyes[0];
        eye.IrisV.X.ShouldBe(f.Z, Tolerance);
        eye.IrisV.Z.ShouldBe(-f.X, Tolerance);
    }

    /// <remarks>
    /// **The recorder is looked at at its eye** — `pEnt->EyePosition()` (`c_tf_player.cpp:6245`) is origin plus
    /// `m_vecViewOffset`, which only the local player has (`DT_LocalPlayerExclusive`, `c_baseplayer.cpp:223-229`). Player 2 at
    /// the origin looks along +x at recorder 5 at (200, 0, 0) with eye height 68, so at (200, 0, 68): f = normalise( (200, 0,
    /// 58) ) from the eye at (0, 0, 10), and `$irisv` = (f.z, 0, −f.x). At its origin, f.z would be negative.
    /// </remarks>
    [Test]
    public void Instances_APlayerLookingAtTheRecorder_LooksAtItsEyeNotItsOrigin()
    {
        EntityModelSet models = new() { PlayerAlive = _ => true, LocalPlayerView = (5, 0f, 0f, 68f) };
        List<SceneProp> props = [Player(2, 0f, 0f), Player(5, 200f, 0f)];

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);

        (float X, float Y, float Z) f = Normalise((200f, 0f, 58f));

        EyeShading eye = instances.Find(instance => instance.EntityIndex == 2).Face.ShouldNotBeNull().Eyes[0];
        eye.IrisV.X.ShouldBe(f.Z, Tolerance);
        eye.IrisV.Z.ShouldBe(-f.X, Tolerance);
    }

    /// <remarks>
    /// **Gathered once a pass, not per looker** (B725 review). Every question the pass asks is a fixed number per player —
    /// the frame start, the face, the looker's gate, and two in the one gathering — so sixteen players ask exactly twice
    /// what eight do. A list rebuilt per looker asks n · (n − 1) more, and sixteen would ask 2 · 120 / 56 ≈ 4.3 times.
    /// The look-at itself allocates nothing either: a pass with every player alive and ahead allocates the same as one
    /// with every player dead, where no candidate is gathered.
    /// </remarks>
    [Test]
    public void Instances_TwiceThePlayers_AskWhoIsAliveTwiceAsOftenAndAllocateNothingMoreForTheLookAt()
    {
        (int eight, long aliveBytes) = OnePass(8, alive: true);
        (int sixteen, _) = OnePass(16, alive: true);
        (_, long deadBytes) = OnePass(8, alive: false);

        eight.ShouldBeGreaterThan(0);
        sixteen.ShouldBe(2 * eight);
        aliveBytes.ShouldBe(deadBytes);
    }

    /// <summary>A warm pass over <paramref name="count"/> players 30 units apart on +x: questions asked, bytes allocated.</summary>
    private static (int Questions, long Bytes) OnePass(int count, bool alive)
    {
        int questions = 0;
        EntityModelSet models = new() { PlayerAlive = _ => { questions++; return alive; } };
        List<SceneProp> props = [];

        for (int index = 1; index <= count; index++)
        {
            props.Add(Player(index, index * 30f, 0f));
        }

        models.Add(props, _ => Faced());
        models.PlaceFrame(props);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: 1d);
        instances.Clear();
        models.Instances(props, instances, seconds: 1d);
        instances.Clear();

        questions = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        models.Instances(props, instances, seconds: 1d);

        return (questions, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>`$irisu`'s direction for an entity's first eye.</summary>
    private static (float X, float Y, float Z) IrisUAt(List<ModelInstance> instances, int entityIndex)
    {
        EyeShading eye = instances.Find(instance => instance.EntityIndex == entityIndex).Face.ShouldNotBeNull().Eyes[0];
        return (eye.IrisU.X, eye.IrisU.Y, eye.IrisU.Z);
    }

    /// <summary>`$irisu` for an eye with up +z at <paramref name="eye"/> looking at <paramref name="target"/>: −right.</summary>
    private static (float X, float Y, float Z) Rightward((float X, float Y, float Z) eye, (float X, float Y, float Z) target)
    {
        (float X, float Y, float Z) forward = Normalise((target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z));
        (float X, float Y, float Z) right = Normalise(Cross(forward, (0f, 0f, 1f)));
        return (-right.X, -right.Y, -right.Z);
    }

    private static SceneProp Player(int index, float x, float y, float yaw = 0f) =>
        new(
            index,
            "models/player/scout.mdl",
            ScenePropTrack.Classify("models/player/scout.mdl"),
            new ScenePose { X = x, Y = y, Yaw = yaw },
            null,
            ClientSideAnimated: true);

    private static PropModels.ModelFrames Faced() => ModelFramesFixture.OneSecondLoop(string.Empty) with
    {
        Eyeballs = [new StudioEyeball(0, (0f, 0f, 10f), 0f, 0.5f, (0f, 0f, 1f), (1f, 0f, 0f), 1f)],
        Mouths = [new StudioMouth(0, (1f, 0f, 0f), 0)],
        HasTeeth = true,
    };

    private static (float X, float Y, float Z) Normalise((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return (v.X / length, v.Y / length, v.Z / length);
    }

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) => (
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));
}
