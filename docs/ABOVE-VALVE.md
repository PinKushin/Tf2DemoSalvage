# What this viewer does above Valve

**Every feature the engine does not have, each with a test that fails if it is lost.** The parity rule covers what the engine
does. It says nothing about what the engine lacks, and that gap lost instant rewind for a month (D246). A parity fix may never
remove anything here. If one would, stop and tell the owner.

`AboveValveConformanceTests` reads the table below and fails if a named test no longer exists. Add a row, and its test, in the same
commit as the feature. The list came from `docs/DECISIONS.md` and the transport code on 2026-11-04 and may not be complete.

| Feature | Decision | Test |
|---|---|---|
| Instant rewind: a step back never reads the demo from its start | D217, D246 | `Rewind_AShortStepBack_ReadsNoMoreThanOneCheckpointSpan` |
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
