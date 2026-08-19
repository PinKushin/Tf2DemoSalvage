# Corpus suite audit — 2026-08-19

`Tf2DemoSalvage.Corpus.Tests` holds **125 tests**. This audits them against the rule written into
its `AssemblyTestPolicy.cs`: a test stays when its evidence *requires real engine bytes*, and moves
to `Core.Tests` as a synthetic demo otherwise.

The audit changed the rule. It named two categories; the suite contains four.

## What it costs

Measured from `corpus.trx` on the gcor corpus, 2026-08-19:

| | |
|---|---|
| wall clock | **53 s** |
| CPU across all results | **369 s** (the suite runs parallel) |
| the 23 report tests | **49 s** — 13% of the cost, **zero assertions** |
| `DemoTimeline.Build` on a real demo | **~20 s**, and it happens **ten times** |

The single largest cost is not a category at all. Ten tests each call `DemoTimeline.Build` on the
same handful of files, rebuilding the entire timeline from scratch every time — roughly **200 of
the 369 seconds**, over half the suite, spent recomputing an identical result.

Slowest ten:

```
 37.43s  EveryDemo_CompilesBackToItsOwnBytes          <- the flagship; earns it
 25.07s  PlayerFacing_SequenceAndCycle_AreReported    <- a report
 24.83s  PlayersAt_BetweenFrames_MovesThroughPositionsNoFrameContains
 21.89s  PlayersInAMatch_DoNotAllFaceTheSameWay
 20.61s  Build_KeyframesCostFarLessThanAPosePerFrame
 20.40s  EntitiesAreHiddenAndComeBack_RatherThanLingering
 20.35s  PropsAt_ReturnsFewerModelsThanTheDemoEverHeldOf
 20.21s  Build_SomethingSomewhereMoves
 20.15s  Build_FindsModelsOnEveryEra
 20.00s  PlayersAt_CarriesTheYawTheTrackHolds
```

## The four categories

**A — Totality.** The engine wrote these bytes and reads them back, so anything that fails to
decode is our defect. A synthetic body proves the decoder handles shapes someone thought to write;
only a real file carries a shape nobody thought of.

*Examples:* `EveryDemo_CompilesBackToItsOwnBytes`, `Container_EveryCorpusDemo_WalksCleanlyAndAgreesWithItsHeader`,
`EveryUserCommand_DecodesAndReEncodesExactly`, `EveryWritableMessage_ReproducesItsOwnBitsExactly`,
`OpeningSnapshot_DecodesEveryEntityItNames`, `EveryDemo_TracesWithoutAnUnreadableBlock`,
every `*DecodesToPcm` (real codec payloads cannot be synthesised at all).

**B — Corroboration between paths that share no code.** A sound index comes out of a delta-coded
bit stream and the precache table out of `svc_CreateStringTable`; the index landing inside the
table is a fact about the *file*. Write both sides synthetically and the test checks this project
against its own beliefs.

*Examples:* `HeaderFields_MatchAnIndependentParser` (the only true differential — against another
parser entirely), `SoundNumbers_AddressTheSoundPrecacheTable`,
`UserCommand_ViewAngles_AgreeWithTheCameraTrack`,
`NetTickRunsOnTheServerClock_AtAConstantOffsetFromTheDemoClock`,
`Schema_ServerClassCountMatchesWhatServerInfoReported`.

**C — "The corpus exercises this path."** Most of the suite looks like it is in this category, and
**most of it should not be.** These tests assert that a real recording contains a crouch, a death,
a mid-game join, and they justify the assertion as a control: *"if the recording contained no death
the assertion below would pass against any code at all."*

That guard exists only because **the corpus is an uncontrolled fixture**. A synthetic test
constructs the crouch, so there is nothing to guard — the case is present by construction. The
control is a cost of using found data, not evidence about the code.

The other thing these tests appear to claim — *this happens in real gameplay, so the synthetic
test is not covering a fiction* — is a claim about **TF2**, not about this parser. A test does not
establish it either; reading the SDK does, which is what the conformance suites are for.

**What survives the strip is narrower and does belong here: "our decoder produces X from real
bytes."** `players.ShouldContain(player => player.IsCrouched)` really means *the decoder extracts
crouch state from a real recording*, which is totality of a single field wearing a fact about the
corpus. Worth keeping, worth re-framing, and worth **one** test rather than ten — the expensive
part is `DemoTimeline.Build`, not the assertion.

What does not survive is the pure corpus-composition claim, which measures the demos rather than
the code: `Scene_BothExclusiveTables_AreExercisedInTheCorpus`,
`Timeline_ARealDemo_CarriesMoreThanOneSkin`, `PlayerPositions_AreSpreadAcrossTheMap`.

*Re-frame and keep:* `PlayerFlags_DerivedCrouchAndAirborne_FollowTheFlags`,
`Timeline_DeadPlayers_AreNeverDrawn`, `JumpPhase_ARealJump_IsSeenInBothPhases`,
`MidGameJoins_AreInTheRoster` — each says the decoder gets that field out of real bytes.

**D — Reports.** 23 tests ending `_IsReported` / `_AreReported`. They print and assert nothing
beyond a non-empty guard; three probe files (`BodyGroupProbe`, `CarriedItemProbe`,
`WeaponEntityProbe`) contain **no `Should` call at all** and therefore cannot fail.

## What actually moves

Fewer tests than expected. The first pass assumed several were superseded by the synthetic tests
written on 2026-08-19; reading them showed otherwise, because most carry a category-C claim on top
of the logic.

**One genuine duplicate:** `CorpusTraceTests.EntitiesAreOff_UnlessAskedFor` is exactly
`EntityAssemblyDemoTests.Trace_ASnapshotWithASchema_ExpandsEntitiesRatherThanCountingThem`, and the
synthetic version is stronger — it asserts both halves of the opt-in rather than only the default.

**Plausibility ranges that a synthetic now states exactly** — these keep their category-C half and
should shed their range assertions to `Core.Tests`, where the answer is known rather than bounded:
`PacketEntities_EntityCounts_StayWithinEngineLimits`, `PacketEntities_DeltaFromTick_IsAlwaysInThePast`,
`EveryDamageValueAndOrigin_IsPlausible`, `EntityIndices_AscendAndStayInsideTheEntityLimit`,
`PlayerPositions_LandInsideTheWorldBounds`, `Schema_PropertiesLookLikeSourceEngineFields`,
`SteamIds_AreInTheRenderedTextFormat`, `UserIdsAndEntityIndices_AreDistinctIdentifiers`.

## Ranked actions

1. ~~**Memoise `DemoTimeline.Build` per file.**~~ **Done, and it was the biggest win by a wide
   margin.** 33 call sites now go through `TimelineCache`, a static
   `ConcurrentDictionary<string, Lazy<DemoTimeline>>` with `ExecutionAndPublication` — the same
   pattern `Viewer3D.Tests.MapCache` uses for maps, chosen over an NUnit fixture because a shared
   fixture serialises the tests inside it and this assembly is `ParallelScope.All`.

   Measured: **CPU 369.1 s → 166.1 s**, wall clock **53 s → 35 s**, 138 tests unchanged and zero
   failures. A timeline is safe to share because it is finished when returned — built once from a
   byte array, then only queried.

   Note this had nothing to do with synthetic-vs-corpus. Over half the suite's cost was one
   computation performed ten times.
2. **Give the three assertion-free probes an assertion or delete them.** A test that cannot fail
   is not a test; it is a script that runs on every gate. 49 s across the 23 reports.
3. **Delete `EntitiesAreOff_UnlessAskedFor`**, superseded and weaker.
4. **Move the eight plausibility ranges**, keeping their existence half here.

5. **Delete the pure corpus-composition tests.** `Scene_BothExclusiveTables_AreExercisedInTheCorpus`,
   `Timeline_ARealDemo_CarriesMoreThanOneSkin` and `PlayerPositions_AreSpreadAcrossTheMap` measure
   the demos rather than the code. If the corpus needs a property, `manifest.json` is where to say
   so — a failing test there means someone added a demo, not that the parser broke.

Nothing in A or B should move. C keeps only the half that says *the decoder produced this field
from real bytes*, and sheds the existence-controls that exist solely because found data is an
uncontrolled fixture.

## The correction worth keeping

The first draft of this audit treated C as a legitimate third category and recommended keeping all
of it. That was wrong, and the reason is worth stating because it is easy to repeat: a control that
guards against **the fixture** having drifted looks exactly like a control that guards against
**the code** being wrong. Both read as `ShouldNotBeEmpty("or the assertion below measures
nothing")`. Only the second is a test.
