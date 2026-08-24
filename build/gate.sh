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
# **"Test Run Aborted" in the viewer suite is probably the desktop, not the code.** Seen once on
# 2026-08-20: the run died at 192 of 512 and the floor caught it. Viewer3D.Tests creates real D3D
# devices, and at the time another application was in exclusive full screen — the owner's video
# player — which is a known way for device creation to fail. Unproven: it did not reproduce in four
# clean runs afterwards, and nothing was captured from the crash itself.
#
# Worth knowing before chasing it as a defect, and worth noting that this suite is NOT run under
# run-exclusive.ps1 the way the UI suite is, because it takes no desktop of its own — it just wants
# a GPU that nobody else has taken exclusively.
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
run Tf2DemoSalvage.Core.Tests     core     1497

# Raised to 74: UndeclaredHeaderReportingTests, six cases covering each clause of the CLI's
# "did the header state a length" check plus the finalised-header control.
run Tf2DemoSalvage.Cli.Tests      cli        74
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
run Tf2DemoSalvage.Audio.Tests    audio     116

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
run Tf2DemoSalvage.Presentation.Tests presentation 108
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
run Tf2DemoSalvage.Content.Tests  content   640
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
run Tf2DemoSalvage.Corpus.Tests   corpus     106
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
run Tf2DemoSalvage.Viewer3D.Tests viewer    628

echo
echo "The UI suite is NOT run here: it takes over the desktop and belongs inside run-exclusive.ps1."
echo "  pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests"
