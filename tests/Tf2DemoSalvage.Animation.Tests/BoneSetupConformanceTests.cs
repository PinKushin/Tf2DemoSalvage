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
            StageState.Partial,
            "Fills the pose parameter array before the blend. We compute move_x, move_y, " +
            "body_pitch and body_yaw by NAME and normalise them (EntityModels.PoseValues), which " +
            "is Valve's mechanism; what is missing is reading the networked m_flPoseParameter " +
            "array, so any parameter the server set that we do not compute stays at zero."),

        new BoneStage(
            "boneSetup",
            StageState.Absent,
            "Constructing IBoneSetup( hdr, boneMask, poseparam ) — the object every later stage " +
            "runs through. The BONE MASK is the part with no equivalent here at all: it is what " +
            "lets the engine build only the bones a caller needs, and it is the input to the " +
            "readable/writable accounting that makes SetupBones idempotent. Its absence is why " +
            "the ordering had to be solved with a depth sort (B181)."),

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
            "masterBone. This is the one stage that is genuinely there."),

        new BoneStage(
            "MaintainSequenceTransitions",
            StageState.Absent,
            "Keeps the previous sequences alive and decays them, so a sequence change eases " +
            "rather than snapping. Nothing here holds a previous sequence at all, so every " +
            "change is a cut."),

        new BoneStage(
            "AccumulateLayers",
            StageState.Partial,
            "Overlays the entity's animation layers. StudioGestureWeights exists, so the gesture " +
            "half has been looked at; the layer array itself is not accumulated, and TF2 leans on " +
            "layers for aiming and reloading."),

        new BoneStage(
            "Init",
            StageState.Absent,
            "auto_ik.Init( hdr, angles, origin, time, framecount, boneMask ) — the throwaway IK " +
            "context that CalcAutoplaySequences writes its IK rules into. No IK exists here, and " +
            "the .mdl parser does not read ikchainindex, so the data is not even loaded."),

        new BoneStage(
            "CalcAutoplaySequences",
            StageState.Absent,
            "Applies every sequence flagged STUDIO_AUTOPLAY, which is how a model animates parts " +
            "of itself with nothing driving it — flags, chains, idle machinery."),

        new BoneStage(
            "GetBoneControllers",
            StageState.Absent,
            "Reads the entity's m_flEncodedController array. It IS networked — " +
            "baseanimating.cpp:248, eleven bits per controller, SPROP_ROUNDDOWN over 0..1 — so " +
            "this is recoverable from a demo rather than lost."),

        new BoneStage(
            "CalcBoneAdj",
            StageState.Absent,
            "Applies those controllers to the bones they drive. Guarded by numbonecontrollers(), " +
            "which the .mdl parser does not read either."),

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
            StageState.Absent,
            "Read twice and both uses are stages. It gates enrolment into the threaded pre-pass — " +
            "only ROOTS are enrolled, which is what keeps the merge recursion and the parallelism " +
            "from ever overlapping (c_baseanimating.cpp:2897) — and it is the parent link the " +
            "merge follows. Nothing here has an equivalent; the ordering is a depth sort (B181)."),

        new BoneStage(
            "StandardBlendingRules",
            StageState.Partial,
            "The blend itself. Its own twelve stages are the table above: one implemented, three " +
            "partial, six absent, two not applicable."),

        new BoneStage(
            "Init",
            StageState.Absent,
            "m_pIk->Init( hdr, angles, origin, time, framecount, mask ) — the entity's real IK " +
            "context, distinct from the throwaway one CalcAutoplaySequences uses. Preceded by a " +
            "teleport guard that clears targets, which is the engine's own answer to the question " +
            "a scrubbing viewer raises: what happens to a stateful simulation across a seek."),

        new BoneStage(
            "UpdateIKLocks",
            StageState.Absent,
            "Applies the locks the game code asked for this frame, before the targets are solved."),

        new BoneStage(
            "UpdateTargets",
            StageState.Absent,
            "Resolves each chain's goal into world space — for a foot, where the ground is."),

        new BoneStage(
            "CalculateIKLocks",
            StageState.Absent,
            "Traces against the world to decide where a locked foot actually rests. This is the " +
            "one that makes feet plant, and heavy.mdl declares chains for HANDS as well, which is " +
            "how an off-hand grip is pinned to a weapon."),

        new BoneStage(
            "SolveDependencies",
            StageState.Absent,
            "The two-bone solve itself, writing the result into the bone array and marking each " +
            "bone it computed so BuildTransformations skips it."),

        new BoneStage(
            "BuildTransformations",
            StageState.Partial,
            "Turns local pos/q into bone-to-world down the hierarchy. Ours concatenates parents, " +
            "but the merge runs AFTER rather than first, the bone mask is not consulted, and " +
            "procedural and jiggle bones are not computed at all (B180, B182)."),

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
            "Valve's, including the one-based index; what differs is that ours recomputes per " +
            "child instead of once per entity (finding 35 section 4)."),
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
