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

### 2. Where to read next, by concentration of branches — SUPERSEDED, and the table below is why

**Every function in this table has now been read, and the ranking was the wrong axis the whole
time.** The skill this audit runs under says so in its own words: a function implemented well and one
implemented badly have the same branch count, so this ranks by how much work a full implementation
would be rather than by what is wrong on screen. Its top entry was 299 undrawn ragdolls — a feature
the owner plays with switched off, and a number that turned out to be 159.

**What replaced it, and it has produced better findings.** Pick a subject that is drawn or on the
wire right now, then ask what quantity decides whether anybody can see the gap:

- **B317** came from a probe line reading `proctype QUATINTERP 4 of 540`. Four bones out of 540 is
  invisible by branch count and by proportion. The quantity that mattered was whether any vertex is
  weighted to them — all four are — and it is a forearm that does not twist on every player in every
  demo.
- **B316** came the other way: `GetSequenceForDeath` looks worth implementing until you count how
  many corpses can reach it, which is about one in a hundred.

The table is kept because the reading it directed was worth doing, not because the ordering was.

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
| cosmetics | `CreateBoneAttachmentsFromWearables`, off the PLAYER's wearable list — **not** off `m_hRagWearables`, see below |
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

**"Eligible" was carrying almost all the weight in that sentence and this entry did not say so.**
`GetSequenceForDeath` is a `switch` on `m_iDamageCustom` with two cases and no default
(`tf_player_shared.cpp:13441-13455`): headshots, decapitations and backstabs get one of TF2's two
death animations, and **every other death returns -1** and goes straight to physics. Measured:

| demo | corpses | eligible |
|---|---|---|
| `serveme-627619-stv-2026-08-07` (comp 6v6) | 159 | **0** |
| `20120707-0042-koth_idioteque_a3` | 457 | 22 |
| `20140607_2350_koth_pro_viaduct_rc4` | 147 | 5 |

A quarter of those keep the animation, so **about one corpse in a hundred plays one**. The comp match
scores zero because a 6v6 fields no sniper and no spy — and the control that the field decodes at all
is the spread of values there: `TF_DMG_CUSTOM_NONE`, `STANDARD_STICKY`, `ROCKET_DIRECTHIT`,
`AIR_STICKY_BURST`. **So the corpse pose is a physics question, essentially entirely.**

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

**`m_hRagWearables` is networked and is NOT how a corpse gets its hats.** This entry previously said
it was, which is the third wrong-mechanism claim in this one function. Valve's own declaration doubts
it:

```cpp
CUtlVector<CHandle<CEconWearable > > m_hRagWearables;		// These look like they are no longer used?
```

`c_tf_player.h:1132`. The client touches it in exactly one place — `EndFadeOut`, to
`AddEffects( EF_NODRAW )` and `SetMoveType( MOVETYPE_NONE )` them (`c_tf_player.cpp:1652-1660`) —
and never draws from it; the server only `Remove()`s them (`tf_player.cpp:401-408`). It is a
networked field whose whole client life is being hidden.

The real path is `CreateBoneAttachmentsFromWearables( pRagdoll, m_bWasDisguised )`
(`c_tf_player.cpp:10169-10251`), which walks the **PLAYER's** wearable list, skips viewmodel
wearables, disguise mismatches and `EF_NODRAW` items, builds a `C_EconWearableGib` per survivor from
`pItem->GetModel()`, takes `m_nSkin` off the item and the team off the ragdoll, and then
`MoveBoneAttachments` moves them across. **That list this project already has** — the player's
wearables are ordinary bone-merged entities in the demo — so the cosmetics are reachable, just not
through the field that looked like it was for them.

**Still open, and each for its own reason:** the physics (B58) — **which the measurement above makes
the only thing that matters for the pose** — `m_nBody` from the player, the cosmetics off the
player's wearable list, and the gold/ice/zombie overrides. B316 is filed but is now known to be worth
little on its own.

**B316 is worth reading before touching the pose.** A corpse currently stands upright, and the line
everyone finds is `LookupSequence( "RagdollSpawn" )` — which is the `else` of
`if ( !pPlayer->IsLocalPlayer() && ... )`. A SourceTV recording has no local player, so every corpse
in one takes the OTHER branch and copies `pPlayer->GetSequence()`. Implementing `RagdollSpawn` would
be a cited, plausible fix to the case this project's reference demo never contains.

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

### 6. A procedural bone rule on every player model, decoded and named and run by nothing — FIXED (B317)

**The first finding produced by ranking on what is DRAWN rather than on branch count**, and it would
never have surfaced the old way: `DoQuatInterpBone` is a small function on a small number of bones.

```
proctype QUATINTERP  4 of 540   demo.mdl:hlp_forearm_L SKINNED, demo.mdl:hlp_forearm_R SKINNED,
                                scout.mdl:hlp_forearm_L SKINNED, scout.mdl:hlp_forearm_R SKINNED
```

**Four bones in 540, and the proportion is not the number to look at.** What decides whether an
unimplemented rule is visible is whether any vertex is weighted to the bone it drives — a procedural
bone nothing is skinned to computes a transform that reaches no mesh. All four report `SKINNED`; the
probe was taught to say so for exactly this question, so this was a forearm that did not twist with
the wrist.

**"On every class model" is what this entry first said, and counting the game disproved it.** Over
all 14,109 shipped models the rule appears on **three classes — scout, heavy and demoman** (each as
two files, the ordinary and the HWM one) plus `hlp_patella_L`/`_R`, knee helpers, on the
Mann-vs-Machine bot engineer. Seven models, fourteen bones. The original claim came from a single
tick that happened to contain a demoman and a scout, which is the smallest sample that could produce
it — a reminder that "every X" from one observation is a guess wearing a quantifier.

**It was the `decoding-a-field-is-not-honouring-it` shape.** `StudioProcedureType` declared all five
rules with citations, `StudioBone.ProcedureType` read the field, and a repository-wide grep for a
consumer of `QuaternionInterpolate` outside its own declaration returned nothing. The type's remarks
said *"this project implements none of them yet"*, which had gone stale in both directions — jiggle
bones had been implemented since, and nobody revisited the sentence.

**Two engine details that a plausible implementation gets wrong**, both now pinned by tests:

- The control is read **relative to its parent**, not in world space. Reading the world matrix is
  right whenever the parent happens to be unrotated — every simple fixture and no real skeleton.
- The rule **replaces** the animated transform. `CalcProceduralBone` returns true and the engine's
  loop `continue`s, so a procedural bone's keyframed rotation never reaches the skeleton. Every other
  pass in this pipeline is additive, which is what makes the mistake easy.

**Measured wired, not merely built:** `DRIVEN 10 bones posed by a quaternion-interpolation rule,
furthest move 0.72 units` on a real demo, carried from where the work happened rather than counted
from what the models declare. On a point one unit out that is 2·sin(θ/2) — **a 42-degree twist**,
which is the magnitude a wrist roll should spread down a forearm.

**That magnitude read ZERO on its first run, and the instrument was at fault rather than the rule.**
It measured translation alone, and a twist rotates about a fixed origin — so it was measuring the one
quantity that cannot change, and reporting "the rule does nothing" about correct code. The instrument
had been written that same hour precisely to separate "it ran" from "it mattered", which is how
easily the unfaithful-proxy fault survives being looked for.

## The rule this audit exists to enforce

**A rule written in our own comments is not an enforced rule.** `WorldRenderer` said *"a SKINNED
model is put there by its bones and its matrix stays at identity"* and nothing checked it; it held
by accident for months and then cost an evening. When an audit finds an invariant stated in prose,
the finding is not "it is documented" — it is "write the assertion or delete the claim".

## The pose pipeline, audited 2026-09-05 — one divergence in six subjects

Read in full: `CalcPoseSingle`, `StandardBlendingRules`, `AddSequenceLayers`,
`CalcAutoplaySequences`, `MaintainSequenceTransitions`, `CalcBoneAdj`, with every override of every
virtual each calls. The finding is one divergence and five confirmations, and both halves are worth
recording — a subject that comes back clean is a subject nobody has to read again.

**What matched, with what was checked:**

| subject | ours | why it is the same |
|---|---|---|
| 2D blend grid | `StudioBlendGrid.ThreeWay` | Valve's own triangle decomposition — the quad split by the parity of `(i0+j0)`, three barycentric corners. `panim[4]` is sized four; a 2D grid fills three |
| the 3-way path being live at all | — | `anim_3wayblend` defaults `"1"` and is `FCVAR_REPLICATED` (`bone_setup.cpp:1838`), so the four-corner bilinear branch is the dead one |
| pose blending | `StudioPoseBlend.Blend` | `QuaternionBlend`/`BlendBones` exactly — align chosen per bone by `BONE_FIXED_ALIGNMENT`, linear lerp, normalize |
| the order of the whole pass | `EntityModels`, `SkeletonPose` | local layers → sequence layers → transitions → overlays → autoplay → `CalcBoneAdj`, which is `StandardBlendingRules`' order (`c_baseanimating.cpp:1985-2003`) |
| sequence transitions | `TransitionsFor` | queued fade-outs, which is how the engine runs them — one `AccumulatePose` per queued entry |

**Three absences that are NOT divergences, and the instrument that settled them.** `sequence-flags`
over 14,109 shipped models:

- **`PoseIsAllZeros` / `ScaleBones`** — 810 animations carry `STUDIO_ALLZEROS` and **every one is
  reached by a delta**, zero by an ordinary sequence. Expanding a delta all-zeros corner to identity
  and blending gives exactly what `ScaleBones` gives, so the absence is arithmetic-equivalent.
- **The `bResult` residue** — `CalcPoseSingle` returning false also skips `AddLocalLayers`, which
  would be a real loss. It needs a `STUDIO_LOCAL` sequence with an all-zeros corner: **0 of 26,387**.
- **`STUDIO_CYCLEPOSE`** — **0 of 26,387**. No shipped TF2 model takes its cycle from a pose
  parameter.

**The divergence: `bInterpolate`, filed as B346 and fixed.** `CheckForSequenceChange` clears the
transition queue on `(seqdesc.flags & STUDIO_SNAP) || !bInterpolate`, and only the first half ran —
with the whole line quoted in our own comment directly above the code that implemented half of it.
That is this document's own rule biting: **a rule written in our own comments is not an enforced
rule**, and quoting a two-clause guard while running one clause is the same fault as documenting an
invariant nothing checks.

**Method note worth keeping: a stale instrument is a finding too.** `sequence-flags` still printed
`AddSequenceLocks/SolveSequenceLocks, not implemented` a day after B311 implemented them and measured
88 applied on the pose path. A census that keeps reporting a gap after it closes is how a fixed thing
gets re-filed, and it is the same class as the counters this document warns about elsewhere.

## The weapon overrides of `StandardBlendingRules`, audited 2026-09-05

**The base is not the behaviour, and this is the clearest case of it in the tree.** Reading
`C_BaseAnimating::StandardBlendingRules` to the closing brace tells you nothing about the minigun,
because everything the minigun does happens in an override that runs *after* `BaseClass::` returns.
Seven live overrides exist; three of them are TF2 weapons and two of those exist for one purpose —
turning a barrel bone the animation does not turn.

| override | file:line | what it adds |
|---|---|---|
| `CTFMinigun` | `tf_weapon_minigun.cpp:1068` | spins `barrel` from `m_iWeaponState` — **was missing, fixed as B347** |
| `CTFGrenadeLauncher` | `tf_weapon_grenadelauncher.cpp:610` | rotates `procedural_chamber` on a spline — **filed, not built** |
| `C_TFViewModel` | `tf_viewmodel.cpp:313` | the same barrel, by name, for the viewmodel |
| `C_BaseFlex` | `c_baseflex.cpp:227` | entirely inside `#ifdef HL2_CLIENT_DLL` — nothing for TF2 |
| `CEconEntity` | `econ_entity.cpp:890` | delegates to `ViewModelAttachmentBlending`, `{}` for all but those two |
| `C_Barnacle` | `c_barnacle.cpp:203` | HL2 |
| `C_AI_BaseHumanoid` | `c_ai_basehumanoid.cpp:77` | **the whole file is `#if 0`** — does not compile into the game |

**Two of those rows are absences worth having measured**, because both look like features until read:
`C_BaseFlex`'s body is HL2-only, and `C_AI_BaseHumanoid`'s override does not exist in a built game at
all. Implementing either would be implementing nothing.

**`ChildLayerBlend` is dead the same way**, and it is called unconditionally from
`StandardBlendingRules` (`c_baseanimating.cpp:2005`): its body opens with a bare `return;`
(`:1909`), so the bone-merge loop beneath is unreachable. A reader who quoted the call site and not
the body would build a whole child-merge pass that TF2 never runs.

**Method note: the instrument had to be extended before the question could be asked.** "Does this
model have a bone called `barrel`" is a question about a model chosen by NAME, and every bone census
here walked a demo's drawn props — where a weapon is not among the 14 skinned models at a tick. The
`model` probe now lists bone names, which answered it in one call: `c_minigun.mdl` carries
`weapon_bone, barrel, c_weapon_stattrack`.

### The family closed, 2026-09-05: four live barrel paths, one dead

B347 and B348 between them implement the two world-model paths. The full table, measured rather than
inferred, is in `docs/findings/09`. What the audit established beyond the two fixes:

- **The axis is `z` on every LIVE path.** The `a.x` in `CTFViewModel::StandardBlendingRules`
  (`tf_viewmodel.cpp:333`) belongs to the v_model era: it poses the viewmodel ENTITY, which every
  demo checked resolves to `c_*_arms.mdl`, and those carry no `v_minigun_barrel`. The model that
  does — `v_minigun_heavy.mdl`, bone 2 of 18 — is still shipped and no longer reached.
- **That explains the commented-out block in the world file**, which B347 flagged as a trap without
  knowing why. It is a paste from when the axis was `x`, and its guarding comment ("Weapon happens
  to be aligned to (0,0,0) / If that changes, use this code block instead") invites exactly the
  reader who would inherit it.
- **Two write styles, split by which model is posed.** World paths assign the whole quaternion and
  check no bone mask; viewmodel-attachment paths read the existing angles, replace one component,
  and are wrapped in `if ( hdr->boneFlags( iBarrelBone ) & boneMask )`. They agree only while the
  animation leaves the other two components at zero on that bone.

**The two viewmodel-attachment paths are a MEASURED non-divergence** (`tf_weapon_minigun.cpp:1343`,
`tf_weapon_grenadelauncher.cpp:683`), filed as B349. They read the bone's existing angles and
replace one component where the world paths assign outright — but both barrel bones have identity
bind rotations and **no animation in either model tracks them**: every animation moves exactly one
bone, `weapon_bone`. So `q[bone]` is identity when the override runs, and read-modify-write on
identity yields the same pure-Z quaternion the flat assign produces. Arithmetically equal, not
merely similar.

**So the family is closed.** Four live paths, two implemented and two proved equivalent to them, one
dead. The measurement that settled it is now a probe rather than a one-off: `model <path>` reports
bind rotations and per-animation tracked bones, so the same question can be asked of any weapon in
one call.

### `StandardBlendingRules` is now accounted for end to end

The pose audit above checked seven of its eight steps. The eighth, `UnragdollBlend`
(`c_baseanimating.cpp:1873`), is **unreachable**, and both of its arming routes fail independently:

- **`m_bStoreRagdollInfo` is never set true anywhere in the SDK.** It is initialised to `false`
  (`:703`) and read at `:1788` and `:4926`; there is no assignment of `true` in the tree. So the
  `SaveRagdollInfo` call inside `BecomeRagdollOnClient` cannot fire.
- **`m_hUnragdoll` is never sent.** `C_ServerRagdoll::UpdateOnRemove` arms the blend only when that
  handle points at an animating entity (`ragdoll.cpp:657`), and it is set by `CRagdollProp::
  SetUnragdoll` — an HL2 physics-prop path. `DT_Ragdoll.m_hUnragdoll` IS in a TF2 demo's schema, and
  across 60,000 expanded snapshots of `tf2-2026-pub-pov-cheater` it is sent **zero times**. Control:
  `m_hOwnerEntity`, an ordinary handle, appears 11,921 times in the same dump.

So the blend from a ragdoll back into animation has nothing to blend from. Implementing it would be
implementing a path TF2 does not take — the same answer as `ChildLayerBlend` and the three dead
overrides, reached by measurement rather than by reading alone.

**Evidence class: read-from-source** for the `m_bStoreRagdollInfo` half, **measured** for the
networked half. **What is NOT established:** this is one demo. A map that spawned a `prop_ragdoll`
with an unragdoll target would reach it, and nothing here rules that out for TF2 content generally —
only for the recording checked.

| step | state |
|---|---|
| `InitPose` → `AccumulatePose` | implemented, order verified |
| `MaintainSequenceTransitions` | implemented; its `bInterpolate` half was B346 |
| `AccumulateLayers` | implemented |
| `CalcAutoplaySequences` | implemented, and its position in the order verified |
| `CalcBoneAdj` | implemented |
| `ChildLayerBlend` | **dead** — body opens with a bare `return;` |
| `UnragdollBlend` | **unreachable** — neither arming route fires |
| the weapon overrides that run after it | B347, B348 implemented; B349 proved equivalent |

## The animation state, audited 2026-09-05 by DENOMINATOR rather than by reading

A different method from the pose audit above, and it found more per hour: list every method of
`CMultiPlayerAnimState` (52 of them), diff against what this repository cites, and run down the ones
with **no citation at all**. Five had none.

| function | outcome |
|---|---|
| `CalcMovementPlaybackRate` | **dead** — zero call sites in TF2's hierarchy; the class is `DECLARE_CLASS_NOBASE` so the base's four calls belong to a tree TF2 never instantiates |
| `GetInterpolatedGroundSpeed` | **dead** — its only non-debug use is commented out inside the function above |
| `ComputeFireSequence` | **empty body, no caller** |
| `ShouldUpdateAnimState` | its general conditions we already honour; its TF2-specific one (a custom player model that opts out of class animations) is **unreachable** — `m_iszCustomModel` arrives EMPTY in all five sends across two demos, one of them a Halloween map |
| `PlayFlinchGesture` | **a real divergence — B350**, and most flinches were affected |

**Four dead ends and one defect is a good ratio for the cost**, and the four are worth having written
down: each looked like a feature from its declaration, and one of them (`CalcMovementPlaybackRate`)
computes exactly the speed-matching a reader would expect a viewer to need.

**The method generalises.** A function this project has never cited is a function nobody has compared
against the engine — which is a sharper filter than branch count, and cheaper than reading a whole
subsystem. `parity <filter>` already ranks what we DO cite; the gap is what it cannot show.

### The same sweep over `CTFPlayerAnimState` — eight uncited, zero divergences

Repeating the denominator method on TF2's own subclass found nothing to fix, and the reasons are
worth keeping so the eight do not get re-read:

| function | why it is not a gap |
|---|---|
| `CheckStunAnimation` | a state machine whose OUTPUT is `PLAYERANIMEVENT_STUN_BEGIN/MIDDLE/END`, which reach a demo as `CTEPlayerAnimEvent` and which `PlayerGestureEvent.Map` already handles. Reproducing the machine would be reproducing the sender |
| `CheckPasstimeThrowAnimation` | same shape — its events (34–36) are mapped |
| `CheckCYOAPDAAnimtion` | same shape — its events (37–39) are mapped |
| `GetCurrentMaxGroundSpeed` | feeds `m_flMaxGroundSpeed`, whose ONLY read is `GetInterpolatedGroundSpeed` (`:1100`) — used in a commented-out line and a debug print. Plus one item-testing-bot branch |
| `Taunt_ComputePoseParam_MoveX` / `_MoveY` | driven by `pTFPlayer->m_nButtons`, which a demo carries for the recorder alone |
| `Vehicle_ComputePoseParam_MoveYaw`, `Vehicle_LeanAccel` | TF2 has no rideable vehicle in normal play |
| `IsItemTestingBot` | the item-testing mode only |

**The event enum was checked against the SDK while here**, since a numbering slip would silently map
the wrong gesture: ours matches member for member through `AttackPrimarySuper = 40`, and every
unmapped member already carries its reason — jump, swim, death, spawn and snap-yaw drive the MAIN
sequence rather than a layer, and `CustomGestureSequence` and `DoubleJumpCrouch` are commented out in
the SDK itself.

**So the flinch was the only defect in either animstate**, and it was in the RESOLUTION step rather
than the mapping — which is why a sweep of the event table would not have found it. Two sweeps, one
defect, and eleven dead ends now written down instead of waiting to be rediscovered.

### The uncited sweep is now a probe: `parity <filter> <class>`

Two hand sweeps found one defect (B350) and eleven dead ends; a third found B351. That is a good
enough ratio to stop doing it by hand. The probe lists every method of a named class that this
project has **never** cited.

**Getting the question right took three tries, and the wrong versions were not obviously wrong:**

- **By cited LINE alone** — 28 uncited for `CMultiPlayerAnimState` against 5 by hand. It reports
  `HandleJumping` as unstudied because the citation points at TF2's override in
  `tf_playeranimstate.cpp`; that is a fact about which file was quoted, not about whether the
  mechanism was compared.
- **By NAME alone** — misses a comment that cites `multiplayer_animstate.cpp:1443` without writing
  `SetupPoseParameters`.
- **The union of both**, over `managed/`, `tests/` AND `docs/`. The test tree matters because a
  conformance suite is where a mechanism's citation most often lives; the docs tree matters because
  four of the five functions the first sweep ran down were DEAD, so their answer exists only as
  prose. Leaving docs out would have offered them again on every run — the re-reading the probe
  exists to prevent.

It reproduces both hand sweeps: `CTFPlayerAnimState` now reports **0 uncited**, and
`CMultiPlayerAnimState` reports 8, of which 5 are `Debug*`/`ShowDebugInfo`.

**The three real leads it left** are `AddVCDSequenceToGestureSlot` — chased immediately, and it is
B351, taunts — plus `GetGestureSlotLayer` and `InitGestureSlots`, which are slot plumbing.

## The player's body number — a whole engine mechanism with no implementation (B352)

**Ranked by what is already on screen, which is the rule this document is written under.** Every
player in every modern demo wears cosmetics; every one of those cosmetics is drawn; and the body
number of the player underneath was hard-wired to the spy's mask or to zero. So this was not a
missing feature nobody would see — it was a wrong pixel on twelve players at once, every frame.

| | |
|---|---|
| **The engine** | `CTFPlayerShared::RecalculatePlayerBodygroups` (`tf_player_shared.cpp:13693`) clears `m_nBody` and rebuilds it from the equipped set in three passes; each item resolves its `player_bodygroups` names against the wearer and sets them (`econ_entity.cpp:2044`). |
| **Ours, before** | `PlayerProps.Add` set `Body` to the spy mask's contribution or to 0. Nothing else ever wrote it. |
| **Visible when wrong** | A hat sits on top of the hair it replaces; a headset draws through the one on the cosmetic; the demoman's grenades stay on his chest under the bandolier. |
| **Evidence class** | read-from-source for the passes, measured for the 747 items and the post-fix run |
| **Would falsify it** | a client that applied the item's bodygroups somewhere else — searched, and `RecalculatePlayerBodygroups` is the only writer besides `ValidateModelIndex`'s mask |

### How it was found: by asking what the SCHEMA declares that nothing reads

Not by a sweep of engine methods this time, and not by looking at output. `items_game.txt` carries a
block that no code in this repository mentioned. **A key the game ships and we never read is the
same lead as an engine method we never cite** — and it is cheaper to check, because the denominator
is a file rather than a class:

```bash
grep -c '"player_bodygroups"' items_game.txt      # 747
grep -rn "player_bodygroups" --include=*.cs .     # nothing
```

That pairing — a large number on the left and zero on the right — is the whole finding. It is worth
adding to the rotation alongside `parity <filter> <class>`.

### Three neighbours, decided rather than deferred

Reading the function to its closing brace turned up three more arms, and each got an answer instead
of a shrug:

- **`hide_bodygroups_deployed_only`** — implemented. It is what the three passes exist for, eight
  shipped items declare it, and all eight are weapons.
- **The style arm** (`GetAdditionalHideBodygroups`, `GetBodygroupName`) — **proved dead in a demo**
  rather than deferred. `GetStyleInfo` needs `GetSOCData`, which finds an inventory only for the
  subscribed account (`econ_item_view.cpp:839`); a demo has none, so a live client watching another
  player takes the same branch we do. 102 shipped items declare `additional_hidden_bodygroups` and
  none of them can reach it here. The exception is the networked `item style override` attribute,
  already B234.
- **`wm_bodygroup_override`** — a real divergence, filed as B353 with its two items named. It
  addresses a part by index rather than by name and needs a second resolver.

**A wrong key name nearly turned the third into a false absence.** `use_model_bodygroup_override`
was invented from the accessor's C++ name and returned zero matches; the schema spells it
`wm_bodygroup_override`, and the control that exposed the mistake was `player_bodygroups` returning
747. Same shape as the `strings`-binary failure two sections up: **an absence claim about shipped
data needs a control in the same file, keyed the same way.**

### The measurement, and the instrument that lied on its first run

`bodygroups <demo> <tick> [class]` reports what the production resolver returned, call by call —
never a second computation from the schema, which would agree with the schema and say nothing about
the scene. Its first run reported "no such part on this model" for all 24 requests, which reads as a
decisive negative and was a fact about the probe: `EntityModelSet.Geometry` answers nothing until a
map sets it. It now prints a control first and says outright that a zero there voids everything
below.

## The rest of `CEconEntity` — one divergence, one non-divergence, and a correction

Having read `UpdateBodygroups` to its closing brace for B352, the cheapest next question was the
denominator for its own class. Sixteen `CEconEntity` methods have never been cited here; seven have
no mention at all. Three were chased.

### `ShouldDraw` — a real divergence, 23 items (B354, fixed the same day)

The whole function is two lines and we implement the second one:

```cpp
bool CEconEntity::ShouldDraw()
{
    if ( ShouldHideForVisionFilterFlags() )
        return false;
    return BaseClass::ShouldDraw();
}
```

(`econ_entity.cpp:1800`.) An item declaring `vision_filter_flags` is hidden from a viewer who does
not have the matching vision. **23 shipped items declare it** — four Pyroland (the Pet Balloonicorn,
the Pet Reindoonicorn, the Infernal Orchestrina, the Burning Bongos) at `TF_VISION_FILTER_PYRO`, and
nineteen MvM robot skins at `TF_VISION_FILTER_ROME`. We draw all 23 to everybody.

**It was implementable, which is why it was filed rather than dismissed — and then built.** The
viewer's flags are the recorder's, and the source is reachable from a demo: `vision_opt_in_flags` is
an item attribute, B234 decodes attributes, and `ItemSchema.AttributeDefinitionIndex` resolves one by
name. `IMomentSource.Recorder` now carries the recorder into `MomentInfo` beside the round state.

**Two of the engine's three flag sources turned out not to matter, and saying WHY is the useful
part.** The Halloween arm sets a bit no shipped item requires, so it cannot change a drawing
decision; the Rome arm needs MvM state and a client convar a demo does not carry, so ROME is never
granted and the nineteen MvM skins are hidden — TF2's own default for a viewer who has not opted in.
Both were checked against the shipped data rather than deferred, which is what turned an
"unimplementable" claim into a scoped one.

### `ValidateEntityAttachedToPlayer` — a non-divergence, and the reason is worth keeping

It is the client's anti-griefing check: a hat parented to something that is not its owner is
suppressed, and `UpdateWearableBodyGroups` skips an item that fails it. Two independent reasons it
cannot fire for us, and the first is decisive:

```cpp
m_bDeemedInvalid = engine->IsHLTV() ? false : !ValidateEntityAttachedToPlayer( bRetry );
```

(`c_baseentity.cpp:1383`.) **Under HLTV the check is not run at all**, so every SourceTV demo already
takes our branch. And on a POV demo it runs but cannot conclude: it needs
`pOwner->Inventory()->GetSOC()` — the Steam inventory of the player being validated — which a client
has only for itself, so the "lost connection to the GC" arm returns true and trusts the server.

This is the second mechanism today whose absence is at parity because **the engine's own
preconditions do not hold for a spectator** — the first was the style arm of `UpdateBodygroups`. It
is worth naming as a class of result: checking the precondition costs one call chain and converts a
plausible defect into a settled question.

### `TranslateViewmodelHandActivity` — a non-divergence, resolved before the wire

196 entries in `s_viewmodelacttable` (`tf_weaponbase.cpp:4292`) map a base viewmodel activity plus a
weapon role onto a role-specific one, so a primary weapon's hands play `ACT_PRIMARY_VM_IDLE` rather
than `ACT_VM_IDLE`. A table that size looks like a substantial gap.

**It runs before the value is networked.** The call site is inside `SendWeaponAnim`
(`basecombatweapon_shared.cpp:1216`), which translates and then calls
`SendViewModelMatchingSequence` → `SetSequence` on the viewmodel — and `m_nSequence` on
`DT_BaseViewModel` is what a demo carries. The client never re-derives it; by the time anything is
recorded, the translation has already happened and collapsed into a sequence number.

Same shape as the two above and worth stating as the third: **a translation applied by the sender is
not a translation the receiver owes.** The test is where the call sits relative to the network
variable, not how large the table is.

The remaining uncited pair, `SetupWeights` and `UsesFlexDelayedWeights`, are both downstream of
facial flexes, which `docs/RISKS.md` already records as unimplemented — a worn item with its own
flex controllers takes the wearer's weights, and there are no weights to take. Filed here only so the
next sweep of this class does not chase them again.

### A correction to B352's stated reason, which was wrong

B352 recorded that the eight `player_bodygroups` entries valued 0 are unreachable because "a demo
carries no vision filter". **That reason is false.** `C_TFPlayer::GetVisionFilterFlags`
(`c_tf_player.cpp:8028`) is not only a local-player opt-in: the Halloween arm applies to everyone
whenever `IsHolidayActive( kHoliday_HalloweenOrFullMoon )`, and that reads `IsHolidayMap`, which a
demo does carry. A Halloween recording has a live vision filter.

The conclusion survives, for a different and stronger reason — **measured, not inferred**. The eight
zero-valued entries belong to items 16, 125, 382, 384, 440, 938, 1073 and 30406. The 23 items
declaring `vision_filter_flags` are 738, 745, 746, 995 and 30143–30161. **The two sets are
disjoint**, so no shipped item can both be hidden by a vision filter and have a state-0 bodygroup
entry to apply when it is.

Recorded rather than quietly edited, because the wrong version was a plausible-sounding inference
that would have been repeated: it reads like a fact about demos and is actually a fact about nothing.
