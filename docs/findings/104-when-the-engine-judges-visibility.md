# 104 — When the engine judges an entity's visibility (B578)

**Question.** B578 said the engine judges visibility once, at the change, and we judge it every moment in five filters
(`MomentScene.Build`: first-person, weapon, disguise, respawn-room, vision). Which of those five does the engine
really cache, which inputs re-judge the cached ones, and are any of those inputs left without a trigger?

**Evidence class:** read from published source (`F:/src/source-sdk-2013`, 2013 SDK; TF2's live client is closed, so a
later change to any of these is not ruled out). No code changed; no measurement taken.

## The cache and its triggers

The cache is membership in the leaf system. `C_BaseEntity::UpdateVisibility` (`c_baseentity.cpp:1370-1432`) adds the
entity when `ShouldDraw() && !IsDormant()` and removes it otherwise. `C_BaseAnimating::UpdateVisibility`
(`c_baseanimating.cpp:3161-3178`) adds the model effects or stops the attached particles. `CTFWeaponBase::UpdateVisibility`
(`tf_weaponbase.cpp:3267-3278`) also updates the extra wearables and marks the owner's bodygroups dirty.

What calls it (clangd incoming callers of `C_BaseEntity::UpdateVisibility`, each read where cited):

| trigger | site |
|---|---|
| dormancy flip | `C_BaseEntity::SetDormant`, `c_baseentity.cpp:4015-4024` |
| entity created | `C_BaseEntity::OnDataChanged`, `:3317-3320`; `C_BaseCombatWeapon::OnDataChanged`, `c_basecombatweapon.cpp:166-169` |
| any data update that flips its own `ShouldDraw()` | `m_bOldShouldDraw = ShouldDraw()` in `PreDataUpdate` (`:2199`), compared in `PostDataUpdate` (`:2652-2656`) |
| move parent changed | `PostDataUpdate`, `:2646-2650` |
| effects changed (`EF_NODRAW`) | `AddEffects` / `RemoveEffects` / `SetEffects` / `ClearEffects` (caller list; bodies not quoted) |
| model pointer changed | `SetModelPointer` (caller list; body not quoted) |
| owner's active weapon changed | `C_TFPlayer::OnDataChanged`, `c_tf_player.cpp` (in 4411-4893): old and new active weapon each `UpdateVisibility()` |
| owner's `TF_COND_DISGUISED` flipped | `C_TFPlayer::OnDataChanged` → `UpdateWearables()` (`c_tf_player.cpp:9969-9977`: base class, then the active weapon). The base class's loop over the wearables was **not located** (clangd did not resolve `BaseClass::UpdateWearables`) — interpolated that it calls `UpdateVisibility` on each worn item, as its comment says |
| local observer leaves in-eye | `C_TFPlayer::OnDataChanged`, local player only: the old target's `UpdateVisibility()`, and the old and new target's `UpdateWearables()` on a target or in-eye change |
| local custom model visibility | `C_TFPlayer::OnDataChanged`, `m_bOldCustomModelVisible` |

## What the engine judges every frame anyway

Three of B578's five filters are **not** cached in the engine. They are asked in `DrawModel`, which runs every frame:

- **First person, the player.** `C_BasePlayer::DrawModel` returns 0 when `!ShouldDrawThisPlayer()`
  (`c_baseplayer.cpp:1445-1456`), which asks `InFirstPersonView()` (`:1992-2011`).
- **First person, the weapon.** `C_BaseCombatWeapon::DrawModel` (`c_basecombatweapon.cpp:481-493`) hides the weapon of
  the in-eye observer target. Valve's comment says why it is there: *"we don't check that in ShouldDraw() since this may
  change without notification"*.
- **Respawn-room visualizer.** `C_FuncRespawnRoomVisualizer::DrawModel` (`c_func_respawnroom.cpp:48-64`) checks the round
  state and the local player's team on every draw.

Also per frame in the leaf system's collate (`CClientLeafSystem::CollateRenderablesInLeaf`, `clientleafsystem.cpp:1574-1752`):
the render-frame dedupe, `GetFxBlend() == 0`, the frustum/area cull and occlusion. `ShouldDraw` is **not** called there.
And `C_TFPlayer::DrawModel` (`c_tf_player.cpp:6929-6994`) returns 0 per frame for a ragdolled player or one fully invisible.

**So `FirstPersonVisibility` and `RespawnRoomVisibility` judging per moment is parity, not a divergence**, and caching
them would create one. That takes the first-person block out of B578's scope (0.04 ms after the B578 comparer fix, measurements/2026-10-24-drawlist-parts.md).

## The three that are cached, and whether every input has a trigger

| ours | engine rule | inputs ours reads | trigger in the engine |
|---|---|---|---|
| `WeaponVisibility` (holstered) | `C_BaseCombatWeapon::ShouldDraw` `c_basecombatweapon.cpp:399-452`: `EF_NODRAW`, no owner → draw, player owner → `m_iState == WEAPON_IS_ACTIVE` | the weapon's own `m_iState` and owner | own data update (`PostDataUpdate` compare), plus the owner's active-weapon change. **Every input covered.** |
| `DisguiseVisibility` | `CTFWearable::ShouldDraw` `tf_item_wearable.cpp:319-412`; `CTFWeaponBase::ShouldDraw` `tf_weaponbase.cpp:3226-3262` | the owner's disguise condition, team, disguise class and team, and the **viewer's** team | the owner's `TF_COND_DISGUISED` flip → `UpdateWearables`. **A change of the viewer's team has no trigger** on other players' items (no caller found), nor does a disguise class/team change while staying disguised (not read whether that recreates the disguise wearables) |
| `VisionVisibility` | `CEconEntity::ShouldDraw` → `ShouldHideForVisionFilterFlags` `econ_entity.cpp:1802-1829` | the item's filter flags and the **viewer's** vision flags | the item's own update only. **No caller re-judges items when the viewer's vision flags change** (none in the incoming-caller list) |

**What that means.** Where every input has a trigger, the engine's cached verdict always equals a fresh one, and
judging per moment produces the same drawn set: a scheduling difference only. Where an input has no trigger, the
engine keeps a stale verdict until the item's next update of its own (`PostDataUpdate` compares old and new
`ShouldDraw()`). An item that changes often refreshes quickly. A static one could stay stale for a long time. Our
per-moment judgment is never stale.

So there are two possible readings of parity, and **they give different drawn sets**:

1. **Parity of the answer:** keep judging per moment (or memoize on the inputs, which is the same thing). It matches the
   engine wherever the engine is not stale.
2. **Parity of the structure:** judge only at the engine's trigger sites and keep the stale verdicts. This needs a
   per-entity "last update" history that rebuilds on a seek (`docs/memory/a-seek-rebuilds-what-playing-accumulated.md`).
   It would fail an equivalence test against the per-frame path by design, in exactly the untriggered cases.

**Not chosen here.** The brief said to stop for the owner when the engine's rule is ambiguous. The rule is clear, but
the choice it forces is the owner's.

## Cost, for scale (no new measurement)

From `docs/measurements/2026-10-24-drawlist-parts.md`: `filters` is 0.059 ms on gummo first person, 0.022 on the empty
view, after the comparer fix. The three cached filters are the empty view's share, about 0.02 ms (measurements/2026-10-24-drawlist-parts.md; the empty view runs every filter except the first-person block, and includes respawn-room, so this is an upper bound). A cache keyed on the
inputs would read the same three or four fields per prop to detect a change, plus a lookup, because `SceneProp`s are
rebuilt every moment. **No gain is expected from either reading.**

## Neighbors not checked

- `C_TFPlayer::ShouldDraw` hides the local player while `TF_COND_ZOOMED` (`c_tf_player.cpp:8006-8023`), and
  `CTFWearable::ShouldDraw` hides his cosmetics too (`tf_item_wearable.cpp:338-340`). Nothing in `Scene` names the
  condition outside the HUD crosshair. Not checked: whether this can be seen at all, given the zoomed local player is in
  first person. It could matter in a mirror or monitor view (B511).
- `CTFWearable::ShouldDraw`'s ghost-mode and taunt-repurpose branches (`:325-336`, `:404-407`); not compared with ours.
  Ghost mode since B767; the taunt branch is in the next section (B770).

## A wearable its weapon controls, and the drawn list is the wrong place to look the weapon up (B770)

*Evidence class: read from published source, then measured on the corpus.*

`CTFWearable::ShouldDraw` ends with a branch for a wearable whose `m_hWeaponAssociatedWith` names a weapon
(`tf_item_wearable.cpp:378-410`): it hides when that weapon is not the owner's active one, when (third person only) the
weapon's own `ShouldDraw` is false, and when the weapon is repurposed for a taunt. `CEconWearable::ShouldDraw` beneath it
hides a tied wearable on a dead owner (`econ_wearable.cpp:395`, `ShouldDrawWhenPlayerIsDead` is
`GetWeaponAssociatedWith() == NULL`). The server sets the handle in `CTFWeaponBase::UpdateExtraWearables` on a weapon's
viewmodel extra wearable always, and on its world extra wearable only when a viewmodel one exists (`tf_weaponbase.cpp:993`,
`:1018-1022`). Measured on `demostf-cp_process_f12-2026-08-08-2207`: 2 of 120 `CTFWearable`s carry it, a Botkiller on a
sticky launcher, each with a `CTFWearableVM` twin.

**The first implementation looked the weapon up in the moment's props, and that answer is wrong for the common case.**
A holstered weapon carries `EF_NODRAW`, so `PropsAt` never lists it: at tick 20000 the demoman held his grenade launcher,
sticky launcher 360 was in no prop list, the handle resolved to nothing, and the Botkiller drew on his back. Every unit
test passed, because each built the weapon as a prop. The engine dereferences against the client's entity list, drawn or
not, so the timeline now does: it keeps the handle only while a track with that slot and serial is alive. **And it asks
again when the weapon is born or ends**, because a wearable is transmitted under its own PVS check (`CTFWearable::
ShouldTransmit`) and can enter a tick before its weapon; read only at the wearable's own keyframes, the answer would
hold the null from that first tick.

**`!pWeapon->ShouldDraw()` is read as "its weapon left this list"**, which holds because every rule that can remove a
weapon (holstered, disguise, control stun, first person, the hidden local player, and the vision filter, which is
`CEconEntity::ShouldDraw` and so the weapon's too) runs before this one. The first version ran before the vision filter
and the review caught it. A new weapon rule added after `WeaponWearableVisibility` would break it silently.

**The disguise branch is not the ghost branch.** Ghost mode returns before the weapon test for every wearable, but the
disguise branch is guarded by `!IsViewModelWearable()` (`:344`), so a disguised spy's `CTFWearableVM` still answers to
its weapon. The first version skipped both for a disguised owner.

Not compared: whether TF2's live client still has the branch as written; the 2013 SDK is the source.

## A viewmodel wearable is two entities' worth of rules (B771)

*Evidence class: read from published source, measured on the corpus.*

A weapon's extra viewmodel wearable is a `CTFWearableVM` following `pOwner->GetViewModel()` (`econ_wearable.cpp:185-192`),
drawn only in viewmodel mode (`:375-393`) and in the translucent viewmodel group (`:454-460`). This project drew it as a
world prop, attached to a viewmodel entity no world pass has: on the f12 match the Botkiller's twin (362) sat in the
draw list beside the world Botkiller (364). Two things that look settled turned out not to hold for this class: **its
move parent is not its owner** — `DisguiseVisibility` looked the owner up through the move parent first and found the
viewmodel, so a ghost's viewmodel wearable escaped the ghost rule until a test asked — and **the disguise branch skips
it** (`!IsViewModelWearable()`, `tf_item_wearable.cpp:344`), leaving it to the weapon branch.
