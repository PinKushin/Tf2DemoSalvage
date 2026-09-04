using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>What one stage of the engine's bone pipeline is, and whether this project has it.</summary>
/// <param name="Call">The name as it appears in the SDK, which is what the extraction returns.</param>
/// <param name="State">How far this project has it.</param>
/// <param name="Note">What it does, and what its absence costs. One line, for the report.</param>
internal sealed record BoneStage(string Call, StageState State, string Note);

/// <summary>How far a pipeline stage has been carried across.</summary>
internal enum StageState
{
    /// <summary>Not started.</summary>
    Absent,

    /// <summary>Some of it, with the rest named in the note.</summary>
    Partial,

    /// <summary>Matches the engine.</summary>
    Implemented,

    /// <summary>Deliberately not applicable here, with the reason in the note.</summary>
    NotApplicable,
}

/// <summary>
/// The engine's bone pipeline, enumerated from the SDK, against what this project implements.
/// </summary>
/// <remarks>
/// **This is B182: the pose path was the one subsystem with no denominator.**
/// <c>SdkCoverageTests</c> generates one from the SDK for 489 shader parameters, 66 BSP lumps and 54
/// studio structures, so a missing one cannot hide. The animation and bone pipeline — the subsystem
/// with the most engine behaviour per line — had none, which is why *"how far has it diverged"* was
/// unanswerable rather than merely unanswered.
///
/// **The stage list is READ, never typed.** That distinction is not pedantry: the hand-written list
/// filed in B182 was wrong by two stages within a day of being written — <c>GetPoseParameters</c>
/// and <c>ChildLayerBlend</c> were both missing from it. A list extracted from the function body
/// cannot omit a stage silently; it can only fail loudly when the engine grows one.
///
/// **The test does not fail on a GAP.** An unimplemented stage is a fact to report, not a broken
/// build — the same contract <c>SdkCoverageTests</c> has. It fails when a call appears in the
/// engine's body that nobody here has classified, which is the one thing a denominator exists to
/// catch.
///
/// **Skips when the SDK is absent**, like every other test that needs something outside the
/// repository. Set <c>SOURCE_SDK</c> to point at a checkout.
/// </remarks>
public sealed class BoneSetupConformanceTests
{
    /// <summary>Where <c>C_BaseAnimating</c> lives.</summary>
    private const string ClientAnimating = "src/game/client/c_baseanimating.cpp";

    /// <summary>The blend stage: everything that produces the local <c>pos</c> and <c>q</c> arrays.</summary>
    private const string StandardBlendingRules =
        "void C_BaseAnimating::StandardBlendingRules";

    /// <summary>Calls in the engine's body that are not pipeline stages, each with its reason.</summary>
    /// <remarks>
    /// **Listed rather than filtered by a pattern, so every exclusion is visible.** A regex that
    /// dropped "anything starting with Get" would also drop <c>GetPoseParameters</c>, which is a
    /// real stage — the first one. An exclusion that can be read is an exclusion that can be argued
    /// with.
    /// </remarks>
    private static readonly Dictionary<string, string> NotStages =
        new(StringComparer.Ordinal)
        {
            ["VPROF"] = "profiler scope, compiled out of a release build",
            ["DevMsgRT"] = "debug print behind r_sequence_debug",
            ["Q_stristr"] = "string compare inside that debug print",
            ["GetInt"] = "ConVar read, part of the same debug guard",
            ["GetString"] = "ConVar read, part of the same debug guard",
            ["entindex"] = "entity number, compared against the debug ConVar",
            ["SequencesAvailable"] = "guard: the model has no sequence data yet",
            ["GetNumSeq"] = "guard: clamp a sequence number that is out of range",
            ["GetSequence"] = "reads the sequence the entity is playing",
            ["SetSequence"] = "the clamp itself, when the sequence is out of range",
            ["GetCycle"] = "reads the cycle the blend runs at",
            ["GetRenderAngles"] = "entity placement, passed to the IK context",
            ["GetRenderOrigin"] = "entity placement, passed to the IK context",
            ["pSeqdesc"] = "sequence description, for the debug print's label",
            ["pszLabel"] = "sequence name, for the debug print",
            ["pszName"] = "model name, for the debug print",
            ["numbonecontrollers"] = "guard: skip CalcBoneAdj when the model declares none",
        };

    /// <summary>
    /// Every stage this project recognises, with how far it has been carried across.
    /// </summary>
    /// <remarks>
    /// **Seeded with two entries and no more, on purpose, in the first commit.** The red run prints
    /// the engine's own call list and that output is what the rest of this table is transcribed
    /// from — rather than somebody's reading of the function, which is exactly how B182's list came
    /// to be wrong by two stages.
    ///
    /// It is not empty only because CA1812 rejects a record nothing constructs, which is this
    /// repository's anti-stub gate doing its job.
    /// </remarks>
    private static readonly IReadOnlyList<BoneStage> Stages =
    [
        new BoneStage(
            "GetPoseParameters",
            StageState.Implemented,
            "Fills the pose parameter array before the blend, on BOTH of the engine's two paths, " +
            "which is what this entry used to say was half missing. An entity that networks the " +
            "array wins outright — server/baseanimating.cpp:243 sends it whole and " +
            "c_baseanimating.cpp:1401 hands it straight to the blend, so a sentry's aim comes off " +
            "the wire (EntityModels.PoseValues, the pose.PoseParameters branch). A player cannot " +
            "take that path because tf_player.cpp:769 EXCLUDES the array for them, so move_x, " +
            "move_y, body_pitch and body_yaw are derived as CBasePlayerAnimState derives them, " +
            "matched by NAME rather than by index. The two are Valve's own split and an entity is " +
            "only ever on one side of it."),

        new BoneStage(
            "boneSetup",
            StageState.Implemented,
            "Constructing IBoneSetup( hdr, boneMask, poseparam ) — the object every later stage " +
            "runs through. There is no separate object here; the mask and the pose parameters are " +
            "arguments to AnimatingEntity.SetupBones and SkeletonPose.Build, which is a shape " +
            "difference rather than a behaviour one. The MASK itself is honoured where the engine " +
            "honours it: `if ( !( hdr->boneFlags( i ) & boneMask ) ) continue;` " +
            "(c_baseanimating.cpp:1517) is SkeletonPose.Build's own first test, and the " +
            "readable/writable accounting that makes SetupBones idempotent is there too, widened " +
            "by what was asked last frame exactly as `boneMask |= m_iPrevBoneMask` does. " +
            "THIS ENTRY READ 'Absent, no equivalent here at all' UNTIL 2026-09-03 and was stale on " +
            "both halves: it also blamed the missing mask for an ordering depth sort that B181 and " +
            "D88 had already deleted, having found the engine has no ordering code at all."),

        new BoneStage(
            "InitPose",
            StageState.Partial,
            "Seeds pos/q from the bind pose. StudioBones.RestPose exists and is used, but not as " +
            "a separable stage the later ones accumulate onto — it is folded into Skeleton()."),

        new BoneStage(
            "AccumulatePose",
            StageState.Implemented,
            "Blends the entity's own sequence in at weight 1. StudioBlendGrid resolves the grid, " +
            "StudioSequences picks the frame, StudioMotion supplies the ground speed for the " +
            "two-pass move_x/move_y rescale, and the animation-model bone remap is Valve's " +
            "masterBone. Its own two layer passes are here too (B294): AddLocalLayers at weight " +
            "one ahead of everything, AddSequenceLayers at the parent's weight after it, each " +
            "claiming the autolayers the other skips, with the envelope, the spline, the " +
            "cross-fade bias and the pose-driven index. Both are used by TF2 — sentry3's idle and " +
            "c_engineer_arms' throwable arms. Not reproduced: a LOCAL layer on a non-main " +
            "sequence, which needs a nested compose a flat layer list cannot express, and which " +
            "no measured model asks for. Seq IK locks are absent with the rest of IK."),

        new BoneStage(
            "MaintainSequenceTransitions",
            StageState.Implemented,
            "Keeps the previous sequences alive and decays them, so a sequence change eases " +
            "rather than snapping. Implemented in EntityModelSet as Valve's queue (B286): the " +
            "outgoing sequence is pushed with MIN( prev.fadeouttime, next.fadeintime ), keeps " +
            "playing while it fades, is weighted by GetFadeout's 3s^2-2s^3 spline and is removed " +
            "at zero; STUDIO_SNAP empties the queue. Both of CheckForSequenceChange's triggers are " +
            "here since B300: the sequence NUMBER changing, and bForceNewSequence — a sequence " +
            "restarting at the same number, which reaches this layer as a changed " +
            "AnimationStartSeconds because that is what the timeline makes of the parity. Measured " +
            "as real but rare: 8 restarts against 3121 number changes over 1508 tracks of z1800, " +
            "every one on a hidden entity. The only piece left out is the clip-to-time-remaining " +
            "block, which Valve has commented out — writing it would be a divergence, not parity."),

        new BoneStage(
            "AccumulateLayers",
            StageState.Implemented,
            "Overlays the entity's animation layers, in m_nOrder, each accumulated onto the result " +
            "of the last. SlerpBones' delta branch is implemented — QuaternionMA under " +
            "STUDIO_POST, QuaternionSM otherwise — with the per-bone weight list read through the " +
            "group's bone map, which is how a reload plays on a running player (B284). For a TF2 " +
            "PLAYER the source is not the wire at all: tf_player.cpp:774 excludes overlay_vars, so " +
            "the layers come from CTEPlayerAnimEvent temp entities (B282), and an entity that DOES " +
            "send m_AnimOverlay — a sentry, a dispenser — has its array walked in m_nOrder (B285). " +
            "The per-bone branch is honoured too: a bone flagged BONE_FIXED_ALIGNMENT takes " +
            "QuaternionSlerpNoAlign rather than the aligning form (bone_setup.cpp:1492), in Valve's " +
            "argument order, which matters because that function's antipodal arm is not symmetric " +
            "(B292). No TF2 model measured sets the flag; it is implemented for parity."),

        new BoneStage(
            "Init",
            StageState.Absent,
            "auto_ik.Init( hdr, angles, origin, time, framecount, boneMask ) — the throwaway IK " +
            "context that CalcAutoplaySequences writes its IK rules into. No IK solver exists " +
            "here. **The CHAINS are loaded, contrary to what this entry said**: " +
            "StudioIkChains.Read has existed since the reader was written, and every TF2 player " +
            "model declares four — rhand, lhand, rfoot, lfoot, three links each, measured on " +
            "scout, heavy and engineer (B296). What is missing is the solver and the per-animation " +
            "ikrule tracks, not the data."),

        new BoneStage(
            "CalcAutoplaySequences",
            StageState.Implemented,
            "Applies every sequence flagged STUDIO_AUTOPLAY, which is how a model animates parts " +
            "of itself with nothing driving it — flags, chains, idle machinery (B291). " +
            "EntityModels.AutoplayFor is the loop: the list is COMPUTED by walking the merged " +
            "sequences for the flag, as studio.cpp:658 builds Valve's, the cycle is " +
            "flRealTime * Studio_CPS wrapped, and the weight is a literal one. It runs last of " +
            "the three accumulating passes, which is where c_baseanimating.cpp:1996 runs it."),

        new BoneStage(
            "GetBoneControllers",
            StageState.Implemented,
            "Reads the entity's m_flEncodedController array. It IS networked — " +
            "baseanimating.cpp:248, eleven bits per controller, SPROP_ROUNDDOWN over 0..1 — so " +
            "this is recoverable from a demo rather than lost, and B288 recovers it: " +
            "EntityState.BoneControllers reads the array by input index and the pose carries it."),

        new BoneStage(
            "CalcBoneAdj",
            StageState.Implemented,
            "Applies those controllers to the bones they drive (B288). SkeletonPose.Adjust is the " +
            "switch on type & STUDIO_TYPES: the three translations add units to pos[k], the three " +
            "rotations convert DEGREES and compose with QuaternionSM at weight one, the input is " +
            "chosen by inputfield rather than by list position, and the clamp runs before the lerp " +
            "between start and end. The .mdl parser reads the controllers now too."),

        new BoneStage(
            "ChildLayerBlend",
            StageState.NotApplicable,

            // **Read before classifying, and it is the reason that rule exists.** The obvious move
            // is to file a stage nobody implemented as a gap. This one's body opens with `return;`
            // — Valve disabled it — and what follows is four FIXMEs including "needs a new type of
            // EF_BONEMERGE (EF_CHILDMERGE?)" and "probably needs an IK merge system of some sort
            // =(". Implementing it would be a DEPARTURE from the shipped engine, not parity, and
            // it would have been ~40 lines of work to reproduce something that never runs.
            "Dead in the shipped engine: the body's first statement is `return;`. Valve's own " +
            "FIXMEs say it needs an EF_CHILDMERGE that does not exist. Matching the engine here " +
            "means doing nothing, and doing something would be the departure."),

        new BoneStage(
            "UnragdollBlend",
            StageState.NotApplicable,
            "Blends a corpse back out of ragdoll. Nothing here simulates ragdolls yet, so there " +
            "is no state to blend from — this becomes applicable when death is drawn, not before."),
    ];

    /// <summary>The entry point every consumer of a bone goes through.</summary>
    private const string SetupBones = "bool C_BaseAnimating::SetupBones";

    [Test]
    public void Pipeline_EveryCallSetupBonesMakes_IsClassified()
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
            return;
        }

        IReadOnlyList<string> calls =
            SdkInventory.CallsIn(SdkInventory.FunctionBody(ClientAnimating, SetupBones));

        HashSet<string> known = new(SetupStages.Select(stage => stage.Call), StringComparer.Ordinal);

        List<string> unclassified =
            calls.Where(call => !known.Contains(call) && !NotSetupStages.ContainsKey(call)).ToList();

        unclassified.ShouldBeEmpty(
            $"SetupBones makes calls nothing here has classified.{Environment.NewLine}" +
            $"unclassified, in engine order: {string.Join(", ", unclassified)}{Environment.NewLine}" +
            $"every call, in engine order: {string.Join(", ", calls)}");
    }

    [Test]
    public void Extraction_SetupBonesBody_IsFoundAndPlausible()
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
            return;
        }

        string body = SdkInventory.FunctionBody(ClientAnimating, SetupBones);

        // The control for the classification test above, on the same reasoning: an extraction that
        // returned nothing would report the whole entry point as covered.
        body.Length.ShouldBeGreaterThan(2000, "the brace matcher returned a body too short to be the real one");
        body.ShouldContain("StandardBlendingRules", Case.Sensitive);
        body.ShouldContain("BuildTransformations", Case.Sensitive);
    }

    /// <summary>The stages <c>SetupBones</c> itself performs, around the blend.</summary>
    /// <remarks>
    /// **Seeded with one entry and no more in this commit**, for the same reason the blend table
    /// was: the red run prints the engine's own call list, and the rest is transcribed from that.
    /// CA1812 rejects a record nothing constructs, so it cannot start empty.
    /// </remarks>
    private static readonly IReadOnlyList<BoneStage> SetupStages =
    [
        new BoneStage(
            "GetMoveParent",
            StageState.Partial,
            "Read twice and both uses are stages. The PARENT LINK half is here and always was: " +
            "AnimatingEntity.Follows is what the merge follows, and asking it to set its own bones " +
            "is Valve's whole ordering mechanism (bone_merge_cache.cpp:130). " +
            "THIS ENTRY SAID 'Nothing here has an equivalent; the ordering is a depth sort (B181)' " +
            "UNTIL 2026-09-03, and both halves were stale — B181 and D88 DELETED that depth sort, " +
            "having found the engine has no ordering code at all, which is the same wrong claim the " +
            "boneSetup entry above carried. " +
            "What is genuinely absent is the other use: gating enrolment into the THREADED pre-pass " +
            "so that only roots are enrolled and the merge recursion can never overlap the " +
            "parallelism (c_baseanimating.cpp:2897). There is no threaded pre-pass here, so the " +
            "guard has nothing to guard."),

        new BoneStage(
            "StandardBlendingRules",
            StageState.Partial,
            "The blend itself. Its own thirteen stages are the table above: eight implemented, one " +
            "partial, two absent, two not applicable. " +
            "IT READ 'one implemented, three partial, six absent' UNTIL 2026-09-03 — a count taken " +
            "when it was written and never retaken, which is the same fault as the four stale " +
            "OPEN entries in RISKS.md found the same night. Recount it when a state changes; the " +
            "numbers are one awk over this table."),

        new BoneStage(
            "Init",
            StageState.NotApplicable,
            "m_pIk->Init( hdr, angles, origin, time, framecount, mask ) — the entity's real IK " +
            "context, distinct from the throwaway one CalcAutoplaySequences uses. What Init stores " +
            "is the root transform, the frame counter and the bone mask, and every one of those is " +
            "read by the CIKTarget half alone: the root transform positions a GROUND target's " +
            "floor, and the frame counter ages a LATCH. With ATTACHMENT and GROUND measured at " +
            "zero across 16,417 animations there is nothing for it to hold (B299). " +
            "IkContext takes what it needs per call instead. " +
            "The teleport guard is the same story — `if (Teleported() || IsNoInterpolationFrame()) " +
            "m_pIk->ClearTargets()` clears targets, and a context with no targets has nothing to " +
            "clear across a seek. That answer is worth keeping written down, because a scrubbing " +
            "viewer WILL raise the question again the moment a latching rule type appears."),

        new BoneStage(
            "UpdateIKLocks",
            StageState.NotApplicable,
            "Applies the locks the game code asked for this frame, before the targets are solved. " +
            "Unreachable for TF2 with the two stages below, and for the same measured reason: it " +
            "acts on CIKTarget entries, and nothing establishes one."),

        new BoneStage(
            "UpdateTargets",
            StageState.NotApplicable,
            "Resolves each chain's goal into world space — for a foot, where the ground is. " +
            "PROVABLY DEAD FOR TF2, and the proof is in the switch rather than in our data: only " +
            "IK_ATTACHMENT and IK_GROUND establish a target (bone_setup.cpp:3741), with " +
            "`// case IK_SELF:` commented out beside them; IK_RELEASE and IK_UNLATCH only reduce a " +
            "weight on a target something else made, and the closing loop is gated on " +
            "est.flWeight > 0. Measured over 16,417 animations of every model two demos draw: " +
            "ATTACHMENT 0, GROUND 0. So every target stays weightless (B299)."),

        new BoneStage(
            "CalculateIKLocks",
            StageState.NotApplicable,
            "Traces against the world to decide where a locked foot actually rests — the one that " +
            "makes feet plant. TF2 declares no IK_GROUND rule at all, which is why the famous " +
            "symptom of missing IK, a sliding foot, is not what this project's IK ever fixed. Same " +
            "CIKTarget dependency as UpdateTargets."),

        new BoneStage(
            "SolveDependencies",
            StageState.Implemented,
            "The two-bone solve itself, writing the result into the bone array and rebuilding the " +
            "local pose through SolveBone. Both types TF2 actually declares are here: IK_SELF " +
            "(B296) and IK_RELEASE (B299), the latter blending the chain's target back toward the " +
            "animation's own end position without touching the chain's weight, under Valve's " +
            "comment 'move target back towards original location'. The other four are Valve's own " +
            "no-ops — IK_WORLD is Assert(0), ATTACHMENT and GROUND are bare breaks whose work is " +
            "in UpdateTargets, and UNLATCH's body is commented out."),

        new BoneStage(
            "BuildTransformations",
            StageState.Partial,
            "Turns local pos/q into bone-to-world down the hierarchy. Ours concatenates parents, " +
            "and JIGGLE bones are now simulated on the matrix the concatenate produces, which is " +
            "Valve's goalMX (B293) — the whole of jigglebones.cpp:60, including the frame counters, " +
            "the clamped-up deltaT and the reflex-angle branch. Still partial: the merge runs AFTER " +
            "rather than first, the bone mask is not consulted, and the FOUR rules CalcProceduralBone " +
            "handles are absent (B180, B182) — though no bone in any demo measured uses one of them, " +
            "22 of 379 and 4 of 198 procedural bones all being jiggle."),

        new BoneStage(
            "ControlMouth",
            StageState.NotApplicable,
            "Drives the mouth flex from a playing voice line. TF2 demos carry voice as a separate " +
            "stream this project does not decode, and no lip-sync data reaches us — so there is " +
            "nothing to drive it with rather than a stage being skipped."),

        new BoneStage(
            "SetupBones_AttachmentHelper",
            StageState.Partial,

            // The arithmetic matches and the OWNER does not, which is the distinction worth
            // keeping: ours resolves one attachment inside the consumer's loop iteration, where
            // the engine runs one pass over the whole table per entity and caches it, gated on
            // BONE_USED_BY_ATTACHMENT never having been requested before.
            "Resolves the attachment table against the finished bones. The arithmetic matches " +
            "Valve's, including the one-based index and the ATTACHMENT_FLAG_WORLD_ALIGN branch " +
            "that keeps the bone's position and throws its rotation away; what differs is that " +
            "ours recomputes per child instead of once per entity (finding 35 section 4). " +
            "The one line NOT reproduced is FormatViewModelAttachment (c_baseanimating.cpp:2081), " +
            "which squashes an attachment by worldFov/viewmodelFov because the viewmodel renders " +
            "through a different projection. It is a C_BaseViewModel override and the base is an " +
            "empty body, so it reaches viewmodels only — and nothing here is positioned by a " +
            "viewmodel's attachment. Measured: z1800 has ZERO attachment-parented props at five " +
            "ticks against dozens of bone-merged ones as the control, and the pub demo's three are " +
            "map doors on CDynamicProp. It would matter if this project drew muzzle flashes or " +
            "tracers at viewmodel attachments, which it does not."),
    ];

    [Test]
    public void Extraction_StandardBlendingRulesBody_IsFoundAndPlausible()
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
            return;
        }

        string body = SdkInventory.FunctionBody(ClientAnimating, StandardBlendingRules);

        // **The control, and it is not decoration.** An extraction that returned nothing would
        // report every stage as classified and the pipeline as fully covered — the most flattering
        // possible way to be wrong, and a shape this project has hit twice with instruments that
        // answered confidently about nothing. The floors are set below what the function actually
        // contains so a real edit does not redden them, and far above empty.
        body.Length.ShouldBeGreaterThan(1000, "the brace matcher returned a body that is too short to be the real one");
        body.ShouldContain("InitPose", Case.Sensitive);
        body.ShouldContain("AccumulatePose", Case.Sensitive);

        SdkInventory.CallsIn(body).Count.ShouldBeGreaterThan(15, "too few calls to be the real body");
    }

    /// <summary>Calls in <c>SetupBones</c> that are not stages, each with its reason.</summary>
    private static readonly Dictionary<string, string> NotSetupStages =
        new(StringComparer.Ordinal)
        {
            // Not a call at all: `#if defined( ... )` survives into the body, because the
            // extraction blanks comments and literals but does not resolve the preprocessor.
            // Listed rather than filtered by pattern, so the exclusion stays visible.
            ["defined"] = "a preprocessor operator, not a function",

            ["VPROF_BUDGET"] = "profiler scope",
            ["DevMsgRT"] = "rate-limited developer warning about bone access",
            ["DevWarning"] = "the attachment helper's failure message",
            ["Warning"] = "the bone-array-too-small message",
            ["Msg"] = "the threading contention message, behind a debug define",
            ["ExecuteNTimes"] = "rate limiter around that Warning",
            ["Assert"] = "debug assertion",
            ["AUTO_LOCK"] = "scoped lock macro; the lock itself IS a stage and is listed",
            ["MDLCACHE_CRITICAL_SECTION"] = "model cache lock macro",
            ["GetClassname"] = "text for the bone-access warning",
            ["GetInt"] = "ConVar reads: cl_SetupAllBones and the debug toggles",
            ["GetBool"] = "ConVar read for the threading debug check",
            ["IsToolRecording"] = "widens the mask when the engine is recording, which we never are",
            ["IsBoneAccessAllowed"] = "debug guard around the warning above",
            ["Find"] = "the duplicate-enrolment assertion on g_PreviousBoneSetups",
            ["Count"] = "sizes: the cached bone count, and the enrolment list's",
            ["Base"] = "raw pointer for the memcpy out",
            ["memcpy"] = "copying the finished bones to the caller's array",
            ["sizeof"] = "the size of that copy",
            ["ClearPerfCounters"] = "performance counters behind STUDIO_ENABLE_PERF_COUNTERS",
            ["GetModelPtr"] = "the studio header",
            ["SequencesAvailable"] = "guard: the model has no sequence data",
            ["GetSequence"] = "guard: an entity with no sequence has no bones to build",
            ["flags"] = "reads STUDIOHDR_FLAGS_STATIC_PROP, which selects the one-matrix path",
            ["numikchains"] = "guard: only allocate an IK context if the model has chains",
            ["AddFlag"] = "EFL_SETTING_UP_BONES, so move children keep their transform invalid",
            ["RemoveFlag"] = "clearing it",
            ["IsRagdoll"] = "guard: IK is not run on ragdolls",
            ["IsModelScaled"] = "guard: model scaling opts out of IK",
            ["Teleported"] = "guard: a teleport clears IK targets",
            ["IsNoInterpolationFrame"] = "the same guard's other half",
            ["ClearTargets"] = "part of that teleport handling",
            ["StartBlending"] = "the pose debugger, which has no counterpart here",
            ["memset"] = "filling pos/q with 0xFF so uninitialised bones produce NaNs in debug",
            ["MatrixCopy"] = "the static-prop path: one matrix, no blending",
            ["AngleMatrix"] = "building the entity transform from render angles and origin",
            ["GetRenderAngles"] = "part of that",
            ["GetRenderOrigin"] = "part of that",
            ["GetBoneForWrite"] = "the static-prop path's destination",
            ["GetBoneArrayForWrite"] = "the array handed to the IK solver",
            ["GetReadableBones"] = "accessor state, read as part of the cache check",
            ["GetWritableBones"] = "accessor state",
            ["SetReadableBones"] = "accessor state",
            ["SetWritableBones"] = "accessor state",
            ["LastBoneChangedTime"] = "part of the per-frame reset condition",
            ["TrackBoneSetupEnt"] = "profiling hook",
            ["CIKContext"] = "allocating the IK context; the stages that USE it are listed",
            ["AddToTail"] = "enrolling into g_PreviousBoneSetups, part of the threaded pre-pass",
            ["TryLock"] = "the threaded path's non-blocking acquire, part of the lock stage",
            ["Unlock"] = "its release",
        };

    [Test]
    public void Pipeline_EveryCallStandardBlendingRulesMakes_IsClassified()
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
            return;
        }

        IReadOnlyList<string> calls =
            SdkInventory.CallsIn(SdkInventory.FunctionBody(ClientAnimating, StandardBlendingRules));

        HashSet<string> known = new(Stages.Select(stage => stage.Call), StringComparer.Ordinal);

        List<string> unclassified =
            calls.Where(call => !known.Contains(call) && !NotStages.ContainsKey(call)).ToList();

        // Reported in engine order with the whole list attached, because the failure IS the
        // denominator: what it prints is what the table below has to account for.
        unclassified.ShouldBeEmpty(
            $"the engine's blend stage makes calls nothing here has classified.{Environment.NewLine}" +
            $"unclassified, in engine order: {string.Join(", ", unclassified)}{Environment.NewLine}" +
            $"every call, in engine order: {string.Join(", ", calls)}");
    }
}
