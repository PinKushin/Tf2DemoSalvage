using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Two instances of one model are told apart by the instance they came from (B356).
/// </summary>
/// <remarks>
/// **This exists because a diagnostic could not tell two resupply lockers apart.**
/// `Device3D.Classify` keyed its change guard by MODEL PATH while the input it names as the varying
/// one is the animation FRAME, so a map carrying two of anything reported a render-group change on
/// every draw: measured, `_locker` alone wrote 11,576 lines on one two-minute run, 5,788 in each
/// direction, none of them a model changing its mind.
///
/// **A per-model diagnostic cannot be fixed downstream** — the identity has to reach the renderer, and
/// the only thing that can put it there is whoever builds the instance. So the claim under test is
/// that it does: the guard's own keying is then one dictionary key, and a test of Valve's grouping
/// rules belongs in `Tf2DemoSalvage.Rendering.Tests` where those already are.
/// </remarks>
public sealed class ModelInstanceIdentityTests
{
    [Test]
    public void Instances_TwoPropsOfOneModel_CarryTheirOwnEntityIndices()
    {
        // **The manipulation: the same model twice, which is the case that broke.** Two lockers on
        // one map, two entities, one model path.
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Locker(11), Locker(12)];
        List<ModelInstance> instances = [];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, instances);

        instances.Count.ShouldBe(2);
        instances.Select(one => one.EntityIndex).OrderBy(index => index).ShouldBe([11, 12]);

        // The model path really is shared, which is what made a per-path key wrong rather than merely
        // imprecise: without this the test would pass against two different models.
        instances.Select(one => one.ModelPath).Distinct().ShouldHaveSingleItem();
    }

    [Test]
    public void ModelInstance_BuiltByHand_IsUnidentifiedRatherThanTheWorldspawn()
    {
        // **−1, not 0.** Entity 0 is the worldspawn, so a default of zero would have every
        // hand-built instance — every test, and the viewmodel path — claim to be it.
        new ModelInstance("models/props_gameplay/resupply_locker.mdl", [], null, null)
            .EntityIndex.ShouldBe(-1);
    }

    /// <summary>A prop of the model that produced the flapping log.</summary>
    private static SceneProp Locker(int entityIndex) =>
        new(
            entityIndex,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0 },
            null,
            ClientSideAnimated: false);

    /// <summary>One triangle, which is all an identity test needs of the geometry.</summary>
    private static PropModels.ModelFrames Frames() =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 0),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 0),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 0),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);
}
