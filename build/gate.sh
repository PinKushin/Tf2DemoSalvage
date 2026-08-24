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

# 10: the bone pipeline's denominator (B182) and the tests for the instrument that produces it.
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
run Tf2DemoSalvage.Animation.Tests animation 10
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
run Tf2DemoSalvage.Audio.Tests    audio     142

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
run Tf2DemoSalvage.Presentation.Tests presentation 135
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
run Tf2DemoSalvage.Content.Tests  content   666
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
run Tf2DemoSalvage.Corpus.Tests   corpus     109
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
run Tf2DemoSalvage.Viewer3D.Tests viewer    641

echo
echo "The UI suite is NOT run here: it takes over the desktop and belongs inside run-exclusive.ps1."
echo "  pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests"
