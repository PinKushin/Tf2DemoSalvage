# Parity audit — every branch, not just the one that bit

**The owner's instruction, and it earned its place the hard way:** *"keep auditing for parity, if we
have parity for everything we have implemented, all sides of anything that has more than one branch,
then we can start going on and actually implementing the stuff we still dont have"*.

Every expensive bug of 2026-08-30 was **one branch of a multi-branch engine function, implemented on
one side only**:

| bug | the function | what was missing |
|---|---|---|
| B236 | `C_TFPlayer::GetSkin` + `ValidateModelIndex` | the mask is a SKIN and a BODYGROUP; we did the skin |
| B240 | `C_BaseEntity::ShouldDraw` | the `kRenderNone` test, which is its first line |
| B241 | `C_BaseEntity::CalcAbsolutePosition` | branch 3 of 3, so a parented prop lost its angles |
| B233 | `InitPerClassStringArray` | `basename` — one of the key's two forms |
| B232 | `CTFWearable::ShouldDraw` + `CTFWeaponBase::ShouldDraw` | the weapon half of a mirrored pair |

Not one was found by measuring our own output. Every one was found by reading the engine function
end to end.

## The instrument

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -- parity
dotnet run --project tools/Tf2DemoSalvage.Probe -- parity econ_entity
```

It reads every `file.cpp:line` citation in `managed/` — **405 distinct**, which is the denominator —
finds the enclosing function in `source-sdk-2013`, counts its branch points and ranks them.

**A branch count is a SCREEN, not a verdict.** It cannot tell whether a branch is implemented; it
says where the risk is concentrated so the reading starts where there is most to get wrong. The
reading is still the work.

## Findings

### 1. `attached_models` is not implemented at all — CLOSED (B251 world, B252 first person)

`CEconEntity::UpdateAttachmentModels` (`econ_entity.cpp:1078`) is this project's **most-cited**
engine function — eleven citations — and the first thing it does is a mechanism we have never
touched:

```cpp
int iAttachedModels = pItemDef->GetNumAttachedModels( iTeamNumber );
for ( int i = 0; i < iAttachedModels; i++ )
{
    attachedmodel_t *pModel = pItemDef->GetAttachedModelData( iTeamNumber, i );
    ...
    m_vecAttachedModels.AddToTail( attachedModelData );
}
```

An item definition can hang **extra models on itself**, per team, and a festivized variant on top.
The string `attached_models` appears **29 times** in the shipped `items_game.txt` and **zero times**
in `managed/`. Two measured examples:

- the Degreaser's pilot light, `c_degreaser_pilotlight.mdl`
- the Quick-Fix's `c_overhealer.mdl`

So twenty-nine items are drawn with a piece missing, silently, on every demo that contains one.
Nothing reports it because nothing asks: the model is never named, so it never fails to load.

**Note what sits three lines below it in the same block: `custom_particlesystem`.** That is the
unusual-effect and weapon-effect mechanism, and it is on the list of things to build next — the same
function carries both, which is an argument for doing them together.

#### Closed, and three things measured on the way out

**The count was 29 occurrences of the string; the mechanism reaches 325 items.** `attached_models`
is inherited through prefabs, so a block written once is carried by every item that names that
prefab — 356 entries across 325 items, 42 of them plain and 314 festivizer-gated. A grep for the
string undercounts the blast radius by an order of magnitude, which is the general hazard: the
shipped schema is a language with inheritance, and counting its tokens is not counting its effects.

**Every one of the 356 declares `model_display_flags 3`.** Not one is 1 or 2. So the mask B252 built
— `DrawEconEntityAttachedModels`' `(m_iModelDisplayFlags & iMatchDisplayFlags)` — is correct parity
and, on shipped data, filters nothing whatever. That is worth writing down twice over:

- it is the exact case `CLAUDE.md`'s fixtures-before-corpus rule predicts. A corpus test could not
  have caught a wrong mask, because **every real input predicts the same observation** whether the
  filter works or is absent. The synthetic fixture — three entries at flags 3, 1 and 2 — has ground
  truth precisely because no real file provides it.
- it is a fact about **today's** `items_game.txt`, not about the engine. Valve reads the field, so a
  future item may use it, and a reader who finds the filter apparently dead should not delete it.

**Confirmed on the production path rather than by eye, and the distinction is stated because the
picture is still owed.** `serveme-627619-stv-2026-08-07`, player 6: the viewmodel sample carries
item 200 with attribute **2053** (`is_festivized`) from tick 1, so the item and its attributes reach
the first-person prop and the delegate resolves `c_scattergun_festivizer.mdl`. What that does not
show is the frame. See the instrument gap below, which is why.

### 1b. WITHDRAWN — this finding was wrong, and the way it was wrong is the point

**It claimed `--first-person` does not exist and is silently swallowed. It exists.**
`LaunchOptions.cs:145` parses it and sets `FirstPerson`, with a comment explaining why it was added:
*"The capture a person actually wants to look at is the first-person one, and until this flag existed
the only route to it was the UI suite pressing V."*

**The mistake was the search, not the reasoning.** The grep ran over
`managed/Tf2DemoSalvage.Viewer3D/*.cs`, found `--autoplay` and nothing else, and the absence was
believed. Launch options live in `Presentation`, one project along. That is
`docs/memory/an-empty-search-needs-a-control.md` exactly — an absence claim with no control, where
asking the same grep for something that MUST be there (`--first-person` itself, from the shell
history that had already used it) would have shown the scope was wrong.

**Worse, the wrong conclusion was load-bearing for a measurement.** Believing the flag was inert is
why B253 and B254 were both measured in third-person free-camera mode and reported as though they
described the viewer's ordinary state. For a POV demo that is not the ordinary state at all — see
B256 — so those numbers describe a view TF2 never shows.

**What survives of the finding**, and it is smaller: the viewmodel PASS still needs
`info.Followed`, which `--first-person` alone does not set on a point-of-view recording, because
there is no spectated entity to follow. So a headless first-person capture reaches the camera and
still logs `viewmodel pass skipped … camera False`. That is B256's territory rather than a missing
launch option: on a POV demo the followed entity is the recorder, and nothing tells the viewer so.

### 2. Where to read next, by concentration of branches

From the ranked output, ignoring the shader helpers (a separate job):

| branches | function | why it matters here |
|---|---|---|
| 40 | `C_TFRagdoll::CreateTFRagdoll` | death is a separate entity; we already know that half |
| 35 | `C_BaseEntity::ComputeFxBlend` | transcribed for B221 — every fade, pulse and cloak |
| 29 | `C_BaseAnimating::SetupBones` | six citations, and the bone pipeline is load-bearing |
| 29 | `C_BaseAnimating::DoAnimationEvents` | not implemented at all; muzzle flashes and sounds |
| 25 | `CClientLeafSystem::CollateRenderablesInLeaf` | what draws and in which list |
| 19 | `CEconEntity::UpdateAttachmentModels` | finding 1 above |

### 3. `C_BaseAnimating::DoAnimationEvents` is not implemented, and the MDL event array is never read — CLOSED by B275

**Closed 2026-09-03, and the finding below is kept verbatim because it was correct when written.**
Both measurements have since been answered: `mstudioevent_t` is parsed by
`Content/Assets/StudioEvent.cs`, the firing rule lives in `AnimationEventFiring.cs` with the
backwards-jump and old-system branches, `m_nResetEventsParity` is carried onto the pose by
`DemoTimeline` and read by the firing rule, and `EntityModels` calls it from the pose build at the
point the cycle is advanced — which B275 records as the part that mattered, since a player's cycle
is never on the wire and a probe reading `m_flCycle` reported zero events on a demo full of them.

**This entry sat marked OPEN for eight days after it was fixed**, and it was found by reading the
document rather than by anything that checks. Two conformance entries in
`BoneSetupConformanceTests` were stale the same way on the same day — `GetPoseParameters` filed
Partial for a gap B269 closed, and `AccumulateLayers` for one B285 closed. **A stale OPEN is worse
than a stale number**: it sends the next session to re-implement something that is already there,
and the re-implementation looks like progress the whole time.

Next down the branch-count list, and it is the `attached_models` shape again: a whole mechanism
absent rather than a branch missed. Two measurements, both from this repository:

- **`mstudioevent_t` is never parsed.** `numevents` and `eventindex` appear in `managed/` exactly
  once between them, inside a comment in `StudioLayout.cs` that names the eight preceding ints to
  justify `SequenceBoundsMinOffset = 32`. The events array they point at is not read anywhere.
- **`m_nResetEventsParity` is decoded and has ZERO consumers.** `EntityState.ViewmodelResetEventsParity`
  has exactly one reference in the whole repository: its own declaration. That is
  `docs/memory/decoding-a-field-is-not-honouring-it.md` exactly, and the same shape as
  `m_flPlaybackRate`, which was decoded, retained, unit-tested, and read by nothing while every
  animation played at rate 1.

**The mechanism.** Events live in the MODEL, not the demo: each `mstudioseqdesc_t` carries
`numevents` and `pevent[i].cycle`, and the client fires an event when the play cycle crosses that
value. Only client-side events fire here — `AE_TYPE_CLIENT` under the new system, or `event >= 5000`
under the old one, which is the `//Adrian - Support the old event system` branch. `resetEvents`
re-arms them for a replay, which is what the orphaned parity field above is for, and a backwards
cycle jump greater than 0.5 is treated as a loop so the tail events fire before the head ones.

**What TF2 actually hangs off it**, from `c_tf_player.cpp:9132` onward: `AE_WPN_HIDE`/`AE_WPN_UNHIDE`,
`AE_CL_BODYGROUP_SET_VALUE` and its `_CMODEL_WPN` forwarder, `AE_CL_PLAYSOUND`,
`AE_WPN_PLAYWPNSOUND`, `AE_CL_EXCLUDE_PLAYER_SOUND`, `AE_TAUNT_ENABLE_MOVE`/`DISABLE_MOVE`, and the
cigarette and head throws that `DispatchEffect` as particles.

**Before implementing it, settle one question — and Valve raises it themselves.** Directly above
`AE_WPN_HIDE` sits their own comment saying `SetWeaponVisible` "shouldn't even be callable on the
client", because "on the client it just overrides whatever it was networked --- only until the next
time it is networked". Both weapon visibility and `m_nBody` ARE networked, so on the surface every
one of these client events is a transient override this project would decode correctly anyway.

**That reading is probably wrong, and the reason is delta compression.** A networked property is
re-sent when it CHANGES, not every tick. So "until the next time it is networked" can mean *never*
for an entity whose `m_nBody` the server does not touch again — the client-side value persists for
the rest of the round. Under that reading `AE_CL_BODYGROUP_SET_VALUE` has durable visible effect and
belongs implemented.

Marked as an INTERPOLATION rather than a measurement: it follows from how Source deltas work, and
it has not been checked against a demo where an animation event sets a bodygroup the server never
re-sends. That check is the first thing to do here, and it needs the event array parsed — which is
step one of implementing the feature regardless, so nothing is wasted either way.

**READ AND SETTLED, 2026-09-01. Three things, and one of them corrects this entry.**

**(a) The demo question is answered: events fire during playback, unconditionally.** This was the
"first thing to do here" above, filed as an interpolation. It is now a reading of the source. No
branch in `C_BaseAnimating::DoAnimationEvents` (`c_baseanimating.cpp:3550`), in `FireEvent`
(`:3889`), in `C_BaseAnimatingOverlay::DoAnimationEvents` (`c_baseanimatingoverlay.cpp:428`), or
anywhere in the call chain that reaches them — `FrameStageNotify(FRAME_RENDER_START)` →
`OnRenderStart()` → `SimulateEntities()` → every entity's `Simulate()` → `DoAnimationEvents` — tests
`engine->IsPlayingDemo()`, prediction state, or dedicated-server status. The single guard on the
call is `if ( gpGlobals->frametime != 0.0f )` (`c_baseanimating.cpp:5162`), which is nonzero during
demo playback exactly as it is live. `IsPlayingDemo` DOES appear in `c_baseanimating.cpp` three
times — all inside `#if defined( REPLAY_ENABLED )` ragdoll bookkeeping, none in this path.

**(b) This entry stated the array's home wrongly, and the correction matters for the parser.** The
events do not live on `mstudioanimdesc_t` — that struct (`studio.h:723`) has no event members at
all. `numevents`, `eventindex` and `pEvent(i)` are on **`mstudioseqdesc_t`** (`studio.h:817`), which
is what `DoAnimationEvents` reads (`seqdesc.numevents`, `GetEventIndexForSequence( seqdesc )`).
Anyone implementing this from the paragraph above would have gone looking in the wrong structure.

**(c) The parity field is 3 bits, and the client writes it too.** `m_nResetEventsParity` is sent as
`SendPropInt( SENDINFO( m_nResetEventsParity ), EF_PARITY_BITS, SPROP_UNSIGNED )`
(`baseanimating.cpp:254`) with `EF_PARITY_BITS` 3 (`const.h:301`) — so a 3-bit unsigned wrap, not an
int. And `C_BaseAnimating::ResetSequenceInfo` increments it CLIENT-side
(`c_baseanimating.cpp:5575`), so it is not purely a received value. The reset test is
`m_nResetEventsParity != m_nPrevResetEventsParity` OR `m_nEventSequence != GetSequence()`
(`:3618-3621`); either sets `flEventCycle = 0` and `m_flPrevEventCycle = -0.01` to catch the
zeroth-frame events. `NotifyShouldTransmit(SHOULDTRANSMIT_START)` re-baselines both (`:4676`) so an
entity re-entering the PVS does not replay its whole sequence.

**Name resolution is a load-time step with a cache flag**, worth knowing before parsing: only
`AE_TYPE_NEWEVENTSYSTEM` events carry a name, and `SetEventIndexForSequence`
(`shared/animation.cpp:60`) resolves `pszEventName()` through `EventList_IndexForName`, writes the
resolved id back into `pevent->event`, ORs the registry's type flags into `pevent->type`, and marks
the sequence `STUDIO_EVENT` (`studio.h:3091`) so it happens once. The registry itself is filled by
`EventList_RegisterSharedEvents()` at world init (`c_world.cpp:73`).

**MEASURED, 2026-09-01, once the array could be read: the VISIBLE half has no content to act on.**
The event array is now parsed (`StudioEvent`), so the question stopped being an argument about the
engine and became a count over the game's own files. Client-side events in twelve real TF2 models —
the scout, soldier, demo, engineer, spy and heavy animation sets, two weapons, and the sentry,
dispenser and teleporter:

| id | what it is | count |
|---|---|---|
| `5004` | `CL_EVENT_SOUND` — emit a sound | 206 |
| `7001` | the footstep branch of `C_TFPlayer::FireEvent` | 150 |
| `7000` | handled by nothing — falls to `default: break` | 1 |
| `6002` | one shell eject, in a viewmodel | 1 |

**Not one `AE_CL_BODYGROUP_SET_VALUE`, `AE_WPN_HIDE` or `AE_WPN_UNHIDE` in any of them.** So the
worry above — that a client bodygroup event has durable visible effect because delta compression
means the networked value may never be re-sent — is real reasoning about a case that does not
arise: the HANDLER exists in `C_TFPlayer::FireEvent`, and the CONTENT does not use it. That gap
between "the engine can do this" and "anything asks it to" is the thing a branch-count ranking
cannot see, and it is why the audit is supposed to start from what is on screen.

**Which leaves sounds, and those already have a decision.** Every remaining client event is
`CL_EVENT_SOUND` or the footstep, and `docs/memory/sound-the-demo-does-not-carry.md` records that
client-predicted sound is authoring rather than decoding — footsteps by name. So implementing
`DoAnimationEvents`' firing logic faithfully would reproduce exactly the class of thing this
project has already decided not to reproduce.

**What was built anyway, and why it was still worth it:** the array itself, with its conformance
test. It is the denominator — this table could not have been produced without it — and any future
question about events starts from a parser that exists rather than from a guess. The firing logic
(the cycle bookkeeping, `m_nResetEventsParity`, `m_flPrevEventCycle`, the looped-tail pass) is
**not built**, deliberately, and this is the record of why.

**The sample is twelve models, not the whole game.** Taunt animations and workshop cosmetics were
not checked, and a bodygroup event may well exist somewhere in them; if one turns up, the parser is
already in place and this entry is the thing to revisit.

**The sound events are a different question with a decision already attached.** `AE_CL_PLAYSOUND`
and `AE_WPN_PLAYWPNSOUND` are generated by the client from the model, not carried by the demo —
the same family as footsteps, where `docs/memory/sound-the-demo-does-not-carry.md` records that
reproducing them is authoring rather than decoding. Whatever is decided for one should be decided
for both, and separately from the visual half.

### 4. Every corpse is missing, and it is an APPEARANCE gap rather than the physics one B58 filed — FIXED (B315)

Top of the branch-count list at 40 branches, and the measurement is blunt:
**`serveme-627619-stv-2026-08-07` contains 159 `CTFRagdoll` entities. We decoded all of them and
drew none.**

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- corpses serveme-627619-stv-2026-08-07
```

**That number was recorded here as 299 and could not be reproduced.** The command above did not
exist when it was written, and nothing survives to say how it was counted — which is exactly what
`docs/memory/a-measurement-recorded-as-a-conclusion-expires.md` is about. 159 is the count of
distinct corpses, keyed by entity index AND serial; the first attempt at the probe keyed on index
alone and said **87**, because slots are reused briskly and every reuse collapsed into its
predecessor (B92's lesson, met again). Whether 299 counted per-tick observations or something else
is not knowable now. Measured, and the command is beside it so the next reader can check.

**B58 already covers this and covers the wrong half.** It reads `DT_TFRagdoll`, lists the fields,
and concludes correctly that the physics START CONDITION is fully networked — origin, force, force
bone, on-ground, plus every death variant. What it does not say is that **nothing about how the
corpse LOOKS is networked at all**, and that is the reason nothing draws:

```cpp
IMPLEMENT_CLIENTCLASS_DT_NOBASE( C_TFRagdoll, DT_TFRagdoll, CTFRagdoll )
```

`NOBASE`. The table inherits nothing, so there is no `m_nModelIndex`, no `m_nSkin`, no `m_nBody`,
no `m_vecOrigin` and no `m_angRotation` — the same shape as `CBaseViewModel`, and for a viewer the
same consequence: a generic prop path asks for a model index, gets none, and correctly draws
nothing. The corpses are not lost in the decode. They were never described.

`CreateTFRagdoll` is where the client builds every one of those fields, which is why the function is
40 branches long:

| what | derived from |
|---|---|
| model | the class model for `m_iClass` — or the PLAYER's current model when the player entity is still around and is not a spy being drawn as their disguise |
| skin | `m_iTeam == TF_TEAM_RED ? 0 : 1`, adjusted again by `AdjustSkinIndexForZombie` |
| body | copied off the player, after `RecalcBodygroupsIfDirty`, unless `m_bFeignDeath` without `m_bWasDisguised` |
| origin | `m_vecRagdollOrigin`; angles from the player's render angles |
| cosmetics | `CreateBoneAttachmentsFromWearables` — and `m_hRagWearables` IS networked, eight ehandles, so this half is reproducible |
| head/torso/hand scale | read off the player, not off the wire, despite `m_flHeadScale` etc. being sent |

**One branch cannot be reproduced, and it is worth knowing before anyone tries.** Whether a death
plays a death ANIMATION or falls as physics is decided by an unnetworked client coin flip:

```cpp
if ( !m_bIceRagdoll && !tf_always_deathanim.GetBool() && (RandomFloat( 0, 1 ) > 0.25f) )
    iDeathSeq = -1;
```

Three quarters of eligible deaths discard the animation, by a `RandomFloat` on the recording
client's own stream, recorded nowhere. So a replay cannot know which of the two a given corpse
showed — it is a client-generated decision, the same class as
`docs/memory/sound-the-demo-does-not-carry.md`, not a decode gap.

**This paragraph used to say the choice was "a divergence to be ASKED about rather than chosen
quietly", and that was wrong** (D136). The owner, put the question: *"you should of done it valves
way."* **Valve's way is the branch itself** — the engine draws a random number, so we draw one, and
a 25/75 split is not an approximation of the engine but a reproduction of it. An unrecoverable INPUT
does not make the LOGIC a choice; `a-divergence-is-asked-not-documented.md` covers deliberately doing
something else, and this is not that.

**The one thing genuinely forced on us is the SEED**, and it is a consequence of a capability the
client lacks: this project can seek, so the draw must be keyed to the corpse rather than taken from
a running stream, or scrubbing backwards would show a different death each time.

**And `m_bGib` is not part of this.** It is networked — `RecvPropBool( RECVINFO( m_bGib ) )`
(`c_tf_player.cpp:524`) — so gibbing is recorded in the demo and read rather than guessed. The draw
sets `iDeathSeq = -1` (`:831`), which clears `bPlayDeathAnim` (`:836`); the split is death animation
against plain ragdoll physics and nothing else.

**Priority argument, from the owner's own scoping.** B59 records that ragdolls are wanted "for frag
vid makers" and that deaths are much of what a frag video shows. 159 in one match, every one
decoded and invisible, was the largest single visible gap this audit has measured.

#### What was done — B315

**The appearance half is closed.** A corpse now derives its model from `m_iClass` and its skin from
`m_iTeam`, exactly as `CreateTFRagdoll` does, and reaches the scene as an ordinary `SceneProp`:

- `RagdollAppearance.Of` — the derivation, with `c_tf_player.cpp:681-720` quoted beside it.
- `RagdollProps.Fill` — corpses into the prop buffer, appended after `PropsAt` rather than instead
  of it.
- `RagdollFade` — `C_TFRagdoll::ClientThink`'s expiry rule, without which the map fills with bodies.
- `TimelineMoments.ClassModels` — where the class table comes from, read per call because the
  archives and the demo open on independent schedules.

**The orientation is the same gap as the model, one field along.** `DT_TFRagdoll` carries no angles
either, so a corpse faces north — every body in a match pointing the same way. The client reaches
back through the networked `m_hPlayer` for `SetAbsAngles( pPlayer->GetRenderAngles() )`
(`c_tf_player.cpp:766`), and yaw only: a player's pitch lives in the head's pose parameters, so
carrying it tips the corpse over backwards for anyone who died looking up.

**A divergence was caught by the conformance test before it shipped, and it is the kind that hides.**
The obvious implementation reuses `PlayerSkin.ForTeam`, which is already this project's team-to-skin
rule. It is the wrong rule for a corpse. The two come from two different engine functions that agree
on RED and BLU and disagree on everything else:

```cpp
// C_TFPlayer::GetSkin, c_tf_player.cpp:7807-7817
case TF_TEAM_RED:  nSkin = 0; break;
case TF_TEAM_BLUE: nSkin = 1; break;
default:           nSkin = 0; break;

// C_TFRagdoll::CreateTFRagdoll, c_tf_player.cpp:712-719
if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
```

A player with no team falls to RED; a corpse with no team falls to BLU. `Skin_ForNoTeamAtAll_IsBlu`
failed against the reuse, which is the whole reason the test was written before the code.

**And reading the fade changed what the fade IS — then measurement showed the feature does not work
without it.** `cl_ragdoll_fade_time` defaults to 15 and a corpse does not last 15 seconds.
`C_TFRagdoll::ClientThink` calls `StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f )` and
RETURNS whenever `IsRagdollVisible()` (`c_tf_player.cpp:1532-1545`), so the timer is restarted on
every think a corpse is on screen. **A corpse being looked at never fades at all**; one that has left
view expires 4.95 seconds later.

The first implementation drew a corpse for as long as its ENTITY existed, which looked defensible and
was measured wrong: the server keeps one ragdoll per player and destroys it only at that player's
next death (`UTIL_Remove`, `tf_player.cpp:15602`), so bodies accumulate across a match — **36 alive a
quarter through, 43 at half, 57 at three quarters, against a twelve-player roster.** `RagdollFade` is
`ClientThink`'s rule and brings the same three samples to **4, 2 and 4 drawn**, which is what TF2
shows. Visibility comes from the previous frame's posed set, the same arrangement
`EntityModels.PosedEntities` already uses for the interpolation list and for the same engine reason.

**Three intermediate readings were wrong on the way there, and each was measured rather than argued:**

| window tried | what it gave | why it was wrong |
|---|---|---|
| creation → last update | every corpse drawn for ONE tick | 158 of 159 corpses receive exactly one update — everything `DT_TFRagdoll` sends is a fact about the moment of death |
| creation → entity delete | 57 at once | the server's ragdoll outlives the drawn corpse by design |
| … → also entity `Leave` | 61 at once | STV keeps corpses in its PVS, so leaves barely fire |

**Still open, and each for its own reason:** the physics (B58), `m_nBody` from the player, the
`RagdollSpawn` sequence lookup, the cosmetics through `m_hRagWearables`, and the gold/ice/zombie
overrides.

### 5. A prop with an EMPTY model path is reported DRAWN — ANSWERED: the probe, and a miscount

**Both readings were right about different halves, and neither was the renderer failing.**

**Why it said DRAWN — the probe.** `DrawnPropProbe` built its `kept` set from the ShouldDraw
visibility rules alone (`DisguiseVisibility`, `RespawnRoomVisibility`) and never consulted whether
the prop names anything drawable, so "DRAWN" meant "survived visibility". A prop with no model
passes that trivially. Fixed by extracting production's own predicate — `EntityModelSet.CanDraw`,
adopted at both production call sites, not a copy — and giving the probe a third state. It now
reads `NOMODEL`.

**What the renderer actually did — nothing, but it counted it wrong.** `Instances` tested the model
KIND only, so an empty-path Studio prop fell through to the `no batches` count, which is the label
for a model that failed to LOAD. `DrawTally.NotDrawable` already carried a `<no model>` case that
nothing could reach. Now it can. **The pose was never paid** — the geometry test sits before the
bone work, which is the engine's own order, so this was a miscount rather than wasted work.

**The residue is a real content gap, and it is B263**: measured on `z1800` at tick 20000, **24
bone-merged cosmetics name no model at all** — 23 `CTFWearable` and one `CTFWearableRobotArm` —
while most wearables at the same tick resolve their paths fine. So the item-schema/dynamic-model
resolution works and does not cover these.

### 5a. The original filing, kept

Noticed in the control run for finding 4 and recorded rather than chased, because it was not what
that run was asking:

```
DRAWN 1 '' kind Studio class 'CTFWearable' entities [744] ... attached 6 merged True mode 0
```

An empty model path drawn as a Studio model. It may be the dynamic-model case
(`docs/memory/negative-model-indices-are-dynamic.md`, where cosmetics come from the `DynamicModels`
table rather than `modelprecache`) surfacing as an unresolved path, and it may be the probe
reporting rather than the renderer doing. **Both readings are guesses.** Filed with its evidence so
the next person starts from the observation instead of rediscovering it.

## The rule this audit exists to enforce

**A rule written in our own comments is not an enforced rule.** `WorldRenderer` said *"a SKINNED
model is put there by its bones and its matrix stays at identity"* and nothing checked it; it held
by accident for months and then cost an evening. When an audit finds an invariant stated in prose,
the finding is not "it is documented" — it is "write the assertion or delete the claim".
