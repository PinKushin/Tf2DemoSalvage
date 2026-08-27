# Convar coverage — what the engine ships, and what this viewer answers to

**D104.** The owner, after `mat_phong` had to be built before B170 could be tested at all:

> *"we are going to go through all valves settings, see which ones we need to import, and fill out
> our cvar list completely so we can stop forgetting to make shit bindable the right way"*

## The denominator is generated, not written here

`tf/cvarlist.log` is the game's own dump, produced by the installed build:

```
3,668 total convars/concommands
  2,660 convars
  1,008 concommands
```

`CvarNameConformanceTests` reads it and fails when this viewer answers to a name the engine does not
know and that is not declared as ours. **That is the part that cannot go stale.** A table of names
kept by hand can; the file the game writes cannot.

**Why a wrong name is a defect and not a cosmetic issue.** D69 says a real TF2 config must work
wholesale, and ignoring unknown commands is the feature rather than an oversight — so a viewer that
calls something by the wrong name silently drops the line a user actually pasted. Nothing complains,
and the result is indistinguishable from the setting having no effect.

## What this viewer names today

**42 names across two surfaces**: 28 bound actions (`KeyBindings.Commands`) and 14 settings
(`ViewerSettings`'s `*Command` constants). The test reads both, the second by reflection, because
checking only the first is how `texture_quality` went unexamined until this document was written.

**34 are Valve's.** Eight are ours, each with a reason that was checked against the shipped list
rather than assumed:

| name | why it is ours |
|---|---|
| `mat_fullscreen_mode` | No `fullscreen` or `videomode` convar exists at all. `mat_setvideomode` is a command taking width, height and windowed. |
| `togglefullscreen` | Follows the above — nothing of Valve's to bind. |
| `mat_surfacecolours` | The category view is this project's diagnostic; Valve colours nothing by what it is. |
| `cl_screenshot_folder` | `cl_screenshotname` names a FILE; nothing of Valve's names a directory. |
| `resetcamera` | The free camera is this project's; Valve's spectator has no reset. |
| `opendemo` | `playdemo` takes a demo NAME; this opens a picker, which the engine has no command for. |
| `resetspeed` | `demo_timescale 1` would be faithful and was rejected deliberately: this also clears REVERSE, which the engine cannot express (D97). |
| `texture_quality` | **Provisional — a question, not an answer.** See below. |

**`texture_quality` is the one entry that is unfinished.** Unlike the other seven, Valve DOES ship a
convar in this area: `mat_picmip`, default `-1`, archived. It is not a rename, because picmip counts
mip levels *dropped* and this setting is a quality enum — adopting the name means adopting the scale.
Recorded as a decision to make rather than carried as a justification.

## The `demo_` family, which is the one most directly ours

Eighteen entries. **This viewer implements two.**

| convar | | note |
|---|---|---|
| `demo_togglepause` | **done** | bound to play/pause |
| `demo_fov_override` | **done** | wins over `fov_desired`, as the engine prefers it |
| `demo_pause` | gap | we toggle; the engine also has explicit pause |
| `demo_resume` | gap | the other half of the pair |
| `demo_timescale` | gap | playback speed; see `resetspeed` above |
| `demo_gototick` | gap | seek to a tick — we seek, under no name |
| `demo_setendtick` | gap | |
| `demo_pauseatservertick` | gap | |
| `demo_interpolateview` | gap | view interpolation during playback |
| `demo_legacy_rollback` | gap | legacy view interpolation |
| `demo_avellimit` | gap | angular velocity limit before interpolation gives up |
| `demo_interplimit` | gap | origin velocity limit, same idea |
| `demo_fastforwardstartspeed` | gap | |
| `demo_fastforwardfinalspeed` | gap | |
| `demo_fastforwardramptime` | gap | |
| `demo_quitafterplayback` | out of scope | a viewer is not a game session |
| `demo_recordcommands` | out of scope | recording, not playback |
| `demo_debug` | out of scope | the engine's own tracing |

**The four interpolation convars are the interesting ones**, because they are not preferences: they
describe how the engine decides when interpolation has gone wrong and rolls back. Anything this
viewer does between ticks is answering the same question, so they belong to the same conversation as
`docs/CONFORMANCE.md` rather than to a settings menu.

## Families sized, for the next pass

Counted from the same file, and deliberately not classified line by line here — a list of 2,660
entries triaged in one sitting would be a list nobody checked.

| family | entries | relevance |
|---|---|---|
| `snd_` | 63 | real — the audio path exists and answers to none of them |
| `r_draw*` | 35 | real — three implemented (`r_drawworld`, `r_drawentities`, `r_drawviewmodel`) |
| `mat_` debug views | 25 | mostly done — the drawflat/luxels/normalmaps/bumpbasis/leafvis set landed with B210 |
| `spec_` | 12 | partly — spectator modes; the free camera already imitates the roaming spectator (B215) |
| `cl_show*` | 16 | `cl_showfps` done; the rest are HUD panels |
| `viewmodel_` | 2 | done — `viewmodel_fov` and `viewmodel_fov_demo` |
| `fov_` | 1 | done — `fov_desired` |
| `ai_`, `nb_`, `bot_`, `nav_`, `replay_`, `vr_`, `sixense_` | ~350 | out of scope — behaviour that never runs during playback, and hardware this viewer has none of |
| `tf_`, `sv_`, `mp_`, `net_` | ~1,050 | **NOT out of scope**, see below |

### A whole category was missed, and the first draft of this page got it wrong

That last row said *"out of scope — a server, a game, or hardware this viewer has none of"*. The
owner: *"some of the server game and hardware stuff we will need to implement, we have to emulate
parts of those systems to run the demo"*.

That is correct, and this project's own code already contradicted the claim before it was written.
The free camera's speeds are `sv_maxspeed`, `sv_specspeed` and `sv_specaccelerate` (B215) — server
convars, baked here as constants.

**The blind spot is structural, not a slip.** `CvarNameConformanceTests` checks names this viewer
ANSWERS TO. It cannot see a convar whose *value* we depend on and whose *name* we never accept,
because from the test's point of view that convar simply does not appear. Measured by intersecting
the shipped list against every convar name mentioned anywhere in `managed/` — comments included,
since that is where a borrowed default is cited:

| family | depended on, by value |
|---|---|
| `sv_` | `sv_maxspeed`, `sv_specspeed`, `sv_specaccelerate`, `sv_specnoclip`, `sv_cheats`, `sv_downloadurl` |
| `cl_` | `cl_forwardspeed`, `cl_sidespeed`, `cl_upspeed`, `cl_interp`, `cl_first_person_uses_world_model`, `cl_drawleaf`, `cl_showpos` |
| `snd_` | `snd_gain`, `snd_gain_min`, `snd_refdb`, `snd_refdist` |
| `host_` | `host_timescale` |

**Twenty-one, of which this viewer accepts one name** (`cl_showfps`). The other twenty are baked.

### The distinction that matters for these, and it is not "should we accept the name"

For a setting like `mat_phong`, the value belongs to the WATCHER: it is their config, their machine,
their preference. For most of the twenty it belongs to the RECORDING — `sv_maxspeed` is what the
server ran, `cl_interp` is what the recorder's client used, and a demo replayed with the watcher's
values is replaying something that did not happen.

So there are three states, not two, and this page's table above only distinguishes two of them:

1. **Accepted from the config** — the watcher's preference. 42 names today.
2. **Taken from the demo** — what the server or the recording client had. The right home for most
   of the twenty, and none are read that way today.
3. **Valve's declared default** — the fallback when neither of the first two supplies a value, and
   the state all twenty are in now, correct or not.

**"Declared", not "baked", and the distinction is D106.** The owner: *"baked default is never the
right answer i dont think, at least not if its not a baked default valve has"*. A default is right
as a VALUE and wrong as a `const float`: Valve wrote a name, a default and the ability to change it,
so copying only the number discards the two parts that make it portable across the nineteen years of
builds this project already reads.

So the third home is not an exemption from D106 — it is the case D106 is mostly about. None of the
twenty is exempt because it is client-side; the client-side ones simply cannot take their value from
the demo, which decides the SOURCE and says nothing about the FORM.

`docs/memory/a-default-is-not-a-constant.md` is the standing rule for the third: a default is a
`ConVar` declaration that can change between builds, not a number to copy in. What this page adds is
that even a correctly-read default is the *fallback*, and for an emulated system the demo outranks
it.

### Which of the three wins — answered, and it is Valve's answer rather than ours

**Done 2026-08-27 for the movement set.** `public/tier1/iconvar.h` settles the precedence in its own
comment above `FCVAR_REPLICATED`:

> It's a ConVar that's shared between the client and the server. At signon, the values of all such
> ConVars are sent from the server to the client … **If a change is requested it must come from the
> console (i.e., no remote client changes)** … If a value is changed while a server is active, it's
> replicated to all connected clients.

and the flag's own trailing comment calls it *"server setting enforced on clients"*. So for a
replicated ConVar the order is **demo, then Valve's declared default** — the watcher's config is not
a third rung that loses, it is not on the ladder at all. For the three `cl_` speeds it is stronger
again, because they are `FCVAR_CHEAT` as well: a player could not have changed them without
`sv_cheats`, so there is no watcher value to consider.

That leaves the earlier three-state picture correct for the *set* of twenty and wrong if read as a
precedence chain for any one of them. A given ConVar's value has exactly one source, decided by its
flags:

| flags | source | example |
|---|---|---|
| `"rep"` | the demo, else Valve's default | `sv_maxspeed`, `cl_forwardspeed` |
| `"user"` | the demo's `userinfo`, else Valve's default | `cl_interp` |
| neither | the watcher's config, else Valve's default | `snd_gain`, `cl_showpos` |

**Eight of the twenty are done.** `EngineConVars` holds the declarations — name, default string,
`Replicated`, `Cheat` — and `ServerConVars` resolves them against what a demo's `NET_SetConVar`
carried. `MovementConVarConformanceTests` checks each declaration against **both** the SDK and
`cvarlist.log`, which are thirteen years apart and agree; `CorpusServerConVarTests` is the assertion
that the values actually reach the timeline on a real demo.

| convar | state |
|---|---|
| `sv_maxspeed`, `sv_specspeed`, `sv_specaccelerate`, `sv_specnoclip` | **declared and read from the demo** |
| `cl_forwardspeed`, `cl_backspeed`, `cl_sidespeed`, `cl_upspeed` | **declared and read from the demo** |
| `sv_cheats`, `sv_downloadurl`, `host_timescale` | still baked; replicated, so the same shape applies |
| `cl_interp` | still baked; `userinfo`, which is a different reader |
| `cl_showpos`, `cl_drawleaf`, `cl_first_person_uses_world_model`, `snd_gain`, `snd_gain_min`, `snd_refdb`, `snd_refdist` | still baked; client-only, so the config is the missing source |

**The free camera was the first because it was the smallest complete instance.** Its speed is
`sv_maxspeed * sv_specspeed` (B215) and both are replicated, so it exercises the declaration, the
demo read and the fallback in one number. `FreeFlightPath.SpeedPerSecond` takes a `ServerConVars`
where it took two `const float`s, and `FreeCameraController.SetServer` logs when a server moved any
of them — so a jump server replaying at the wrong speed is a line in the log rather than a feeling
about the camera.

**Undeclared names are kept rather than dropped.** A real match demo sends forty values against the
eight declared here; refusing the rest would throw on an ordinary file, and discarding them would
lose the evidence the mod question needs. `ServerConVars.Changed` reports only the declared ones a
server actually moved, so a vanilla server — which re-sends `sv_maxspeed 320` among its forty —
reports nothing.

### Measured: what a demo actually carries

The shipped list marks replicated convars `"rep"`, and that splits the twenty cleanly:

| | convars | can a demo carry it? |
|---|---|---|
| replicated | `sv_maxspeed`, `sv_specspeed`, `sv_specaccelerate`, `sv_specnoclip`, `sv_cheats`, `sv_downloadurl`, `cl_forwardspeed`, `cl_sidespeed`, `cl_upspeed`, `host_timescale` | **yes** — the server sends these |
| userinfo (`"user"`) | `cl_interp` | **yes** — it travels in the `userinfo` string table, which this project already reads for the roster |
| client-only | `cl_showpos`, `cl_drawleaf`, `cl_first_person_uses_world_model`, `snd_gain`, `snd_gain_min`, `snd_refdb`, `snd_refdist` | no — the watcher's, so the value comes from their config or from Valve's declared default |

**`cl_forwardspeed`, `cl_sidespeed` and `cl_upspeed` are the surprise.** Despite the `cl_` prefix all
three are flagged `"sv"`, `"cheat"` and `"rep"` — server-controlled and replicated. The free camera's
walk speed is derived from `cl_forwardspeed` (B215), so that is a server value this viewer treats as
its own constant.

**And `NET_SetConVar` is already decoded.** `SetConVarMessage` carries the name/value pairs and the
assembly round-trips them. Nothing consumes them.

Read from every corpus demo:

```
2007 stv   6   sv_skyname, mp_timelimit, think_limit, sv_turbophysics, mp_winlimit, tv_transmitall
2008 stv   7   ... plus mp_maxrounds
2011 stv   5   sv_skyname, think_limit, sv_turbophysics, tf_gamemode_cp, tv_transmitall
2013 stv   9   ... plus steamworks_sessionid_server
z1800     30   mp_allowspectators, mp_tournament, mp_tournament_post_match_period, ...
f12 pov    6   func_break_max_pieces, sv_skyname, think_limit, sv_turbophysics, tf_gamemode_cp, ...
```

**Not one of the twenty appears in any of them** — a claim that holds for the corpus and **fails on
real match demos**, where `sv_downloadurl` does appear. See below. Either way a server sends what it
changed, every server here ran Valve's values for movement, and so the baked defaults were right at
the time of writing by luck rather than by design. A server that raised `sv_maxspeed` would send it,
this viewer would decode it, and ignore it.

**That last sentence is no longer true for the movement set** (2026-08-27) — the values are read and
used, and `CorpusServerConVarTests` asserts it against a real demo. It stays here because it is the
shape the remaining twelve are still in, and because "decoded correctly and ignored" is the failure
mode to recognise rather than one occurrence of it.

### The POV question, measured — and the answer is not the structural one

The era POV demos carry no `net_setconvar` while their STV counterparts do, which admits two
readings: those servers changed nothing, or POV recordings do not carry the message. The owner
expected the second — *"im pretty sure the answer is just going to be POV demos dont have that info,
STV does"* — and pointed at the discriminating sample, ten of benroads' POV demos from competitive
servers, 2013 to 2017.

**They carry 32 to 40 values each.** So POV demos do carry it, the structural reading is wrong, and
the era POVs are empty because they are the owner's own solo recordings where nothing was changed.
An empty result means an unchanged server, which is what it looked like and is now measured rather
than assumed.

### What a competitive server actually changes, and why it matters more than the count

From `20150119_2240_cp_process_final_(ovo)_blu.dem`, forty values. The gameplay ones are expected —
`mp_tournament`, nine `tf_tournament_classlimit_*`, `tf_damage_disablespread`,
`tf_use_fixed_weaponspreads`, `tf_weapon_criticals`. These are not:

```
sv_client_predict            sv_client_max_interp_ratio
sv_mincmdrate  sv_minupdaterate  sv_maxrate  sv_minrate
sv_downloadurl
```

**`sv_downloadurl` is one of the twenty**, so the claim that none of them appears in any demo held
only for the corpus and not for real match demos.

**And the interpolation clamps are the find.** `sv_client_max_interp_ratio`, `sv_mincmdrate` and
`sv_minupdaterate` bound what the recording client's `cl_interp` could actually have been — the
server constrains it. So reconstructing how a demo looked to the person who recorded it needs both
halves: their `cl_interp` from `userinfo`, and the server's clamps from `net_setconvar`. Neither is
read today, and a viewer that interpolates by its own rule is not showing what they saw.

### Mods are the case that breaks the current arrangement

The owner: *"the cvars can change by server… some mods will change move speed and all the other
settings for the most part, like jailbreak. the only mods we might currently work with are DM and
MGE, because those keep most things constant, but jump, surf, and other mods might not run right."*

That is the sharp edge of D106. A vanilla competitive server already changes forty convars without
touching movement; a jump or surf server changes movement itself, and every baked constant in the
free camera and the movement path is then wrong for that demo — silently, because the value arrives,
is decoded, and is ignored.

It also sets the scope honestly: **DM and MGE should work today** because they leave the movement
values alone, and **jump and surf should not be assumed to**. That is a testable claim and no demo
of either is in the corpus yet.

**And the trace does not show any of this** — it prints `svc_setconvar;` with no payload while the
assembly prints all six. Filed as B220.

## The rules this establishes

**Before inventing a name, search `cvarlist.log`.** It is one grep, it is the game's own answer, and
the test now fails if the search is skipped. Where a name genuinely has no engine equivalent, the
exception table is the place to say so — with the reason, checked, so a later reader can tell a
decision from an oversight.

**Before baking a number, ask where it should come FROM.** A convar's value can belong to the
watcher, to the recording, or to Valve's default, and those are three different answers. Writing the
default in as a constant answers the third by accident — which is right only when the first two do
not apply, and nothing currently records which case a given number is in.

**And note what the conformance test cannot see.** It measures names this viewer answers to, so a
convar we depend on silently is invisible to it. That is how a whole category went unlisted until the
owner named it, and it is worth remembering before treating a green test here as coverage.
