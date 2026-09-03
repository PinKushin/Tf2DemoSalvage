using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A delta sequence that also carries a blend grid must stay a difference through the grid.
/// </summary>
/// <remarks>
/// **The grid is a second densifying step, and the first version of it dropped the flag** (B298).
/// `Locals` blends the up-to-three grid corners into one pose, and blending two SPARSE poses means
/// filling in the bones neither corner lists. What to fill them with is the whole question:
///
/// <code>
///   if (animdesc.flags &amp; STUDIO_DELTA) { q[i].Init( 0,0,0,1 ); pos[i].Init( 0,0,0 ); }
///   else                                { q[i] = pSeqbone[j].quat; pos[i] = pSeqbone[j].pos; }
/// </code>
///
/// (<c>CalcVirtualAnimation</c>, <c>bone_setup.cpp:933</c>.) The FRAME blend beside it was told;
/// the grid blend was not, so a delta grid came back holding whole bind transforms where it should
/// have held nothing.
///
/// **This is not a hypothetical shape — it is every TF2 player's aim matrix.**
/// `PRIMARY_aimmatrix_idle` is a 3x4 grid, delta on the sequence and on the animation behind it,
/// and `stand_PRIMARY` reaches it by autolayer. Measured on `z1800` before the fix: its root bone
/// arrived carrying a 63-degree rotation and a 14-unit offset, which is a bind pose rather than a
/// difference — and added over the body at full weight it turned seven of fifteen players upside
/// down, head some seventy units BELOW the foot.
/// </remarks>
public sealed class DeltaBlendGridTests
{
    /// <summary><c>STUDIO_DELTA</c> — <c>studio.h:3080</c>.</summary>
    private const int Delta = 0x0004;

    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    [Test]
    public void Locals_ADeltaSequenceWithABlendGrid_LeavesAnUnlistedBoneAtIdentity()
    {
        PropModels.SkinnedModel model = Grid(delta: true);

        IReadOnlyList<StudioBonePose> pose = model.Locals(0, 0, 0f, [0.5f]);

        StudioBonePose root = Root(pose);

        root.Rotation.X.ShouldBe(0f, Tolerance, "a delta's unlisted bone rotates by nothing");
        root.Rotation.W.ShouldBe(1f, Tolerance);
        root.Position.Y.ShouldBe(0f, Tolerance, "and it is displaced by nothing");
    }

    /// <remarks>
    /// **The control, and it is what makes the test above mean anything.** The same fixture without
    /// the delta bit must come back carrying the bind pose — so a fix that simply zeroed every
    /// unlisted bone would redden here. Without it, "the grid honours the flag" and "the grid
    /// ignores every bone it was not given" are the same observation.
    /// </remarks>
    [Test]
    public void Locals_AnOrdinarySequenceWithABlendGrid_LeavesAnUnlistedBoneAtItsRest()
    {
        PropModels.SkinnedModel model = Grid(delta: false);

        IReadOnlyList<StudioBonePose> pose = model.Locals(0, 0, 0f, [0.5f]);

        StudioBonePose root = Root(pose);

        root.Rotation.X.ShouldBe(RestRotationX, Tolerance, "an ordinary animation seeds from rest");
        root.Position.Y.ShouldBe(RestHeight, Tolerance);
    }

    /// <summary>The rest rotation's X, chosen large enough that no rounding could reach it.</summary>
    private const float RestRotationX = 0.5f;

    /// <summary>The rest position's height, in the same shape as the aim matrix's own 14 units.</summary>
    private const float RestHeight = 14f;

    /// <summary>The bone the assertions read, found by number rather than by position in the list.</summary>
    private static StudioBonePose Root(IReadOnlyList<StudioBonePose> pose)
    {
        foreach (StudioBonePose one in pose)
        {
            if (one.Bone == 0)
            {
                return one;
            }
        }

        return default;
    }

    /// <summary>
    /// A one-bone model whose only sequence blends a two-cell grid, optionally as a delta.
    /// </summary>
    /// <remarks>
    /// **Neither cell animates anything**, which is the condition that exposes the seeding: with no
    /// channels in the file every bone of the result comes from whatever the blend fills it with,
    /// so the fill IS the measurement. A fixture whose animations moved the bone would report what
    /// the animation said and never reach the branch.
    ///
    /// **The delta bit is written into the animation descriptors**, at <c>animdesc.flags</c>,
    /// because that is the field `CalcVirtualAnimation` tests and the field production reads. Set
    /// on the sequence record alone it would leave the byte-level path untested.
    /// </remarks>
    private static PropModels.SkinnedModel Grid(bool delta)
    {
        byte[] file = AnimatedStudioBytes.OneSecondLoop(
            animations: 2, sequences: 1, autoLayerOn: -1, autoLayers: null, delta: delta);

        List<StudioBone> bones =
        [
            new StudioBone(
                Name: "root",
                Parent: -1,
                Position: (0f, RestHeight, 0f),
                Rotation: (RestRotationX, 0f, 0f, 0.8660254f),
                PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: ~0),
        ];

        StudioBlendGrid grid = new(
            groupX: 2,
            groupY: 1,
            animations: [0, 1],
            parameterX: 0,
            parameterY: -1,
            startX: 0f,
            endX: 1f,
            startY: 0f,
            endY: 0f);

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
        [
            (0, [new StudioSequence(
                Animation: 0,
                Flags: delta ? Delta : 0,
                Label: "aim",
                Blend: grid,
                Activity: "idle",
                ActivityWeight: 1)]),
        ];

        return new PropModels.SkinnedModel(
            Bones: bones,
            Models: [file],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [new StudioPoseParameter("body_yaw", 0f, 1f, 0f)],
            MasterPose: [[0]]);
    }
}
