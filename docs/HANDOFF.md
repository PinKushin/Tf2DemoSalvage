# Handoff — the project is a parity audit-and-fix loop, and the frame has a floor

Written 2026-09-01. **Supersedes the previous handoff**, whose subjects — launch options, the chase
camera, displacement collision — are done and merged.

Everything below is on `main` and pushed. Gate green: **4,501 across twelve assemblies, plus 30 UI.**

```bash
TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh
```

```bash
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
```

## Read this first — what the project IS now

**The owner, this session:** *"this project is basically a parity audit and fix loop at this point,
to make sure you dont mess up"*. That is the shape of the work. Everything below follows from it,
and two decisions were recorded that change how a result is judged:

- **D129 — the target is BETTER than TF2, not equal to it.** We draw no projectiles, no particles and
  no ragdolls, and skinning is on the GPU where Valve's `SetupBones` runs on the processor. So a
  number that merely approaches Valve's is a number that will fall behind the moment a feature
  lands. **Every frame measurement must state what is NOT being drawn when it quotes a rate.**
- **D130 — finish decoding a Valve subsystem before building on it.** The demo wire IS completely
  decoded, and this session kept finding fields sitting there unused: `m_bClientSideAnimation` with
  3,319 occurrences in one recording, `m_nResetEventsParity` with zero consumers. **Decode complete,
  consumption incomplete** is the pattern — and the better half to be missing.

## The reference numbers, measured in the game

Captured by the owner on `cp_badlands`, both overlays visible:

| view | TF2 | frame | GPU |
|---|---|---|---|
| a room with props | 893 fps | 1.12 ms | **30%** |
| facing a blank wall | 1135, TF2's own counter 1236 | 0.81–0.88 ms | **29%** |

**The GPU figure is the one that matters.** TF2 is CPU-bound at thirty per cent of the card while
turning in sub-millisecond frames. Ours, first person on `tf2-2026-pub-pov-clean`, is about 5.8 ms
busy and 2.8–3.7 ms facing nothing. Badlands against process is not a paired measurement; treat it
as an order of magnitude.

## What is open, and the one thing that needs a decision

### B259 fix 3 — the floor. THIS IS A DECISION, NOT A BUG FIX

An empty view still costs **2.4 ms with zero props posed**. Traced to the end:

| pass | ms | why it cannot start from the visible set |
|---|---|---|
| `sample` | 0.8 | the filters are downstream of the enumeration that builds the list they filter |
| `drawlist` | 0.6 | same |
| `models` | 0.3 | packing must see the broad set, or a model coming into view hitches — B163 measured 385 ms in one frame |
| `pose` | 0.7 | placement is needed whether or not a prop is drawn |

**The engine never enumerates.** `CClientLeafSystem` maintains per-leaf renderable lists
INCREMENTALLY — inserted when an entity moves, removed when it leaves — so `BuildRenderablesList`
reads only visible leaves. We rebuild from the timeline every frame, which is the honest cost of a
design that can seek where the engine cannot.

**Do not drift into this.** A per-leaf entity index surviving across frames and invalidated by a seek
is real architecture with a correctness hazard the current design does not have: a stale index draws
things that have moved. The prize is most of the gap to 0.85 ms. **Ask before starting it.**

### Parity gaps still open, in the owner's rough order of interest

1. **`C_BaseAnimating::DoAnimationEvents` is unimplemented and the MDL event array is never parsed.**
   `mstudioevent_t` is unread; `m_nResetEventsParity` is decoded with zero consumers. Full account in
   `docs/PARITY-AUDIT.md` finding 3, including the question that must be settled first — whether the
   client-side events are transient overrides the networked state re-asserts. That reading is
   probably wrong because delta compression means "the next time it is networked" can be never, and
   it is filed as an interpolation rather than a measurement.
2. **D128 — a POV demo must be locked to the recorder's view.** Decided and not implemented: no free
   camera, no third person, because a POV demo is PVS-limited and a free camera shows a room that was
   never recorded. SourceTV keeps the free camera.
3. **Ragdolls are entirely undrawn** — 299 in one demo, all decoded. `DT_TFRagdoll` is
   `IMPLEMENT_CLIENTCLASS_DT_NOBASE`, so nothing about how a corpse LOOKS is networked; every
   appearance field is built by `CreateTFRagdoll`. **The owner does not want these** — he plays with
   ragdolls off — so this is filed, not queued.
4. **A prop with an EMPTY model path is reported DRAWN.** Noticed in a control run, unexamined, two
   readings both guesses. `docs/PARITY-AUDIT.md` finding 5.

## What landed this session

Drawing: **B252** first-person attachments masked by display flags, and the econ attributes gating
them. All 356 shipped attachment entries declare `model_display_flags 3`, so the mask filters nothing
on real data — which is why the synthetic fixture was essential and a corpus test could never have
caught a wrong mask.

Parity/performance, all measured: **B254** cull entities before posing them (`CollateRenderablesInLeaf`
order), **B255** pose after the view (`CViewRender`'s `SetUpView` → `BuildWorldLists` →
`BuildRenderablesList`), **B258** derive player animation inputs only for players, **B259 fixes 1–2**
the client-side animation gate and the interpolation list. Frame went 11 ms → 5.8 ms busy.

Instruments, because none of this was visible before: `FrameRateLog` and `MomentCostLog` average
every frame rather than sampling one, `--measure <seconds>` counts PLAYBACK and prints to stdout, and
`--help` lists what the viewer accepts.

## Gotchas that cost real time this session

**The `--first-person` claim was made wrong AGAIN.** The previous handoff already corrected it —
*"an inference from a bad invocation that was never checked and then written down as fact"* — and
this session filed a parity-audit finding saying the flag does not exist and is silently swallowed.
It exists, in `LaunchOptions.cs:145`. The grep ran over `Viewer3D` and launch options live in
`Presentation`. **An absence claim needs a control, and a grep's scope is a claim about the grep.**
Finding 1b in `docs/PARITY-AUDIT.md` is now marked WITHDRAWN.

**Instruments lied five times, and every wrong turn started with one.** A cull counter the viewmodel
pass reset before it was read, reporting zero while working. `posed 600 of 0 selected`, because a
`with` expression copied the wrong record's fields. `posed 452 of 567` in an empty view — a derived
`selected − culled` that counted undrawable props as posed, where the true figure was `0 of 578`, and
it nearly sent an audit after a working frustum. A pose residual that subtracted `anim` twice and
printed `rest -0.4`. A build checked with `grep -E "error C"`, which matches `error CS` and not the
analyzer's `error S`, so a stale binary ran and reported the old format.

**`--measure` exists so a measurement is one call.** It counts seconds of PLAYBACK, not wall clock —
a run timed from process start spends its first twenty seconds on archives and the map, so a "forty
second" measurement was two seconds of frames. And it prints to stdout because the log is BUFFERED:
reading it mid-run shows asset loading and nothing else, which was twice misread, once as the viewer
having exited on its own.

**Measure only a FOCUSED window.** `NoFocusSleep` is the engine's own `engine_no_focus_sleep`, and an
unattended run is clamped. A 150-second run came out cleanly bimodal — p25 16 fps, median 106 — and
the clamped lines are recognisable on their own: phases summing to 10 ms under a 63 ms frame with
`unaccounted 0`.

**Inserting a member above an existing one splits it from its doc comment.** Seven build breaks in
one session, and `CS1572` names the WRONG member every time. Anchor on the END of the preceding
member. Recorded in `docs/memory/insert-below-the-member-not-above-it.md`.

## Verification, which the owner believes is the weak point

He is right, and this session is evidence: three sabotage runs found holes a green suite did not.
Emptying the player list across the `Build`/`Pose` split left **290/290 passing** (B257 — it took
three attempts to write a test that could fail, and the fix was the FIXTURE, not the assertion).
Two defects in the interpolation list were caught by the numbers alone. **He has suggested subagent
audits and they have not been run.** That is the standing offer to take up.
