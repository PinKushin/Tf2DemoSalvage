using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On a real payload match, every move child that is not bone-merged is DRAWN and LIT at parent × local (B551).
/// </summary>
/// <remarks>
/// **Output level, through the production pass.** <c>MoveChildPlacementConformanceTests</c> pins the arithmetic on
/// hand-built props; this proves real wire props reach it: <c>pl_badwater</c>'s gates are <c>CDynamicProp</c>s on
/// <c>CBaseDoor</c>s, and measured with the <c>move-parent</c> probe, 473 of 473 child samples sit somewhere other
/// than their local origin — so a pass that read the local pose as world would fail every one.
///
/// Models are a stand-in triangle with no illumination offset, because the question is placement, not geometry, and
/// CI has no game files. The expectation is the parent's own placement applied to the child's local point — one
/// level, which is all this demo carries (no chain deeper than one, measured).
/// </remarks>
public sealed class MoveChildPlacementCorpusTests
{
    [Test]
    public void Instances_GateChildrenOnBadwater_AreAtParentTimesLocal()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("pl_badwater_pro_v12"));

        List<SceneProp> props = [];
        timeline.PropsAt(timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2), props);
        Dictionary<int, SceneProp> byEntity = props.ToDictionary(prop => prop.EntityIndex);

        List<SceneProp> children =
        [
            .. props.Where(prop => prop is { AttachedTo: { } parent, BoneMerged: false } &&
                                   byEntity.TryGetValue(parent, out SceneProp? above) && above.AttachedTo is null &&
                                   prop.Kind == SceneModelKind.Studio)
                // A model per entity, so each instance names the child it came from.
                .Select(prop => prop with { ModelPath = $"models/b551/{prop.EntityIndex}.mdl" }),
        ];
        HashSet<int> parents = [.. children.Select(child => child.AttachedTo!.Value)];

        // The control: an instrument that found no children would pass everything below.
        children.Count.ShouldBeGreaterThan(10);

        EntityModelSet models = new();
        models.Add(children, _ => Triangle());
        List<ModelInstance> drawn = [];
        models.Instances([.. children, .. props.Where(prop => parents.Contains(prop.EntityIndex))], drawn);

        int misplacedAsLocal = 0;

        foreach (SceneProp child in children)
        {
            ScenePose parent = byEntity[child.AttachedTo!.Value].Pose;
            (float x, float y, float z) = new PropTransform(
                    parent.X, parent.Y, parent.Z, parent.Pitch, parent.Yaw, parent.Roll, 1f)
                .Apply(child.Pose.X, child.Pose.Y, child.Pose.Z);

            ModelInstance instance = drawn.Single(model => model.ModelPath == child.ModelPath);
            (float X, float Y, float Z) lit = instance.Origin.ShouldNotBeNull();

            instance.Matrix[12].ShouldBe(x, 0.05f, child.ModelPath);
            instance.Matrix[13].ShouldBe(y, 0.05f, child.ModelPath);
            instance.Matrix[14].ShouldBe(z, 0.05f, child.ModelPath);
            lit.X.ShouldBe(x, 0.05f, child.ModelPath);
            lit.Y.ShouldBe(y, 0.05f, child.ModelPath);
            lit.Z.ShouldBe(z, 0.05f, child.ModelPath);

            if (System.MathF.Abs(x - child.Pose.X) + System.MathF.Abs(y - child.Pose.Y) + System.MathF.Abs(z - child.Pose.Z) > 1f)
            {
                misplacedAsLocal++;
            }
        }

        // And the subject can tell the two readings apart: most of these children are not at their local origin.
        misplacedAsLocal.ShouldBeGreaterThan(children.Count / 2);
    }

    private static PropModels.ModelFrames Triangle() =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);
}
