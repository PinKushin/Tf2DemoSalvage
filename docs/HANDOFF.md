# Handoff — two-pass models, and a viewmodel bug narrowed to one bone

Written 2026-08-28, late. **Supersedes the previous handoff.**

## Read this first

The demoman's sticky launcher "disappears" during a sticky charge on `cp_process_f12`. It does not
disappear. **It is deformed** — its visible geometry is stretched to nearly four times its size by
one bone, and at that size it does not read as a weapon.

**The bug is in the ARMS' animation, and it is one bone.**

```
c_demo_arms.mdl, read from the file:
  [16] vm_weapon_bone    parent 6  flags 0x40200  rest (0, 0, 0)
  [17] vm_weapon_bone_1  parent 6  flags 0x40200  rest (0, 0, 0)
  separation in the REST pose: 0 units

c_demo_arms, measured in the viewer:
  bone 16 at (656.4, 1469.4, 651.4)
  bone 17 at (583.0, 1463.9, 596.5)      ~92 units apart
```

Two bones with the **same parent** and an **identical rest transform** must move together. Ours
diverge by 92 units under animation. The weapon merges onto both, so 45 of its visible vertices go
with bone 17 and the model tears.

**Next step:** in `SkeletonPose.Build`, compare what the arms' current sequence writes for bone 16
against bone 17 — the `_overrideOf` / `animated` path. Find why one moves and the other does not.

## What is already ruled out — do not re-litigate

Each of these was killed by measurement, and several cost hours:

| candidate | how it died |
|---|---|
| the two-pass work (D114) | the **pre-change tree still drops out** — control run in a scratch worktree |
| the weapon switch to his primary | `m_hActiveWeapon` really does move; the Iron Bomber renders correctly |
| `m_iWeaponMode = TF_WEAPON_PRIMARY_MODE` on charge | we read that field nowhere |
| the player being dead | a red herring; one event, generalised badly. Dropouts are 60 ms–4.6 s, a respawn is 23 s |
| `EF_NODRAW` / `IsOnScreen` | removing the flag changed nothing |
| the attachment's sequence | changed, reverted, re-applied; still drops |
| bone collapse ("span" proxy) | false-positived on the **ubersaw**, which draws perfectly |
| the camera | matrix finite throughout; a broken projection cannot select one weapon |
| occlusion / depth range | not occluded, and `Viewport near` already matches `DepthRange(0, 0.1)` |
| the bone MERGE | copies faithfully; our loop and name matching are byte-for-byte Valve's |
| the MODEL | the file says the two bones are coincident |
| a stale parent | detector silent — **but never sabotage-verified** |
| procedural bones (B182) | both bones report `proc 0` |

## Valve citations earned tonight — these are solid

- **Valve merges the viewmodel weapon.** `C_ViewmodelAttachmentModel::InitializeAsClientEntity` adds
  `EF_BONEMERGE` and `EF_BONEMERGE_FASTCULL` (`econ_entity.cpp:848`).
- **The attachment lives `EF_NODRAW`** and is made visible only for the instant the viewmodel draws
  it — same function, and its comment says so.
- **Its model comes from the item schema**, `pItem->GetPlayerDisplayModel( iClass, team )`
  (`econ_entity.cpp:1167`) — NOT from the weapon entity's model index. Trying the latter drew no
  weapon at all.
- **Nothing ever calls `SetSequence` on the attachment.** It keeps its own sequence and the merge
  places it; its blending hook is `{}` for every weapon but two (`econ_entity.h:125`).
- **On a demo, viewmodel visibility is purely the camera.** `C_BaseViewModel::ShouldDraw`
  (`c_baseviewmodel.cpp:277`) returns `IN_EYE && target == owner` under HLTV; the branch that reads
  `EF_NODRAW` is unreachable during playback.
- **A dead spectated player is shown in third person** and never reaches `CalcViewModelView`
  (`hltvcamera.cpp:307`).
- **`DT_BaseViewModel` networks `m_hWeapon`** (`baseviewmodel_shared.cpp:567`) — the engine asks the
  viewmodel what it holds; we ask the player and reconstruct. Decoded now, logged beside our answer,
  not yet deciding.

## Remaining viewmodel parity gaps

`m_fEffects`, `m_nBody`/`m_nSkin` on the viewmodel, `m_nAnimationParity` /
`m_nNewSequenceParity`, `m_flPoseParameter[]`, `CalcViewModelLag` / `AddViewmodelBob`, and the
dead → third-person camera (half done: no viewmodel, still first person).

## The instruments, all `LogDebug` behind `developer 1`

Run the viewer with `+developer 1` — the owner's standing instruction, and none of these cost
anything without it. **None of them is capped by a report count** (see below).

- `Device3D` — viewmodel pass transitions, per-model bone degeneracy/span/placement, camera matrix
  finiteness, render-group changes.
- `WorldRenderer` — "drew NOTHING in the {pass} pass", and per-model submission changes with body,
  skin and resolved materials.
- `EntityModels.ReportPosedSize` — the posed VERTEX extent of a viewmodel, body-filtered, with
  per-bone vertex centroids when it stretches. **This is the one that found the bug.**
- `BoneMergeCache` — the pairing with UNMATCHED bones named, and a per-bone copy report that fires
  when the copied bones spread past 50 units.
- `SkeletonPose` — "STALE PARENT", a bone built on a parent the mask skipped.
- `SoundPresenter` — sound submitted vs dropped for zero gain (the audio path had no output-level
  instrument at all).

## What this session got wrong, because it will save the next one hours

**Nine instruments were built that could not see what they were aimed at.** In order: a one-second
sample for a 60 ms event; a bone-degeneracy test blind to a collapsed basis; a COUNT of viewmodel
props where identity was needed (the file warns about this eight lines away); a distance computed
between two timestamps three seconds apart; a 100-unit threshold for a 20-unit effect; a global
report budget spent by two animating props before the subject spoke; a posed-size walk that measured
hidden bodygroups; a merge report capped at 24 copies, all from startup; and a span proxy that
false-positived on a working weapon.

The owner, twice: *"OMG STOP CAPPING YOUR FUCKING TESTS AND LOGS"* and *"you should have learned your
lesson when you had the second wait"*. He is right. **`developer 1` is the control; a count cap is
unrelated to the event and can only ever be luck.** Bound a diagnostic by the SIGNAL — a transition,
a threshold on the symptom — never by a number.

Two process rules the owner set, now D115 and memories:

- **State the assumptions he can falsify before instrumenting.** "The sticky launcher doesn't draw"
  was taken literally for hours; one sentence — the arms are there too — retired four mechanisms.
- **Run the control before arguing about authorship.** Three correct arguments that the change was
  not mine were not evidence. The control took one launch.

## Where things are

- Branch `fix/viewmodel-dropout`, gate green (D1..D115, all floors), viewer works.
- `main` has the two-pass work (D114) and the first instrument commit, both green.
- Everything in `docs/findings/44`, `docs/DECISIONS.md` D114–D115, `docs/RISKS.md` B221–B222.
