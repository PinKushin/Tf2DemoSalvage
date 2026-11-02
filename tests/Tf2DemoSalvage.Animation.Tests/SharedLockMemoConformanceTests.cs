using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>One sequence's locks share one memo of built bones, as <c>SolveSequenceLocks</c>'s do.</summary>
/// <remarks>
/// **Valve builds one <c>CBoneBitList</c> per <c>SolveSequenceLocks</c> and hands it to every <c>SolveLock</c>**
/// (<c>bone_setup.cpp:4301-4309</c>). A lock marks its end bone and the ancestors <c>BuildBoneChain</c> walked
/// (<c>:4419</c>), then <c>Studio_SolveIK</c> and the slam write <c>boneToWorld</c> for its three bones (<c>:4437-4442</c>)
/// without unmarking them. So a LATER lock whose chain hangs below that end bone builds on the SLAMMED matrix — the held
/// rotation at the solved position — not on a re-concatenation of the local pose that <c>SolveBone</c> and the
/// <c>flLocalQWeight</c> slerp then wrote (<c>:4446-4450</c>).
///
/// **The arrangement**: a leg (links 1, 2, 3) and a second leg (4, 5, 6) hanging off the first one's foot, bone 3. The base
/// is the bind pose, so both locks capture it: bone 3 at (20, 0, 0) unrotated, bone 6 at (45, 0, 0). The layer moves the
/// root up 2 and turns bone 3 a quarter about Z, and lock 1's rotation weight is 1, so bone 3 ends turned while its slam was
/// unturned.
///
/// **Valve's answer**: under the slammed (unturned) bone 3, bone 6 already sits at its held (45, 0, 0), so lock 2 solves to
/// where it is and the second leg keeps its bind locals; the final skeleton then hangs it 25 units along bone 3's FINAL,
/// turned X axis. A memo re-walked after lock 1 instead builds lock 2 on the turned bone 3, solves the foot back toward
/// (45, 0, 0), and the second leg does not follow bone 3's turn.
/// </remarks>
public sealed class SharedLockMemoConformanceTests
{
    private const int Everything = ~0;
    private const float Tolerance = 1e-3f;

    [Test]
    public void Solve_ALockBelowAnEarlierLocksEndBone_BuildsOnTheSlammedMatrix()
    {
        StudioBonePose[] rest =
        [
            new(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(2, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
            new(3, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
            new(4, (5f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(5, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
            new(6, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
        ];
        int[] parents = [-1, 0, 1, 2, 3, 4, 5];
        StudioBone[] bones =
        [
            .. rest.Select(pose => new StudioBone(
                $"bone{pose.Bone}",
                parents[pose.Bone],
                pose.Position,
                pose.Rotation,
                new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: StudioBoneFlags.UsedByAnything)),
        ];
        StudioBonePose[] layer =
        [
            new(0, (0f, 2f, 0f), (0f, 0f, 0f, 1f)),
            new(3, (10f, 0f, -5f), (0f, 0f, 0.70710677f, 0.70710677f)),
        ];
        SkeletonPose pose = new(bones, (sequence, _, _, _) => sequence == 0 ? rest : layer)
        {
            IkChains =
            [
                new StudioIkChain("upper", 0, [new StudioIkLink(1, (0f, 0f, 0f)), new StudioIkLink(2, (0f, 0f, 0f)), new StudioIkLink(3, (0f, 0f, 0f))]),
                new StudioIkChain("lower", 0, [new StudioIkLink(4, (0f, 0f, 0f)), new StudioIkLink(5, (0f, 0f, 0f)), new StudioIkLink(6, (0f, 0f, 0f))]),
            ],
            Layers =
            [
                new PoseLayer(
                    1,
                    0,
                    0f,
                    1f,
                    [1f, 1f, 1f, 1f, 1f, 1f, 1f],
                    Locks:
                    [
                        new StudioIkLock(Chain: 0, PositionWeight: 1f, LocalRotationWeight: 1f, Flags: 0),
                        new StudioIkLock(Chain: 1, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0),
                    ]),
            ],
        };

        BoneAccessor built = new(bones.Length);
        pose.Build(Everything, 0d, built, new BoneBitList(bones.Length));

        float[] foot = built.BoneForWrite(3);
        float[] lower = built.BoneForWrite(6);

        pose.AppliedLocks.ShouldBe(2, "the control: both locks solved");
        foot[3].ShouldBe(20f, Tolerance, "the control: lock 1 pinned bone 3 to its bind X");
        foot[7].ShouldBe(0f, Tolerance, "the control: lock 1 pinned bone 3 to its bind Y");
        System.MathF.Abs(foot[4]).ShouldBeGreaterThan(0.5f, "the control: bone 3 ends turned, so its X axis leaves the X direction");

        // Bone 6 hangs 25 units along bone 3's final X axis: the matrix's first column.
        lower[3].ShouldBe(foot[3] + (25f * foot[0]), Tolerance, "X");
        lower[7].ShouldBe(foot[7] + (25f * foot[4]), Tolerance, "Y");
        lower[11].ShouldBe(foot[11] + (25f * foot[8]), Tolerance, "Z");
    }
}
