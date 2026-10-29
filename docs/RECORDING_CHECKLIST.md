# Recording checklist for period clients

What to do while recording a demo on an old TF2 build, so that every era specimen exercises the
same things and a difference between two of them is an **era difference rather than a difference
in what happened that day**.

The era demos so far were recorded ad hoc. That is why `svc_VoiceData` had no coverage until the
2007 session, why chat has never been captured below protocol 24 except once, and why the voice
client-to-player mapping is still unresolved — the one recording that carries voice has a single
speaker in it. None of those are hard to capture; they were simply not on a list.

**The solo list below is now scripted:** `tools/specimen/tf2ds_specimen.cfg` and a per-era override. The procedure is
in `docs/measurements/2026-10-28-era-specimens-v2-plan.md`. A step a build refuses is recorded here, per era.

**Run the whole list on every build.** Two to four minutes is enough (see the corpus manifest on
why era specimens stay short), and a build that refuses a step is itself a finding.

---

## Before you start

**Step 0, every era: start the client the way it ships.** The owner, 2026-10-29: *"the agent should always look for a
bat or shortcut first"*.
- List the client's folder, one level up and one level down, for `.bat`, `.cmd`, `.lnk` and loader executables:
  `RUN_TF2.exe`, `SmartSteamLoader.exe` with its `.ini`, `tf2.exe`, and `rev.ini`/`steamemu.ini`.
- Read each one: the text, or the target and arguments.
- Start the client through that launcher, with the RCON arguments added to the launcher's own command line. Hash the
  launcher first and restore it afterwards.
- `hl2.exe` started raw is the last resort, used only where no launcher exists.

Measured 2026-10-29: 2008 started raw failed with `CFileSystem_Steam::Init() failed`, and 2007 started raw froze after
the config banner. The recipes per build are in `docs/measurements/2026-10-28-era-specimens-v2-plan.md`.

```
fps_max 66           // BEFORE the map: fps_max is `notconnected`, refused on a loaded map, and every wait counts frames
tv_enable 1          // before the map loads, or SourceTV will not attach
map <mapname>
```

Record **both** points of view of the same session where possible:

```
record pov<year>     // POV: carries dem_usercmd and dem_consolecmd
tv_record stv<year>  // SourceTV: carries neither, and is what a real match archive looks like
```

**A step the build rejects is data, not a failure.** An unrecognised console command prints
`Unknown command: <name>` and that line travels into the demo as a `TextMsg` — which is how we
know the 2007 client has no `cl_crosshairscale`. Note what it refused rather than skipping it
silently.

---

## Solo — everything below works with one person on a listen server

| # | Do this | What it exercises |
|---|---|---|
| 1 | `say hello from <year>` | `SayText2` → chat decoding, per era |
| 2 | `say_team team line` | the chat message's destination/kind field |
| 3 | `name PinKushin` then `say hi` | a name change through `userinfo`, chat, and the header (the shared specimen name is ASCII, D241) |
| 4 | Hold the mic key and talk for ~10s | `svc_VoiceData`, `svc_VoiceInit` |
| 5 | `name something_else` mid-demo | `svc_UpdateStringTable` on `userinfo` — the mid-game roster path (B22), and `TextMsg #TF_Name_Change` |
| 6 | Change class at least three times | different entity classes, different flattened property sets |
| 7 | Fire every weapon you have at a wall | `svc_TempEntities`, `svc_Sounds`, `svc_BspDecal` (decals) |
| 8 | Blow yourself up (`explode`) | `player_death`, `player_hurt` with attacker == victim |
| 9 | `kill` | `player_death` without damage |
| 10 | Take fall damage, then pick up health and ammo | `player_hurt`, `ItemPickup` |
| 11 | Taunt, and let it run to completion | `PlayerTauntSoundLoopStart` / `End` |
| 12 | As Engineer, build and then destroy each building | `BuiltObject`, object entities entering and being deleted |
| 13 | Capture a control point | `teamplay_point_captured` and friends |
| 14 | `mp_restartgame 1` and let the round restart | round events, `TextMsg` announcements, a second full snapshot |
| 15 | Break a breakable prop if the map has one | `BreakModel` / `CheapBreakModel` |
| 16 | Die and spectate — cycle with `spec_next` | `svc_SetView`, spectator entity state |
| 17 | `pause`, wait, `unpause` | `svc_SetPause` |
| 18 | Type a few unknown commands on purpose | `TextMsg` bodies, and it dates the build's cvar set |

## Needs a second player — the open questions live here

These cannot be done alone, and each one is currently blocking something specific.

| # | Do this | Why it matters |
|---|---|---|
| 19 | **Two people talking, one after the other** | The only way to settle how `svc_VoiceData`'s client slot maps to a player. One speaker cannot distinguish "client == entity" from "client + 1 == entity"; the 2007 demo has one speaker and contradicts the widely repeated +1 rule |
| 20 | One player joins **after** the recording starts | The mid-game join path (B22) against a real update rather than a name change |
| 21 | One player disconnects, another takes the slot | Slot reuse — whether a re-used entity index reports a new serial number |
| 22 | Kill each other several times | `player_death` with distinct attacker and victim, which is what makes a kill attributable |
| 23 | Medic übers the other player | condition properties, which are where a viewer reads game state from |
| 24 | Both talk **at the same time** | Interleaved voice packets from two clients |

**Bots are not a substitute before ~2010.** TF2's own bots (`tf_bot_add`) arrived years after
launch, so for the 2007, 2008 and 2009 builds a second player means a second machine or a second
Steam session on the LAN. Where bots do exist, note it — a bot's `userinfo` record sets the fake
player flag, which is its own thing worth having.

---

## After recording

Check what actually landed rather than assuming, since several of these produce nothing visible
in game:

```bash
tf2demosalvage <demo> --summary        # chat lines, players, event counts
tf2demosalvage <demo> --trace -o t.txt # every message in stream order
```

Then grep the trace for the ones with no in-game feedback — `svc_voicedata`, `svc_bspdecal`,
`svc_setpause`. A step that produced no message is worth knowing about immediately, while the
build is still set up.

---

## What each build refused (the scripted run, 2026-10-28/29)

Read from each run's console. "Refused" means the build printed `Unknown command` or a cheat refusal; the two deliberate
unknowns (`tf2ds_unknown_one`, `tf2ds_unknown_two`) are refused everywhere and are left out below. Whether a step that
printed nothing actually took effect is read from the demo, not from the console.

| build | refused | other findings |
|---|---|---|
| 2007, 3258 (p11) | `cl_disablehtmlmotd`, `mp_respawnwavetime`, `mp_disable_respawn_times`, `tf_bot_quota`, `tf_bot_add`, `addcond`, `removecond` | (historical, 2026-10-28: a UTF-8 `name` from a cfg set nothing and printed the cvar; the cfg name is now ASCII, D241.) `tv_delay` is clamped to at least 10. `con_logfile` diverts `-condebug`'s console.log. Started raw, the client froze after the config banner. |
| 2008, 3420 (p14) | not yet run | Started raw, it failed with `CFileSystem_Steam::Init() failed: failed to find steam interface`. |
| 2010, 4231 (1.0.9.5, p15) | none | Speex voice landed (`svc_VoiceData` 266). |
| 2010, 4269 (1.1.0.0, p15) | none | Needs its SmartSteamLoader; started bare, it has no `gameinfo.txt`. Speex voice landed (44). |
| 2011, 4604 (p16) | `cl_savescreenshotstosteam` | **Silent no-op: voice** (`svc_VoiceData` 0, Speex declared). REVEmu: the first launch after blanking `SteamClientDll` shuts down, the second runs. |
| 2013, 5252 (p24) | none from the script | **Silent no-op: bots** (`tf_bot_add` and `bot` add nothing and print nothing). `fps_max` is refused on a loaded map. The owner's class cfgs end in `sv_cheats 0`, so the script re-sets it before every cheat. |
| live, 11138802 (p24) | none from the script | **Silent no-ops: bots, and voice** (codec `steam`, `svc_VoiceData` 0, Steam offline). With Steam offline, STEAMAUTH drops the local client after about 70 s; `sv_lan 1` prevents it. |

On every build the script now produces, measured in both demos: a capture, a built sentry and dispenser, a pause, 13
deaths. Coverage "none refused" does not mean every step landed: the silent no-ops above are read from the demo.
