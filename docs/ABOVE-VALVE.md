# What this viewer does above Valve

**Every feature the engine does not have, each with a test that fails if it is lost.** The parity rule covers what the engine
does. It says nothing about what the engine lacks, and that gap lost instant rewind for a month (D246). A parity fix may never
remove anything here. If one would, stop and tell the owner.

`AboveValveConformanceTests` reads the table below and fails if a named test no longer exists. Add a row, and its test, in the same
commit as the feature. The list came from `docs/DECISIONS.md` and the transport code on 2026-11-04 and may not be complete.

| Feature | Decision | Test |
|---|---|---|
| Instant rewind: a step back never reads the demo from its start | D217, D246 | `Rewind_AShortStepBack_ReadsNoMoreThanOneCheckpointSpan` |
| The session itself turns rewind on | D246 | `RenderFrame_AStepBackAfterTheOpening_ShowsItsTickWithNoWalk` |
| Rewind through the real transport bar | D246 | `Transport_JumpBackToTheStart_ShowsItWithNoWalk` |
| Maps fetched from a mirror | none found | `MapDownloader_TheDefaultMirror_IsHttps` |
| Export a demo to text and compile it back | none found | `ExportThenCompile_OpenDemo_RebuildsItsBytes` |
| A demo decompiled to a text trace | ROADMAP Phase 1 | `Trace_WritesTheTraceFormatToTheGivenFile` |
| A chosen HUD is kept | D91 | `SaveThenLoad_KeepsTheChosenHud` |
| A rewind during a forward seek | D246 | `Rewind_DuringAForwardWalk_ShowsItsTickWithNoFurtherFrames` |
| Reverse play | D246 | `ANegativeTimeScale_PlaysBackwards` |
| Scrubbing | D217 | `Scrub_SeeksTheClockAndReportsTheMomentWithoutPlaying` |
| A seek back samples what a fresh timeline samples | D217 | `PropsAt_AfterASeekBackwards_MatchesAFreshTimelineFromThereOn` |
| A first-person view retimed by the server's tick | D238 | `Declarations_DemoRetimeView_IsOursOnByDefaultAndTheWatchers` |
| A player's first-stated team, before the demo states it | D217 | `Build_PlayerEntersBeforeTheirTeamIsStated_ReadsTheFirstStatedTeamAtEntry` |
| The HUD rebuilds after going back | D246 | `Advance_ASeekBackward_ResetsAndReplaysTheWindow` |
| Decals rebuild after going back | D246 | `StepOver_AStepBackward_ClearsTheModelsDecals` |
| Particles a seek meets | D221 | `AddHealBeams_ABeamMetBySeek_IsSteppedFromTwoSecondsBackAndKeepsThatUntilASeekBackward` |
| A demo opens with no map | D224 | `LoadDemo_WithNoMapAnywhere_OpensTheDemoAndSaysTheMapIsMissing` |
| Skeletons posed on the cores ahead of the draw, on by default (Valve ships `cl_threaded_bone_setup 0`) | D248 | `Parse_ThreadedBoneSetup_IsReadAsTheEnginesGetBool` |
| The threaded pre-pass draws the same bones as the serial pose | D248 | `Instances_RootsPrePosedAheadOfTheDraw_DrawTheSameBonesAsSerial` |
| A corpse made during a demo skip keeps its ragdoll (TF2 makes none) | D250 | `Instances_ACorpseBornInsideAJumpsSkip_IsSimulatedFromItsDeathAtTheLanding` |
| The viewer's own settings file keeps its menus' cheat ConVars between runs (TF2 keeps none, and refuses them from a config with `sv_cheats` 0) | D253 | `Load_SpecularFullbrightAndPixelVisibility_ReadsEachOne` |
