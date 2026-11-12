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
