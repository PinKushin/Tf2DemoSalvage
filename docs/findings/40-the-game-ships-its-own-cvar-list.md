# The game ships its own cvar list

**Evidence class: read from shipped data.** A plain-text file inside a normal TF2 install; nothing
decompiled, nothing interpolated.

**Read `37-the-engines-demo-vocabulary.md` first.** That finding recovered `fps_max`, `demo_*` and
`engine_no_focus_sleep` from `engine.dll` by hand, and it remains the authority for those. This entry
is about a **cheaper instrument for the same job**, and about a contradiction inside this repository
that the instrument exposed.

## The file

```
<TF2>/tf/cvarlist.log
```

3,672 lines, ending `3668 total convars/concommands`. Every entry carries **name, default, flags and
help text**, one per line, in fixed columns:

```
cl_demoviewoverride                      : 0        : , "cl"           : Override view during demo playback
engine_no_focus_sleep                    : 50       : , "a"            :
fps_max                                  : 400      :                  : Frame rate limiter, cannot be set while connected to a server.
viewmodel_fov                            : 54       : , "a", "cl"      : Sets the field-of-view for the viewmodel.
sensitivity                              : 2        : , "a", "cl"      : Mouse sensitivity.
```

Commands appear with `cmd` where a convar has its default, so `+zoom` and `xload` sit in the same
list. Flags are short archive names — `"a"` for `FCVAR_ARCHIVE`, `"cl"` for `FCVAR_CLIENTDLL`,
`"cheat"`, `"sv"`, `"norecord"`. A convar with no flags shows an empty column, which is how `fps_max`
reads as `FCVAR_NONE` here — matching what finding 37 established from the registration itself.

## What it is worth, stated honestly

**It is not a better source than a registration. It is a faster one, and it covers more ground.**

| | reading the registration (finding 37) | this file |
|---|---|---|
| what it gives | name, default, full flag word, help, callbacks, min/max clamps | name, default, short flags, help |
| how long per convar | a byte scan and a reconstruction | one `grep` |
| how many at once | one | 3,668 |
| authority | a declaration | a **dump** of what the client reported |

The last row is the caveat that matters. A dump records what the client held when it was written, so
a convar the user has archived to a non-default value could in principle be captured as if it were
the default — `FCVAR_ARCHIVE` entries are exactly the ones at risk. Nothing here has been seen to
disagree with a declaration where both exist (`viewmodel_fov 54` matches `view.cpp:111`;
`engine_no_focus_sleep 50` and `fps_max 400` match finding 37's byte-level reads), but where the
answer decides behaviour, cross-check it against a declaration when one exists.

**Where no declaration exists it is the best available source**, and that is most of `engine.dll`,
`materialsystem.dll` and `vguimatsurface.dll` — none of which are in `source-sdk-2013`.

## The contradiction it exposed

`ViewerSettings.DefaultFrameRateLimit` carried this:

> its default could not be recovered from the binary, because the string pool pairs a cvar's name
> with its help text and not with its default

**The reasoning about the string pool is correct.** Dumped around `engine_no_focus_sleep`, the bytes
really do read

```
engine_no_focus_sleep|||Frame rate limiter, cannot be set while connected to a server.||fps_max|
```

so the name sits against the *next* convar's help text, and defaults are single-character literals
shared by hundreds of registrations. Finding 37 got past exactly this by reconstructing the pooled
numeric block (`>400.450.512.1080<`) rather than by reading adjacency.

**So the sentence was not merely superseded — it contradicted a finding already in this repository,
and did so next to the number, where a reader would take it as authoritative.** Finding 37 had
`fps_max` at 400 with `FCVAR_NONE`; `ViewerSettings.cs` said the default was unrecoverable. Both were
written here, weeks apart, and nothing compared them.

That is the failure worth recording, and it is not "we missed a source". It is: **a conclusion of the
form "X cannot be known" was left standing in code after a finding had established X.** The
generalisation is in `docs/memory/an-impossibility-claim-expires.md` — an impossibility claim ages
badly in a way a positive claim does not, because nothing about later work forces a re-read of it.

## What it settles at a glance

| convar | shipped default | bearing |
|---|---|---|
| `cl_demoviewoverride` | `0` | the engine's demo free camera is **off** by default, and the convar is both the enable flag and the speed scale (`view.cpp:712`, `:153`) — see B215 |
| `engine_no_focus_sleep` | `50` | ms slept per frame without focus; shipped Valve runs ~20 fps alt-tabbed. B209 |
| `fps_max` | `400` | agrees with finding 37 |
| `viewmodel_fov` | `54` | agrees with `view.cpp:111`, which this project already matches |
| `sensitivity` | `2` | the raw-count scale, deliberately not used for drag-look (`FreeCameraController`) |

## How to read it

```bash
grep -E "^<name> +:" "<TF2>/tf/cvarlist.log"
```

Anchor on `^` and require the colon. Without it `fps_max` matches inside other convars' help text,
and `volume` matches `dsp_volume`, `snd_musicvolume` and eleven others.
