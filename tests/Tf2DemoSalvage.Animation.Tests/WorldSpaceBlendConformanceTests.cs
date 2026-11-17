using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>`WorldSpaceSlerp` (bone_setup.cpp:1249-1365), which `SlerpBones` runs for a `STUDIO_WORLD` sequence (B741).</summary>
/// <remarks>
/// A root and its child at the origin. The pose is at rest; the layer turns the root 90° about X and the child 90° about Z.
/// Expectations are worked with `System.Numerics`, a second route to the same quaternions.
/// </remarks>
public sealed class WorldSpaceBlendConformanceTests
{
    private const float Tolerance = 1e-4f;

    private static readonly Quaternion RootTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);

    private static readonly Quaternion ChildTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

    /// <remarks>
    /// **Halfway in world space, then solved back to locals** (:1325-1356): the child's world rotation is the halfway
    /// slerp of rest to `RootTurn · ChildTurn`, and its local is that under the root's own halfway turn. Not
    /// `Slerp( rest, ChildTurn, ½ )`, the local-space answer, which is the control.
    /// </remarks>
    [Test]
    public void Slerp_HalfWeight_BlendsTheChildInWorldSpace()
    {
        StudioBonePose[] pose = Rest();

        WorldSpaceBlend.Slerp(Bones(), ~0, pose, Layer(), [1f, 1f], 0.5f);

        Quaternion root = Quaternion.Slerp(Quaternion.Identity, RootTurn, 0.5f);
        Quaternion world = Quaternion.Slerp(Quaternion.Identity, RootTurn * ChildTurn, 0.5f);
        Quaternion child = Quaternion.Conjugate(root) * world;

        Same(pose[0].Rotation, root);
        Same(pose[1].Rotation, child);
        Same(Rest()[1].Rotation, Quaternion.Identity);
        Quaternion.Dot(child, Quaternion.Slerp(Quaternion.Identity, ChildTurn, 0.5f)).ShouldBeLessThan(0.999f, "the control: not the local blend");
    }

    /// <remarks>
    /// **A root keeps its own position** (:1335-1338): only its rotation is solved back; the child's length is lerped
    /// (:1352-1353). The layer moves the root to x 10 and the child to x 4; at half weight the root stays at 0 and the
    /// child is at 2.
    /// </remarks>
    [Test]
    public void Slerp_ARootThatMoves_KeepsItsPositionAndLerpsTheChilds()
    {
        StudioBonePose[] pose = Rest();
        StudioBonePose[] layer = Layer();
        layer[0] = layer[0] with { Position = (10f, 0f, 0f) };
        layer[1] = layer[1] with { Position = (4f, 0f, 0f) };

        WorldSpaceBlend.Slerp(Bones(), ~0, pose, layer, [1f, 1f], 0.5f);

        pose[0].Position.ShouldBe((0f, 0f, 0f));
        pose[1].Position.X.ShouldBe(2f, Tolerance);
    }

    /// <remarks>
    /// **A bone weighed one under a parent weighed one is copied** (:1316-1320), at full weight: the child takes the layer's
    /// local as it is, and the root, which has no parent to weigh, is solved, so it too takes the layer's turn.
    /// </remarks>
    [Test]
    public void Slerp_FullWeight_CopiesTheChildAndSolvesTheRoot()
    {
        StudioBonePose[] pose = Rest();

        WorldSpaceBlend.Slerp(Bones(), ~0, pose, Layer(), [1f, 1f], 1f);

        Same(pose[0].Rotation, RootTurn);
        pose[1].Rotation.ShouldBe(Layer()[1].Rotation, "copied, bit for bit");
    }

    /// <remarks>
    /// **`SkeletonPose` hands a `STUDIO_WORLD` layer to it** (`SlerpBones`, :1391-1395): a layer moving the root to x 10 at
    /// full weight leaves the root where the base put it. The same layer without the flag moves it, the control.
    /// </remarks>
    [TestCase(true, 0f, TestName = "Build_AWorldLayer_KeepsTheBasesRootPosition")]
    [TestCase(false, 10f, TestName = "Build_ALocalLayer_TakesTheLayersRootPosition")]
    public void Build_ALayer_BlendsInTheSpaceItsSequenceSays(bool world, float rootX)
    {
        SkeletonPose skeleton = new(Bones(), (sequence, _, _) => sequence == 0 ? Rest() : Moved())
        {
            Sequence = 0,
            Layers = [new PoseLayer(1, 0f, 1f, [1f, 1f], World: world)],
        };
        BoneAccessor into = new(2) { WritableBones = ~0, ReadableBones = ~0 };

        skeleton.Build(~0, 0d, into, new BoneBitList(2));

        into.Bone(0)[3].ShouldBe(rootX, Tolerance);
    }

    private static StudioBonePose[] Moved()
    {
        StudioBonePose[] layer = Layer();
        layer[0] = layer[0] with { Position = (10f, 0f, 0f) };

        return layer;
    }

    private static void Same((float X, float Y, float Z, float W) actual, Quaternion expected)
    {
        MathF.Abs(Quaternion.Dot(new Quaternion(actual.X, actual.Y, actual.Z, actual.W), expected)).ShouldBe(1f, Tolerance);
    }

    private static StudioBonePose[] Rest() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
    ];

    private static StudioBonePose[] Layer() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (RootTurn.X, RootTurn.Y, RootTurn.Z, RootTurn.W)),
        new StudioBonePose(1, (0f, 0f, 0f), (ChildTurn.X, ChildTurn.Y, ChildTurn.Z, ChildTurn.W)),
    ];

    private static IReadOnlyList<StudioBone> Bones() =>
    [
        new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone("child", 0, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
    ];
}
