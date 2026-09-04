using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A sequence's IK locks: where its chains were before it played, put back after.
/// </summary>
/// <remarks>
/// **This is what keeps a foot planted while the body turns over it.** `AccumulatePose` brackets
/// its whole body with two calls (`bone_setup.cpp:2425` and `:2451`) — `AddSequenceLocks` records
/// each locked chain's end effector BEFORE the sequence is applied, `SolveSequenceLocks` puts it
/// back afterwards, weighted:
///
/// <code>
///   CIKContext seq_ik;
///   if (seqdesc.numiklocks)
///   {
///       seq_ik.Init( m_pStudioHdr, vec3_angle, vec3_origin, 0.0, 0, m_boneMask );
///       seq_ik.AddSequenceLocks( seqdesc, pos, q );
///   }
///   ... the sequence is applied ...
///   if (seqdesc.numiklocks) seq_ik.SolveSequenceLocks( seqdesc, pos, q );
/// </code>
///
/// **Measured before it was written** (B310, B311): 1,333 of 26,387 sequences lock chains, 814 of
/// them under `models/player/` — `PRIMARY_aimmatrix_idle`, `PRIMARY_aimmatrix_run`,
/// `AttackStand_PRIMARY`. **All 2,666 locks carry a non-zero weight**, and they are uniformly
/// `flPosWeight` 1 and `flLocalQWeight` 0 on chains 2 and 3: both feet, pinned fully in position,
/// with their rotation left alone.
///
/// **So the symptom of not having this is that feet SLIDE.** The aim matrix turns the upper body,
/// the legs follow through the skeleton, and nothing holds the feet where they were.
///
/// **All of it is in the MODEL's space.** Valve's throwaway context takes `vec3_angle,
/// vec3_origin` under the comment *"local space relative so absolute position doesn't mater"*, so
/// the capture and the restore are comparable without knowing where the entity stands — no
/// placement, no world matrices.
///
/// **A lock's chain index needs no translation.** `CStudioHdr::pIKChain( i )` forwards straight to
/// the root header's array (`studio.h:2536`), unlike `paramindex`, which looks identical and must
/// be translated through `masterPose` — an omission that once made every player run backwards.
/// </remarks>
public sealed class IkLocks
{
    /// <summary>Which link of a chain is the end effector.</summary>
    /// <remarks>
    /// <c>pchain->pLink( 2 )->bone</c>, in both `AddSequenceLocks` and `SolveLock`. Valve indexes
    /// link two directly rather than taking the last, so a chain with more links locks its third
    /// and not its tip.
    /// </remarks>
    private const int EndLink = 2;

    private readonly BoneChain _chain;
    private readonly int[] _parents;
    private (Vector3 Position, Quaternion Rotation, bool Held)[] _held = [];

    /// <summary>How many locks this has actually solved.</summary>
    /// <remarks>
    /// **Carried out of the code that did the work, not recomputed by a second route** (B243). The
    /// question it answers is the one a unit test cannot: whether production ever reaches this at
    /// all. A branch written and never fed is the fault this audit keeps finding, three times in
    /// its own work this session — and `Studio_SolveIK` refusing a chain, or a lock naming a chain
    /// the model lacks, both leave it unincremented while everything still looks wired.
    /// </remarks>
    public int Applied { get; private set; }

    /// <summary>Prepares a lock bracket for one skeleton.</summary>
    /// <param name="parents">Each bone's parent, or −1 for a root.</param>
    /// <param name="bones">How many bones the skeleton has.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parents"/> is null.</exception>
    public IkLocks(IReadOnlyList<int> parents, int bones)
    {
        ArgumentNullException.ThrowIfNull(parents);

        _parents = [.. parents];
        _chain = new BoneChain(parents, bones);
    }

    /// <summary>Records where each locked chain's end effector is — <c>AddSequenceLocks</c>.</summary>
    /// <param name="locks">The sequence's locks.</param>
    /// <param name="chains">The model's IK chains, which a lock indexes directly.</param>
    /// <param name="pose">The local pose as it stands BEFORE the sequence is applied.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Position AND rotation, because the restore uses them differently** — the position is
    /// blended toward by `flPosWeight` and the rotation is slammed onto the end bone before the
    /// chain is rebuilt. Keeping only the position would leave a foot in the right place pointing
    /// wherever the new sequence turned it.
    /// </remarks>
    public void Capture(
        IReadOnlyList<StudioIkLock> locks,
        IReadOnlyList<StudioIkChain> chains,
        IReadOnlyList<StudioBonePose> pose)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(chains);
        ArgumentNullException.ThrowIfNull(pose);

        if (_held.Length < locks.Count)
        {
            _held = new (Vector3, Quaternion, bool)[locks.Count];
        }

        // **One build for all the locks, because they share a spine.** The memo is what makes that
        // worth doing in one pass rather than per lock.
        _chain.Reset();

        for (int at = 0; at < locks.Count; at++)
        {
            _held[at] = default;

            if (EffectorOf(locks[at], chains) is not { } bone)
            {
                continue;
            }

            _chain.Build(bone, pose);

            (Quaternion rotation, Vector3 position) = Decompose(_chain.Matrix(bone));

            _held[at] = (position, rotation, true);
        }
    }

    /// <summary>Puts each locked chain back — <c>SolveSequenceLocks</c>.</summary>
    /// <param name="locks">The same locks <see cref="Capture"/> was given.</param>
    /// <param name="chains">The model's IK chains.</param>
    /// <param name="pose">The local pose AFTER the sequence, written in place.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<c>SolveLock</c>, `bone_setup.cpp:4295`**, and the order of its four steps is the whole of
    /// it: blend the position, solve the chain onto it, slam the end bone's orientation, then
    /// rebuild the three bones back into local space — with the end bone's own rotation slerped
    /// back toward what the sequence gave it by <c>flLocalQWeight</c>.
    ///
    /// <code>
    ///   p3 = p1 * (1.0 - plock->flPosWeight ) + m_ikLock[i].pos * plock->flPosWeight;
    ///   Studio_SolveIK( pchain, p3, boneToWorld );
    ///   MatrixPosition( boneToWorld[bone], p3 );
    ///   QuaternionMatrix( m_ikLock[i].q, p3, boneToWorld[bone] );
    ///   q2 = q[ bone ];
    ///   SolveBone( … pLink( 2 )->bone … );  QuaternionSlerp( q[bone], q2, plock->flLocalQWeight, q[bone] );
    ///   SolveBone( … pLink( 1 )->bone … );
    ///   SolveBone( … pLink( 0 )->bone … );
    /// </code>
    ///
    /// **The slerp goes from the SOLVED rotation toward the one the sequence produced**, not the
    /// other way, and `flLocalQWeight` of 0 therefore keeps the solve's answer. Every TF2 lock uses
    /// 0, so reading this backwards would be invisible on the common case and wrong on the rare one.
    ///
    /// **The rebuild runs end-first.** Link two's local transform is taken while link one still
    /// holds its old matrix, and so on down — reversing it would convert each bone through a parent
    /// that had already moved.
    /// </remarks>
    public void Solve(
        IReadOnlyList<StudioIkLock> locks,
        IReadOnlyList<StudioIkChain> chains,
        StudioBonePose[] pose)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(chains);
        ArgumentNullException.ThrowIfNull(pose);

        // **A second build, not the captured one.** The pose has changed underneath, which is the
        // entire point of the bracket; reusing the memo would compare the sequence's result against
        // itself and move nothing.
        _chain.Reset();

        for (int at = 0; at < locks.Count && at < _held.Length; at++)
        {
            if (!_held[at].Held || EffectorOf(locks[at], chains) is not { } bone)
            {
                continue;
            }

            StudioIkChain chain = chains[locks[at].Chain];

            _chain.Build(bone, pose);

            // Build the other two links as well: the solver reads all three positions.
            _chain.Build(chain.Links[0].Bone, pose);
            _chain.Build(chain.Links[1].Bone, pose);

            Vector3 now = Position(_chain.Matrix(bone));
            float weight = Math.Clamp(locks[at].PositionWeight, 0f, 1f);

            Vector3 wanted = (now * (1f - weight)) + (_held[at].Position * weight);

            if (!IkContext.SolveChain(chain, wanted, _chain.Bones))
            {
                continue;
            }

            // "slam orientation" — the solver only positions, so the remembered rotation is put
            // onto the end bone while the solve's position is kept.
            Compose(_held[at].Rotation, Position(_chain.Matrix(bone)), _chain.Bones.BoneForWrite(bone));

            (float X, float Y, float Z, float W) sequenced = pose[bone].Rotation;

            Rebuild(bone, pose);

            pose[bone] = pose[bone] with
            {
                Rotation = StudioBones.Slerp(
                    pose[bone].Rotation,
                    sequenced,
                    Math.Clamp(locks[at].LocalRotationWeight, 0f, 1f)),
            };

            Rebuild(chain.Links[1].Bone, pose);
            Rebuild(chain.Links[0].Bone, pose);

            // Counted here rather than at the top of the loop, so a lock that named nothing or
            // whose chain the solver refused is not reported as work done.
            Applied++;
        }
    }

    /// <summary>Which bone a lock pins, or null when it names nothing this model has.</summary>
    private static int? EffectorOf(StudioIkLock held, IReadOnlyList<StudioIkChain> chains)
    {
        if (held.Chain < 0 || held.Chain >= chains.Count ||
            chains[held.Chain].Links.Count <= EndLink)
        {
            return null;
        }

        int bone = chains[held.Chain].Links[EndLink].Bone;

        return bone >= 0 ? bone : null;
    }

    /// <summary>Turns a solved model matrix back into a local one — <c>SolveBone</c>.</summary>
    private void Rebuild(int bone, StudioBonePose[] pose)
    {
        if (bone < 0 || bone >= pose.Length || bone >= _parents.Length)
        {
            return;
        }

        int parent = _parents[bone];

        Span<float> local = stackalloc float[12];

        if (parent < 0 || parent >= _parents.Length)
        {
            _chain.Matrix(bone).CopyTo(local);
        }
        else
        {
            Span<float> inverted = stackalloc float[12];

            StudioBones.Invert(_chain.Matrix(parent), inverted);
            StudioBones.Concatenate(inverted, _chain.Matrix(bone), local);
        }

        (float x, float y, float z, float w) = StudioBones.ToQuaternion(local);

        pose[bone] = new StudioBonePose(bone, (local[3], local[7], local[11]), (x, y, z, w));
    }

    private static Vector3 Position(ReadOnlySpan<float> matrix) =>
        new(matrix[3], matrix[7], matrix[11]);

    private static (Quaternion Rotation, Vector3 Position) Decompose(ReadOnlySpan<float> matrix)
    {
        (float x, float y, float z, float w) = StudioBones.ToQuaternion(matrix);

        return (new Quaternion(x, y, z, w), Position(matrix));
    }

    private static void Compose(Quaternion rotation, Vector3 position, Span<float> matrix) =>
        StudioBones.FromQuaternion(
            (rotation.X, rotation.Y, rotation.Z, rotation.W),
            (position.X, position.Y, position.Z),
            matrix);
}
