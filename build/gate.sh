#!/usr/bin/env bash
# The local merge gate: every test project, one at a time, with the count of each checked.
#
# **Why per project rather than one solution-wide `dotnet test`.** Two reasons, and the second is
# the one that bites.
#
#   - `dotnet test` on the solution runs test ASSEMBLIES concurrently, so Viewer3D.UiTests — which
#     launches the viewer and drives a real window — competes with everything else. Measured
#     2026-08-16: that suite passes in 2 seconds alone and failed one of eight at 10 seconds inside
#     a single-invocation run.
#   - A solution-wide run writes one .trx per project, all with the same file name, so a count check
#     cannot tell them apart. Running one project at a time is what makes the floors below
#     meaningful.
#
# **The floors are the point.** "Passed!" is not the result; the COUNT is. Observed 2026-08-17
# (B104): a solution-wide run reported
#
#     Passed!  - Failed: 0, Passed: 50, Skipped: 0, Total: 50 - Tf2DemoSalvage.Viewer3D.Tests.dll
#
# against a suite of 350, in one second rather than eighty. Nothing in that line is a warning, and
# the floor guarding it at the time was 34 — so the truncation would have passed the check as well
# as the eye. These floors sit just under the real counts for that reason.
#
# Ratcheted rather than exact: adding a test must not redden the build, but removing three hundred
# must. Raise them when the suite grows; lowering one is a decision to state out loud.
#
# **"Test Run Aborted" in the viewer suite WAS the code, and this note used to say otherwise.**
# It read: "probably the desktop, not the code... another application was in exclusive full screen".
# That was written after a single abort on 2026-08-20, was never tested, and stood as the standing
# explanation until 2026-08-24 — when the suite aborted three times in five runs with nothing else
# holding the GPU.
#
# The cause was B178: six fixtures construct a Windows Form, and the assembly runs
# ParallelScope.All with no [Apartment], so forms owning D3D swap chains and OpenAL contexts were
# built concurrently off the STA. Fixed by marking those fixtures; 0 aborts in 3 runs afterwards.
#
# **Kept as a warning about this kind of note.** An explanation that blames the environment,
# excuses a crash, and is never tested will survive indefinitely, because nothing about it can
# fail. If a run aborts here again, suspect the code first.
#
# Still true: this suite is NOT run under run-exclusive.ps1 the way the UI suite is, because it
# takes no desktop of its own — it just wants a GPU that nobody else has taken exclusively.
#
# **No --filter here, and that is deliberate.** Passing one changes which tests EXIST, not merely
# which of them run: NUnit's adapter includes [Explicit] tests when no filter is given and drops
# them as soon as any filter is present. Measured on Content.Tests — 441 unfiltered against 436
# with `--filter 'FullyQualifiedName!~UiTests'`, the five being the diagnostic probes. Two
# invocations that look equivalent therefore report different totals, which is the same class of
# trap as the truncation above. Running whole projects one at a time needs no filter at all.
set -euo pipefail

cd "$(dirname "$0")/.."

here=$(dirname "$0")

# gcor unless told otherwise: the corpus suite over lcor takes about thirty minutes and over gcor
# about thirty seconds, and the difference is 774 MB of modern matches against 20 MB of era
# specimens. Pass TF2DEMOSALVAGE_GCOR_ONLY=0 for the full superset.
export TF2DEMOSALVAGE_GCOR_ONLY="${TF2DEMOSALVAGE_GCOR_ONLY:-1}"

# First, because it takes no time and its failure mode is silent: a decision number used twice makes
# every citation of it ambiguous, and nothing else in the build notices (B118).
"$here/assert-decision-numbers.sh"

run() {
    local project=$1 name=$2 floor=$3

    echo "=== $name"

    # No --no-build, ever: without a compile step a compile error does not stop the run, and the
    # runner tests whatever DLL was last written.
    dotnet test "tests/$project" --logger "trx;LogFileName=$name.trx" > "/tmp/gate-$name.log" 2>&1 || {
        echo "$name: the run itself failed; last lines follow" >&2
        tail -25 "/tmp/gate-$name.log" >&2
        exit 1
    }

    "$here/assert-test-count.sh" "**/$name.trx" "$floor" "$name"
}

rm -f /tmp/gate-*.log

# **Leave nothing running, and clean up on FAILURE too — hence the trap rather than a last line.**
# MSBuild's node reuse and the Roslyn `VBCSCompiler` both outlive the build that spawned them, on
# purpose, so eleven `dotnet test` invocations leave a pile behind: measured 2026-08-25 after one
# gate run, eight MSBuild nodes at ~110 MB each plus a VBCSCompiler holding 502 MB and 547 seconds
# of CPU — about 1.4 GB still resident with the gate long finished.
#
# **Shut down rather than disabled.** `MSBUILDDISABLENODEREUSE=1` would prevent them existing at
# all, but node reuse is worth having ACROSS the eleven projects while the gate runs; the defect is
# only that they persist afterwards. Measured cost of the leftovers on the run itself was small —
# the viewer stage went 2m18s standalone to 2m30s here — so the reason to do this is the memory,
# not the seconds.
#
# `dotnet build-server shutdown` rather than pkill: CLAUDE.md's gotcha 10 is that `pkill -f` matches
# the shell running it, and this script's own command line contains every pattern worth matching.
trap 'dotnet build-server shutdown >/dev/null 2>&1 || true' EXIT

# **The floors are the CURRENT counts, not a comfortable distance below them.**
#
# A floor exists to catch a run that reported success while executing a fraction of the suite —
# a crashed test host does exactly that, and prints "Passed!" with a truncated total. A floor
# with slack in it cannot: core's sat at 1000 against 1163 actual, so 163 tests could vanish
# silently. That is the same defect as B104, where 34 guarded 352.
#
# Set to the exact count, so adding tests keeps passing and REMOVING them fails until the number
# is lowered on purpose. The ratchet is the feature — every lowering should be a deliberate edit
# in the same commit that deleted the tests, which is what makes a silent loss impossible.
# Raised to 1463: FogControllerConformanceTests, which compares EntityState's fog wire names against
# the SENDINFO_STRUCTELEM declarations in fogcontroller.cpp.
# Raised to 1487: DemoHeaderHostileInputTests, 24 cases of input no engine writes — a stamp one
# byte wrong, an unterminated text field, invalid UTF-8, negative counts, NaN and infinity. The
# corpus cannot supply any of them, because every demo in it was written by the engine.
# Raised to 1491: SchemaGapTests, four cases proving the gap instrument works in both directions
# and across both metadata encodings before any marker rests on it.
# 1497: SoundCharConformanceTests, the six that pin soundchars.h against SoundName.
# **Lowered 1504 -> 1497 on 2026-08-22, and the arithmetic is the justification.**
# SoundAttenuationConformanceTests (7) moved to Audio.Tests with SoundAttenuation itself (D53).
# Nothing was deleted: core -7 and content -33 are exactly the +40 the audio floor gained.
# Raised to 1512 on 2026-08-27: SkipTests, eight cases on the shared skip helper. Five predict the
# exact exception type an absent prerequisite produces — IgnoreException, never AssertionException —
# which is the distinction that reddened CI twice that day; three assert the reason survives. None
# touches the filesystem, so all eight run on a runner too.
# Raised to 1529 on 2026-08-27: MovementConVarConformanceTests (6) and ServerConVarsTests (11),
# D106's declarations and the resolver that reads a demo's replicated values. Neither touches the
# filesystem beyond the SDK and cvarlist.log, which the SDK test skips without.
# 1538: InterpolationConVarConformanceTests (5) and SoundConVarConformanceTests (4), completing the
# twenty of D104. The interp suite writes the engine's formula down and implements none of it; the
# sound suite records that cvarlist.log disagrees with the binary about snd_gain_min.
# 1539 -> 1544 on 2026-08-28: ViewmodelAnimationParityConformanceTests (5), which writes down
# C_BaseViewModel::UpdateAnimationParity before implementing it. m_nAnimationParity is how the engine
# says "play that again" when the sequence number cannot change; without it a repeated shot recorded
# no timeline entry at all and played no animation.
# 1544 -> 1550 on 2026-08-29: DirectorShotTests (6), which decode hltv_chase from an AUTHORED demo.
# No demo in the corpus carries the event — measured, not assumed — so the specimen had to be
# written; see the corpus test that asserts that absence and will go red when it stops being true.
# 1550 -> 1556 on 2026-08-29: RenderStateDecodeTests (6) — m_clrRender, m_nRenderFX, m_nRenderMode
# and m_iObserverMode read off HAND-BUILT entities. Synthetic and in Core.Tests deliberately (D38):
# `Corpus.Tests` needs Git LFS, so Stryker never mutates anything placed there, and the two corpus
# suites these replace took tens of seconds to assert what a demo happens to contain.
# 1556 -> 1563: WearableClassConformanceTests (7). CEconWearable::Spawn and CBaseCombatWeapon::
# Equip both call FollowEntity outside their server-only guards, so the client sets EF_BONEMERGE
# itself and it never travels -- 26 of 26 CTFWearable send no m_fEffects at all. Synthetic, and the
# table chain is deliberately not an identity so a one-level walk fails.
# 1563 -> 1567: ReentryPreservesStateTests (4). The class baseline was re-applied on EVERY Enter,
# so an entity that left and re-entered the PVS lost everything it had accumulated -- measured on a
# spawn-door prop re-entering with the SAME serial and zero properties. Three of the four are
# controls: a new serial must still take the baseline, a first enter must, and a delta must not.
# 1567 -> 1575: HandleSerialConformanceTests (5) and ModelIndexFollowsUpdatesConformanceTests (3).
# Both are halves of PostDataUpdate this project had left out. A handle is an index AND a serial and
# masking the serial away resolves a dangling handle to a real, existing, different entity; and
# ValidateModelIndex sits beside HierarchySetParent ABOVE the DATA_UPDATE_CREATED test, so the model
# is re-applied every update while a track fixed it at construction -- which named cp_fulgur's BLU
# spawn door a resupply cabinet for a whole recording, off the class baseline. Two of the three
# model tests are controls: an unchanged index must keep its model, and a changed one must not
# split the track in two.
# 1575 -> 1584: EntityBaselineSlotConformanceTests (9). svc_PacketEntities names one of two
# PER-ENTITY baseline arrays and periodically asks the client to rebuild the other; both fields were
# decoded, round-tripped and consumed by nothing, so every entering entity was read against its
# CLASS baseline -- one representative entity's state shared by the whole class. 2,340 snapshots in
# the owner's recording set update_baseline. Six of the nine are controls, because every wrong
# implementation here still produces plausible numbers: no stored baseline must fall back, the other
# slot must not see the store, a stored baseline of a different class must not apply, a full update
# must ignore it, a snapshot without the flag must store nothing, and a delta update must not become
# a baseline. Five sabotages, each killing exactly its own test.
# 1584 -> 1588: WeaponItemModelConformanceTests (4). A weapon's model comes from its ITEM --
# CEconEntity::UpdateModelToClass resolves pItem->GetPlayerDisplayModel (econ_entity.cpp:382) --
# and every CWeaponMedigun networks NEITHER m_nModelIndex nor m_iWorldModelIndex while stating
# item 211. A null model returned outright, so no track existed and no medigun on any other
# player was ever drawn. Two of the four are controls: a weapon that DOES send a world model
# keeps it, and a track with neither model nor item still stays out of Props, which is the rule
# this relaxes.
# 1588 -> 1591: SequenceParityConformanceTests (3). A prop's animation clock was never stamped,
# so EntityModels' `elapsed = seconds - AnimationStartSeconds` was the WHOLE RECORDING -- the
# spawn cabinets looped for ever, then after the cycle clamp held their last frame for ever.
# C_BaseAnimating measures from a stamp (c_baseanimating.cpp:5480) and knows an animation
# restarted from m_nNewSequenceParity (:4737), which this project read only on the viewmodel and
# whose own remarks admitted it was decoded and not acted on. Two of the three are controls: a
# prop that never restarts keeps its clock, and a prop REPLAYING the same sequence must still
# restart -- which is why the counter exists and why comparing sequence numbers is not enough.
# 1591 -> 1592: the client-side frame reset. C_BaseAnimating has TWO restart signals and reads a
# different one per mode -- m_bClientSideFrameReset only when m_bClientSideAnimation is set
# (c_baseanimating.cpp:5021), m_nNewSequenceParity in either (:4737). cp_fulgur's cabinets are
# client-side animated and send NO server cycle at all, so the toggle is their restart and a fix
# built on parity alone did not move them. The case carries its own control inline: parity and
# sequence held still while the toggle does not move must NOT restart.
# 1592 -> 1593: the frame-reset toggle counts only in CLIENT-side mode, which
# c_baseanimating.cpp:5021 guards it with and the first version left out. Found by auditing every
# EntityState accessor for a production caller -- ClientSideAnimation had none, which is this
# project's recurring failure exactly. The case IS the control: a server-animated prop toggling
# the field must NOT restart on it.
# 1593 -> 1598: PlayerConditionConformanceTests (5). A TF condition is one bit of FIVE networked
# variables chosen by the condition's number (CConditionVars, tf_player_shared.cpp:1041), and this
# project read none of them -- DT_TFPlayerShared was 0 of 66 in WIRE-COVERAGE. Three of the five
# are controls: a bit set in the WRONG variable must not answer, an empty set answers nothing,
# and bit 31 must read as set rather than as a negative int.
# 1598 -> 1599: kRenderNone, and the case here asserts where it does NOT belong (B240). Testing the
# render mode in EntityState.IsDrawn removed the invisible func_doors from the SCENE, and every
# grate prop is parented to one -- so the children lost the transform they hang off and every gate
# vanished outright. ShouldDraw stops an entity DRAWING; CalcAbsolutePosition composes a child onto
# its parent without asking whether the parent renders. The rule lives in Scene now.
# 1599 -> 1602: a weapon's m_iState belongs to the MOMENT, not to the track (B244). It was a track
# scalar, written while the demo was parsed, so every reader asking about a tick received the state
# at the recording's LAST tick -- and a medic whose medigun happened to be holstered at the end drew
# empty-handed for the entire demo. Two of the three are controls, and both are needed: asking only
# about the early tick passes against a track frozen at the FIRST state, which is the same bug
# mirrored, and only the holstered-then-drawn case exercises a weapon coming back into a hand.
# 1602 -> 1605: an entity entering the visible set is decoded against a BASELINE (B245). Read out of
# engine.dll, because the SDK ships no engine networking: CL_CopyNewEntity picks a fromBuf - the
# per-entity checkpoint when the snapshot is a delta and the stored class matches, else the class
# baseline, whose absence is fatal - and never uses what the client is holding. Two of the three are
# the weapon case and its control (a weapon still in the PVS keeps what a delta omits); the third is
# a control on the re-entry tests themselves, because "decoded against its own checkpoint" and
# "kept whatever we had" predict the same observation unless one case has no checkpoint.
# 1605 -> 1607: a per-entity checkpoint must LAYER over the class baseline, not replace it (B248).
# The engine replaces and is right to, because its checkpoints are complete packed entities; ours
# are built from whatever a snapshot carried, so a partial one shadowed a complete class baseline.
# It shipped for an hour and turned cp_fulgur's invisible spawn doors into solid brushwork -
# CBaseDoor's baseline declares m_nRenderMode = 10 and nothing else restates it. One of the two is
# the control: where BOTH know a property the checkpoint must still win, or a re-entering entity
# gets dragged back to one representative entity's state, which is what B231 measured.
# 1607 -> 1616: econ attributes decode (B234). Four on the STATE - the element collision (twenty
# vector elements sharing one Table.Prop key, last write winning), the two lists kept apart, the
# era float spelling folding into the same bits, and the empty control. Five on the RESOLUTION -
# IterateAttributes' chain branch for branch, including the else-if that forecloses the definition's
# attributes when the demos list is taken, and the all-ones INVALID_ITEM_ID gate.
# 1616 -> 1617: B258 split the derived-pose test in two - PlayersAt fills move_x, move_y and
# Speed, PropsAt leaves them alone, since ComputePoseParam_MoveYaw is player animation state and
# the engine derives none of it for a prop.
# 1617 -> 1620: the interpolation list (B259). Three tests: an entity on the list is blended, one
# off it holds its last stated pose, and no list interpolates everything - the last being what every
# caller that does not care relies on.
# 1620 -> 1625: ConstantTrackTests (B259 fix 3 stage A). A track with one keyframe can never answer
# differently, so its whole record is built once - 677 of 1,165 tracks on tf2-2026-pub-pov-clean.
# The load-bearing test is the MOVING pair: a cache that never updates and one that never hits look
# identical from outside, and only a track that must differ at two ticks separates them.
# 1638 -> 1648: the wire's pose parameters (B269). Seven for the decode and the looping
# interpolation LoopingLerp already implemented for the animation cycle, three for how a track
# blends them - including the control that a NON-looping parameter must not wrap, without which the
# looping test passes against code that wraps everything.
# 1648 -> 1650: the model scale's pre-2013 wire name (B271). One for reading it and one for the
# modern name winning when both arrive.
# 1650 -> 1657: m_flSimulationTime's tick encoding (B273). Three for GetNetworkBase, including the
# every-index round trip that is the control against a plain tick/100, and four for the recentring
# that makes eight bits able to name a tick.
# 1657 -> 1663: m_flAnimTime, the second latch clock (3), and the applied-time stamping (3) - the
# last including the control that the SAME two keyframes with no lag are still part way through,
# without which the correction is indistinguishable from clamping early.
# 1663 -> 1665: the animation clock's own history (B274). One where the two clocks disagree and
# each field follows its own, and the control where they agree - without which the first passes
# against code that simply holds the cycle back.
# 1665 -> 1667: EXCLUDE_AUTO_INTERPOLATE on a client-side-animated entity's cycle (B276), and the
# control that a SERVER-animated one still blends - without which the fix is indistinguishable from
# having stopped interpolating the cycle for everything.
# 1667 -> 1668: the model scale is not an interpolated variable (B277), found by asking the inverse
# question - not which flags the engine sets, but whether a field is in AddVar's list at all.
# 1668 -> 1669: the spline's third sample is chosen on the changetime gap, not on arrival (B278).
# The test needed its CONDITION fixed rather than its assertion: the first version sampled a moment
# sitting on the last keyframe, so `At` returned early and never reached the spline.
run Tf2DemoSalvage.Core.Tests     core     1678

# Raised to 74: UndeclaredHeaderReportingTests, six cases covering each clause of the CLI's
# "did the header state a length" check plus the finalised-header control.
run Tf2DemoSalvage.Cli.Tests      cli        74

# 16: the file logging provider (9) plus FileRetention (7), which moved here with the type it covers.
# Every case is a regression test before it is a unit test — each guarantee in the sink was paid for
# by a defect in the static logger it replaces (D83): a per-line open-and-close that wrote 450,157
# lines into 37 MB, retention that raced its siblings into 207 files against a limit of 50, and an
# IO failure that must cost its lines and nothing else. Device-free and net10.0, so it runs on the
# Linux measurement boxes — which is half the reason the sink left the viewer at all.
run Tf2DemoSalvage.Logging.Tests  logging    17

# 7: the GDI glyph rasteriser (D84). Only what genuinely needs a real font — that a face produces
# ink, that a space has none and still advances, and that the scheme's `outline` reaches the pixels.
# Everything about a HUD that is arithmetic is tested in Content.Tests against a fake rasteriser.
#
# The space test earned its place immediately: StringFormat.GenericTypographic does not measure
# trailing spaces and a lone space is entirely trailing, so every space in every HUD string had a
# zero advance. Invisible to every other test here, because they all measure glyphs with ink.
#
# Skips rather than measuring a fallback when Lucida Console is absent. GDI substitutes a default
# face for a missing family instead of failing, so without the skip these would pass having measured
# a font nobody asked for.
#
# **The floor cannot see that**, and it is worth knowing rather than assuming otherwise: this script
# reads `total` from the .trx, which counts skipped tests. Seven skips satisfy a floor of seven. That
# is the standing hazard in docs/memory/a-skip-is-not-a-pass-or-a-failure.md, not a new one — the
# skip protects the MEANING of a pass, and the floor protects the count. Neither covers the other.
run Tf2DemoSalvage.Fonts.Tests    fonts       7

# 15: the bone pipeline's denominator (B182) and the tests for the instrument that produces it.
#
# The denominator now covers BOTH halves — SetupBones itself (10 stages) and StandardBlendingRules
# inside it (12). Extending it caught a second instrument bug, and a worse one than the first:
# asking for "bool C_BaseAnimating::SetupBones" matched SetupBones_AttachmentHelper, declared 700
# lines earlier, so the denominator for the most important function in the pipeline came back as
# eleven attachment calls — every one of which looks like a plausible stage. A match is now refused
# when the next character can continue an identifier.
#
# Two tests read the engine — that StandardBlendingRules' body is found and plausible, and that
# every call it makes is classified as a stage or as noise with a stated reason. The other eight
# cover SdkInventory.Live/CallsIn, which earned them: its first run reported AddTextOverlay,
# GetAbsOrigin and Vector as engine stages, all three from a COMMENTED-OUT line. A denominator that
# reports deleted code asks somebody to implement what Valve removed, and the resulting work looks
# like parity while being its opposite.
#
# The classification test fails on an UNCLASSIFIED call, never on a gap — same contract as
# SdkCoverageTests. An unimplemented stage is a fact to report; an unrecognised one means the engine
# grew something nobody here has looked at.
#
# Plain net10.0 with its own Stryker config, deliberately (B184): Tf2DemoSalvage.Scene is net10.0
# and has NO Stryker config at all, because its pose-path tests live in the Windows-pinned
# Viewer3D.Tests and mutation runs happen on Linux. The replacement must not inherit that.

# 24: AnimatingEntity arrives (9). Every one of those tests is a COUNT, because what is being built
# is not a value — it is a decision about when work happens. "Posed once per frame" and "posed every
# time" produce identical matrices, so no assertion on a matrix can separate them.
#
# Two sabotages, and the second is the instructive one:
#
#   - inverting the per-frame check reddens 1 of 9;
#   - replacing the subset test with a naive "have we posed this frame" cache also reddens 1 of 9 —
#     and passes the other EIGHT. SetupBones_ForABitNotYetBuilt_BuildsAgain is the single test
#     standing between a correct mask cache and a plausible wrong one that returns attachment bones
#     nobody built, which places every attachment at the map origin.
#
# 33: BoneMergeCache (9). The three-deep test is B180, and it is not "fixed" — the state B180
# described no longer exists. One array per entity, the merge writes into it before the transform
# stage, so a bone whose parent was merged rides the merged position with nothing written to make it
# happen. The numbers are chosen so the two readings differ: 40 if the sight rode the merged hand,
# 7 if it read the weapon's own skeleton.
#
# Two tests changed meaning when the merge arrived and that is worth knowing: an entity whose bones
# share no NAME with its parent's does not cause the parent to be posed at all — nothing pairs, the
# follow mask is 0, and the early-out returns. Every test about the recursion has to give the two
# skeletons a bone in common or it measures an entity that never asks.
#
# 36: SkeletonPose, the adapter between the architecture and a real .mdl, with three tests that run
# the WHOLE pipeline on models TF2 ships — a scout's skeleton, a hat merged onto it, and the hat's
# unmatched bones riding the merged parent. Every other test in this assembly uses a fake, so
# together they prove the architecture agrees with fakes written by the same hand; these read
# Valve's files and are the only ones that can fail if the wiring is wrong.
#
# The control test measures along Y, not Z, and that is not a typo: the BIND pose is Y-up, and Z-up
# is what an animation produces. Asserting Z first measured -1.43 and read as "the head is at the
# feet" when it means "the head is where the artist modelled it".
# 41: WeaponMergeContentTests (4) — whether a weapon actually pairs with the class holding it, on
# the models TF2 ships. Measured: c_stickybomb_launcher shares weapon_bone and weapon_bone_1 with
# demo.mdl, 2 of 5. Written because the viewer put weapons in the wrong place and the log could not
# say whether they had paired — the diagnostic for the thing that broke had been deleted with the
# code it lived in, and is now back in BoneMergeCache where it belongs.
# 41 -> 43: the fraction reaches the animation sampler (B279). The arithmetic and the wiring are
# separate defects and only one is loud, so this asserts on what SkeletonPose HANDED the sampler,
# with the control that a pose never given a fraction hands across zero.
# 76 -> 78: IK_RELEASE gives a correction back (B299). One at nearly full weight returns the chain
# to the animation's own pose; one at half lands between — and the half case is what separates a
# release that is APPLIED from one that is ignored, since at full weight "returned it" and "never
# solved" look alike. Nearly, not fully: AddDependencies drops a full-strength release after
# clearing its chain, so weight 1 cannot reach the solver.
run Tf2DemoSalvage.Animation.Tests animation 78

# 23: the scene layer's first test project of its own, and the reason it exists is B184 — Scene is
# plain net10.0 and holds the densest behaviour in the renderer, but every test of it lived in the
# Windows-pinned Viewer3D.Tests, so it had NO Stryker config and could not run on the Linux boxes.
#
# What is in it is the three collaborators B181 pulled out of the draw loop, and the assertions are
# about FREQUENCY rather than values — which is the half that has actually gone wrong here. A
# per-frame line once printed 1,280 times a second (B163); a once-per-model line let a bright
# control point silence a dark one for ever; and the bone-merge report vanished entirely with the
# method it lived in, leaving a viewer run unable to say whether weapons had paired.
#
# "Sampled once" and "sampled every frame" produce identical cubes, so no assertion on a cube can
# separate them. Verified by sabotage: one flipped comparison in the lighting cache reddens exactly
# For_AModelThatHasNotMoved_IsSampledOnce and nothing else.
#
# 34: ViewmodelSceneTests (11). AddViewmodel was 319 lines inside a 7,263-line form and had no tests
# and could not have any — reaching it meant constructing a MainForm, which needs the STA, a device
# and the desktop lock. Three open bugs against that path (B170, B186, B187) had no regression test
# between them.
#
# It moved to Scene, so these are plain net10.0. What they pin is the pair of EXCLUSIVE schemes in
# CTFWeaponBase::GetViewModel — the weapon is either its own viewmodel or a second model parented to
# the class's arms — because drawing both is how one weapon becomes two on screen. The path
# comparison has its own test: the two names come from different places and disagree on slashes, and
# getting it wrong does not throw, it draws the wrong number of models.
#
# **Lowered 645 -> 625 on the viewer and raised 34 -> 54 here in the same commit, and the
# arithmetic is the justification**: exactly twenty moved, nothing was deleted. EntityModelsTests,
# SyntheticSkinnedModel and PlayerAnimationFallbackTests all test Tf2DemoSalvage.Scene types and
# reference no device, no form and nothing from Windows — measured, zero hits for Device3D,
# MainForm, System.Windows.Forms or Silk across all three.
#
# This is the rule B184 records, applied: a piece's test moves in the same commit as the piece, or
# the Windows pin is recreated one file at a time. That is how it reached 115 of 119.
# 88: LevelLightingTests (12), out of MainForm.LightAt/SunAt. The map's lighting query is the
# engine's — IVEngineClient::ComputeLighting, cdll_int.h:392 — and had no test at all, because
# reaching it needed an STA thread, a device and a real map.
# 90: EntityModelSet.Geometry (2), the model source set at map load rather than passed per call,
# which is how the client reaches modelinfo (IVModelInfo.h:146).
# 97: DecodedDemoTests (7), out of MainForm.Decode. Two of them need a demo that carries no schema
# and one that carries a corrupt one — inputs no real file contains, so they are authored through
# DemoWriter rather than taken from the corpus.
# 115: MomentSceneTests (18), out of MainForm.ShowMoment and the four members it drove. Three of
# them exist because of a regression this move nearly shipped: dropping the EnsureWeaponRoles call
# leaves every weapon suffix null and the animation falls back silently, and the viewer suite passes
# 620/620 against it (B193). The tests read `Pose.Slot` out the far end, which is the only place the
# difference shows.
# 123: WeaponModelsTests (8), out of MainForm.WeaponModelFor/WeaponModel/ItemDefinitions. Both
# routes are pinned and so is the order between them: the item index is what the player equipped,
# and the weapon class only knows the stock version — preferring it would draw a stock rocket
# launcher for every reskin in the game. Measured on z1800, 22 of 56 held weapons send no item index
# at all, so the second route is not a fallback for rare cases.
# 133: SpectatorViewTests (10), out of MainForm.FollowedEntity/Spectated/FirstPersonCamera/PlayerAt/
# Ducking. Every case is a pair of demo KINDS, because the two mechanisms are the subject: a POV
# demo carries a recorded camera and an STV demo does not, and one kind alone cannot tell "picked
# the right mechanism" from "only ever does one thing".
# 135: the two viewmodel-source cases in MomentSceneTests, which exist because the missing wiring
# they now report actually shipped (B193).
# 138: the three model-upload cases, added after an audit found `MomentScene.Upload` was assigned
# NOWHERE — so no entity geometry ever reached the GPU (B193, third occurrence).
# 142: GameInstallTests (4), out of ReadMap first-map-only branch. Every case runs WITHOUT TF2,
# which is the path a fresh clone takes and the one that had no test at all.
# 147: DemoModelsTests (5), out of MainForm.DemoModelPaths/WornModelPaths. A sixth was written and
# removed: it asserted the class roster using the real locator and FAILED, because
# Tf2ConfigFiles.DefaultGameFolder looks under Program Files while this machine keeps TF2 on another
# drive. The code was right and the test was measuring the ENVIRONMENT — pointing it at a better
# path would have hidden that rather than fixed it.
# 202 -> 203 on 2026-08-28: EntityModelsTests gains the two-pass wiring case. It is the join between
# a flag read out of a `.mdl` and a renderer that decides from a boolean, and neither side's tests
# can see it — the assignment being absent is the shape of no-op this project has shipped three
# times green.
# 203 -> 208 on 2026-08-28: ChaseCameraConformanceTests (5) against C_HLTVCamera::CalcChaseCamView,
# written before the implementation. Third person is not a nicety here — it is the mode the engine
# falls back to whenever first person is unavailable, and both viewmodel rules this project has been
# arguing about reduce to "the camera is not in an eye".
# 211 -> 215 on 2026-08-28: SpectatorEffectiveModeTests (4). A dead target is watched in third
# person, and the subject is the MODE rather than a swapped camera — which is what makes it a fix
# rather than a fourth attempt at the same citation (D116).
# 215 -> 222 on 2026-08-29: ChaseDirectorConformanceTests (7). The chase camera ignored every
# parameter the hltv_chase event carries — distance, phi, theta, offset — and the director's second
# target, each with a comment saying so. D117 is the rule that came out of that.
# 222 -> 235 on 2026-08-29: ObserverModeConformanceTests (11) writes down
# `C_BasePlayer::LocalPlayerInFirstPersonView` before anything decoded `m_iObserverMode`, and two
# cases pin B225's actual mechanism — `Effective` was asking `SpectatorTarget.Choose` about a
# point-of-view demo, so the mode was decided by the liveness of a player who was not the one being
# watched. The second of those two is the control, and without it "is ANY player dead" would pass.
# 235 -> 238: ParentedPropPlacementTests (3). SceneProp.BoneMerged defaults to false, which is a
# legitimate value, so every construction site that stayed silent claimed it — and ViewmodelScene
# did, which is what made the first-person weapon vanish. The third case enumerates the defaulted
# parameters by reflection so the next silent claim fails here.
# 238 -> 244: WeaponPropModelsTests (6), the Scene half of the same fix -- which props are asked
# about, what they are asked, and what is done with the answer. Four are controls: a prop that
# already has a model is untouched, one with no item is untouched, a lookup that answers null
# must not blank the prop (CI has no game install, so every lookup returns null there), and an
# owner this moment does not know about must ask with NO class rather than with somebody's.
# 244 -> 246: two cache cases for WeaponPropModels. Resolving a weapon's model from its item
# ran EVERY FRAME for every prop awaiting one -- measured on the owner's machine, drawlist mean
# 2.9 ms before and 46.6 ms after, 1,201 slow moments against one, because 122 of cp_fulgur's
# 1,158 prop tracks await a lookup. The key is Valve's own invalidation rule: UpdateModelToClass
# is called from OnOwnerClassChange and ReapplyProvision, so item + owner class is exactly when
# the answer can change. The second case is the control -- one item is four models across
# classes, so a cache keyed on the item alone would hand the engineer the soldier's shotgun.
# 246 -> 247: the item now WINS whenever it names something different, which is
# CEconEntity::UpdateModelToClass's actual rule (econ_entity.cpp:411) rather than the narrower
# 'fill in when the wire said nothing' that shipped first. The new case is its own control's
# partner: an item naming NOTHING must leave the wire's model alone, which is the other half of
# Valve's own `if ( pszModel && pszModel[0] )` and the reason the wider rule is safe on a machine
# with no game installed -- every CI run.
# 247 -> 255: DisguiseConformanceTests (8). A disguised spy is drawn as their disguise TO THE
# ENEMY and only to the enemy -- C_TFPlayer::ValidateModelIndex:8990 and GetSkin:7790, both
# gated on InCond( TF_COND_DISGUISED ) && IsEnemyPlayer(). Half are controls, and the friendly
# case is the one that matters: a teammate sees the spy AS a spy with a mask offset, so an
# implementation without IsEnemyPlayer hides every friendly spy -- the opposite of the game.
# 255 -> 263: DisguiseVisibilityTests (7) and the OfDisguise arm of the SceneProp tripwire (1).
# `ValidateModelIndex` decided which BODY a disguised spy wears; it said nothing about the gear,
# and the gear is separate entities bone-merged onto him. So a friendly spy wore the disguise's
# soldier hats and carried its rocket launcher -- the owner watched it at ticks 870-903 and named
# them. CTFWearable::ShouldDraw (tf_item_wearable.cpp:344) and CTFWeaponBase::ShouldDraw
# (tf_weaponbase.cpp:3226) are MIRROR IMAGES rather than one rule twice: an enemy loses the spy's
# real weapon, a teammate loses the disguise's, and implementing one direction leaves him holding
# two weapons to somebody. Four of the eight are controls -- an enemy must still SEE the disguise,
# an undisguised player's hats are untouched, the spy's own body is never removed, and an enemy spy
# posing as one of OUR spies shows nothing.
# 263 -> 270: RespawnRoomVisibilityTests (7). A spawn's team wall is not drawn to the team that
# spawns behind it -- C_FuncRespawnRoomVisualizer::DrawModel, c_func_respawnroom.cpp:47, whose own
# comment is "Don't draw for friendly players". Measured on cp_fulgur at tick 900: NINE of these
# were in the draw list, three standing inside the stage-one setup gates the owner reported as
# wrong. Three of the seven are controls -- the enemy's wall must still draw, the gate itself must
# never be touched, and an unknown round state must draw rather than being read as the win state.
# The seventh asserts GR_STATE_TEAM_WIN is 5: the enum gives an explicit value only to its first
# member, a first draft said 4, and 4 is GR_STATE_RND_RUNNING -- which would have hidden every
# spawn wall for the whole match while all six other tests passed.
# 270 -> 277: the spy's mask is a BODYGROUP as well as a skin, and only the skin was implemented
# (B236). GetSkin picks WHICH mask is painted; ValidateModelIndex's tail (c_tf_player.cpp:9024) sets
# the body part named spyMask, and on the shipped models/player/spy.mdl the mask mesh is alternative
# 1 of that part -- so at m_nBody = 0 the texture was painted on a mesh nobody drew. Five cases for
# the rule, two for the wiring. Three of the five are controls: an enemy seeing a demoman must NOT
# see a mask, an undisguised spy must lose it, and a disguised player who is not a spy must be
# refused because only the spy's model has that part. The two wiring cases are the only ones that
# could have caught the bug -- WearsMask and the skin offset both had passing tests while nothing
# set the body.
# 277 -> 280: kRenderNone applied where drawing is decided (B240). Two of the three are controls,
# and the third is the one the first attempt broke: a child of a kRenderNone parent must still find
# that parent, because the parent stays in the scene and only its drawing is refused. The other
# control keeps this from deleting the game -- every mode but 10 is a blend that still draws.
# 280 -> 283: the viewmodel takes its owner's team skin (B242). CEconItemView::GetSkin takes the
# team and nothing was passing one, so every viewmodel drew family 0 -- which is RED on every
# two-family c_ model: c_medigun skin0 'c_medigun' skin1 'c_medigun_blue', c_medic_arms skin0
# 'medic_red' skin1 'medic_blue'. The player's BODY has taken its skin from its team since
# PlayerProps was written; the hands in front of the camera never did. Two of the three are controls
# -- RED is family 0, which is ALSO what an unset skin gives, so a BLU-only test could be satisfied
# by a rule that always returns 1 and a RED-only test by changing nothing.
# 283 -> 285: the attachment display-flag mask (B252). DrawEconEntityAttachedModels is called with
# WorldModel from the world draw and ViewModel from the viewmodel path, keeping entries whose
# model_display_flags intersect - both directions asserted, because until the first-person props
# carried an item every attachment was world-drawn and an unfiltered list was indistinguishable
# from a filtered one.
# 285 -> 291: the frustum cull moved ahead of the pose (B254, B255), which is
# CollateRenderablesInLeaf's order. Three tests for the cull, the middle one being the control:
# behind the camera is not posed, in front of it is, and no frustum culls nothing. Then three for
# the Build/Pose split it needed - Build selects without posing, Pose produces the instances, and
# the players survive the gap between them (B257, which took three attempts to make failable).
# 308 -> 321: the distance fade (B268). Eight conformance tests for ComputeDistanceFade's own
# arithmetic - the swap, the -1 branch, the squared falloff - and five for the WIRING, which is
# where the defect actually was: FxBlend.Compute took clientSideFade all along and no caller ever
# passed it, so the conformance half would have passed throughout. The wiring five assert on
# ModelInstance.Alpha coming out of EntityModelSet.Instances.
# 321 -> 329: the wire's pose parameters reaching the blend (B269). Six on `PoseValuesOf`, which
# reports the array the skeleton was posed with, and two on `TimelineMoments.OnNewModel` - the one
# call that travels from the scene INTO the demo, and the only hop in that chain that can fail
# silently.
# 329 -> 332: the client-side animation selection stays wired (written during B279 on a diagnosis
# that was WRONG - the call was never missing; a grep truncated by `head -6` hid it). Kept because
# the scene-level one is the only test that reddens if MomentScene.Build ever loses the call.
# 362 -> 364: a delta blend grid keeps its seeding (B298). One asserts an unlisted bone comes back
# at identity, one asserts the SAME fixture without the delta bit comes back at its rest pose - and
# the pair is the point, since a fix that simply zeroed every unlisted bone would pass the first
# alone. This is what stood seven of fifteen players on their heads.
# 364 -> 366: a sequence restarting at the same NUMBER cross-fades (B300). One asserts the outgoing
# run is queued when only the start time changed, one is the control that an unchanged prop queues
# nothing — without which the first passes on any prop seen twice, and every entity would
# accumulate a fade on every frame.
# 366 -> 369: a scaled model opts out of IK (B301), which needed SyntheticSkinnedModel to be able to
# declare an IK CHAIN in real .mdl bytes for the first time - nothing could test the IK wiring at
# scene level before. Three: the scaled case, an unscaled control without which "no IK ran" and
# "this fixture never had a chain" are the same observation, and a scale one float-epsilon from 1,
# which is Valve's own test rather than an exact comparison.
# 369 -> 374: the 3D skybox camera transform (B152). Five: the sky camera's own position at the map
# origin, the sixteenth-as-far movement that IS the illusion, scale 1 as the control that separates
# "honours the scale" from "ignores the viewer", scale 0 where Valve guards the DIVISION and not the
# offset, and the near and far planes, which are specific quantities rather than round numbers.
# 374 -> 379: r_3dsky's THREE states (B152). It is an int, not a switch: 0 off, 1 only when a
# SURF_SKY face is in view, 2 always - and the third is the one a bool would delete, which matters
# here because this viewer has a free camera that can stand where a map never expected. Plus the
# no-sky_camera case, which has to beat state 2, and the default, which is Valve's 1 and NOT
# cheat-gated where r_skybox on the next line is.
run Tf2DemoSalvage.Scene.Tests    scene     379
# Raised 28 -> 68 on 2026-08-22: RiffConformance (8), SoundScriptConformance (9),
# SoundScriptCatalogConformance (10), SoundScriptProbe (1) moved in from Content.Tests, and
# SoundAttenuationConformance (7) from Core.Tests — 40 in total, against -33 and -7 there. Sound
# belongs to the audio project, including its own parsing (D53).
# 76: SoundFileTests (8). TF2 re-encoded its voice lines from WAV to MP3 keeping the stem, so a 2007
# demo names a .wav that ships today as .mp3 — 60 of the corpus's 63 unopenable played sounds. The
# fallback tries the stated path first, with a both-containers-present control, because without one
# "fell back correctly" and "always uses the mp3" are indistinguishable.
# 89: SoundSampleReaderTests (13). One decoded type for both containers, since TF2 ships 82% MP3 and
# 18% WAV and the mixer must not care which it got. The subtle cases are the ones with tests: 16-bit
# PCM normalises against 32768 not 32767 (one value in the range clips otherwise), 8-bit WAV is
# UNSIGNED and centred on 128 where every wider depth is signed, and ADPCM is refused BY NAME because
# deferring it was agreed only "provided it is reported rather than silently skipped".
# 102: SoundGainTests (12) and the SNDLVL_NONE probe (1, [Explicit]). The tests are split by evidence
# class — Valve's cutoff is compared against the SDK, while the falloff shape and the pan law are this
# project's (B142) and so assert PROPERTIES any acceptable curve must hold rather than pinned values
# that a recovered formula would redden.
# 107: AudioOutputMixTests (5). The only part of the OpenAL sink with a decision in it — mono spread
# across two gains, stereo keeping its own image, and saturation on both sides of the clamp. Written
# device-free deliberately: CI and the measurement boxes have no sound card, and a test that needed
# one would skip exactly where it matters.
# 109: WaveLoopConformanceTests (2). A wave loops if it carries a `cue ` chunk (tier2/riff.h:187),
# with a one-shot as the control — a reader that always looped would pass the first assertion and
# turn every gunshot in the game into a drone.
# 116: ActiveLoopsTests (7). A loop has to be re-attenuated as the listener moves, or it keeps the
# gain implied by wherever the camera stood when it started (B169). Device-free, so it runs where
# there is no sound card — which is everywhere the gate runs.
# 121: SoundscapeCatalogConformanceTests (5). The soundscape list rebuilt from the shipped manifest
# and diffed entry-by-entry against one a running TF2 client printed — 153 for 153. The index is a
# position in that list, so a mis-order plays the wrong ambience rather than none (B173).
# 129: SoundscapeSelectionConformanceTests (8). Choosing a soundscape from the BSP, checked against
# seven positions where the owner ran soundscape_dumpclient in the live game. A differential: the
# engine's answer is the expectation, so this can disagree with it (B173).
# Raised 129 -> 139 on 2026-08-24: SoundscapeMixerTests gained the suppression case (1), and
# SoundScriptProbe gained the loop-marker report (1) that settled whether TF2's ambient waves carry
# a `cue ` chunk at all — they do, which ruled out the reader and pointed at the schedule instead.
# 142: SoundscapeSelectionConformance gains the PVS sensitivity case (B177) — every capture still
# resolves to what the client said with the filter ON, which is exactly what would happen if the
# filter did nothing, so one test measures the reduction instead: 6 of 44 placements from a spawn.
# 161: SoundCacheTests (10), out of MainForm.Sample and the three fields beside it. The engine keeps
# its sample cache behind IEngineSound (IEngineSound.h:89-91) and game code asks it, so a window
# owning one was ours alone — and none of it had a test.
run Tf2DemoSalvage.Audio.Tests    audio     183

# The presenter suite (D62). Sixteen tests, ~24 ms, no window and no desktop lock — which is the
# whole point: this logic lived in MainForm and could only be reached by driving a real form, so
# despite every rule in it having been written from a bug, none of them had a test.
#
# Raised 58 -> 95 on 2026-08-22 by D69's config console: nineteen conformance tests pinning what the
# engine does with a bound key, plus the alias and tokeniser cases underneath them. The floor had
# been left well below the real count, which is the exact failure it exists to catch — a truncated
# run passing the check as easily as it passes the eye.
# 108: SoundScheduleTests (7). Which sounds start as playback moves — the seek that must not replay
# what it skipped, the paused frame that must not repeat its tick, and the dropped frame that must
# not be mistaken for a seek. All three are silent failures that sound like a working viewer.
# Raised 108 -> 116 on 2026-08-24 by SoundSchedule.LiveAt (8). A looping ambient is STATE, not an
# event: cp_process starts six `)ambient/machine_hum.wav` at tick 4 and next mentions them at a round
# restart minutes later, so a cursor over events leaves the map silent from the first reposition on.
# The map load alone is enough to cause it — seven seconds of loading is 466 ticks, and the first
# Advance lands past both the tick-4 start and the tick-334 restart.
# Raised 116 -> 133 on 2026-08-24 by FpsMeterConformanceTests (17), which pins TF2's own
# `cl_showfps` panel — the smoothing weight, the watermark pair, the colour thresholds and both
# format strings — against `vgui_fpspanel.cpp`. Copied rather than invented because the meter exists
# to tell three stutters apart, and an instrument nobody already trusts cannot settle that.
# 135: PlaybackPresenter.Play (2). Setting the view's Playing flag does not start playback — the
# transport's setter deliberately does not raise, so the elapsed clock stayed stopped and the viewer
# sat at tick zero insisting it was playing. Finding it also required making FakeElapsedTime model
# STOPPED: it reported time passing after Reset, which made the fixture blind to the whole class.
# 146: FpsOverlayTests (7), out of MainForm.BuildHud. Every placement case is a pair, because one
# viewport width cannot tell "tracks the viewport" from "sits at a fixed x".
# 159: LaunchOptionsTests (13), out of MainForm.ReadCaptureOptions. Every malformed case is a PAIR
# with its well-formed twin, because a parser that ignored an option entirely passes any test that
# only checks the bad input is refused.
# 246 -> 240 on 2026-08-26 (B206): FreeLookStateTests (11) DELETED and FreeCameraControllerTests (5)
# added, which is a net -6 and the only floor drop here that is not a move. FreeLookState had no
# production caller at all — eleven tests on a type the viewer never ran, while the drag it actually
# performed was written out longhand in MainForm with none. The Fly cases were not ported because
# CameraFlightTests (6) and FreeFlightPathTests (10) already cover the live path including D65's
# cancel guard; the Drag cases and the pitch clamp were, since nothing else asserted them.
# 291 -> 270 on 2026-08-26 (D98): the orthographic camera and the flat player markers are gone, so
# MapOverviewTests (17) and MapZoomTests (8) went with MapOverview and MapZoom — 25 removed — and
# ViewCameraTests (4) is new, covering the first-person fallback that now lands on the free camera
# rather than on an overhead projection. 291 - 25 + 4 = 270.
#
# Nothing was lost that mattered: Valve's CanPlayerBeSeen rules, which MapOverviewTests asserted,
# are written up in docs/findings/38-which-players-can-be-shown.md because the markers return as a
# free-camera option and will need every one of them again.
# 374 -> 379 on 2026-08-26: FreeCameraConformanceTests, five, written before the fix for B215.
# They pin the ROAMING SPECTATOR as the parity reference rather than cl_demoviewoverride, on the
# owner's reading that a demo viewer imitates spectating; then sv_maxspeed*sv_specspeed = 960 as the
# ceiling, and +speed as Source's WALK key at 675 (70.3%, not 50% — the ceiling is computed before
# the halving, so the normal case is clamped and the walking case is not).
# 379 -> 382 same day: three more in FrameTimingConformanceTests for engine_no_focus_sleep (B209),
# which ships at 50ms and FCVAR_ARCHIVE. One asserts the value, one is the control that separates
# "sleeps when unfocused" from "sleeps always", and one pins zero as a real setting rather than a
# rejected one. Sabotage-verified: dropping the focus term reddens the control and nothing else.
# 382 -> 389 on 2026-08-26: WidgetKeysTests, seven, for the widened shortcut guard (B216). The type
# is in Presentation and names no toolkit, which is the point -- porting the front end rewrites the
# ten-line MainForm.FocusKind adapter, not the key rules. Sabotage-verified by dropping HOME from the
# slider set: one test reddened, and it exposed that an eighth assertion compared two calls that move
# together and so could never fail. That one now asserts a value.
# 389 -> 391 same day: list type-ahead (B216). One test asserts a focused list keeps typed
# characters, replacing one that asserted the opposite; one asserts nothing keeps a CTRL/ALT
# combination, which is what stops the playlist swallowing Ctrl+R.
# 396: FreeFlightServerSpeedTests, five cases on the free camera flying at the RECORDING server's
# speeds rather than two constants (D106). Two of them are the wiring — a SetServer with an empty
# body passes everything else in this project.
# 396 -> 402 on 2026-08-28: ViewCameraModeTests (6). ViewCamera.Active could not express three
# camera modes — its own comment recorded that two arguments sufficed because CameraMode had two
# members — and picking the wrong camera is the defect that reads as a culling bug rather than as a
# second camera, so each mode and each fallback is pinned.
# 402 -> 404 on 2026-08-29: `--autoplay` becomes a launch option (D118), plus the one case that can
# reach `Open`'s demo length without a timeline. Autoplay was an environment variable with exactly
# one reference in the repository — its own declaration — so nothing set it and nothing asserted it.
# 404 -> 412 the same day: `--look` and `--zoom` were parsed and consumed by NOTHING (B226). Five
# cases pin `OverheadPlacement.Framed`'s arithmetic and three pin the controller actually calling
# it — a split that earned itself immediately, since sabotaging the wiring reddens the second group
# and leaves the first entirely green.
# 412 -> 413 the same day again: the clock was the one source `Open` never cleared, reachable only
# once `SetDemoLength` stopped switching playback off as a side effect. The case that was there
# asserted `HasDemo` was false on a presenter that had never been loaded — precondition equals
# assertion — so it could not have caught it.
# 413 -> 414: the sound schedule must SURVIVE a level teardown (B228). It did not, and that silenced
# the viewer completely — `Apply` opens the demo and then reads the map, so `LevelSystems.Shutdown`
# nulled the schedule before a frame was drawn. The test that looked like it covered this set
# `sound.Schedule = null` as a precondition and never asserted on it.
# 414 -> 424: `cl_showpos` (D123). PositionReadoutConformanceTests (7), transcribed from
# `vgui_fpspanel.cpp:316` before the type existed — two spaces after each label, the mode switching
# only `pos` and `ang` while `vel` stays the player's, and `> 0` opening the block so `cl_showpos 3`
# draws the view rather than nothing. Plus three on `ToolsPanel`, which was `FpsOverlay` until this:
# Valve's `CFPSPanel` draws both readouts and walks ONE line counter across them, so the position
# sits below the frame rate and a panel that composed them separately would overlap them.
# 424 -> 425: the presenter asks its source for the round state. A WIRING assertion, which is the
# only kind that fails when a value is decoded, retained, unit-tested and never read -- exactly what
# m_flPlaybackRate was for weeks while every animation played at rate 1 with a green suite.
# 425 -> 430: FrameRateLog (D127). The viewer's only frame-cost instrument was StallReport's 30 ms
# threshold, which is silent about every rate above 33 fps - so a 20-second run logged nothing and
# that read as "no slow frames" when it means "nothing exceeded 30 ms". Five tests: the interval,
# the watermarks in the line, that the phases are a MEAN over the interval rather than the frame
# that crossed it, that a reported interval's frames are forgotten, and that a frame with no
# reading neither prints 0 fps nor starts the clock.
# 430 -> 434: MomentCostLog, the same treatment for the parts of `advance`, which the frame log
# showed is 70% of the frame. Four tests, the load-bearing one being that it averages every rebuild
# rather than reporting the one that crossed the count.
# 434 -> 438: --measure and --help (LaunchOptions). Both malformed twins included, per that file's own
# rule that a parser ignoring an option passes any test that only checks the bad input is refused.
run Tf2DemoSalvage.Presentation.Tests presentation 438
# Raised from 606 on 2026-08-21: OverlayLumpConformanceTests adds five (the overlay lump's packed
# field, each constant compared against Valve's own #define) and OverlayRenderOrderProbe one.
# 613: SoundFormatProbe, [Explicit], which measured the shipped audio formats before a decoder existed.
# 638: SoundScriptConformanceTests (9) and SoundScriptProbe (1, [Explicit]). The conformance suite
# checks SoundScript's defaults against CSoundParameters' constructor, its SNDLVL_/CHAN_ resolution
# against soundflags.h and the shipped script headers, and then reads all 21 game_sounds*.txt files
# TF2 ships — 13,052 entries, every one required to carry a wave and an in-range soundlevel.
# 648: SoundScriptCatalogConformanceTests (10). The manifest decides which scripts load, and that is
# not the same set as a glob: TF2 ships 20 game_sounds*.txt files, the manifest lists 16, and reading
# it rather than globbing excludes 3,910 entries the engine does not have.
# **Lowered 648 -> 615 on 2026-08-22, and the arithmetic is the justification.** RiffConformance (8),
# SoundScriptConformance (9), SoundScriptCatalogConformance (10) and SoundScriptProbe (1) moved to
# Audio.Tests along with the readers they cover (D53). 33 here plus 7 from core is exactly the 40 the
# audio floor gained, so nothing was deleted.
# 623: VtfBlockAgreementTests (1) and its sibling, which read 400 of the game's own textures out of
# tf2_textures_dir.vpk and assert the block path and the expanded path produce identical bytes.
# 635: ViewmodelArmsContentTests (5) — what a first-person arms or weapon model actually contains,
# read out of tf2_misc_dir.vpk rather than from a fixture. Two TestCases cover the demoman's weapons,
# and the suite is what killed the "the arms carry the second weapon" theory for him while confirming
# it for the soldier (finding 33).
# 637: ViewmodelSchemeConformanceTests (2) — model_hands for all nine classes, and the demoman's
# pinned exactly. It is what decides whether a first-person weapon is one model or two.
# 638: ViewmodelBoneMergeTests (1) — which of a weapon's bones the arms can supply. It is what
# showed the Original merges cleanly (1 bone, weapon_bone, provided), killing the bone-name theory
# for why it draws too large before any code was changed.
# 640: VtfLowResolutionConformanceTests (2). The thumbnail every VTF carries, which
# mat_showlowresimage draws — and the claim the reader's skip has always encoded without checking:
# it is always DXT1, whatever the texture's own format is.
# 642: the two soundscape probes, both [Explicit] — SoundscapeManifestProbe reports the shipped
# manifest and its load order, EnvSoundscapeProbe reports a map's env_soundscape entities. They are
# what turned B173 from a guess into a mechanism.
# 649: BspVisibilityConformanceTests (7). The visibility lump's run-length encoding, checked against
# Valve's own CompressVis by round-tripping through a transcription of it — plus the two malformed
# cases DecompressVis guards, a run that overruns the row and a zero repeat count.
# 666: SchemeFontConformanceTests (6) and GlyphAtlasTests (11), the portable half of the HUD (D84).
# The scheme reader takes VGUI's font declaration from the shipped
# platform/Resource/SourceScheme.res, because vguimatsurface is not in the SDK — the numbered
# candidates, their yres ranges, and the same font declared twice as a #base override produces.
# The atlas tests are the arithmetic of a HUD — packing, bearings, advances — measured against a
# fake rasteriser, so they need no font and run on the Linux measurement boxes.
#
# Read from the .trx, which said 666 while the console said 650:
# docs/memory/read-the-trx-total-not-the-console.md, hit again today.
# 677, measured: BonePipelineStructTests (10) and BonePipelineStructProbe (1, [Explicit]). The
# structures Valve's bone pipeline reads and this reader never has — bone controllers, IK chains and
# links, jiggle bones, local hierarchy, and the three fields of mstudiobone_t that gate the whole
# engine pipeline: flags, proctype, procindex. Derived from studio.h through CStruct rather than
# counted, and the probe exists because mstudiojigglebone_t is thirty-five consecutive floats.
#
# The jiggle count assertion caught its own author on the first run — 36 against a measured 35, the
# number you get from reading the declaration's comment groups instead of its members. A stride of
# 140 and a member count are now asserted against each other, which is why that disagreed rather
# than passing.
# 694: the bone fields are now READ, not just laid out. BoneFlagReaderTests (6) against hand-built
# bytes, BoneFlagContentTests (10) against models TF2 ships, BoneFlagContentProbe (1, [Explicit]).
#
# The two halves answer different questions and the second is the one that matters: the fixture
# tests can only prove the reader agrees with bytes written by the same hand on the same day. The
# content tests read Valve's files, and their strongest assertion is the NEGATIVE one — no bone in
# any shipped model sets a bit studio.h does not declare. A field read four bytes early still
# yields a number, so "every bit is one the engine names" is what separates a correct offset from a
# wrong one that looks sane.
#
# Verified by sabotage rather than by reading: BoneFlagsOffset - 4 reddens 9 of the 18, and the
# precise inverse edit restores them.
# 707: the IK chain and bone controller TABLES are read now (12), plus a capacity guard (1).
#
# heavy.mdl declares four chains — rhand, lhand, rfoot, lfoot, three links each. The hands were the
# surprise: TF2 uses IK to pin an off-hand grip to a weapon, so this is not only about feet planting.
#
# Verified by sabotage with the exact mistake the comments warn about — IkChainStride 16 -> 48, the
# int unused[8] tail every neighbouring struct has and this one does not. It reddens 2 of 17, and
# WHICH two is the useful part: the SDK stride assertion, and the multi-chain test. A single-chain
# fixture sits at offset 0 under either stride and cannot see it.
# 713: BspVertexNormalsTests (4) and two lump indices pinned against bspfile.h. Read but not drawn
# (D93): the plane normal is NOT a substitute, because vrad replaces the compiler's plane normals
# with true smoothed ones wherever a smoothing group applies (B194).
# **Lowered 713 -> 711 on 2026-08-26, and it is a correction rather than a loss.** The 713 was
# never measured: BspVertexNormalsTests added FOUR tests to 707, and the commit that raised it
# (bb8af0d) verified Viewer and Scene and not Content. A floor is the CURRENT count, so inventing
# one makes the gate permanently red - which the ratchet then reports as missing tests.
# 727: LocalLightConformanceTests (6), which writes down how the engine sums local lights before
# anything here passed one, and LocalLightSelectionTests (8) on the selection a shader would take.
# 740: StudioBoundsConformanceTests (4) pinning the header's bounds offsets against studio.h, and
# StudioBoundsTests (9) reading them off the real scout — including one [Explicit] probe.
# 744 -> 749 on 2026-08-28: StudioHeaderFlagsConformanceTests (4) pins `studiohdr_t.flags` — the one
# header word this reader stepped over for months while `StudioLayout` described it in prose — plus
# StudioModelFlagCensus (1, Explicit), which measured the denominator this whole change needed:
# 88 of 14,109 shipped models carry STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS.
# 749 -> 766 on 2026-08-28: StudioAnimationSectionConformanceTests (7) against
# mstudioanimdesc_t::pAnim, StudioAnimationContinuityTests (1) which is the only test that can fail
# when animation sections are ignored, and the diagnostics that found it (Explicit).
# Sections are why the demoman's sticky launcher tore: every frame was read out of section zero, so
# the run-length walk ran off the end and kept reading. vm_weapon_bone_1 landed 219 units from rest.
# 766 -> 774 on 2026-08-28: BspSweepTests (6) for the first real trace this project has had — a
# plane-by-plane clip that reports a DISTANCE, where IsClear and SeesSky sample and answer a bool.
# Plus two diagnostics. The chase camera cannot be clipped against the world without it.
# 774 -> 778 on 2026-08-29: BspBrushTraceTests (4), which measure the sweep against a REAL map's
# brushes rather than a hand-built tree. The hand-built cases cannot reach that code at all — they
# carry no collision lumps, so they exercise the node-plane fallback. Two paths, one method.
# 778 -> 785 on 2026-08-29: BspTraceMaskConformanceTests (7). The sweep tested CONTENTS_SOLID only,
# so a camera slid through glass, grates and the brushes of doors and lifts. MASK_SOLID is what
# CalcChaseCamView traces with.
# 785 -> 795 on 2026-08-29: DisplacementSweepConformanceTests, the first half of displacement
# collision. Every fraction in it was worked out by hand from `dispcoll_common.cpp` BEFORE the code
# existed — 0.46984375 for a box dropped on a flat triangle, 0.38977903 on a slope — so the numbers
# are a prediction the implementation cannot have shaped.
#
# Two of the ten exist because a sabotage SURVIVED. Disabling the nine edge planes outright left all
# eight original cases green: the "misses beside the triangle" case sat outside the triangle's
# AXIS-ALIGNED bounds, so the axis planes rejected it and the edge planes never spoke. A case inside
# the bounds and beyond the hypotenuse is what measures them, with its mirror just inside as control.
# 795 -> 800: LeafDisplacementReachTests (1) and DisplacementCollisionTests (4). The first is a
# MEASUREMENT that reversed the plan — leaffaces reach none of cp_badlands' 1191 displacement faces
# while reaching all 12,654 flat ones, so terrain is narrowed by bounds, not by leaf. The second is
# a differential against a brute force over every triangle on the map.
# 800 -> 801: WaterMaterialProbe, [Explicit], which prints the shipped `water/water_well_beneath`
# VMT. It settled B62 in one read: shader `Water`, no `$basetexture` by design, refracting against
# `_rt_WaterRefraction`. Reading what the GAME ships is the fifth source CLAUDE.md lists and the one
# that gets forgotten, because it is data rather than code.
# 801 -> 810: WaterShaderConformanceTests (9), `water.cpp:535` transcribed — which of the two passes
# a water material draws, and Valve's `Draw()` fallback when neither applies. The last case walks all
# 64 flag combinations to assert the engine never answers "nothing drawable", which is precisely what
# the magenta chequer was claiming for a material TF2 has never failed to draw (B62).
# 810 -> 817: StudioSkinsConformanceTests (7). `g_skinref[skin][skinref]` written down from Valve's
# own comment at `utils/motionmapper/motionmapper.h:134` before the lookup existed. Synthetic and
# hand-built, so it runs on CI and on the measurement boxes; the table is deliberately NOT the
# identity, because an identity table gives the same answer for every family and cannot tell a
# correct lookup from one that ignores the skin (B229).
# 817 -> 826: EntityTransformConformanceTests (9), CalcAbsolutePosition written down whole --
# all three branches, ConcatTransforms, MatrixAngles including its gimbal-lock branch, and the
# angle shortcut that COPIES the parent stored angles. That last one caught the first
# implementation round-tripping 20 into 19.999998.
# 826 -> 827: SpawnRoomEntityProbe (1, Explicit). It reads cp_fulgur's entity lump and prints every
# gate, door and cabinet with its `parentname` resolved -- which is what established that all eight
# resupply lockers are UNPARENTED and that a parented prop's `origin` key is its parent's world
# position. Every position claim in the B231 hunt before it was checked against another reading of
# our own decode, which cannot falsify a wrong premise; the map can.
# 827 -> 835: ClampCycleConformanceTests (7) and SetupGateStaticPropProbe (1, Explicit).
# C_BaseAnimating::ClampCycle wraps a cycle only when the sequence LOOPS and clamps to 0.999
# otherwise; this project wrapped unconditionally in two places, both spelled
# `advanced - Math.Floor(advanced)`, so every one-shot sequence restarted for ever -- the owner's
# spawn health cabinet opened and shut without stopping. FrameFor already held the last frame
# and took the loop flag to do it, and could never run that branch, because the caller had
# already erased the evidence that the cycle went past one. Three of the seven are controls: an
# in-range cycle must be untouched either way, a looping cycle must still wrap, and the same
# input on a looping sequence must return to the start rather than hold.
# 835 -> 836: GateMaterialProbe (1, Explicit). It decodes the setup gate's materials and reports
# their mean colour, which is how a claim about how something LOOKS gets a number: the frame is
# R49 G33 B16 -- orange in a 3:2:1 ratio -- and the mesh R82 G77 B73, neutral grey. Both correct,
# which is what moved the gate hunt off materials and onto something only a screenshot can settle.
# 836 -> 843: `model_player_per_class` has TWO forms and this read one (B233). Besides a map of
# class to path the block may carry a single `basename` with `%s` placeholders, expanded per class
# by InitPerClassStringArray (tf_item_schema.cpp:489) -- and the reader stored "basename" as though
# it were a class nobody plays, so every item using that form resolved to no model. It appears 5,518
# times in the shipped schema. Six unit cases plus one conformance case on the shipped file.
# Three are controls: an explicitly named class must beat the pattern, a class NOT named must take
# it, and an item with no per-class block at all must still answer its base model when the class is
# unknown -- without that last one the slot-zero rule would swallow every weapon whose owner left.
# 843 -> 851: `attached_models`, the extra models an item hangs on itself (B251). Eight, and they
# are a conformance suite rather than a description of the parser: the display-flag constants by
# value, the default that is MaskAll rather than zero, the festive gate in both directions, the
# per-team split in both directions, prefab inheritance that ACCUMULATES rather than shadowing, and
# an item declaring none. The default matters most — nearly every shipped entry omits the key, so
# defaulting to zero would hide all 29 blocks silently.
# 851 -> 855: definition attributes, IterateAttributes' branch 4 (B234). Both shipped forms - the
# named "attributes" block and the flat "static_attrs" pair - bridged name to index by the
# top-level section, stored_as_integer deciding whether the 32-bit union holds the integer or the
# float's bits, and prefab inheritance overriding per name rather than duplicating.
# 855 -> 858: BspLeafTree.TouchesAny, the box walk the PVS half of B254 needs - one side, the other
# side, and the straddling case that a point walk gets wrong, plus a no-leaf-wanted control.
# 865 -> 876: DoAnimationEvents' traversal (B275). Eleven for the walk, one of which records an
# engine behaviour the first version of it asserted the OPPOSITE of - on a loop, head events below
# the backtrack do not fire.
# 876 -> 882: FrameAt, the inter-frame fraction CalcPoseSingle keeps and this project dropped
# (B279). Six cases, including the control that a loop still lands on the frame FrameFor always
# gave it - only the fraction is new.
# 909 -> 913: dleaf_t's AREA, which is how the engine tells the 3D skybox room from the level
# (B152). Four: the plain read, a leaf whose FLAGS are set — the case that separates a nine-bit read
# from a sixteen-bit one, since with flags zero the two agree — the largest area a nine-bit field
# holds, and a leaf past the end answering -1 rather than area zero.
# 913 -> 918: dleaf_t's FLAGS, the other seven bits of the field the four above read, and the sky
# visibility they encode (B152). Two on the flags themselves - one leaf carrying both an area and
# flags, and a large area that must not leak upward - and three on LEAF_FLAGS_SKY vs SKY2D vs
# neither, where the last is the control that stops an unconditional answer passing the first two.
run Tf2DemoSalvage.Content.Tests  content   918
# 96: SoundCharProbe, [Explicit], which measured the prefix population before SoundName was written.
# 97: SoundResolutionProbe, [Explicit]. It harvests the precached names real demos carry so the fast
# synthetic suite can be built from them, and it is a probe rather than a test because it needs a TF2
# install — on CI and the measurement boxes it would Assert.Ignore and check nothing.
# 98: the probe's second test, which asks whether a sound that will not open still exists under
# another container. That is what turned "63 sounds deleted" into "60 re-encoded, 3 gone".
# Raised 98 -> 101 on 2026-08-23: spectator target cycling walked across z1800's real player list
# (B145). The conformance suite for it is hand-built from the measured shape of that demo, so it
# cannot say whether a real timeline cycles sensibly; these three can, and one of them proves the
# SourceTV camera is never landed on across 222 cycles.
# 103: CorpusWeaponOwnershipTests. One asks whether m_hOwnerEntity reaches us at all for weapons —
# the "did the data arrive" check that was skipped while four fixes were aimed at the rule using it.
# The other pins that no first-person arms model is tracked as a world prop, which is what caught a
# carried weapon's m_nModelIndex being its VIEW model (B160).
# 104: CorpusTimelineSoundTests. svc_Sounds now reaches the timeline with names and ticks, which is
# the first of B168's three pieces and the only one testable without an audio device.
# 106: SoundPopulationProbe (2 cases, [Explicit]). It reports what a demo actually plays, by name,
# soundlevel, pitch, stop and origin — and answered three questions in one run that were
# indistinguishable from the speakers.
# 109: SoundscapeWireProbe (3 cases, [Explicit]). Which demo kinds carry m_audio at all — the
# measurement that showed an STV recording carries the SourceTV CAMERA's soundscape rather than any
# player's, which is why the map's own entities are needed rather than the wire (B173).
# 112: CorpusDecodedDemoTests (3), the end-to-end half of the DecodedDemo move. The synthetic
# fixtures in Scene.Tests carry the load — a corpus test skips without the corpus and so kills no
# mutants — but only a real demo can catch a roster that decodes to nothing.
# 117: CorpusServerConVarTests, the assertion that a real demo's replicated ConVars reach the
# timeline. All four run on committed gcor demos, so they hold under TF2DEMOSALVAGE_GCOR_ONLY and
# in CI rather than skipping there and testing nothing.
# 130: ViewmodelAnimationParityCorpusTests, which asserts m_nAnimationParity reaches the scene from
# a real demo AND changes there. The unit suite cannot fail if the field never arrives — a wrong
# property name reads as a constant zero and every other test still passes.
# 130 -> 133 on 2026-08-28: LifeStateCorpusDiagnostic (3, Explicit), which measured the liveness of
# every player across two demos and chose the UI suite's opening tick. Its report is what showed that
# tick 2500 sat inside a death — the frame that constant was chosen for is a freezecam, not a player.
# 135 -> 136: CorpusObserverModeTests, the level that catches a decode which never populates the
# field. It also measured the number that killed the first theory: across three POV demos, samples
# that are alive AND observing come to ZERO, so the observer-mode rule alone explains none of B225.
# 136 -> 137: CorpusRenderModeDiagnostic. **A count that went UP while the suite got weaker on
# purpose** — the two render/observer corpus TESTS became `[Explicit]` diagnostics that report and
# assert nothing about what a demo contains (D38), and their real claims moved to synthetic tests in
# Core.Tests where Stryker can reach them.
# 137 -> 141: four Explicit diagnostics from the B231 hunt -- brush entity state, parented props,
# bone-merge detection and the missing-model gap. The parented one is what found that the SPAWN
# doors send a local origin with no parent we resolve, which is a separate defect.
# 141 -> 142: ParentLifetimeDiagnostic (1, Explicit), which walks one entity slot across the whole
# recording and prints every transition. It is what found the re-entry bug: the parent is present
# at tick 9781 and gone by 14180, which is a value being overwritten rather than never sent.
# 142 -> 143: LockerParentProbe (1, Explicit). It walks one slot's every update with the instance
# baselines LOADED, which is what named the cause: a creating update carries only what differs from
# the class baseline, so cp_fulgur's BLU spawn door was created holding the baseline's model index
# and origin -- a resupply locker at prop_locker_blu_5's world position. Its first version omitted
# the baselines and reported "the baseline has no parent", which was a fact about an empty decoder.
# 143 -> 144: MedigunPlacementProbe (1, Explicit). It carries two dead theories with it -- the
# medigun tracks that looked misplaced were the ten CTFDroppedWeapon entities on the floor, and
# the bone-merge rule was firing correctly all along.
# 144 -> 145: PropAnimationProbe (1, Explicit), which prints every animation field a prop sends
# update by update. It is what found that the cabinets are CLIENT-side animated and send no cycle
# -- a fix had already been built on the parity, from inferring that field rather than measuring
# it. Per-update rather than final state, because every question here is about a transition.
# 145 -> 146: DisguiseDrawProbe (1, Explicit), which walks the SAME call MomentScene makes --
# PlayerProps.Add -- and reports the model and skin a disguised player actually gets. The
# timeline probe proved the decode and proved nothing about what reaches a screen, which is
# this project's most reliable bug (output-level-assertion-or-it-is-not-done).
#
# **This number is ARITHMETIC, not measured**, against the repo's own rule -- the last full
# corpus run reported 145 and exactly one Explicit test was added since. Recorded as such so
# nobody reads it as a measurement; the next full gate will confirm or correct it.
# 146 -> 148: the era split in the model scale's wire name (B271), which is a question only real
# bytes can answer - the SDK is one build's snapshot and cannot say what an older client sent. The
# pair pins the exclusion SendTableConformanceTests now carries, so the exclusion goes red the day
# it stops being true rather than being justified by prose forever.
# 148 -> 151: the applied-time correction on real bytes (B273). Two measure the lag itself; the
# third is the only one that reddens when the STAMPING is severed, which a sabotage found - the
# other two assert on a histogram measured beside the stamping rather than through it.
# 151 -> 152: the ANIMATION clock reaching real keyframes (B274). Separate from the simulation
# one because the two clocks reach different entities - players send no animation time at all - so
# a single "some clock corrected something" assertion would pass on the simulation half alone.
run Tf2DemoSalvage.Corpus.Tests   corpus     156
# Lowered from 523 on 2026-08-21, and the arithmetic is the justification: FIVE stale gap markers
# were deleted (Cubemaps_AreNotRead, EnvironmentMaps_AreNotImplemented, AttachmentPoints_AreNot-
# Implemented, Attachments_AreNotRead, ViewModels_AreNotDrawn — every one claiming a feature that
# demonstrably works) and FOUR tests added: three in ConformanceGapAuditTests and one precise
# replacement marker, NormalMapAlphaEnvMapMask_IsNotImplemented. Net -1.
#
# A floor that drops is a finding until it is explained. This one is explained by the count.
#
# **Lowered again from 569 on 2026-08-21, net -3, and the arithmetic is the justification.** The
# conformance sweep removed three tests from OverlayPassConformanceTests and added one to
# OverlayOcclusionRenderTests:
#
#   -1  CullMode_TheEnginesDefault_CullsCounterclockwiseWinding — moved, not deleted. It now lives
#       in DecalRenderStateConformanceTests where it is compared against DecalState.Cull instead of
#       merely quoted from imaterialsystem.h. Asserting it in both places would be two sources of
#       truth for one claim.
#   -1  Fade_EveryOverlayCarriesADistanceRange_InItsOwnLump — moved to Content.Tests, because the
#       gap it measures is against BspLumpIndex and that type is internal to Tf2DemoSalvage.Content.
#       Counted in the content floor above, not lost.
#   -1  Fade_TheEngine_ExposesItAsConVars — DELETED outright. It was a bare Assert.Pass carrying a
#       note about convar names, which is a comment with a green tick attached. The note survives as
#       a comment in OverlayLumpConformanceTests, which is what it always was.
#   +1  Render_AnOccluderNearerThanTheBias_LosesToTheMarkingAsValvesConstantIntends — the other side
#       of the depth-bias threshold, so the pair measures the bias rather than the fixture.
#
# Two of the three are relocations and show up in the content floor; only the Assert.Pass is gone.
# Raised to 568: ClipFaceToOverlay_AFaceAtAnAngleToTheBasis_StillProducesAFragment and
# BrushModels_TheGeometryThisProjectBuilds_CarriesTheAtlasCoordinatesTheWorldUses, the measured
# halves of two files that until then only quoted vbsp and vrad.
# **Lowered from 572 to 570, net -2, and the arithmetic is the justification.** FogConformanceTests
# went from four tests to two:
#
#   -2  Fog_TheBlendFactor_IsSquaredBeforeTheLerp and
#       Fog_TheRangeFactor_IsClampedByMaxDensityBeforeSaturating asserted Valve's shader source and
#       then checked arithmetic transcribed into helper functions in the same file —
#       `Squared(0.5f).ShouldBe(0.25f)` tests that squaring squares. Their citations are preserved
#       in Fog_TheEquations_AreRecordedForAnImplementationThatDoesNotExistYet.
#   -1  Fog_TheFirstParameter_IsStartOverRangeDespiteItsMacroName, same, folded into the same test.
#   +1  Fog_NothingInThisRendererReadsTheDecodedFog_WhichIsB139 — the gap, measured by sweeping the
#       Viewer3D assembly for any member naming SceneFog, with a control that the sweep finds a
#       scene type the renderer really uses.
#
# The wire-name half moved to Core.Tests as FogControllerConformanceTests (+2 there), which is why
# the core floor went up in the same commit.
# 579: PngWriterTests (9). The render layer's own PNG encoder, written to get System.Drawing — which
# is Windows-only by design in modern .NET — out of the way so Render could be plain net10.0 (D61).
#
# Worth knowing why the suite has both a round-trip and a byte-level check: the round-trip decodes
# our file with System.Drawing, and swapping ZLibStream for DeflateStream (raw deflate, which PNG
# forbids) was sabotaged deliberately and ALL EIGHT round-trip tests still passed, because that
# decoder is more lenient than the spec. The zlib-header assertion is what actually catches it.
#
# Raised 580 -> 586 on 2026-08-22: FreeFlightTests gained one (Shift on either side of the keyboard,
# which only became possible to get wrong once Shift went through the console) and RealTf2ConfigTests
# gained two that execute the owner's own null-cancelling config rather than counting its binds.
# Net +6 with the rest from the same change.
# 614: PolyOffset_ForALightmappedGenericOverlay_IsNeverRequested, which reads the SDK to establish
# that the only route to a polygon offset is the shader's own request and LightmappedGeneric never
# makes it — the mechanism B70 was missing for three attempts (2026-08-23).
# 615: OpaquePassBlendStateRenderTests, which draws a decal pass and then an opaque prop and asserts
# the prop ignores its texture's alpha — the leak that made every static prop translucent (B154).
# 627: WornModelSkinningTests (2). A model flagged as worn must skin however few bones it has, and
# the stock rocket launcher is the control that says the change stayed narrow (B167).
# 628: LowResImageRenderTests (1). The output-level assertion for mat_showlowresimage — the flag
# reaching the constant buffer and the texture being bound are separately true and separately
# useless, and only a pixel can say the substitution happened.
# 634: SpectatorTargetConformanceTests (6). CBasePlayer::IsValidObserverTarget has four clauses and
# only the team one was implemented, so cycling landed on corpses (B171). Includes the control that
# a living player is still chosen — a filter refusing everyone would satisfy the exclusions alone.
# **Lowered 634 -> 627 on 2026-08-24, and the arithmetic is the justification.** FileRetentionTests
# (7) moved to Logging.Tests with FileRetention itself (D83), which is where it belongs now that the
# type lives beside the log writer rather than in Scene. Nothing was deleted: viewer -7 is exactly
# the 7 the new logging floor gained, on top of its own 9.
# 641, measured: 627 -> 634 by the Valve cvar vocabulary (cl_showfps parsing and the fps_max floor
# departure), then -> 641 by HudRendererTests, which check the screen-to-clip arithmetic without a
# device. The floor had also drifted below CI's 634 for the same suite, which is the failure
# docs/memory/a-floor-must-track-the-number-it-guards.md describes: two copies of one number.
# 645: four that exist because a viewer launch found what 641 could not.
#
#   Instances_ASkinnedModel_…                  nothing had EVER driven Instances() with a skinned
#                                              model. Every other case here loads a BAKED fake, so
#                                              the whole pose path was reachable only by launching.
#                                              It reproduces the ArgumentException that crashed
#                                              playback on the first frame.
#   Instances_AWeaponMergedOntoAPlayer_…       the merge through the wiring rather than through
#                                              AnimatingEntity directly.
#   Instances_AWornItemSharingNoBoneName_…     the only shape that can see an unresolved entity
#                                              placement. Its first two versions PASSED with the
#                                              defect reverted — a merged bone takes the wearer's
#                                              matrix and an unmatched CHILD rides its merged
#                                              parent, so only an unmatched ROOT exposes it.
#   Shortcuts_EveryMenuItem_…                  no key claimed twice. Third instance in one file
#                                              after B165's F11: F12 was dead, and F8 was claimed
#                                              by both the frame rate and reflections.
# Lowered 645 -> 625: twenty tests moved to Scene.Tests with their subjects, which is the +20 there.
# Nothing was deleted, and the two numbers are checked against each other rather than separately.
# 625 -> 621 on 2026-08-25: CameraPlacementTests (4) moved to Presentation.Tests with
# FreeCameraController (B188, B184). Nothing was deleted — presentation went 135 -> 139, which is
# exactly the four, and that arithmetic is the check that a move did not lose anything.
# 629 -> 619 on 2026-08-26 (B207): MapSurfacesTests (10) deleted with MapSurfaces itself. It built
# flat shaded triangles for the ORTHOGRAPHIC top-down view that D49 removed — sorted by height,
# shaded by height, "no depth buffer and none is wanted for a flat view" — and had no production
# caller. Nothing replaced the tests because nothing replaced the feature: under D95 the viewer is
# always 3D. MapScene/MapSceneReader went in the same commit, with no test count to move at all.
# 623 -> 619 on 2026-08-26 (B208): CaptureNameTests (4) MOVED to Presentation.Tests with
# MainForm.CaptureName, which became Captures.Name. Nothing was deleted — presentation went 257 ->
# 283, which is 17 WindowGeometry + 5 Captures + those 4, and that arithmetic is the check.
# 623 -> 602 on 2026-08-26 (D98): the orthographic camera is gone, so TopDownCameraTests (11),
# CameraMatrixTests (6) and ShowPositionsTests (4) went with TopDownCamera and the flat markers.
# 11 + 6 + 4 = 21, which is exactly the drop — nothing else moved.
#
# CameraMatrixTests is the one worth naming: every case asserted TopDownCamera.ToMatrix, the
# orthographic projection itself. There is no replacement because there is no projection; the free
# camera's matrix is FreeCamera.ToMatrix and has its own tests.
# viewer 616 -> 613 on 2026-08-26: `HeightCutTests` (3) went with the height cut itself (B213). The
# feature never worked — it clipped on DEPTH, which is world height only under the orthographic
# projection D98 deleted — so the tests were passing against arithmetic nothing rendered. Nothing
# was lost: the type, its two test files and its three hardcoded keys all went together.
#
# Presentation held at 343 across the same change and that is a COINCIDENCE worth naming, not a
# sign nothing moved: its own `HeightCutTests` (6) went while `TimeScale`'s tests arrived, and the
# floor was raised to 343 in the same session. A floor that happens to hold is not evidence.
# 613 -> 616 on 2026-08-26: DefaultBindingConformanceTests, three, written before the B214 fix.
# The denominator is GENERATED from tf/cfg/config_default.cfg, so it cannot go stale: one test says
# no default may sit on a key TF2 binds to a different command (it would be taken by a pasted
# config), one says that where we speak TF2's command we start on TF2's key, and one says every
# default resolves to a real key. The third caught a live defect on its first run -- Enum.TryParse
# accepts a numeric string, so "1" resolved to Keys.LButton.
# **616 -> 101 on 2026-08-26, and this is a SPLIT rather than a loss** (B184). 99 of the 113 files
# here referenced nothing Windows-only and were pinned to `net10.0-windows` by the project file
# alone; they now live in `Tf2DemoSalvage.Rendering.Tests` on plain net10.0. The arithmetic is the
# check that nothing went missing: **515 + 101 = 616**, exactly what this project ran before.
#
# **84% of the suite came off the Windows TFM**, so it can run on Linux and on the mutation box —
# which was the MVP refactor's actual goal, not tidiness: *"it wass to be able to test more on
# linux, and have compile time safety"*.
#
# What stays genuinely needs a Form, a device, or this assembly: MainForm's construction and
# disposal, the menu, full screen, shortcut collisions, FreeFlight and KeyNames (WinForms `Keys`),
# PngWriter (`System.Drawing`), the binding conformance suite — plus `GlobalUsings.cs` and
# `AssemblyTestPolicy.cs`, which configure this assembly, and `FieldSeedingTests`, which scans
# `managed/Tf2DemoSalvage.Viewer3D`'s source by path and so has that project as its subject.
#
# **Four files were held back by a bad filter before being found**, which is why the survivors were
# re-checked one at a time by what they USE rather than what they mention. Five contained the path
# `C:\Program Files (x86)\Steam` and `Program` is a Viewer3D type; `SkinOverrideConformanceTests`
# named `MainForm` only in a comment; and `PlaylistFilterTests` carried a dead
# `using Tf2DemoSalvage.Viewer3D` when `PlaylistFilter` lives in `Scene`.
# 540: ModelConstantLayoutTests, which counts the float4s the shader declares and asserts they come
# to ModelConstants. Nothing at runtime can check that pair, and a disagreement is the strobing
# garbage the material buffer produced when a replace-all grew two of three arrays.
# 553: OpaqueDrawOrderConformanceTests, which writes down Valve's opaque ordering — brush models
# first, then biggest size bucket to smallest — before anything here sorts.
# 656 -> 685 on 2026-08-28: TwoPassConformanceTests (28) and TwoPassWiringTests (1). The conformance
# suite is nine SDK citations plus nineteen cases over RenderGroups; the wiring test is the one that
# can fail when the loader drops the flag, and it does — verified by manipulation, sniper against
# scout as its control.
# 685 -> 687 on 2026-08-29: MapLevelSweepTests (B227). The only level that can catch a world sweep
# which asks the BSP tree and nothing else — which is what shipped, so the chase camera passed
# through every hillside in TF2 while the primitive's and the whole-map set's own suites stayed
# green. Its condition is SEARCHED for, not assumed: the space just above a displacement vertex is
# usually inside the brush the terrain was carved from, so the brush trace correctly reports
# startsolid there and the column proves nothing. It finds one where brushes are clear and terrain
# is not — (2048, 0, 258.6), terrain 0.689 against brushes 1.0.
# 687 -> 702: FxBlendConformanceTests (15), `C_BaseEntity::ComputeFxBlend` written down before it
# was implemented. Every case is hand-read from c_baseentity.cpp:3343 — including the four that
# MUTATE the entity's alpha, which Valve marks "JAY: HACK for now -- not time based".
# 702 -> 708: B229's three instruments. PropLoadLoggingTests (2) asserts the static-prop loader
# reports through a real logger — it had an `ILogger? props = null` its ONLY caller never passed, so
# every warning it produced went to a NullLogger and four hypotheses were spent reading a log that
# could not contain the answer. PropMaterialResolutionTests (2) is the output-level assertion: no
# placed corner on cp_fulgur names material -1, with cp_process_final as the control.
# ChequeredPropMaterialProbe (2, Explicit) reports where a chequered prop's material actually lives.
# 708 -> 710: BrushEntityAngleProbe and SetupGateEntityProbe, both Explicit. The second dumps
# EVERY keyvalue on a map gate rather than the four already suspected, which is how the parented
# prop_dynamic riders were found at all.
# 710 -> 712: two WornModels cases for a track whose model is not resolved yet. Making a weapon's
# model resolvable from its ITEM let a Studio track reach Props with an EMPTY path, and both load
# set builders select on Kind == Studio -- so the empty string went to PakFile.ReadFile and killed
# the viewer at load. The second case is the control: a track that HAS its path is still worn, so
# the guard tests the path rather than the item, which every cosmetic also carries.
# 712 -> 713: NetworkedPropertyCoverageTests. The denominator for 'what does the demo tell us
# that we ignore', extracted from the SDK's own client RecvTables so it cannot flatter us --
# an audit starting from OUR accessors can only find fields we already decode, and the two
# most expensive gaps of 2026-08-30 were both invisible to that. Writes docs/WIRE-COVERAGE.md.
# Three controls, because an extraction that matched nothing would report perfect coverage of
# an empty set: a floor on the count, the two motivating fields asserted present, and
# SchemaGap's positive control on the other half of the diff. The disguise control earned its
# keep immediately -- the first sweep read only src/game/client and TF's player state is
# declared in src/game/shared/tf, so it reported 0 of 66 for a table it could not see.
# 713 -> 716: the baked animation path measured its cycle from demo time ZERO (B237). Valve's
# advance is over an interval -- flInterval = ( curtime - m_flAnimTime ), c_baseanimating.cpp:5480 --
# and the timeline has stamped AnimationStartSeconds since the cabinets were first looked at, but
# only Simulate's SKINNED path ever read it. Every BAKED prop went through ModelFrames.Select with
# absolute seconds, so a cabinet whose `open` begins 177 seconds in computed a cycle of 183 and
# clamped to the final frame before drawing once: a door already fully open, that never moves.
# Two of the three are controls -- an animation with no stamp must behave exactly as before, and a
# stamp AHEAD of the moment being drawn must not run the animation backwards, which for a looping
# sequence wraps to near the end and snaps a door shut for a frame.
# 716 -> 718: the strobes and flickers test a value Valve truncates to an int first (B246). Valve
# declares `int blend` and assigns `20 * sin(...)` to it, so a wave anywhere in (-1, 0) becomes 0 --
# not less than zero -- and the entity draws at FULL alpha where we drew it invisible, for about
# 1.6% of every cycle on all five effects. One of the two is a control: below -1 the int really is
# negative and the strobe really is off, so an implementation that simply dropped the sign test
# would pass the first and fail the second. Found by reading the engine function end to end during
# the parity audit, not by any measurement of ours.
# 722 -> 726: the cull hands back the 3D skybox room as its own set (B152). Four: no sky area draws
# everything as before, a sky area leaves the room out of the main pass, the sky set holds it alone,
# and the main runs SURVIVE the sky set being built - which they would not if both passes shared
# VisibleWorld's one reused list, a fault that can only exist once both passes do.
run Tf2DemoSalvage.Rendering.Tests rendering 726
# 101 -> 103 on 2026-08-29: LaunchOptionWiringTests (B223, D118). Two tests, and they cost about
# seventeen seconds EACH, because each builds a real MainForm and loads a corpus demo — which reads
# cp_badlands.bsp when Team Fortress 2 is installed. That is the most expensive pair in this file
# per test, and it is deliberate: they are the only instrument in the repository that can observe a
# launch option reaching a running window. `LaunchOptionsTests` proves the command line is READ, and
# `DemoSystemsTests` cannot reach the autoplay path at all because `DemoTimeline`'s constructor is
# private. Autoplay's ordering had broken three times with every suite green.
# 103 -> 106: TransportBarTests. The control had no tests at all, which is how `SetDemoLength` came
# to end with `Playing = false` — a decision about playback made inside the View, invisible from
# `IPlaybackView`, and silent because that setter does not raise. D55's tell, and B223's cause.
# 106 -> 108: the menu printed "F12" for the screenshot long after B214 moved the key to F5 for
# Valve parity (B239). A ShortcutKeyDisplayString is a LABEL rather than a registration, so nothing
# breaks when it lies and no test could see it -- the only instrument was the owner reading the menu
# and pressing a dead key. The first case is a tripwire over EVERY item, so the next hand-typed
# label fails here; the second is its control, because "F9" would satisfy the tripwire (F9 is bound,
# to the surface colours) while still naming the wrong key for the screenshot.
run Tf2DemoSalvage.Viewer3D.Tests viewer    108

echo
echo "The UI suite is NOT run here: it takes over the desktop and belongs inside run-exclusive.ps1."
echo "  pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests"
