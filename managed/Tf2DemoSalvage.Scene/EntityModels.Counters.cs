using System;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>How many jiggle bones the spring simulation ran on, across every entity posed.</summary>
    /// <remarks>
    /// **The only thing that can say the simulation is WIRED** (B293). The reader, the flag test and
    /// the physics each have their own tests and all three passing says nothing about whether the
    /// pose path reaches them — which is the shape that has shipped three no-ops here with a green
    /// suite. This counts what actually ran.
    ///
    /// **Summed rather than per entity**, because the question it answers is about the demo: does
    /// anything on screen jiggle at all. A per-entity number is one dictionary lookup away for
    /// anyone who needs it.
    /// </remarks>
    public int JigglingBones
    {
        get
        {
            int simulated = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    simulated += posed.JigglingBones;
                }
            }

            return simulated;
        }
    }

    /// <summary>
    /// How many bones the last pose pass posed by a quaternion-interpolation rule, across every
    /// entity.
    /// </summary>
    /// <remarks>
    /// **The only thing that can say `STUDIO_PROC_QUATINTERP` is WIRED** (B317), and it is here for
    /// the reason its jiggle neighbour is: the reader, the arithmetic and the dispatch each have
    /// their own tests, and every one of them passing says nothing about whether a real model on a
    /// real demo reaches the rule. Summed from the poses rather than counted from what the models
    /// DECLARE, since a number derived by a second route stays right while the code does nothing.
    /// </remarks>
    public int QuatInterpBones
    {
        get
        {
            int driven = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    driven += posed.QuatInterpBonesBuilt;
                }
            }

            return driven;
        }
    }

    /// <summary>The furthest any quaternion-interpolation rule moved a bone, across every entity.</summary>
    /// <remarks>
    /// **The other half of the pair above**, and the one that says the rule changed the picture
    /// rather than merely running — a magnitude, like `IkLocks`' furthest move. Zero with a non-zero
    /// count would mean the triggers reproduce the animated pose, which is a real possible outcome
    /// and not one a count can report.
    /// </remarks>
    public float QuatInterpFurthestMove
    {
        get
        {
            float furthest = 0f;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    furthest = Math.Max(furthest, posed.QuatInterpFurthestMove);
                }
            }

            return furthest;
        }
    }

    /// <summary>How many IK chains the last pose pass actually solved, across every entity.</summary>
    /// <remarks>
    /// **The only thing that can say IK is WIRED** (B296). The solver, the rule reader, the error
    /// decode and the context each have their own tests, and all of them passing says nothing about
    /// whether the pose path reaches them — the shape that has shipped three no-ops here with a
    /// green suite. This counts chains that actually reached the solver.
    /// </remarks>
    public int SolvedIkChains
    {
        get
        {
            int solved = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    solved += posed.SolvedChains;
                }
            }

            return solved;
        }
    }

    /// <summary>The flinch every class model declares, and the one the others fall back to.</summary>
    /// <remarks>
    /// **`ACT_MP_GESTURE_FLINCH_CHEST` is the engine's own literal**
    /// (<c>multiplayer_animstate.cpp:376</c>), not a choice of ours — the substitution names it
    /// twice in four lines, as both the guard and the replacement.
    /// </remarks>
    private const string FlinchChestActivity = "ACT_MP_GESTURE_FLINCH_CHEST";

    /// <summary>Flinches that fell back to the chest animation — the wiring check for B350.</summary>
    /// <remarks>
    /// **Counted where the substitution happens, and only when it SUCCEEDS.** A conformance test
    /// proves the fallback picks chest when asked; only this says a real demo asks. Zero here beside
    /// a demo that fires 69 non-chest flinch events would mean the events never reach the gesture
    /// feed, which is a different defect from the one this fixes.
    /// </remarks>
    public int SubstitutedFlinches { get; private set; }

    /// <summary>What this set has drawn and failed to draw over its whole life.</summary>
    /// <remarks>
    /// **Exposed so a whole recording can be audited rather than a frame watched.** The owner's
    /// question is what a demo asks for and never gets — *"missing meshes or materials or textures, or
    /// something isnt being draw"* — and every existing report is per frame, rate-limited and only on
    /// a change, so a model that fails once in a hundred thousand frames appears in no line anybody
    /// reads.
    ///
    /// **Carried from the draw loop's own calls** (B243), not recomputed by a second walk.
    /// </remarks>
    public DrawTally Tally => _tally;

    /// <summary>Areaportal windows given their distance blend — the wiring check for B358.</summary>
    /// <remarks>
    /// **Counted where the blend is WRITTEN, not where a window is recognised**, which is the
    /// distinction B347's counter got wrong: incremented at the intent rather than at the work, it
    /// reported success for a rotation nothing applied. Zero here on `koth_harvest_final`, which
    /// carries six of them, means the three floats never arrived or the view origin was never set.
    /// </remarks>
    public int PortalWindowsFaded { get; private set; }

    /// <summary>The alpha a fully solid entity draws at, as a byte.</summary>
    private const int FullyOpaqueAlpha = 255;

    /// <summary>Barrel bones actually WRITTEN — the wiring check for B347.</summary>
    /// <remarks>
    /// **Summed out of the poses that did the work, not counted here** (B243). The first version
    /// incremented a field in <see cref="SpinBarrel"/> as soon as it had a state and a bone index,
    /// which measured this method's INTENT: sabotaging `SkeletonPose.SpinBarrel`, the code that
    /// actually writes the rotation, left every assertion green. A count kept beside the work is a
    /// second route, and a second route is free to be wrong.
    ///
    /// **The same shape as <see cref="AppliedIkLocks"/>**, which reads `posed.AppliedLocks` for
    /// exactly this reason.
    /// </remarks>
    public int SpunBarrels
    {
        get
        {
            int spun = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    spun += posed.SpunBarrels;
                }
            }

            return spun;
        }
    }

    /// <summary>Chambers whose bone this pass wrote — the wiring check for B348.</summary>
    /// <remarks>
    /// **Counted here rather than carried out of <c>SkeletonPose</c>, unlike <see cref="SpunBarrels"/>,
    /// and the difference is deliberate.** The pose writes ONE barrel bone, so a single counter
    /// there cannot say which weapon asked for it. This says the chamber path ran; `SpunBarrels`
    /// says a bone was actually written, and both being non-zero is what says the two agree.
    /// </remarks>
    public int TurnedChambers { get; private set; }

    /// <summary>The furthest a chamber has turned, in radians.</summary>
    public float FurthestChamberAngle { get; private set; }

    /// <summary>The furthest any barrel has turned, in radians.</summary>
    /// <remarks>
    /// **The question a count cannot answer**: whether the barrels MOVE. A non-zero count with a
    /// zero angle would mean every minigun is idle, which looks identical on screen to the spin
    /// never running and is a different fact about the demo.
    /// </remarks>
    public float FurthestBarrelAngle { get; private set; }

    /// <summary>Transition queues emptied because the entity JUMPED — the <c>bInterpolate</c> half.</summary>
    /// <remarks>
    /// **Separated from the <c>STUDIO_SNAP</c> half deliberately** (B346). One total would let an
    /// authored cut stand in for a teleport, and the whole question this answers is whether the
    /// half that was missing until now ever fires on a real recording.
    /// </remarks>
    public int QueuesClearedByADiscontinuity { get; private set; }

    /// <summary>Transition queues emptied because the incoming sequence carries <c>STUDIO_SNAP</c>.</summary>
    public int QueuesClearedBySnap { get; private set; }

    /// <summary>Sequence changes the transitioner saw at all — the CONTROL for the two above.</summary>
    /// <remarks>
    /// **An absence is uninterpretable without it.** Zero clears can mean the guard never fires or
    /// that no entity ever changed sequence in the window, and only this tells them apart.
    /// </remarks>
    public int SequenceChangesSeen { get; private set; }

    /// <summary>Cross-fades actually queued, which is what the clears above prevent.</summary>
    public int TransitionsCreated { get; private set; }

    /// <summary>How many sequence IK locks the pose path actually applied.</summary>
    /// <remarks>
    /// **The same question `SolvedIkChains` answers for rules, asked of locks** (B311): the unit
    /// tests prove `IkLocks` pins an effector when it is called, and only this says whether a real
    /// demo ever calls it. A lock naming a chain the model lacks, or a chain `Studio_SolveIK`
    /// refuses, leaves this at zero while every part looks wired.
    /// </remarks>
    public int AppliedIkLocks
    {
        get
        {
            int applied = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    applied += posed.AppliedLocks;
                }
            }

            return applied;
        }
    }

    /// <summary>How many applied locks actually moved an effector, and the furthest move.</summary>
    /// <remarks>
    /// **The question a screenshot cannot answer**, made numeric: whether a lock holds a foot or
    /// merely runs. `AppliedIkLocks` says the bracket executed; this says it changed the pose, and
    /// by how far. Zero moves with a non-zero apply count would mean the solve is computing the
    /// position the sequence already had — which looks identical on screen and is a different
    /// defect from not running at all.
    /// </remarks>
    public (int Moved, float Furthest) IkLockEffect
    {
        get
        {
            int moved = 0;
            float furthest = 0f;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    (int entityMoved, float entityFurthest) = posed.LockEffect;

                    moved += entityMoved;
                    furthest = MathF.Max(furthest, entityFurthest);
                }
            }

            return (moved, furthest);
        }
    }

    /// <summary>What the IK pass was handed, for telling apart the ways it can do nothing.</summary>
    /// <remarks>
    /// **Three numbers because "nothing solved" has three causes** and they need different fixes: no
    /// entity carries chains, or chains but no rules were read, or rules but none weighed anything.
    /// A single count cannot distinguish them, and this project has twice spent a measurement on
    /// the wrong one of a pair.
    /// </remarks>
    public (int Chained, int Ruled, int Weighed) IkWork
    {
        get
        {
            int chained = 0;
            int ruled = 0;
            int weighed = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                // Stryker disable once : a mutant that empties the guard body leaves 'posed'
                // unassigned (CS0165), and Safe Mode then drops every mutation in this method —
                // B410.
                // **Only what was built this frame** (finding 102): the rules are gathered at bone setup, so an entity the
                // frame never set up still holds its last build's, which this would otherwise report as current (B243).
                if (animating.Pose is not SkeletonPose posed || !animating.BuiltThisFrame)
                {
                    continue;
                }

                chained += posed.IkChains.Count;
                weighed += posed.IkErrors.Count;

                // **Counting the rules that can RAISE a chain's weight, which only `IK_SELF` does.**
                // A release blends the target back without touching the weight, so a chain holding
                // only releases is never solved — reporting them together would make "a correction
                // is playing" and "a correction is being given back" the same number.
                //
                // **`weighed` and `ruled` being equal is now the NORMAL reading**, and it is not a
                // sign releases were dropped. `AddDependencies` removes every rule on a chain when
                // one arrives at full strength and drops a full-strength release outright
                // (`bone_setup.cpp:3319`), and a release in force is usually at full strength — so
                // what survives to here is mostly selves that nothing cancelled (B299).
                foreach ((StudioIkRule rule, _, _, _) in posed.IkErrors)
                {
                    if (rule.Type == StudioIkRuleType.Self)
                    {
                        ruled++;
                    }
                }
            }

            return (chained, ruled, weighed);
        }
    }
}
