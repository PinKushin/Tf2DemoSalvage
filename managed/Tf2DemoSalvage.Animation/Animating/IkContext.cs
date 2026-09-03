using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// What one chain is being asked to reach — Valve's <c>ikchainresult_t</c>.
/// </summary>
internal struct IkChainResult
{
    /// <summary>Where the chain's end should end up, in world space.</summary>
    public Vector3 Position;

    /// <summary>How it should be turned there.</summary>
    public Quaternion Rotation;

    /// <summary>How strongly, zero meaning the chain is left alone.</summary>
    public float Weight;
}

/// <summary>
/// Applies an animation's IK rules to a built skeleton — Valve's <c>CIKContext</c>.
/// </summary>
/// <remarks>
/// **Only <c>IK_SELF</c> does work, and that is a measurement rather than a simplification.** Of the
/// scout's 2035 IK rules, 1829 are <c>IK_RELEASE</c> — which tells a chain to let go and solves
/// nothing — 206 are <c>IK_SELF</c>, and there are ZERO of the other four types. The heavy agrees.
/// So the whole of TF2's IK is a hand held to another bone on the same model: an off hand on a
/// weapon's grip (B296).
///
/// **There is no <c>IK_GROUND</c> in TF2 at all**, which is worth stating because the famous symptom
/// of missing IK is a foot sliding instead of planting. That is not what this fixes and never could
/// be — the engine is never asked to plant a TF2 player's foot.
///
/// **The order is Valve's and it matters.** Every chain's current end position is read FIRST, from
/// the skeleton as the animation left it; then the rules blend a target over those; then each chain
/// with any weight is solved and its three bones written back. Reading a chain's current position
/// after another chain had been solved would feed one correction into the next.
/// </remarks>
public sealed class IkContext
{
    /// <summary>Which link of a chain is its end — the one that reaches the target.</summary>
    /// <remarks>
    /// **Valve indexes <c>pLink(2)</c> throughout**, hard-coded: the solver is a TWO-bone solver,
    /// so a chain is a hip, a knee and a foot whatever its links are called. A chain with fewer
    /// than three links has no end to reach and is skipped rather than clamped.
    /// </remarks>
    private const int EndLink = 2;

    /// <summary>Scratch, one per chain, reused across frames.</summary>
    private IkChainResult[] _results = [];

    /// <summary>Where each chain's end started, before any rule touched it.</summary>
    private IkChainResult[] _original = [];

    /// <summary>How many chains were solved on the last pass.</summary>
    /// <remarks>
    /// **Carried from where the work happened** (B243), so a diagnostic can report what ran rather
    /// than what the model declares. A model declaring four chains and solving none is the wiring
    /// question worth asking, and a count derived from the model could not tell them apart.
    /// </remarks>
    public int Solved { get; private set; }

    /// <summary>Applies every rule of one animation to a built skeleton.</summary>
    /// <param name="chains">The model's IK chains.</param>
    /// <param name="errors">
    /// Each rule that asked for something, with where it wants its chain and how strongly —
    /// gathered from every accumulated sequence, so the rule travels with its target rather than
    /// being indexed into one animation's list.
    /// </param>
    /// <param name="bones">The skeleton, in world space, rewritten in place.</param>
    /// <param name="parents">Each bone's parent, for rebuilding the chain afterwards.</param>
    /// <param name="local">Each bone's local matrix, rewritten for the three that moved.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<c>CIKContext::SolveDependencies</c>, <c>bone_setup.cpp:4046</c>**, reduced to the one
    /// rule type TF2 uses. Each rule's error is a transform in its target bone's space:
    ///
    /// <code>
    ///   QuaternionMatrix( pRule->q, pRule->pos, local );
    ///   ConcatTransforms( boneToWorld[pRule->bone], local, worldTarget );
    ///   float flWeight = pRule->flWeight * pRule->flRuleWeight;
    ///   pChainResult->flWeight = pChainResult->flWeight * (1 - flWeight) + flWeight;
    ///   MatrixAngles( worldTarget, q2, p2 );
    ///   pChainResult->pos = pChainResult->pos * (1.0 - flWeight) + p2 * flWeight;
    ///   QuaternionSlerp( pChainResult->q, q2, flWeight, pChainResult->q );
    /// </code>
    ///
    /// **The chain's accumulated weight is not a sum**, it is
    /// <c>w' = w(1 − f) + f</c> — the same shape as the blend beside it, so two rules on one chain
    /// approach one rather than exceeding it.
    ///
    /// **After the solve the end bone's ROTATION is forced**, not left as the solver placed it:
    /// the solver only positions, and `QuaternionMatrix( pChainResult->q, p3, … )` then turns the
    /// end bone to the target's orientation while keeping the position the solve produced.
    /// </remarks>
    public void Solve(
        IReadOnlyList<StudioIkChain> chains,
        IReadOnlyList<(StudioIkRule Rule, Vector3 Position, Quaternion Rotation, float Weight)> errors,
        BoneAccessor bones,
        IReadOnlyList<int> parents,
        IReadOnlyList<float[]> local)
    {
        ArgumentNullException.ThrowIfNull(chains);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(local);

        Solved = 0;

        if (chains.Count == 0 || errors.Count == 0)
        {
            return;
        }

        if (_results.Length < chains.Count)
        {
            _results = new IkChainResult[chains.Count];
            _original = new IkChainResult[chains.Count];
        }

        // **Every chain's CURRENT end is read first, before any rule is applied.** Valve's own loop
        // does this over all chains up front; taking one chain's position after another had been
        // solved would feed a correction into a neighbour.
        for (int chain = 0; chain < chains.Count; chain++)
        {
            _results[chain] = default;

            if (chains[chain].Links.Count <= EndLink)
            {
                continue;
            }

            int bone = chains[chain].Links[EndLink].Bone;

            if (bone < 0 || bone >= parents.Count)
            {
                continue;
            }

            (_results[chain].Rotation, _results[chain].Position) = Decompose(bones.Bone(bone));

            // **Kept unblended as well, because `IK_RELEASE` asks for it back.** Valve re-derives
            // it per rule with `BuildBoneChain( pos, q, bone, … )`, which reads the LOCAL pose and
            // so returns the animation's own answer however many rules have already blended into
            // the chain result. Reading `_results[chain]` a second time would return the running
            // blend instead, and a release would then pull toward its own previous output.
            _original[chain] = _results[chain];
        }

        // **Outside the loop, because a stackalloc inside one accumulates.** Each iteration would
        // take another 96 bytes that are not released until the method returns, and the loop runs
        // once per rule — which for a model with many rules on one animation is a stack that grows
        // with the content. Same buffers, reused.
        Span<float> error = stackalloc float[12];
        Span<float> target = stackalloc float[12];

        // **A rule per entry rather than an index into one list, because the rules come from
        // SEVERAL sequences.** `AccumulatePose` calls `AddDependencies` for every sequence it
        // accumulates — the main one and each autolayer — so two rules with the same index in
        // different animations are different rules. Carrying the rule removes the collision.
        foreach ((StudioIkRule declared, Vector3 position, Quaternion rotation, float weight) in
            errors)
        {
            if (weight <= 0f)
            {
                continue;
            }

            if (declared.Chain < 0 || declared.Chain >= chains.Count)
            {
                continue;
            }

            ref IkChainResult result = ref _results[declared.Chain];

            // **`IK_RELEASE` moves the target back toward where the animation had the chain, and
            // it does NOT raise the chain's weight** — `bone_setup.cpp:4128`, under Valve's own
            // comment *"move target back towards original location"*:
            //
            //     float flWeight = pRule->flWeight * pRule->flRuleWeight;
            //     BuildBoneChain( pos, q, bone, boneToWorld, boneComputed );
            //     MatrixAngles( boneToWorld[bone], q2, p2 );
            //     pChainResult->pos = pChainResult->pos * (1.0 - flWeight) + p2 * flWeight;
            //     QuaternionSlerp( pChainResult->q, q2, flWeight, pChainResult->q );
            //
            // **It is the type TF2 declares most, by eight to one.** Measured over every animation
            // of every model `z1800` draws: 1674 `IK_SELF` against 13359 `IK_RELEASE`, and zero of
            // the other four. Treating it as "solves nothing" — which the weight line makes look
            // true — applied every self correction at full strength.
            if (declared.Type == StudioIkRuleType.Release)
            {
                IkChainResult was = _original[declared.Chain];

                result.Position = (result.Position * (1f - weight)) + (was.Position * weight);
                result.Rotation = Quaternion.Slerp(result.Rotation, was.Rotation, weight);

                continue;
            }

            // **The other four types do nothing here, and that is Valve's code rather than a
            // simplification.** `IK_WORLD` is `Assert( 0 )`; `IK_ATTACHMENT` and `IK_GROUND` are
            // bare `break`s, because their work happens through `CIKTarget` in `UpdateTargets`;
            // `IK_UNLATCH`'s body is commented out entirely. TF2 declares none of the four.
            if (declared.Type != StudioIkRuleType.Self ||
                declared.Bone < 0 ||
                declared.Bone >= parents.Count)
            {
                continue;
            }

            // The error, as a transform in the target bone's own space, taken to world space.
            Compose(rotation, position, error);

            StudioBones.Concatenate(bones.Bone(declared.Bone), error, target);

            (Quaternion turned, Vector3 placed) = Decompose(target);

            result.Weight = (result.Weight * (1f - weight)) + weight;

            result.Position = (result.Position * (1f - weight)) + (placed * weight);
            result.Rotation = Quaternion.Slerp(result.Rotation, turned, weight);
        }

        for (int chain = 0; chain < chains.Count; chain++)
        {
            if (_results[chain].Weight <= 0f || chains[chain].Links.Count <= EndLink)
            {
                continue;
            }

            if (Reach(chains[chain], _results[chain], bones, parents, local))
            {
                Solved++;
            }
        }
    }

    /// <summary>Solves one chain and writes its three bones back.</summary>
    /// <remarks>
    /// **The rebuild is the half that is easy to leave out.** `Studio_SolveIK` writes world
    /// matrices; everything downstream reads LOCAL positions and rotations, so the three bones have
    /// to be converted back through their parents — Valve calls `SolveBone` on links two, one and
    /// zero, in that order (<c>bone_setup.cpp:4219</c>). Skipping it leaves the world matrices
    /// right and every later stage reading the pose the animation had.
    /// </remarks>
    private static bool Reach(
        StudioIkChain chain,
        IkChainResult result,
        BoneAccessor bones,
        IReadOnlyList<int> parents,
        IReadOnlyList<float[]> local)
    {
        int thigh = chain.Links[0].Bone;
        int knee = chain.Links[1].Bone;
        int foot = chain.Links[EndLink].Bone;

        if (thigh < 0 || knee < 0 || foot < 0 ||
            thigh >= parents.Count || knee >= parents.Count || foot >= parents.Count)
        {
            return false;
        }

        if (!SolveChain(chain, result.Position, bones))
        {
            return false;
        }

        // "force angle" — the solver positions, and the target's own rotation is then applied to
        // the end bone while keeping the position the solve produced.
        Vector3 reached = new(
            bones.Bone(foot)[3], bones.Bone(foot)[7], bones.Bone(foot)[11]);

        Compose(result.Rotation, reached, bones.BoneForWrite(foot));

        Rebuild(foot, bones, parents, local);
        Rebuild(knee, bones, parents, local);
        Rebuild(thigh, bones, parents, local);

        return true;
    }

    /// <summary>Runs the two-bone solver for a chain — <c>Studio_SolveIK</c>'s dispatcher.</summary>
    /// <remarks>
    /// **<c>bone_setup.cpp:2690</c>**, and the branch is on whether the chain states a knee
    /// direction: with one, the direction is rotated into world space by the first link's own
    /// matrix and the knee's current position is used as the preference; without one, the solver
    /// derives a preference from where the animation already had the knee.
    /// </remarks>
    private static bool SolveChain(StudioIkChain chain, Vector3 target, BoneAccessor bones)
    {
        int thigh = chain.Links[0].Bone;
        int knee = chain.Links[1].Bone;
        int foot = chain.Links[EndLink].Bone;

        Vector3 atThigh = Origin(bones.Bone(thigh));
        Vector3 atKnee = Origin(bones.Bone(knee));
        Vector3 atFoot = Origin(bones.Bone(foot));

        float upper = (atKnee - atThigh).Length();
        float lower = (atFoot - atKnee).Length();

        Vector3 wanted = target - atThigh;
        Vector3 preference;

        Vector3 stated = new(
            chain.Links[0].KneeDirection.X,
            chain.Links[0].KneeDirection.Y,
            chain.Links[0].KneeDirection.Z);

        if (stated.LengthSquared() > 0f)
        {
            // `VectorRotate( tmp, pBoneToWorld[ pLink(0)->bone ], targetKneeDir )` — the direction
            // is in the thigh's space and has to be turned into the world, without translating.
            preference = Rotate(bones.Bone(thigh), stated);

            // Valve exaggerates the preference for a nearly straight leg, and the distance it uses
            // is the reach less the shorter link, floored at the chain's own length, times a
            // hundred. Reproduced rather than normalised, because Valve's own note beside it says
            // a too-short knee direction is what causes trouble — so the exaggeration is the fix
            // rather than the problem.
            float span = MathF.Max(
                upper + lower, wanted.Length() - MathF.Min(upper, lower)) * 100f;

            preference = (atKnee - atThigh) + (preference * span);
        }
        else
        {
            // The no-direction overload: the knee's offset from the straight line between thigh and
            // foot, which is where the animation already had it.
            float straight = (atFoot - atThigh).Length();

            if (straight > (upper + lower) * StudioIkSolver.StraightEnough)
            {
                return false;
            }

            Vector3 half = (atFoot - atThigh) * (straight > 0f ? upper / straight : 0f);

            preference = (atKnee - atThigh) - half;
        }

        // "too far away?" and "too close?" — the reach is clamped at both ends before solving.
        float limit = (upper + lower) * StudioIkSolver.StraightEnough;

        if (wanted.Length() > limit)
        {
            wanted = Vector3.Normalize(wanted) * limit;
        }

        float closest = MathF.Max(
            MathF.Abs(upper - lower) * 1.15f, MathF.Min(upper, lower) * 0.15f);

        if (wanted.Length() < closest)
        {
            Vector3 original = atFoot - atThigh;

            wanted = original.LengthSquared() > 0f
                ? Vector3.Normalize(original) * closest
                : new Vector3(closest, 0f, 0f);
        }

        if (!StudioIkSolver.Solve(upper, lower, wanted, preference, out Vector3 solvedKnee))
        {
            return false;
        }

        StudioIkSolver.Align(bones.BoneForWrite(thigh), solvedKnee);
        StudioIkSolver.Align(bones.BoneForWrite(knee), wanted - solvedKnee);

        Place(bones.BoneForWrite(knee), solvedKnee + atThigh);
        Place(bones.BoneForWrite(foot), wanted + atThigh);

        return true;
    }

    /// <summary>Turns a world matrix back into a local one — <c>SolveBone</c>.</summary>
    /// <remarks>
    /// <c>MatrixInvert( pBoneToWorld[iParent], worldToBone ); ConcatTransforms( worldToBone,
    /// pBoneToWorld[iBone], local ); MatrixAngles( local, q[iBone], pos[iBone] );</c>
    /// (<c>bone_setup.cpp:3501</c>). A root bone has no parent to invert and is left alone.
    /// </remarks>
    private static void Rebuild(
        int bone,
        BoneAccessor bones,
        IReadOnlyList<int> parents,
        IReadOnlyList<float[]> local)
    {
        int parent = parents[bone];

        if (parent < 0 || parent >= parents.Count || bone >= local.Count)
        {
            return;
        }

        Span<float> inverted = stackalloc float[12];

        StudioBones.Invert(bones.Bone(parent), inverted);
        StudioBones.Concatenate(inverted, bones.Bone(bone), local[bone]);
    }

    /// <summary>The position a bone matrix carries.</summary>
    private static Vector3 Origin(ReadOnlySpan<float> matrix) =>
        new(matrix[3], matrix[7], matrix[11]);

    /// <summary>Writes a position into a bone matrix, leaving its rotation alone.</summary>
    private static void Place(Span<float> matrix, Vector3 at)
    {
        matrix[3] = at.X;
        matrix[7] = at.Y;
        matrix[11] = at.Z;
    }

    /// <summary>Rotates a direction by a matrix without translating it — <c>VectorRotate</c>.</summary>
    private static Vector3 Rotate(ReadOnlySpan<float> matrix, Vector3 direction) =>
        new(
            (matrix[0] * direction.X) + (matrix[1] * direction.Y) + (matrix[2] * direction.Z),
            (matrix[4] * direction.X) + (matrix[5] * direction.Y) + (matrix[6] * direction.Z),
            (matrix[8] * direction.X) + (matrix[9] * direction.Y) + (matrix[10] * direction.Z));

    /// <summary>A matrix as a rotation and a position — <c>MatrixAngles</c>.</summary>
    private static (Quaternion Rotation, Vector3 Position) Decompose(ReadOnlySpan<float> matrix)
    {
        (float x, float y, float z, float w) = StudioBones.ToQuaternion(matrix);

        return (new Quaternion(x, y, z, w), new Vector3(matrix[3], matrix[7], matrix[11]));
    }

    /// <summary>A rotation and a position as a matrix — <c>QuaternionMatrix</c>.</summary>
    private static void Compose(Quaternion rotation, Vector3 position, Span<float> matrix) =>
        StudioBones.FromQuaternion(
            (rotation.X, rotation.Y, rotation.Z, rotation.W),
            (position.X, position.Y, position.Z),
            matrix);
}
