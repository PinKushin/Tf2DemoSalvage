# Handoff — the first-person camera and the viewmodel

Written 2026-08-20 at the end of a long session, on branch `feat/viewmodel`. Everything committed
is green; one defect is identified with evidence and not yet fixed.

Read `docs/findings/01-container.md` and `docs/findings/04-entities.md` for the reasoning behind
the decisions below — this file is the state of play, not the research.

---

## What works, and is merged to `main`

The first-person camera. `V` enters and leaves it.

| Piece | Where |
|---|---|
| `democmdinfo_t` decode | `Core/Container/RecordedView.cs` |
| Per-class eye heights | `Core/Scene/PlayerEye.cs` |
| Camera placement | `Viewer3D/FreeCamera.AtEye` / `.SpectatingEye` |
| Per-tick lookup | `DemoTimeline.RecordedViewAt`, `.RecorderEntityIndex` |
| Mode switching | `Viewer3D/CameraMode.cs`, `MainForm` |
| Hiding the viewed player | `Viewer3D/FirstPersonVisibility.cs` |

Confirmed by the owner on a capture: no checkerboard, no blobs, the view looks right.

## What is on `feat/viewmodel`, unmerged

Five commits. All tests green, gate green, UI suite 12/12.

| Piece | Where |
|---|---|
| Viewmodel property readers | `EntityState.Viewmodel*` |
| Per-tick weapon lookup | `DemoTimeline.ViewmodelAt(tick, player)` |
| Mirrored cull state | `WorldRenderer.CullFor`, `_viewmodelCull` |
| Placement at the eye | `MainForm.AddViewmodel` |
| Uncommitted | `ViewmodelClassAgreementTests.cs`, a UI test edit |

---

## THE DEFECT, AND WHAT IT WAS — fixed 2026-08-20

**`ViewmodelAt` returned the wrong weapon when a demo carried more than one viewmodel entity.**

Evidence, from `ViewmodelClassAgreementTests` — it compares the resolved weapon against the
recorder's networked `m_iClass`, two unrelated decode paths:

```
AGREED
  2007 granary  class 1 (scout)    v_scattergun_scout
  2007 granary  class 1 (scout)    v_pistol_scout        <- weapon switch, tracked correctly
  2008 granary  class 3 (soldier)  v_rocketlauncher_soldier

DISAGREED
  2009 badlands class 3 (soldier)  v_watch_spy
  2009 badlands class 1 (scout)    v_watch_spy
  2009 badlands class 1 (scout)    v_watch_spy           <- frozen while the class changes
```

The 2009 badlands demo is the one the entity survey reported as carrying **2 viewmodel entities**;
every other demo carries one. `ViewmodelAt` collapses them into a single list and returns whichever
was recorded most recently, so the wrong one wins.

**The fix was a field transcribed from the send table and then never read: `m_nViewModelIndex`**
(`baseviewmodel_shared.cpp:563`, 1 bit unsigned). `MAX_VIEWMODELS` is 2; slot 0 is the weapon in
hand and slot 1 is the off hand — `CTFPlayer::GetOffHandViewModel` is `return GetViewModel( 1 )`,
claimed by the spy's Invis Watch and by grenades. `ViewmodelAt` now filters on it.

Full write-up in `docs/findings/04-entities.md`; the scoping decision is D28.

**The 2013 sniper question is settled, and not the way it was expected to go.** The owner said he
never played sniper on that demo. The demo disagrees, on an independent decode path: at some ticks
`m_iClass` is 2 with `v_sniperrifle_sniper` in hand, and across the file he plays scout, sniper,
soldier, demo and pyro. The resolution was right; the recollection was not.

**The agreement test now asserts instead of reporting.** As first written it printed AGREED and
DISAGREED and asserted only that *something* had been compared — so it could not have proved this
fix, and an empty DISAGREED list is also what "that demo stopped resolving a weapon at all" looks
like. It now names the two-viewmodel demo and requires a comparison from it.

**Still short of the engine, deliberately:** the off hand is drawn *alongside* the weapon, not
instead of it — a cloaking spy sees both — so a spy demo will show one viewmodel short. D28.

---

## Things that cost time, so they do not cost it again

**A plausible model path is not a correct one.** `c_sniper_arms.mdl` was accepted as working
because it looks exactly like a real weapon. The owner's "I never went sniper" is the only reason
the agreement test exists, and it found a genuine bug in one run. Prefer a cross-check between two
unrelated decode paths over any assertion that a value looks reasonable.

**Seven instrument bugs preceded any decoder bug in this work.** Every one presented as a clean
zero or an empty result that would have been written down as a fact about the format:

- grepping assembly output for `CTFViewModel` returned nothing — so did grepping for `CTFPlayer`,
  because class names are not printed there at all;
- a survey reported "0 of 37 owners are players" because `ClassName` is seeded by
  `DemoTimeline.Build` and a hand-rolled entity walk never calls `SetClassName`;
- a Python probe reported view origin `0,0,0` for every demo including POV ones, because it
  mis-walked the command stream;
- the modelprecache table is Snappy-compressed, so `entry index=` cannot be grepped out of the
  assembly output at all.

Put a positive control in the same sweep as any absence claim.

**Two ordering bugs, both found by tests rather than by reading.** `RecordViewmodels` originally ran
*before* a packet's messages were applied, so an entity entering on that packet was never recorded.
The capture test originally shot the frame two seconds before the weapon packed. Synchronise on the
condition, never on the clock.

**A missing model is not a bug.** A demo's precache names the model the *recording* used, and TF2
replaced `v_models` with `c_models` around 2011 — so a 2013 recording can name
`v_scattergun_scout.mdl` at a tick where the current install has no such file. The renderer reports
`no-batches` and draws nothing, correctly. The UI capture therefore synchronises on the viewmodel
being *resolved*, not drawn.

**What the demo does not carry, do not invent.** `CalcViewModelView` adds bob, lag and shake after
placing the viewmodel at the eye. All three are functions of movement and elapsed time, not of
anything recorded. Deliberately not implemented.

---

## Still not implemented, and inventoried

- **The HUD** — `ClientSystems_TheHud_IsWhereADecodedEventBecomesVisible`.
- **Viewmodel FOV and depth range.** The engine draws viewmodels with a separate FOV and its own
  depth handling; this draws them in the world pass. Not yet investigated.

## How to run things

```bash
bash build/gate.sh
```

```bash
pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
```

The UI suite refuses to run while a game holds the foreground — that is the guard working, not a
failure. It skipped an entire run this session because TF2 was focused.

Captures land in `%LOCALAPPDATA%\Tf2DemoSalvage\shot-*.png`, named to millisecond resolution
because two in the same second used to overwrite each other.
