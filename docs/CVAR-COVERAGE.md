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
3. **Valve's default, baked** — the honest fallback when the demo does not carry it, and the state
   all twenty are in now, correct or not.

`docs/memory/a-default-is-not-a-constant.md` is the standing rule for the third: a default is a
`ConVar` declaration that can change between builds, not a number to copy in. What this page adds is
that even a correctly-read default is the *fallback*, and for an emulated system the demo outranks
it.

**None of that is measured yet.** Which of the twenty a demo actually carries — in its string tables,
its server info, or not at all — is the next pass, and it is an evidence question rather than a
classification one.

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
