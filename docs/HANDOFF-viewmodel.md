# Handoff — first-person rendering, materials, and attached items

Rewritten 2026-08-20 at the end of a long session on branch `feat/viewmodel`. The version this
replaces described the viewmodel as an open defect; it now works. Everything below is committed and
green.

Read `docs/findings/30-viewmodel-drawing.md` for the reasoning — this file is the state of play.

---

## What works now

| Piece | Where |
|---|---|
| First-person camera, POV and SourceTV | `FreeCamera.AtEye` / `.SpectatingEye`, `MainForm.FirstPersonCamera` |
| Choosing whom to spectate | `SpectatorTarget` |
| Viewmodel arms, correct class and animation | `DemoTimeline.ViewmodelAt`, `MainForm.AddViewmodel` |
| The weapon in those hands | `ItemSchema`, `MainForm.WeaponModel` |
| Drawing both in their own pass | `ViewmodelPass`, `Device3D.DrawViewmodels` |
| Items hung from a named attachment | `StudioAttachment`, `AttachmentPlacement` |
| Matrix boundary, in one place | `MatrixConvention` |

Verified by capture on z1800: a sniper's arms and rifle in first person, players in correct
materials, a sapper on a sentry's `build_point_0`.

```bash
./managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe tools/corpus/demos/z1800.dem --tick 40000 --first-person --shot out.png
```

Gate: core 1438, cli 68, audio 28, content 588, corpus 85, viewer 505. UI 12 of 12, and it refuses
to run while a game holds the desktop — that is the guard, not a failure.

---

## The thing worth carrying forward

**"Drawn and invisible" had FIVE separate causes in one feature.** Each looked identical from
outside, and each was found only after the previous one was fixed:

1. **Not loaded.** `DemoModelPaths` walks class models and `timeline.Props`, and a viewmodel is not
   a prop — it has no origin, so the timeline deliberately keeps it out. It was in neither list.
2. **Not uploaded.** `EntityModelSet.Add` fills this process's copy; the renderer keeps its own on
   the GPU via `UploadModels`. The world's props upload when their set grows; `AddViewmodel` threw
   that return value away.
3. **Wrong sequence.** `m_nSequence` 1 on an arms model is `r_handposes`, a one-frame pose holder.
   The real viewmodel animations start at 2 and carry `ACT_*_VM_*`.
4. **Wrong owner.** An unowned viewmodel was matching everybody, which is right for POV and wrong
   for SourceTV.
5. **Wrong posing mechanism.** The weapon has one sequence and no animation, so posed independently
   it sits at its own origin — at the camera, inside the near plane. It must be bone-merged onto the
   arms.

**The renderer had been printing the answer to (2) on every frame the whole time:**

```
WARN [render] a model was posed but the renderer has no geometry for it
```

with a comment above it reading "the renderer's copy of the packed set is older than the caller's,
which draws nothing and reports nothing". Four rounds of new instrumentation went in before anyone
read the warnings already there. **The corollary to "logs are the debugger" is that the logs already
written are the first place to look, not the last.**

---

## Mechanisms established, with citations

**A viewmodel is drawn in its own pass.** `CViewRender::DrawViewModels` keeps the view's origin and
angles and replaces three things: FOV 54 (`viewmodel_fov`, and TF2 reads `viewmodel_fov_demo` during
playback — our only case), near plane 1 against the world's `VIEW_NEARZ` of 7, and depth range
0…0.1. The depth range is what keeps a gun out of a wall. Restoring the world camera afterwards is
mandatory: it is written on view CHANGE, not per frame, so a pass that leaves its own projection
behind makes the whole map draw at 54 degrees.

**The weapon model is not in the demo.** The viewmodel attachment is created with
`InitializeAsClientEntity` (`econ_entity.cpp:1153`) — no edict, nothing networked. The held weapon
entity carries no drawable model either. What the demo does carry is
`DT_ScriptCreatedItem.m_iItemDefinitionIndex`, and `items_game.txt` turns that into a model through
`model_player`, inherited along the `prefab` chain. Stock weapons are four lines and a prefab, so a
reader looking only at the definition answers nothing for most of the game.

**Twenty-two of fifty-six held weapons send no item index**, so the stock item for the weapon's class
is the fallback — `baseitem` plus `item_class`, the same pairing `LINK_ENTITY_TO_CLASS` makes in
code. Together they answered for 56 of 56.

**Items hang from a wearer two different ways.** A hat shares bone names and is bone-merged; a halo,
canteen, spellbook or sapper shares none and hangs from a named attachment.
`ConcatTransforms( GetBone( iBone ), pattachment.local, world )`, stored ONE-based
(`PutAttachment( i + 1, world )`), with `m_iParentAttachment` naming a point on the WEARER — the
spellbook itself declares no attachments, a scout declares 29.

**Two matrix conventions, on purpose.** Bones reach the shader in Valve's 3×4 column-vector layout
and are used raw. The model matrix is `row_major float4x4` transforming a row vector. Crossing
between them is a transpose plus a translation move, and it lives in `MatrixConvention` — once,
because the second implementation is how the two would drift.

---

## Corrections the owner made, which changed the work

- **`viewmodel_fov` is a setting.** It was read from the SDK, its clamp was quoted, and then 54 was
  written in as a constant. Reading source produced a correct number and an incorrect conclusion.
- **Grenades do not use the off hand.** `tf_weaponbase_grenade.cpp:74` calls `SetViewModelIndex( 1 )`
  and reads as a second case; TF2's throwables were cut before release and no shipped item names the
  class. Living SDK code that nothing exercises, read as evidence about the game — the same shape as
  `$modblend`.
- **If a convention is wrong, fix it rather than transposing around it.** Checked: it is two
  conventions, not a wrong one. But the boundary was unnamed and had been implemented twice, which
  is the real defect and is now fixed.
- **Only the SourceTV camera should lack a viewmodel.** Two players lacked one; the second turned out
  to be dead (`life 2`, `EF_NODRAW`, no active weapon), which is correct.

---

## Still open

- **The off hand is read but not drawn.** `OffHandViewmodelAt` exists and is tested; wiring it into
  `AddViewmodel` beside the main hand is the remaining step. Both are on screen at once in game — a
  cloaking spy has weapon and watch, and the watch is the only user of slot 1.
- **Bob, lag and shake** are deliberately absent. All three are functions of movement and elapsed
  time rather than anything a demo records, so implementing them would be inventing motion.
- **Cloak is computed, not recorded.** `m_flInvisibility` is not networked; only `m_flCloakMeter` is.
  The level is recomputed from `m_nPlayerCond` and timers. The viewmodel band is
  `TF_VM_MIN_INVIS 0.22`…`TF_VM_MAX_INVIS 0.5`, and an observer sees a spy clamped to
  `tf_teammate_max_invis` (0.95) rather than fully invisible — a demo has no local player, so that is
  always our branch.
- **The HUD** — nothing decoded is drawn as an overlay yet.
- **Two z1800 players resolve no viewmodel** because their owner handle never decoded. No hands beats
  another player's hands.
- **`docs/DECISIONS.md` numbers nine decisions twice — B118, found while checking this file's own
  citations.** D20–D28 each name two entries, one per heading level, and both series are cited from
  source comments. A citation of "D28" resolves to the viewmodel decision or to user messages
  depending on which you read. Renumbering the later series to D34–D42 is the fix; it needs roughly
  thirty references classified by what they say, so it was recorded rather than done unprompted.

## Corpus notes

`z1800` is the only real match in the committed corpus — a 9v9 Highlander game on `koth_harvest_final`
with 25 players, all nine classes, and the only demo using attachments (three sappers). Every era
specimen is a solo recording, so it has one player and no cosmetics. Era matches are effectively
unobtainable: pre-2013 competitive mostly used live Mumble casts and there was no central archive
before demos.tf, which is why D5 says to build schema-driven rather than corpus-driven.
